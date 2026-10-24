using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies configurable raster policy independent of executable interface validation without native state.</summary>
public sealed class ConfigurableRasterizerDescriptionTests
{
    #region Public API
    /// <summary>Construction retains the former neutral raster defaults.</summary>
    [Fact]
    public void DefaultsAreExplicit()
    {
        var raster = Create(new()).Rasterizer;
        Assert.Equal(ClipOrigin.LowerLeft, raster.ClipOrigin);
        Assert.Equal(ClipDepthMode.NegativeOneToOne, raster.ClipDepth);
        Assert.Equal(PointSpriteCoordOriginParameter.UpperLeft, raster.PointSpriteOrigin);
        Assert.False(raster.LineSmooth || raster.PolygonSmooth);
    }

    /// <summary>Each independent active setting contributes to pipeline identity.</summary>
    [Fact]
    public void ActiveRasterSettingsChangeIdentity()
    {
        var baseline = Create(new());
        RasterizerDesc[] alternatives = [new() { ClipOrigin = ClipOrigin.UpperLeft },
            new() { ClipDepth = ClipDepthMode.ZeroToOne }, new() { PointSpriteOrigin = PointSpriteCoordOriginParameter.LowerLeft },
            new() { LineSmooth = true },
            new() { PolygonSmooth = true }];
        foreach (var raster in alternatives) Assert.NotEqual(baseline, Create(raster));
    }

    /// <summary>Malformed authored values reject even when the corresponding feature is disabled.</summary>
    [Fact]
    public void InvalidInactiveParametersReject()
    {
        RasterizerDesc[] invalid = [new() { ClipOrigin = (ClipOrigin)(-1) }, new() { ClipDepth = (ClipDepthMode)(-1) },
            new() { PointSpriteOrigin = (PointSpriteCoordOriginParameter)(-1) }];
        foreach (var raster in invalid) Assert.ThrowsAny<ArgumentException>(() => Create(raster));
    }

    /// <summary>Modern raster behavior remains accepted while unsupported clip control rejects.</summary>
    [Fact]
    public void ProfileAndClipCapabilitiesAreEnforced()
    {
        var core = Capabilities() with { CoreProfile = true };
        Create(new() { LineSmooth = true, PolygonSmooth = true, PointSpriteOrigin = PointSpriteCoordOriginParameter.LowerLeft }, core);
        var noControl = Capabilities() with { ClipControl = false };
        Create(new(), noControl);
        Assert.Throws<NotSupportedException>(() => Create(new() { ClipOrigin = ClipOrigin.UpperLeft }, noControl));
        Assert.Throws<NotSupportedException>(() => Create(new() { ClipDepth = ClipDepthMode.ZeroToOne }, noControl));
        Create(new(), Capabilities() with { MaxClipDistances = 0 });
    }


    #endregion

    #region Private
    /// <summary>Creates a minimal complete graphics description with deterministic support.</summary>
    private static GraphicsPipelineDesc Create(RasterizerDesc raster, GraphicsCapabilities? capabilities = null, ShaderPipelineIdentity? shader = null) =>
        new(shader ?? Shader(), new([]), new([new(PixelInternalFormat.Rgba8)]), DynamicPipelineState.Viewport,
            capabilities ?? Capabilities(), rasterizer: raster);

    /// <summary>Creates shader identity without executable output metadata.</summary>
    private static ShaderPipelineIdentity Shader()
    {
        var stages = new List<ShaderStageContract> {
            new("test.vsh", "test.vsh", ShaderStageKind.Vertex, new()),
            new("test.fsh", "test.fsh", ShaderStageKind.Fragment, new()) };
        return new("test", new(new ShaderSettings(new GpuShaderContract("test", stages, 1))));
    }

    /// <summary>Supplies representative compatibility support independent of installed hardware.</summary>
    private static GraphicsCapabilities Capabilities() => new() {
        Graphics33 = true, ClipControl = true, MaxClipDistances = 8, MaxDrawBuffers = 8,
        MaxVertexAttributes = 16, MaxVertexBindings = 16, MaxVertexStride = 2048, MaxVertexRelativeOffset = 2047,
        MaxSamples = 8, MaxSampleMaskWords = 1, MinLineWidth = 1, MaxLineWidth = 16, MinPointSize = 1, MaxPointSize = 64 };
    #endregion
}
