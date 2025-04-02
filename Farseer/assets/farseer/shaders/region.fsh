#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

in vec4 color;

layout(location = 0) out vec4 outColor;

void main()
{
    outColor = color;
    if (outColor.a < 0.1) discard;
}
