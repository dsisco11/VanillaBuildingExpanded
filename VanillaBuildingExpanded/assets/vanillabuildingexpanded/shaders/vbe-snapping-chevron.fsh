#version 330 core

in vec3 localPosition;
in vec3 surfaceNormal;
flat in vec2 glyphDirections;
in float glyphCoverage;
uniform vec3 selectedNormal;
uniform vec3 axisU;
uniform vec3 axisV;
uniform vec2 resolvedDirections;
uniform vec4 chevronColor;

#include oit.fsh

void main() {
    vec3 normal = normalize(cross(dFdx(localPosition), dFdy(localPosition)));
    if (!gl_FrontFacing) normal = -normal;
    if (dot(surfaceNormal, surfaceNormal) > 0.0) normal = normalize(surfaceNormal);
    if (dot(normal, selectedNormal) <= 0.0) discard;
    vec2 uv = vec2(dot(localPosition, axisU), dot(localPosition, axisV));
    bool active = all(equal(glyphDirections, resolvedDirections));
    vec4 color = active ? chevronColor : vec4(1.0, 0.5, 0.0, 0.2);
    vec2 edgeDistance = min(uv, vec2(1.0) - uv);
    float fade = smoothstep(0.0, 0.15, min(edgeDistance.x, edgeDistance.y));
    float alpha = color.a * glyphCoverage * fade;
    if (alpha <= 0.0) discard;
    OIT(vec4(color.rgb, alpha), 0.0);
}
