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

// uniform float viewDistance;
uniform float farViewDistance;

out vec3 worldPosf;
out vec4 rgbaMain;
out vec4 rgbaFog;
out float fogAmountf;
out float nightVisionStrengthv;

#include vertexflagbits.ash
#include colorutil.ash
#include shadowcoords.vsh
#include fogandlight.vsh
#include vertexwarp.vsh

void main()
{
    vec4 worldPos = modelMatrix * vec4(vertexPositionIn, 1.0);

    worldPos.xyz = applyPerceptionWarping(worldPos.xyz);

    // "Fade" into sky color by distance
    float distFade = length(worldPos.xz) / (farViewDistance * 0.8);
    vec4 color = vec4(mainColor * dayLight, clamp(1.0 - distFade, 0.0, 1.0));

    // Subtract approximately alpha of chunks for smooth-ish fade
    float chunk_a = clamp(18.0 - 20.0 * length(worldPos.xz) / viewDistance + max(0, worldPos.y / 50.0), -1, 1);
    color.a = min(1.0 - chunk_a, color.a);

    float fogAmount = getFogLevel(worldPos, fogMinIn, fogDensityIn);

    worldPosf = worldPos.xyz;
    rgbaMain = color;
    rgbaFog = rgbaFogIn;
    fogAmountf = clamp(fogAmount + clamp(1 - 4 * dayLight, -0.04, 1), 0, 1);
	nightVisionStrengthv = nightVisionStrength;

    vec4 camPos = viewMatrix * worldPos;
    gl_Position = projectionMatrix * camPos;
}
