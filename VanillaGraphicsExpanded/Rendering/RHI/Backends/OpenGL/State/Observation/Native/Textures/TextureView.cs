using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureView after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureView")]
    internal static void ObserveTextureView(uint a0, int a1, uint a2, int a3, uint a4, uint a5, uint a6, uint a7)
    {
        int @texture = unchecked((int)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        int @origtexture = unchecked((int)a2);
        PixelInternalFormat @internalformat = unchecked((PixelInternalFormat)a3);
        int @minlevel = unchecked((int)a4);
        uint @numlevels = a5;
        int @minlayer = unchecked((int)a6);
        uint @numlayers = a7;
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var destination = manager.FindObservedResource(GpuResourceKind.Texture, texture) as GpuResourceMetadataEntry<GpuTextureMetadata>;
        var source = manager.FindObservedResource(GpuResourceKind.Texture, origtexture) as GpuResourceMetadataEntry<GpuTextureMetadata>;
        if (destination is null || destination.IsRealized || source?.IsUsable != true || !source.Metadata.ImmutableStorage
            || minlevel < 0 || numlevels == 0 || minlayer < 0 || numlayers == 0 || !TryImageTarget((int)target, out var dimension)) return;
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext) { destination.BeginRefresh(GpuMetadataChange.Storage); return; }
        int availableLevels = (source.Metadata.View?.LevelCount ?? source.Metadata.ImmutableLevels) - minlevel;
        int availableLayers = (source.Metadata.View?.LayerCount ?? 1) - minlayer;
        if (availableLevels <= 0 || availableLayers <= 0) return;
        if (dimension is TextureDimension.One or TextureDimension.Two or TextureDimension.Three or TextureDimension.Rectangle or TextureDimension.TwoMultisample
            && numlayers != 1) return;
        int selectedLevels = (int)Math.Min(numlevels, (uint)availableLevels);
        int selectedLayers = (int)Math.Min(numlayers, (uint)availableLayers);
        var request = new GpuTextureViewInitialization { Source = origtexture, Target = dimension,
            Format = OpenGLImageValues.Format(internalformat), BaseLevel = minlevel, LevelCount = selectedLevels,
            BaseLayer = minlayer, LayerCount = selectedLayers };
        GpuTextureMetadata candidate;
        try { candidate = TextureViewDescriptor.Create(request, source.Metadata, new RHICapabilities(GpuSupport.Graphics)); }
        catch (ArgumentException) { return; }
        cache.InterruptNativeResourceOperation(GpuResourceKind.Texture, texture);
        if (InterruptNativeMetadataCompletion(destination, GpuMetadataChange.Storage)) return;
        manager.RecordNativeTextureView(destination, source);
        manager.PublishMetadata(destination, candidate, GpuMetadataChange.Storage);
    }
    #endregion
}
