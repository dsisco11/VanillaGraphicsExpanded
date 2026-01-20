using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

internal sealed class PbrLumOnPipelineTargets : IDisposable
{
    private bool _isDisposed;

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

        // PBR Direct MRT outputs
        DirectLightingMrt = RequireGBuffer(GBuffer.CreateMRT(
            new[]
            {
                DynamicTexture2D.Create(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "Test.DirectDiffuse"),
                DynamicTexture2D.Create(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "Test.DirectSpecular"),
                DynamicTexture2D.Create(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "Test.Emissive"),
            },
            depthTexture: null,
            ownsTextures: true,
            debugName: "Test.PbrDirectMrt"));

        // Velocity (RGBA32F)
        Velocity = RequireGBuffer(GBuffer.CreateMRT(
            new[]
            {
                DynamicTexture2D.Create(screenW, screenH, PixelInternalFormat.Rgba32f, debugName: "Test.Velocity"),
            },
            depthTexture: null,
            ownsTextures: true,
            debugName: "Test.VelocityFbo"));

        // HZB depth pyramid: 4x4 -> 2x2 -> 1x1
        Hzb = new HzbTestPyramid(screenW, screenH, mipLevels: 3);

        // Probe anchors (2x2)
        ProbeAnchor = RequireGBuffer(GBuffer.CreateMRT(
            new[]
            {
                DynamicTexture2D.Create(probeW, probeH, PixelInternalFormat.Rgba16f, debugName: "Test.ProbeAnchorPosition"),
                DynamicTexture2D.Create(probeW, probeH, PixelInternalFormat.Rgba16f, debugName: "Test.ProbeAnchorNormal"),
            },
            depthTexture: null,
            ownsTextures: true,
            debugName: "Test.ProbeAnchorMrt"));

        // Atlas MRTs (16x16): radiance RGBA16F + meta RG32F
        AtlasTrace = RequireGBuffer(GBuffer.CreateMRT(
            new[]
            {
                DynamicTexture2D.Create(atlasW, atlasH, PixelInternalFormat.Rgba16f, debugName: "Test.AtlasTraceRadiance"),
                DynamicTexture2D.Create(atlasW, atlasH, PixelInternalFormat.Rg32f, debugName: "Test.AtlasTraceMeta"),
            },
            depthTexture: null,
            ownsTextures: true,
            debugName: "Test.AtlasTraceMrt"));

        AtlasTemporal = RequireGBuffer(GBuffer.CreateMRT(
            new[]
            {
                DynamicTexture2D.Create(atlasW, atlasH, PixelInternalFormat.Rgba16f, debugName: "Test.AtlasTemporalRadiance"),
                DynamicTexture2D.Create(atlasW, atlasH, PixelInternalFormat.Rg32f, debugName: "Test.AtlasTemporalMeta"),
            },
            depthTexture: null,
            ownsTextures: true,
            debugName: "Test.AtlasTemporalMrt"));

        AtlasFiltered = RequireGBuffer(GBuffer.CreateMRT(
            new[]
            {
                DynamicTexture2D.Create(atlasW, atlasH, PixelInternalFormat.Rgba16f, debugName: "Test.AtlasFilteredRadiance"),
                DynamicTexture2D.Create(atlasW, atlasH, PixelInternalFormat.Rg32f, debugName: "Test.AtlasFilteredMeta"),
            },
            depthTexture: null,
            ownsTextures: true,
            debugName: "Test.AtlasFilteredMrt"));

        // Gather output (half-res)
        IndirectHalf = RequireGBuffer(GBuffer.CreateMRT(
            new[]
            {
                DynamicTexture2D.Create(halfW, halfH, PixelInternalFormat.Rgba16f, debugName: "Test.IndirectHalf"),
            },
            depthTexture: null,
            ownsTextures: true,
            debugName: "Test.IndirectHalfFbo"));

        // Upsample output (full-res)
        IndirectFull = RequireGBuffer(GBuffer.CreateMRT(
            new[]
            {
                DynamicTexture2D.Create(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "Test.IndirectFull"),
            },
            depthTexture: null,
            ownsTextures: true,
            debugName: "Test.IndirectFullFbo"));

        // Final composite output
        Composite = RequireGBuffer(GBuffer.CreateMRT(
            new[]
            {
                DynamicTexture2D.Create(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "Test.Composite"),
            },
            depthTexture: null,
            ownsTextures: true,
            debugName: "Test.CompositeFbo"));
    }

    public GBuffer DirectLightingMrt { get; }

    public GBuffer Velocity { get; }

    public HzbTestPyramid Hzb { get; }

    public GBuffer ProbeAnchor { get; }

    public GBuffer AtlasTrace { get; }

    public GBuffer AtlasTemporal { get; }

    public GBuffer AtlasFiltered { get; }

    public GBuffer IndirectHalf { get; }

    public GBuffer IndirectFull { get; }

    public GBuffer Composite { get; }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        DirectLightingMrt.Dispose();
        Velocity.Dispose();
        Hzb.Dispose();
        ProbeAnchor.Dispose();
        AtlasTrace.Dispose();
        AtlasTemporal.Dispose();
        AtlasFiltered.Dispose();
        IndirectHalf.Dispose();
        IndirectFull.Dispose();
        Composite.Dispose();
    }

    private static GBuffer RequireGBuffer(GBuffer? gBuffer)
    {
        if (gBuffer == null)
        {
            throw new InvalidOperationException("Failed to allocate required test GBuffer");
        }

        return gBuffer;
    }
}
