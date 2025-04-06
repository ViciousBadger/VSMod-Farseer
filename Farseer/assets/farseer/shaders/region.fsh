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

uniform float skyTint;

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

    // Sample sky with special parameters for terrain color.
    vec4 terraColor = vec4(1);
    vec4 terraGlow = vec4(1);
    float sealevelOffsetFactor = skyTint;
    getSkyColorAt(worldPos.xyz, sunPosition, sealevelOffsetFactor, clamp(dayLight, 0, 1), horizonFog, terraColor, terraGlow);

    // Approximate the *real* sky color for a nice fade.
    vec4 skyColor = vec4(1);
    vec4 skyGlow = vec4(1);
    float sealevelOffsetFactorSky = 0.25;
    vec3 worldPosInSky = normalize(worldPos.xyz) * 250.0;
    getSkyColorAt(worldPosInSky, sunPosition, sealevelOffsetFactorSky, clamp(dayLight, 0, 1), horizonFog, skyColor, skyGlow);

    float murkiness = max(0, getSkyMurkiness() - 14*fogDensityIn);
	skyColor.rgb = applyUnderwaterEffects(skyColor.rgb, murkiness);
    skyGlow.y *= clamp((dayLight - 0.05) * 2 - 50*murkiness, 0, 1);

    terraColor *= dayLight - (0.14 * (1-dist));
	terraColor.rgb = mix(terraColor.rgb, rgbaFog.rgb, fogAmountf);
    terraGlow *= dist;

    float fade = min(1.0, dist * dist);
    //float fade = 1.0;
    outColor = mix(terraColor, skyColor, fade);
    outGlow = mix(terraGlow, skyGlow, fade);

    // Darker tint based on distance.
    //outColor.rgb *= 0.7 + (dist * 0.3);


#if SSAOLEVEL > 0
	outGPosition = vec4(0);
	outGNormal = vec4(0);
#endif

}
