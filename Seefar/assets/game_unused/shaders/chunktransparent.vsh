#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in vec2 uvIn;
// rgb = block light, a=sun light level
layout(location = 2) in vec4 rgbaLightIn;
layout(location = 3) in int renderFlagsIn;

// Bits 0..7 = season map index
// Bits 8..11 = climate map index
// Bits 12 = Frostable bit
// Bits 13, 14, 15 = If a windmode is set, these 3 bits are used to offset the season position for more varied leaf colors
// Bits 16-23 = temperature
// Bits 24-31 = rainfall
layout(location = 4) in int colormapData;



uniform vec4 rgbaFogIn;
uniform vec3 rgbaAmbientIn;
uniform float fogDensityIn;
uniform float fogMinIn;
uniform vec3 origin;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform float forcedTransparency;

out vec4 rgba;
out vec4 rgbaFog;
out float fogAmount;
out vec2 uv;
out vec4 worldPos;
out vec3 vertexPos;

flat out int renderFlags;
flat out vec3 normal;
out float normalShadeIntensity;

#include vertexflagbits.ash
#include shadowcoords.vsh
#include fogandlight.vsh
#include vertexwarp.vsh
#include colormap.vsh

void main(void)
{
	worldPos = vec4(vertexPositionIn + origin, 1.0);
	
	worldPos = applyVertexWarping(renderFlagsIn, worldPos);
	worldPos = applyGlobalWarping(worldPos);

	vec4 cameraPos = modelViewMatrix * worldPos;
	
	gl_Position = projectionMatrix * cameraPos;

	calcShadowMapCoords(modelViewMatrix, worldPos);
	calcColorMapUvs(colormapData, vec4(vertexPositionIn + origin, 1.0) + vec4(playerpos, 1), rgbaLightIn.a, false);
	
	fogAmount = getFogLevel(worldPos, fogMinIn, fogDensityIn);
	uv = uvIn;
	
	rgba = applyLight(rgbaAmbientIn, rgbaLightIn, renderFlagsIn, cameraPos);
	rgba.a = clamp(2 * (2 - length(worldPos.xz) / viewDistance) - 2 + max(0, worldPos.y / 50.0), -1, 1) - forcedTransparency;
	
	rgbaFog = rgbaFogIn;
	renderFlags = renderFlagsIn;
	
	// To fix Z-Fighting on blocks over certain other blocks. 
	if (gl_Position.z > 0) {
		int zOffset = (renderFlags & ZOffsetBitMask) >> 8;
		gl_Position.w += zOffset * 0.00025 / max(3, gl_Position.z * 0.05);
	}
	
	normal = unpackNormal(renderFlags);
	normalShadeIntensity = min(1, rgbaLightIn.a * 1.5);
}
