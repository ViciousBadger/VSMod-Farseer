#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in vec2 uvIn;
// rgb = block light, a=sun light level
layout(location = 2) in vec4 rgbaLightIn;
// Check out vertexflagbits.ash for understanding the contents of this data
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

uniform float shadowIntensity = 1;
uniform vec3 lightPosition;

out vec4 rgba;
out vec2 uv;
out vec4 rgbaFog;
out float fogAmount;
out vec3 normal;
out vec3 vertexPosition;
out vec4 worldPos;
out vec4 camPos;
out float lod0Fade;
out float nb;

#if SSAOLEVEL > 0
out vec4 gnormal;
#endif


flat out int renderFlags;

#include vertexflagbits.ash
#include shadowcoords.vsh
#include fogandlight.vsh
#include vertexwarp.vsh
#include colormap.vsh

void main(void)
{
	bool isLeaves = ((renderFlagsIn & WindModeBitMask) > 0); 

	vec4 truePos = vec4(vertexPositionIn + origin, 1.0);
	vertexPosition = vertexPositionIn;
	
	worldPos = applyVertexWarping(renderFlagsIn, truePos);
	worldPos = applyGlobalWarping(worldPos);
	
	camPos = modelViewMatrix * worldPos;

	gl_Position = projectionMatrix * camPos;
	
	calcShadowMapCoords(modelViewMatrix, worldPos);
	calcColorMapUvs(colormapData, truePos + vec4(playerpos, 1.0), rgbaLightIn.a, isLeaves);
	
	fogAmount = getFogLevel(worldPos, fogMinIn, fogDensityIn);
	uv = uvIn;
	
	rgba = applyLight(rgbaAmbientIn, rgbaLightIn, renderFlagsIn, camPos);
	
	// Distance fade out
	rgba.a = clamp(2.0 - 2.0 * length(worldPos.xz) / viewDistance + max(0, worldPos.y / 50.0), -1, 1);
	
	rgbaFog = rgbaFogIn;
	
	renderFlags = renderFlagsIn;
	normal = unpackNormal(renderFlagsIn);
	
#if SSAOLEVEL > 0
	gnormal = modelViewMatrix * vec4(normal.xyz, 0);
	gnormal.w = isLeaves ? 1 : 0; // Cheap hax to make SSAO on leaves less bad looking;
#endif


	// To fix Z-Fighting on blocks over certain other blocks. 
	if (gl_Position.z > 0) {
		int zOffset = (renderFlags & ZOffsetBitMask) >> 8;
		gl_Position.w += zOffset * 0.00025 / max(0.1, gl_Position.z * 0.05);
	}
	


	//  11.3.24: For performance, we now pre-calculate the LOD0 fade alpha value in the vertex shader, and pass it to the fragment shader; this reduces conditionality in the fragment shader

	// Lod 0 fade
	// This makes the lod fade more noticable, actually O_O
	if ((renderFlags & Lod0BitMask) != 0) {
		
		// We made this transition smoother, because it looks better,
		// if you notice chunk popping, revert to the old, harsher transition
		// Radfast and Tyron, May 28 2021 ^_^
		float b = clamp(10 * (1.05 - length(worldPos.xz) / viewDistanceLod0) - 2.5, 0, 1);
		//float b = clamp(20 * (1.05 - length(worldPos.xz) / viewDistanceLod0) - 5, 0, 1);
				
		lod0Fade = 1 - b;
	}
	else    lod0Fade = 0.0;
	

	//  14.3.24: We can also pre-calculate nb

#if SHADOWQUALITY > 0
	float intensity = 0.34 + (1 - shadowIntensity)/8.0; // this was 0.45, which makes shadow acne visible on blocks
#else
	float intensity = 0.45;
#endif
	nb = max(max(intensity, 0.5 + 0.5 * dot(normal, lightPosition)), normal.y * 0.95);
}
