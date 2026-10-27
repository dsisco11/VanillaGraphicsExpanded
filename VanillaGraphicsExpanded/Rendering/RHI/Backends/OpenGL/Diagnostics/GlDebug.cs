namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Provides cached support and explicit diagnostic policy to backend modules.</summary>
internal static partial class GlDebug
{
    #region Public API
    /// <summary>Enables per-command raster state error checks; disabled by default in every build.</summary>
    /// <remarks>Checked boundary restoration owns its checks independently and suppresses nested diagnostics.</remarks>
    public static bool CheckStateTransitions { get; set; }
    #endregion

    #region Private
    /// <summary>Checks optional labeling support without letting diagnostic setup fail rendering.</summary>
    private static bool SupportsKhrDebug()
    {
        try { return GpuSupport.Graphics.SupportsKhrDebug; }
        catch { return false; }
    }
    #endregion
}
