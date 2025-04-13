#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPositionIn;

uniform mat4 modelMatrix;
uniform mat4 viewMatrix;
uniform mat4 projectionMatrix;

uniform vec4 rgbaFogIn; 
uniform float fogMinIn; 
uniform float fogDensityIn; 

uniform float farViewDistance;
uniform float globeEffect;

out vec4 worldPos;
out float yLevel;
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
    yLevel = vertexPositionIn.y;
    worldPos = modelMatrix * vec4(vertexPositionIn, 1.0);
    worldPos = applyGlobalWarping(worldPos);

    float distStart = viewDistance * 0.785;
    dist = (length(worldPos.xz) - distStart) / (farViewDistance - distStart - 512);

    // Makes the transition much less jank by forcing the far terrain into the
    // ground at close range
    worldPos.y -= max(0, mix(5, 0, dist*50));

    worldPos.y -= globeEffect * pow(dist, 2.0) * farViewDistance;

    fogAmount = getFogLevel(worldPos, fogMinIn, fogDensityIn);

    rgbaFog = rgbaFogIn;
	nightVisionStrengthv = nightVisionStrength;

    vec4 camPos = viewMatrix * worldPos;
    gl_Position = projectionMatrix * camPos;
}
