using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

/// <summary>A scheduled storage slot with a unique admission ticket guarding delayed completion.</summary>
internal readonly record struct LumOnWorldProbeUpdateRequest(
    int Level,
    Vec3i LocalIndex,
    Vec3i StorageIndex,
    int StorageLinearIndex,
    LumOnWorldProbeImportanceFlags ImportanceFlags = LumOnWorldProbeImportanceFlags.None, long Ticket = 0);
