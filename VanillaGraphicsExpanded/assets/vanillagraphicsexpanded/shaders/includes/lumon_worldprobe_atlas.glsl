#ifndef LUMON_WORLDPROBE_ATLAS_GLSL
#define LUMON_WORLDPROBE_ATLAS_GLSL

// World-probe atlas constants.
//
// IMPORTANT:
// - Screen-probe atlas uses LUMON_OCTAHEDRAL_SIZE (8) in lumon_octahedral.glsl.
// - World-probe atlas is intentionally separate and may be higher resolution.

// Default: higher-res than screen probes (UE5 Lumen-like for world probes).
#ifndef VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE
	#define VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE 16
#endif

// Texels traced/uploaded per probe update (direction slicing). Used by the upcoming
// atlas-based world-probe update path.
#ifndef VGE_LUMON_WORLDPROBE_ATLAS_TEXELS_PER_UPDATE
	#define VGE_LUMON_WORLDPROBE_ATLAS_TEXELS_PER_UPDATE 32
#endif

// Keep both a macro and typed consts for convenience.
#define LUMON_WORLDPROBE_OCTAHEDRAL_SIZE VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE
const int LUMON_WORLDPROBE_OCTAHEDRAL_SIZE_I = VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE;
const float LUMON_WORLDPROBE_OCTAHEDRAL_SIZE_F = float(VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE);

const int LUMON_WORLDPROBE_ATLAS_TEXELS_PER_UPDATE_I = VGE_LUMON_WORLDPROBE_ATLAS_TEXELS_PER_UPDATE;

#endif

