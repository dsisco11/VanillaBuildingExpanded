#version 330 core

uniform vec4 colorIn;

#include oit.fsh

void main() {
    OIT(colorIn, 0.0);
}
