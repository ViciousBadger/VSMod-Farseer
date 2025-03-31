#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

in float distanceFromCamera;

uniform vec3 color; 
uniform int viewDistance;

layout(location = 0) out vec4 outColor;

void main()
{
    // float fade = mix(0.0, 1.0, distanceFromCamera / viewDistance);
    float fade = 0.3;
    vec4 col = vec4(color, fade);
    outColor = col;
}
