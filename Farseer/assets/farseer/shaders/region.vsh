#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPositionIn;

uniform mat4 modelMatrix;
uniform mat4 viewMatrix;
uniform mat4 projectionMatrix;

uniform float viewDistance;

uniform vec4 horizonColorDay; 
uniform vec4 horizonColorNight; 
uniform vec4 fogColor; 
uniform float dayLight; 

out vec4 color;

void main()
{
    vec4 worldPos = modelMatrix * vec4(vertexPositionIn, 1.0);

    color = mix(horizonColorNight, horizonColorDay, dayLight);
    color *= fogColor;
    color.rgb -= clamp(((viewDistance + 1024) - length(worldPos.xz)) / viewDistance, 0.0, 1.0) * 0.1;
    color.a *= 1.0 - clamp(20 * (1.2 - length(worldPos.xz) / viewDistance) - 5, -1, 1);

    vec4 camPos = viewMatrix * worldPos;

    gl_Position = projectionMatrix * camPos;
}
