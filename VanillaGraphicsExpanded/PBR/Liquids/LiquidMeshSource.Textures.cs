using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Admits persistent texture recipients for the current engine liquid source without acquiring ownership.</summary>
internal sealed partial class LiquidMeshSource
{
    private BorrowedTexture?[] atlasInputs = [];
    private BorrowedTexture? primaryDepthInput, shadowNearInput, shadowFarInput;
    private bool disposed;

    #region Internal API
    /// <summary>Lends engine visibility depth without changing receiver-separation policy.</summary>
    internal GpuTextureRef PrimaryDepth => primaryDepthInput;
    /// <summary>Lends the optional near shadow allocation.</summary>
    internal GpuTextureRef ShadowNear => shadowNearInput;
    /// <summary>Lends the optional far shadow allocation.</summary>
    internal GpuTextureRef ShadowFar => shadowFarInput;
    /// <summary>Admits current external allocations before any liquid boundary begins.</summary>
    internal void PrepareTextureInputs(ICoreClientAPI api, int[] atlases)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        OpenGlStateCache.Current.RequireOutsideEngineBoundary();
        // Atlas names may change independently of pool count. Keep each original association
        // until replacement and let the common manager certify surviving metadata.
        if (atlasInputs.Length != atlases.Length)
        {
            for (int index = atlases.Length; index < atlasInputs.Length; index++) atlasInputs[index]?.Dispose();
            Array.Resize(ref atlasInputs, atlases.Length);
        }
        for (int index = 0; index < atlases.Length; index++)
            ReplaceInput(ref atlasInputs[index], atlases[index]);
        var frames = api.Render.FrameBuffers;
        ReplaceInput(ref primaryDepthInput, frames[(int)EnumFrameBuffer.Primary]?.DepthTextureId ?? 0);
        ReplaceInput(ref shadowNearInput, frames[(int)EnumFrameBuffer.ShadowmapNear]?.DepthTextureId ?? 0);
        ReplaceInput(ref shadowFarInput, frames[(int)EnumFrameBuffer.ShadowmapFar]?.DepthTextureId ?? 0);
    }
    /// <summary>Lends a previously admitted atlas; pool callbacks perform no metadata acquisition.</summary>
    internal GpuTextureRef AtlasTexture(int index)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return atlasInputs[index];
    }
    #endregion

    #region Public API
    /// <summary>Retires recipients while leaving every engine texture's native ownership unchanged.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var atlas in atlasInputs) atlas?.Dispose();
        atlasInputs = [];
        primaryDepthInput?.Dispose(); primaryDepthInput = null;
        shadowNearInput?.Dispose(); shadowNearInput = null;
        shadowFarInput?.Dispose(); shadowFarInput = null;
    }
    #endregion

    #region Private
    /// <summary>Withdraws absent optional inputs and otherwise preserves the original live manager association.</summary>
    private static void ReplaceInput(ref BorrowedTexture? input, int nativeName)
    {
        if (nativeName > 0) BorrowedTexture.GetOrReplace(ref input, nativeName);
        else { input?.Dispose(); input = null; }
    }
    #endregion
}
