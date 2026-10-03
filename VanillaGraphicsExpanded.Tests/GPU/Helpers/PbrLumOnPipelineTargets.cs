using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Owns pipeline attachment instances and the framebuffers that borrow their images.</summary>
internal sealed class PbrLumOnPipelineTargets : IDisposable
{
    private bool _isDisposed;
    private readonly GpuResourceCollection _resources = new();

    #region Public API
    #region Construction
    /// <summary>Allocates owning attachments and framebuffer bindings for the complete lighting pipeline.</summary>
    public PbrLumOnPipelineTargets()
    {
        int screenW = LumOnTestInputFactory.ScreenWidth;
        int screenH = LumOnTestInputFactory.ScreenHeight;

        int halfW = LumOnTestInputFactory.HalfResWidth;
        int halfH = LumOnTestInputFactory.HalfResHeight;

        int probeW = LumOnTestInputFactory.ProbeGridWidth;
        int probeH = LumOnTestInputFactory.ProbeGridHeight;

        int atlasW = LumOnTestInputFactory.OctahedralAtlasWidth;
        int atlasH = LumOnTestInputFactory.OctahedralAtlasHeight;

        // Reclaim every completed allocation if later target creation fails.
        try
        {
            // PBR Direct MRT outputs
            DirectLightingMrt = _resources.Own(GpuFramebuffer.Create(
                new[]
                {
                    _resources.Own(new GpuFramebufferAttachment(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "Test.DirectDiffuse")),
                    _resources.Own(new GpuFramebufferAttachment(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "Test.DirectSpecular")),
                    _resources.Own(new GpuFramebufferAttachment(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "Test.Emissive")),
                },
                depth: null,
                debugName: "Test.PbrDirectMrt"));

            // Velocity (RGBA32F)
            Velocity = _resources.Own(GpuFramebuffer.Create(
                new[]
                {
                    _resources.Own(new GpuFramebufferAttachment(screenW, screenH, PixelInternalFormat.Rgba32f, debugName: "Test.Velocity")),
                },
                depth: null,
                debugName: "Test.VelocityFbo"));

            // HZB depth pyramid: 4x4 -> 2x2 -> 1x1
            Hzb = new HzbTestPyramid(screenW, screenH, mipLevels: 3);

            // Probe anchors (2x2)
            ProbeAnchor = _resources.Own(GpuFramebuffer.Create(
                new[]
                {
                    _resources.Own(new GpuFramebufferAttachment(probeW, probeH, PixelInternalFormat.Rgba16f, debugName: "Test.ProbeAnchorPosition")),
                    _resources.Own(new GpuFramebufferAttachment(probeW, probeH, PixelInternalFormat.Rgba16f, debugName: "Test.ProbeAnchorNormal")),
                },
                depth: null,
                debugName: "Test.ProbeAnchorMrt"));

            // Atlas MRTs (16x16): radiance RGBA16F + meta RG32F
            AtlasTrace = _resources.Own(GpuFramebuffer.Create(
                new[]
                {
                    _resources.Own(new GpuFramebufferAttachment(atlasW, atlasH, PixelInternalFormat.Rgba16f, debugName: "Test.AtlasTraceRadiance")),
                    _resources.Own(new GpuFramebufferAttachment(atlasW, atlasH, PixelInternalFormat.Rg32f, debugName: "Test.AtlasTraceMeta")),
                },
                depth: null,
                debugName: "Test.AtlasTraceMrt"));

            AtlasTemporal = _resources.Own(GpuFramebuffer.Create(
                new[]
                {
                    _resources.Own(new GpuFramebufferAttachment(atlasW, atlasH, PixelInternalFormat.Rgba16f, debugName: "Test.AtlasTemporalRadiance")),
                    _resources.Own(new GpuFramebufferAttachment(atlasW, atlasH, PixelInternalFormat.Rg32f, debugName: "Test.AtlasTemporalMeta")),
                },
                depth: null,
                debugName: "Test.AtlasTemporalMrt"));

            AtlasFiltered = _resources.Own(GpuFramebuffer.Create(
                new[]
                {
                    _resources.Own(new GpuFramebufferAttachment(atlasW, atlasH, PixelInternalFormat.Rgba16f, debugName: "Test.AtlasFilteredRadiance")),
                    _resources.Own(new GpuFramebufferAttachment(atlasW, atlasH, PixelInternalFormat.Rg32f, debugName: "Test.AtlasFilteredMeta")),
                },
                depth: null,
                debugName: "Test.AtlasFilteredMrt"));

            // Gather output (half-res)
            IndirectHalf = _resources.Own(GpuFramebuffer.Create(
                new[]
                {
                    _resources.Own(new GpuFramebufferAttachment(halfW, halfH, PixelInternalFormat.Rgba16f, debugName: "Test.IndirectHalf")),
                },
                depth: null,
                debugName: "Test.IndirectHalfFbo"));

            // Upsample output (full-res)
            IndirectFull = _resources.Own(GpuFramebuffer.Create(
                new[]
                {
                    _resources.Own(new GpuFramebufferAttachment(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "Test.IndirectFull")),
                },
                depth: null,
                debugName: "Test.IndirectFullFbo"));

            // Final composite output
            Composite = _resources.Own(GpuFramebuffer.Create(
                new[]
                {
                    _resources.Own(new GpuFramebufferAttachment(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "Test.Composite")),
                },
                depth: null,
                debugName: "Test.CompositeFbo"));
        }
        catch
        {
            try { Hzb?.Dispose(); }
            finally { _resources.Dispose(); }
            throw;
        }
    }

    #endregion

    #region Targets
    public GpuFramebuffer DirectLightingMrt { get; }

    public GpuFramebuffer Velocity { get; }

    public HzbTestPyramid Hzb { get; }

    public GpuFramebuffer ProbeAnchor { get; }

    public GpuFramebuffer AtlasTrace { get; }

    public GpuFramebuffer AtlasTemporal { get; }

    public GpuFramebuffer AtlasFiltered { get; }

    public GpuFramebuffer IndirectHalf { get; }

    public GpuFramebuffer IndirectFull { get; }

    public GpuFramebuffer Composite { get; }

    #endregion

    #region Cleanup
    /// <summary>Retires framebuffers and queues owned attachment storage for fixture cleanup.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        // The collection retires FBOs first, then queues their owned attachment storage.
        try { Hzb.Dispose(); }
        finally { _resources.Dispose(); }
    }

    #endregion
    #endregion
}
