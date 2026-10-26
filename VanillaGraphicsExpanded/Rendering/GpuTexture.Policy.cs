namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains wrapper allocation policy independently of canonical image metadata.</summary>
public abstract partial class GpuTexture
{
    #region Internal API
    /// <summary>Supplies native-free sampling policy before the creation transaction starts.</summary>
    internal void SetAllocationPolicy(TextureFilterMode filter, string? label)
    {
        filterMode = filter;
        debugName = label;
    }
    #endregion
}
