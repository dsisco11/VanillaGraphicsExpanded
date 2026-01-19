using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

internal readonly record struct LumOnWorldProbeUpdateRequest(
    int Level,
    Vec3i LocalIndex,
    Vec3i StorageIndex,
    int StorageLinearIndex);
