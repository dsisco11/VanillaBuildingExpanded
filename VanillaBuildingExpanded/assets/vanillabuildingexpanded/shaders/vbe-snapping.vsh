#version 330 core

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in int renderFlagsIn;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform float depthBias;

out vec3 localPosition;
out vec3 surfaceNormal;

#include vertexflagbits.ash

void main() {
    localPosition = vertexPositionIn;
    surfaceNormal = (renderFlagsIn & NormalBitMask) != 0 ? unpackNormal(renderFlagsIn) : vec3(0.0);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertexPositionIn, 1.0);
    int terrainPass = renderFlagsIn & 0xFF;
    int zOffset = (renderFlagsIn & ZOffsetBitMask) >> 8;
    if (terrainPass == 4) {
        gl_Position.w += 0.0008 / max(0.1, gl_Position.z);
    } else if (terrainPass == 3 || terrainPass == 6) {
        if (gl_Position.z > 0.0)
            gl_Position.w += float(zOffset) * 0.00025 / max(3.0, gl_Position.z * 0.05);
    } else if (terrainPass != 5 && gl_Position.z > -1.0) {
        gl_Position.w += float(zOffset) * 0.00025 / ((gl_Position.z + 3.0) * 0.05);
    }
    gl_Position.z -= depthBias * gl_Position.w;
}
