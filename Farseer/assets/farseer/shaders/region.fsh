#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

in vec3 vertexPos;

in vec4 rgbaMain;
in vec4 rgbaFog;
in float fogAmountf;
in float nightVisionStrengthv;

uniform float fogDensityIn;
uniform float fogMinIn;
uniform vec3 sunPosition;
uniform float dayLight; 

layout(location = 0) out vec4 outColor;

#include noise3d.ash
#include dither.fsh
#include fogandlight.fsh
#include skycolor.fsh
#include underwatereffects.fsh

void main()
{
    vec4 col = rgbaMain;

    float sealevelOffsetFactor = 0.0;
	float dayLight = 1;
	float horizonFog = 0;
    vec4 skyGlow = getSkyGlowAt(vec3(vertexPos.x, vertexPos.y, vertexPos.z), sunPosition, sealevelOffsetFactor, clamp(dayLight, 0, 1), horizonFog, 0.7);

    col.rgb *= mix(vec3(1), 1.2 * skyGlow.rgb, skyGlow.a);
    col.rgb *= max(1, 0.9 + skyGlow.a/10);

 //    float baseBloom = max(0, 0.25 - fogAmountf/2);
	// #if BLOOM == 1
	// 	col.rgb *= 1 - baseBloom;
	// #endif

	col.rgb = mix(col.rgb, rgbaFog.rgb, fogAmountf);

    col.rgb += vec3(0.1, 0.5, 0.1) * nightVisionStrengthv;

    outColor = col;
    if (outColor.a < 0.1) discard;
}
