#version 330 core

layout(location = 0) in vec3 vertexPositionIn;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

void main() {
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertexPositionIn, 1.0);
    gl_Position.w += 0.0014;
}
