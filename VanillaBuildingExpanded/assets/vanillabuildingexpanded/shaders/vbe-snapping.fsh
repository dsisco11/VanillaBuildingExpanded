#version 330 core

in vec3 localPosition;
in vec3 surfaceNormal;
uniform vec3 selectedNormal;
uniform vec3 axisU;
uniform vec3 axisV;
uniform vec2 resolvedDirections;
uniform vec4 colorIn;
uniform float centerThreshold;

#include oit.fsh

vec2 regionBounds(float direction) {
    if (direction < 0.0) return vec2(0.0, 0.5 - centerThreshold);
    if (direction > 0.0) return vec2(0.5 + centerThreshold, 1.0);
    return vec2(0.5 - centerThreshold, 0.5 + centerThreshold);
}

void main() {
    vec3 normal = normalize(cross(dFdx(localPosition), dFdy(localPosition)));
    if (!gl_FrontFacing) normal = -normal;
    if (dot(surfaceNormal, surfaceNormal) > 0.0) normal = normalize(surfaceNormal);
    if (dot(normal, selectedNormal) <= 0.0) discard;
    vec2 uv = vec2(dot(localPosition, axisU), dot(localPosition, axisV));
    vec2 boundsU = regionBounds(resolvedDirections.x);
    vec2 boundsV = regionBounds(resolvedDirections.y);
    if (uv.x < boundsU.x || uv.x > boundsU.y || uv.y < boundsV.x || uv.y > boundsV.y) discard;
    vec2 center = vec2(boundsU.x + boundsU.y, boundsV.x + boundsV.y) * 0.5;
    float halfSize = min(boundsU.y - boundsU.x, boundsV.y - boundsV.x) * 0.5 - 0.015;
    float radius = min(0.045, halfSize);
    vec2 offset = abs(uv - center) - vec2(halfSize - radius);
    float distance = length(max(offset, vec2(0.0))) + min(max(offset.x, offset.y), 0.0) - radius;
    float feather = max(0.05, max(fwidth(uv.x), fwidth(uv.y)));
    float alpha = colorIn.a * (1.0 - smoothstep(-feather, 0.0, distance));
    if (alpha <= 0.0) discard;
    OIT(vec4(colorIn.rgb, alpha), 0.0);
}
