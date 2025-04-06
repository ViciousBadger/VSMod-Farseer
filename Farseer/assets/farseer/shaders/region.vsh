#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPositionIn;

uniform mat4 modelMatrix;
uniform mat4 viewMatrix;
uniform mat4 projectionMatrix;

uniform vec3 mainColor; 
uniform vec3 sunColor; 
uniform float dayLight; 

uniform vec4 rgbaFogIn; 
uniform float fogMinIn; 
uniform float fogDensityIn; 

uniform float farViewDistance;

out vec4 worldPos;
out vec4 rgbaFog;
out float dist;
out float fogAmount;
out float nightVisionStrengthv;

#include vertexflagbits.ash
#include colorutil.ash
#include shadowcoords.vsh
#include fogandlight.vsh
#include vertexwarp.vsh

void main()
{
    worldPos = modelMatrix * vec4(vertexPositionIn, 1.0);
    worldPos = applyGlobalWarping(worldPos);

    float distStart = viewDistance * 0.80;
    dist = (length(worldPos.xz) - distStart) / (farViewDistance - distStart - 512);

    float chunkAlpha = clamp(17.0 - 20.0 * length(worldPos.xz) / viewDistance + max(0, worldPos.y / 50.0), 0, 1);
    dist -= chunkAlpha;

    fogAmount = getFogLevel(worldPos, fogMinIn, fogDensityIn);

    rgbaFog = rgbaFogIn;
    //fogAmountf = clamp(fogAmount + clamp(1 - 4 * dayLight, -0.04, 1), 0, 1);
	nightVisionStrengthv = nightVisionStrength;

    vec4 camPos = viewMatrix * worldPos;
    gl_Position = projectionMatrix * camPos;
}
