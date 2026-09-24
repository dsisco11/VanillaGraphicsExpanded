using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Exposes the existing capture identity table to the lighting publication owner.</summary>
internal sealed partial class LumonSceneFeedbackUpdateRenderer
{
    #region Lighting identity access
    /// <summary>Returns render-thread chunk identities shared with page capture.</summary>
    internal GpuShaderStorageBuffer? LightingSlots => slotInfoBuffer?.Ssbo;
    /// <summary>Exposes slot reuse generations so lighting cannot inherit a recycled page's history.</summary>
    internal System.ReadOnlySpan<ushort> LightingSlotGenerations => slotGenerations;
    #endregion
}
