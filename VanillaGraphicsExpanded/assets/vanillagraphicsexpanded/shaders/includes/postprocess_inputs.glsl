#extension GL_ARB_shading_language_420pack : require
layout(std140, binding = 28) uniform PostprocessInputs { vec4 passInfo; vec4 effect; vec4 sun; vec4 solar; };
