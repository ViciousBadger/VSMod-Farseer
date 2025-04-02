#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

// per-vertex
layout(location = 0) in vec3 vertexPositionIn;

// custom data
layout(location = 1) in vec2 coord;
layout(location = 2) in vec4 heightmap;

uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;
uniform mat4 modelMatrix;

uniform float viewDistance;

uniform vec4 horizonColorDay; 
uniform vec4 horizonColorNight; 
uniform vec4 fogColor; 
uniform float dayLight; 

out vec4 color;

void main()
{
    // vec4 worldPos = vec4(coord.x * 32.0, 256.0, coord.y * 32.0, 0) + vec4(vertexPositionIn, 0);
    float height = heightmap.x;
    if (vertexPositionIn.x > 0.0 && vertexPositionIn.z > 0.0) {
        height = heightmap.w;
    } else if (vertexPositionIn.x > 0.0) {
        height = heightmap.y;
    } else if (vertexPositionIn.z > 0.0) {
        height = heightmap.z;
    }

    vec3 chunkOffset = vec3(coord.x * 32.0, height, coord.y * 32.0);

    vec4 worldPos = modelMatrix * vec4(chunkOffset + vertexPositionIn, 1.0);

    vec4 camPos = viewMatrix * worldPos;

    color = mix(horizonColorNight, horizonColorDay, dayLight);
    color *= fogColor;
    color.rgb -= clamp(((viewDistance + 1024) - length(worldPos.xz)) / viewDistance, 0.0, 1.0) * 0.1;
    color.a *= 1.0 - clamp(20 * (1.2 - length(worldPos.xz) / viewDistance) - 5, -1, 1);

    gl_Position = projectionMatrix * camPos;
}
