#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

in vec4 worldPos;
in float yLevel;
in vec4 rgbaFog;
in float dist;
in float fogAmount;
in float nightVisionStrengthv;

uniform float fogDensityIn;
uniform float fogMinIn;
uniform float horizonFog;
uniform vec3 sunPosition;
uniform float dayLight; 

uniform float skyTint;
uniform vec4 colorTint;
uniform float lightLevelBias;
uniform float fadeBias;
uniform int seaLevel;

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

float bias(float value, float bias) {
  float exp = log(0.5) / log(bias);
  return pow(value, exp);
}

void main()
{
    if (dist < 0.0 || dist > 1.0) discard;

    // Sample sky with special parameters for terrain color.
    vec4 terraColor = vec4(1);
    vec4 terraGlow = vec4(1);
    float a = seaLevel + 2;
    float b = seaLevel - 2;
    float sealevelOffsetFactor = skyTint + ((yLevel - a) / (b - a)) * (-skyTint);;
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

    //terraColor *= dayLight - (0.14 * (1-dist));
    //terraColor.rgb = mix(terraColor.rgb, colorTint.rgb, colorTint.a * (1-dist));
    terraColor.rgb = mix(terraColor.rgb, colorTint.rgb, colorTint.a);
  	//terraColor.rgb = mix(terraColor.rgb, rgbaFog.rgb, fogAmount);
    terraColor *= bias(clamp(dayLight, 0, 1), lightLevelBias);
    terraColor = applyFog(terraColor, fogAmount);
    terraColor = applySpheresFog(terraColor, fogAmount, worldPos.xyz);
    terraGlow *= dist;

    float fade = min(1.0, bias(dist, fadeBias));
    outColor = mix(terraColor, skyColor, fade);
    outGlow = mix(terraGlow, skyGlow, fade);

#if SSAOLEVEL > 0
	outGPosition = vec4(0);
	outGNormal = vec4(0);
#endif

}
