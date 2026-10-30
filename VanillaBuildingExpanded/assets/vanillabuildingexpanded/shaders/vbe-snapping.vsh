#version 330 core

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in int renderFlagsIn;

out vec3 localPosition;
out vec3 surfaceNormal;

#include vbesnappingposition.ash

void main() {
    localPosition = vertexPositionIn;
    surfaceNormal = (renderFlagsIn & NormalBitMask) != 0 ? unpackNormal(renderFlagsIn) : vec3(0.0);
    int terrainPass = renderFlagsIn & 0xFF;
    gl_Position = nativePosition(vertexPositionIn, float((renderFlagsIn & ZOffsetBitMask) >> 8), terrainPass);
}
