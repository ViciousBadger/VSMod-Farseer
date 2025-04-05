#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

in vec3 worldPosf;
in vec4 rgbaMain;
in vec4 rgbaFog;
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
    if (rgbaMain.a < 0.05) discard;

    outColor = rgbaMain;

    // Apply sky glow, similarly to with clouds, because the distant atmosphere
    // should be colored by sun
    float sealevelOffsetFactor = 0.25;
    vec4 skyGlow = getSkyGlowAt(vec3(worldPosf.x, worldPosf.y+100,worldPosf.z), sunPosition, sealevelOffsetFactor, clamp(dayLight, 0, 1), horizonFog, 0.7);

    outColor.rgb *= mix(vec3(1), 1.2 * skyGlow.rgb, skyGlow.a);
    outColor.rgb *= max(1, 0.9 + skyGlow.a/10);

    float baseBloom = max(0, 0.25 - fogAmountf/2);
#if BLOOM == 1
 	outColor.rgb *= 1 - baseBloom;
#endif

	outColor.rgb = mix(outColor.rgb, rgbaFog.rgb, fogAmountf);

    float murkiness = max(0, getSkyMurkiness() - 14*fogDensityIn);
	outColor.rgb = applyUnderwaterEffects(outColor.rgb, murkiness);

    outGlow.y *= clamp((dayLight - 0.05) * 2 - 50*murkiness, 0, 1);

    outColor.rgb += vec3(0.1, 0.5, 0.1) * nightVisionStrengthv;

    // haxyFade
    if (outColor.a < 0.999) {
        vec4 skyColor = vec4(1);
        vec4 skyGlow = vec4(1);

        getSkyColorAt(worldPosf, sunPosition, sealevelOffsetFactor, clamp(dayLight, 0, 1), horizonFog, skyColor, skyGlow);
        outColor.rgb = mix(skyColor.rgb, outColor.rgb, max(1-dayLight, max(0, rgbaMain.a)));
    }

#if SSAOLEVEL > 0
	outGPosition = vec4(0);
	outGNormal = vec4(0);
#endif

}
