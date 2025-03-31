#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

// per-vertex
layout(location = 0) in vec3 vertexPositionIn;

// custom data
layout(location = 1) in vec2 coord;
layout(location = 2) in vec4 heightmap;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

out float distanceFromCamera;

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
    vec4 worldPos = vec4(chunkOffset + vertexPositionIn, 1.0);
    vec4 camPos = modelViewMatrix * worldPos;

    distanceFromCamera = length(worldPos.xyz);

    gl_Position = projectionMatrix * camPos;
}
