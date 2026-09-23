using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Binds borrowed cache resources for immediate render-thread hit evaluation.</summary>
internal sealed class SurfaceLightingBindings : IDisposable
{
    private readonly GpuUniformBuffer parameters = GpuUniformBuffer.Create(debugName: "SurfaceLighting.Consumer");
    private readonly byte[] bytes = new byte[96];

    #region Consumer binding
    /// <summary>Publishes a zero-sized domain when unavailable, keeping valid darkness distinct from missing data.</summary>
    public void Bind(SurfaceLightingSnapshot? snapshot)
    {
        Array.Clear(bytes);
        if (snapshot is { } value)
        {
            UboPacking.WriteUVec4(bytes, 0, (uint)value.TileSize, (uint)value.TilesPerAxis, (uint)value.TilesPerAtlas, 0);
            UboPacking.WriteIVec4(bytes, 32, value.Origin.X, value.Origin.Y, value.Origin.Z, 0);
            UboPacking.WriteIVec4(bytes, 48, value.Dimensions.X, value.Dimensions.Y, value.Dimensions.Z, 0);
            UboPacking.WriteIVec4(bytes, 64, value.Ring.X, value.Ring.Y, value.Ring.Z, 0);
            value.Patches.BindBase(1); value.Slots.BindBase(2); value.Readiness.BindBase(3);
        }
        parameters.UploadOrResize(bytes, growExponentially: false);
        parameters.BindBase(GpuBindingRegistry.Ubo.Lights);
        GlStateCache.Current.BindTexture(TextureTarget.Texture2DArray, 16, snapshot?.Material.TextureId ?? 0);
        GlStateCache.Current.BindTexture(TextureTarget.Texture2DArray, 17, snapshot?.OutgoingRadiance.TextureId ?? 0);
        GlStateCache.Current.BindTexture(TextureTarget.Texture2DArray, 18, snapshot?.PageTable.TextureId ?? 0);
        for (int unit = 16; unit <= 18; unit++) GpuSamplers.NearestClamp.Bind(unit);
    }
    #endregion

    #region Lifetime
    /// <summary>Releases only the consumer-owned parameter buffer.</summary>
    public void Dispose() => parameters.Dispose();
    #endregion
}
