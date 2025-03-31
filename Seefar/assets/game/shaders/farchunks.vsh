#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

// per-vertex
layout(location = 0) in vec3 vertexPositionIn;

// layout(location = 1) in vec2 uvIn;
// layout(location = 2) in vec4 colorIn;
// layout(location = 3) in int flags;

// custom shorts
layout(location = 1) in vec2 coord;
layout(location = 2) in float heightmap[4];

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

void main()
{
    // vec4 worldPos = vec4(coord.x * 32.0, 256.0, coord.y * 32.0, 0) + vec4(vertexPositionIn, 0);
    vec4 worldPos = vec4(0, 256.0, 0, 0) + vec4(vertexPositionIn, 0);
    vec4 camPos = modelViewMatrix * worldPos;

    gl_Position = projectionMatrix * camPos;
}
