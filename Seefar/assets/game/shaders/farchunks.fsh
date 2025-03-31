#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) out vec4 outColor;

void main()
{
    vec4 col = vec4(0.0, 1.0, 0.0, 1.0);
    outColor = col;
}
