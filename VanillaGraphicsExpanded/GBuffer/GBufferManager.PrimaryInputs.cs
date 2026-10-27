using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded;

/// <summary>Publishes persistent consumer references at the engine primary framebuffer's existing lifecycle boundary.</summary>
public sealed partial class GBufferManager
{
    private BorrowedTexture? primaryColorInput;
    private BorrowedTexture? primaryDepthInput;

    #region Public API
    /// <summary>Lends the current primary color allocation without exposing its ownership.</summary>
    public GpuTextureRef PrimaryColorTexture => primaryColorInput;
    /// <summary>Lends the engine visibility depth allocation independently of corrected material depth.</summary>
    public GpuTextureRef PrimaryDepthTexture => primaryDepthInput;
    #endregion

    #region Private
    /// <summary>Reuses certified original associations and reacquires only replacement or unavailable external storage.</summary>
    private void PublishPrimaryInputs(FrameBufferRef primary)
    {
        // Native names enter only at the engine publication boundary. Shader capture retains
        // these wrappers rather than discovering storage from an integer during each submission.
        int color = primary.ColorTextureIds.Length == 0 ? 0 : primary.ColorTextureIds[0];
        if (color > 0) BorrowedTexture.GetOrReplace(ref primaryColorInput, color);
        else { primaryColorInput?.Dispose(); primaryColorInput = null; }
        if (primary.DepthTextureId > 0) BorrowedTexture.GetOrReplace(ref primaryDepthInput, primary.DepthTextureId);
        else { primaryDepthInput?.Dispose(); primaryDepthInput = null; }
    }

    /// <summary>Retires managed recipients without deleting the engine's texture storage.</summary>
    private void RetirePrimaryInputs()
    {
        primaryColorInput?.Dispose(); primaryColorInput = null;
        primaryDepthInput?.Dispose(); primaryDepthInput = null;
    }
    #endregion
}
