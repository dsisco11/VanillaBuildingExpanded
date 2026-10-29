#version 330 core

layout(location = 0) in vec3 vertexPositionIn;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform float depthBias;

out vec3 localPosition;

void main() {
    localPosition = vertexPositionIn;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertexPositionIn, 1.0);
    gl_Position.z -= depthBias * gl_Position.w;
}
