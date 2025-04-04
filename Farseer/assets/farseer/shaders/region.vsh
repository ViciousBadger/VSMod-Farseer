#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPositionIn;

uniform mat4 modelMatrix;
uniform mat4 viewMatrix;
uniform mat4 projectionMatrix;

uniform vec4 horizonColorDay; 
uniform vec4 horizonColorNight; 

uniform vec3 sunColor; 
uniform float dayLight; 

uniform vec4 rgbaFogIn; 
uniform float fogMinIn; 
uniform float fogDensityIn; 

// uniform float viewDistance;
uniform float farViewDistance;

out vec3 vertexPos;
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

    vec4 color = mix(horizonColorNight, horizonColorDay, 0.5 + dayLight * 0.5);
    color = color * dayLight;
    //color *= fogColor;

    // color.a *= 1.0 - clamp(20 * (1.2 - length(worldPos.xz) / viewDistance) - 5, 0.0, 1.0);

    // Fade by distance using color (to avoid transparency weirdness)
    float distFade = length(worldPos.xz) / (farViewDistance * 0.8);
    color.rgb += distFade * 0.1 * dayLight * clamp(1.0 - fogDensityIn, 0.0, 1.0);

    // Fade out near the *far* render distance
    color.a *= clamp(20 * (1.1 - length(worldPos.xz) / farViewDistance) - 5, 0.0, 1.0);

    float chunk_a = clamp(17.0 - 20.0 * length(worldPos.xz) / viewDistance + max(0, worldPos.y / 50.0), -1, 1);

    if (chunk_a > 0.9) {
        color.a = 0.0;
    }

    float fogAmount = getFogLevel(worldPos, fogMinIn, fogDensityIn);

    vertexPos = worldPos.xyz;
    rgbaMain = color;
    rgbaFog = rgbaFogIn;
    fogAmountf = clamp(fogAmount + clamp(1 - 4 * dayLight, -0.04, 1), 0, 1);
	nightVisionStrengthv = nightVisionStrength;

    // Cut completely at near render distance
    // if (length(worldPos.xz) < viewDistance * 0.8) {
    //     color.a = 0.0;
    // }

    vec4 camPos = viewMatrix * worldPos;

    gl_Position = projectionMatrix * camPos;
}
