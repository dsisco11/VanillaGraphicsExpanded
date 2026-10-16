#version 330 core
#extension GL_ARB_separate_shader_objects : require
/** Generates a fullscreen triangle without native geometry or temporal inputs. */
void main()
{
    vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);
    gl_Position=vec4(p*2.0-1.0,0,1);
}
