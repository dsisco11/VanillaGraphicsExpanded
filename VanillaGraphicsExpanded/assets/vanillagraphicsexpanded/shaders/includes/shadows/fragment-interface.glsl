// Fragment material rejection precedes writes through this explicit virtual-pool interface.
flat in uint vge_shadowView;
uniform usamplerBuffer shadowViews;
layout(std430) readonly buffer ShadowWriteEntries { uint shadowWriteEntries[]; };
layout(std430) readonly buffer ShadowWritableAssignments { uvec4 shadowWritableAssignments[]; };
layout(r32ui) coherent uniform uimage2D shadowBank0;
layout(r32ui) coherent uniform uimage2D shadowBank1;
layout(r32ui) coherent uniform uimage2D shadowBank2;
layout(r32ui) coherent uniform uimage2D shadowBank3;
