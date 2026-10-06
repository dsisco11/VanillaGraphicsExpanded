#version 430 core
@import "../includes/tests/normal-inputs.glsl"
#define VGE_TERRAIN_NORMAL_INPUTS
@import "../includes/vge_terrain_normal.glsl"
layout(location=0) in vec3 world;
layout(location=0) out vec4 result;
void main(){ vec3 authored=smoothNormal!=0?normalize(vec3(0,1,1)):vec3(0,1,0);result=vec4(VgeTerrainNormal(authored,world,transmission),1); }
