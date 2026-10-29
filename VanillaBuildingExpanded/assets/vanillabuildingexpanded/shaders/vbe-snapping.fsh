#version 330 core

in vec3 localPosition;
uniform vec3 selectedNormal;
uniform vec3 axisU;
uniform vec3 axisV;
uniform vec2 appliedDirections;
uniform vec4 colorIn;
uniform float markingWidth;

#include oit.fsh

float directionalBand(float coordinate, float direction, float pixelWidth) {
    float distance = direction * (coordinate - 0.5) - 0.15;
    if (direction == 0.0 || distance <= 0.0) return 0.0;
    return smoothstep(0.0, max(markingWidth, pixelWidth), distance);
}

void main() {
    vec3 normal = normalize(cross(dFdx(localPosition), dFdy(localPosition)));
    if (!gl_FrontFacing) normal = -normal;
    vec2 uv = vec2(dot(localPosition, axisU), dot(localPosition, axisV));
    vec2 pixelWidth = fwidth(uv);
    float mask = max(directionalBand(uv.x, appliedDirections.x, pixelWidth.x),
                     directionalBand(uv.y, appliedDirections.y, pixelWidth.y));
    if (dot(normal, selectedNormal) <= 0.0 || mask <= 0.0) discard;
    OIT(vec4(colorIn.rgb, colorIn.a * mask), 0.0);
}
