#version 330 core

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in int renderFlagsIn;
layout(location = 2) in vec3 glyphDataIn;
layout(location = 3) in vec3 glyphNormalIn;
layout(location = 4) in vec3 sourcePositionAIn;
layout(location = 5) in vec3 sourcePositionBIn;
layout(location = 6) in vec3 sourcePositionCIn;
layout(location = 7) in vec3 sourceWeightsIn;
layout(location = 8) in vec3 sourceOffsetsIn;

out vec3 localPosition;
out vec3 surfaceNormal;
flat out vec2 glyphDirections;
out float glyphCoverage;

#include vbesnappingposition.ash

void main() {
    localPosition = vertexPositionIn;
    surfaceNormal = glyphNormalIn;
    glyphDirections = glyphDataIn.xy;
    glyphCoverage = glyphDataIn.z;
    int terrainPass = renderFlagsIn & 0xFF;
    gl_Position = nativePosition(sourcePositionAIn, sourceOffsetsIn.x, terrainPass) * sourceWeightsIn.x
                + nativePosition(sourcePositionBIn, sourceOffsetsIn.y, terrainPass) * sourceWeightsIn.y
                + nativePosition(sourcePositionCIn, sourceOffsetsIn.z, terrainPass) * sourceWeightsIn.z;
}
