#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

in vec4 worldPos;
in vec4 rgbaFog;
in float dist;
in float fogAmountf;
in float nightVisionStrengthv;

uniform float fogDensityIn;
uniform float fogMinIn;
uniform float horizonFog;
uniform vec3 sunPosition;
uniform float dayLight; 

layout(location = 0) out vec4 outColor;
layout(location = 1) out vec4 outGlow;
#if SSAOLEVEL > 0
layout(location = 2) out vec4 outGNormal;
layout(location = 3) out vec4 outGPosition;
#endif

#include noise3d.ash
#include dither.fsh
#include fogandlight.fsh
#include skycolor.fsh
#include underwatereffects.fsh

void main()
{
    if (dist < 0.0) discard;

    vec4 skyColor = vec4(1);
    vec4 skyGlow = vec4(1);
    float sealevelOffsetFactor = 1.0;

    getSkyColorAt(worldPos.xyz, sunPosition, sealevelOffsetFactor, clamp(dayLight, 0, 1), horizonFog, skyColor, skyGlow);

    outColor = skyColor * (dayLight - (0.14 * (1-dist)));
    outGlow = skyGlow * dist;

    // Darker tint based on distance.
    //outColor.rgb *= 0.7 + (dist * 0.3);

	outColor.rgb = mix(outColor.rgb, rgbaFog.rgb, fogAmountf);

#if SSAOLEVEL > 0
	outGPosition = vec4(0);
	outGNormal = vec4(0);
#endif

}
