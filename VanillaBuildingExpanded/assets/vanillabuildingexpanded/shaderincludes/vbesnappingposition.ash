uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform float depthBias;

#include vertexflagbits.ash

vec4 nativePosition(vec3 vertex, float zOffset, int terrainPass) {
    vec4 position = projectionMatrix * modelViewMatrix * vec4(vertex, 1.0);
    if (terrainPass == 4) {
        position.w += 0.0008 / max(0.1, position.z);
    } else if (terrainPass == 3 || terrainPass == 6) {
        if (position.z > 0.0)
            position.w += zOffset * 0.00025 / max(3.0, position.z * 0.05);
    } else if (terrainPass != 5 && position.z > -1.0) {
        position.w += zOffset * 0.00025 / ((position.z + 3.0) * 0.05);
    }
    position.z -= depthBias * position.w;
    return position;
}
