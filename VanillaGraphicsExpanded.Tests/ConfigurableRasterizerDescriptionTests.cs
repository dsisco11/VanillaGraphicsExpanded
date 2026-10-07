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
        Assert.False(raster.AlphaTest || raster.PointSmooth || raster.LineSmooth || raster.PolygonSmooth || raster.LineStipple || raster.PolygonStipple);
        Assert.Equal(AlphaFunction.Always, raster.AlphaComparison);
        Assert.Equal(0, raster.AlphaReference);
        Assert.Equal(1, raster.LineStippleFactor);
        Assert.Equal(ushort.MaxValue, raster.LineStipplePattern);
        Assert.Equal(128, raster.PolygonStipplePattern.Count);
        Assert.All(raster.PolygonStipplePattern, value => Assert.Equal(byte.MaxValue, value));
    }

    /// <summary>Each independent active setting contributes to pipeline identity.</summary>
    [Fact]
    public void ActiveRasterSettingsChangeIdentity()
    {
        var baseline = Create(new());
        RasterizerDesc[] alternatives = [new() { ClipOrigin = ClipOrigin.UpperLeft },
            new() { ClipDepth = ClipDepthMode.ZeroToOne }, new() { PointSpriteOrigin = PointSpriteCoordOriginParameter.LowerLeft },
            new() { AlphaTest = true }, new() { PointSmooth = true }, new() { LineSmooth = true },
            new() { PolygonSmooth = true }, new() { LineStipple = true }, new() { PolygonStipple = true }];
        foreach (var raster in alternatives) Assert.NotEqual(baseline, Create(raster));
        var alpha = new RasterizerDesc { AlphaTest = true };
        Assert.NotEqual(Create(alpha), Create(alpha with { AlphaComparison = AlphaFunction.Less }));
        Assert.NotEqual(Create(alpha), Create(alpha with { AlphaReference = .5f }));
        var line = new RasterizerDesc { LineStipple = true };
        Assert.NotEqual(Create(line), Create(line with { LineStippleFactor = 256 }));
        Assert.NotEqual(Create(line), Create(line with { LineStipplePattern = 0 }));
        Assert.NotEqual(Create(new() { PolygonStipple = true }), Create(new() { PolygonStipple = true, PolygonStipplePattern = new(new byte[128]) }));
    }

    /// <summary>Patterns are copied and structurally compared while inactive parameters canonicalize.</summary>
    [Fact]
    public void PatternOwnershipAndInactiveCanonicalizationAreStable()
    {
        byte[] bytes = Enumerable.Range(0, 128).Select(value => (byte)value).ToArray();
        var authored = new RasterizerDesc { PolygonStipplePattern = new(bytes), AlphaComparison = AlphaFunction.Less,
            AlphaReference = .5f, LineStippleFactor = 256, LineStipplePattern = 17 };
        Assert.Equal(Create(new()), Create(authored));
        var enabled = Create(authored with { PolygonStipple = true, AlphaTest = true, LineStipple = true });
        var copy = Create(authored with { PolygonStipple = true, AlphaTest = true, LineStipple = true, PolygonStipplePattern = new(bytes.ToArray()) });
        Assert.Equal(enabled, copy);
        Assert.Equal(enabled.GetHashCode(), copy.GetHashCode());
        int hash = enabled.GetHashCode();
        bytes[0] = 255;
        Assert.Equal(0, enabled.Rasterizer.PolygonStipplePattern[0]);
        Assert.Equal(hash, enabled.GetHashCode());
        Assert.Equal(.5f, enabled.Rasterizer.AlphaReference);
        Assert.Equal(256, enabled.Rasterizer.LineStippleFactor);
    }

    /// <summary>Malformed authored values reject even when the corresponding feature is disabled.</summary>
    [Fact]
    public void InvalidInactiveParametersReject()
    {
        RasterizerDesc[] invalid = [new() { ClipOrigin = (ClipOrigin)(-1) }, new() { ClipDepth = (ClipDepthMode)(-1) },
            new() { PointSpriteOrigin = (PointSpriteCoordOriginParameter)(-1) }, new() { AlphaComparison = (AlphaFunction)(-1) },
            new() { AlphaReference = float.NaN }, new() { AlphaReference = float.PositiveInfinity },
            new() { AlphaReference = -.1f }, new() { AlphaReference = 1.1f },
            new() { LineStippleFactor = 0 }, new() { LineStippleFactor = 257 },
            new() { PolygonStipplePattern = new(new byte[127]) }, new() { PolygonStipplePattern = new(new byte[129]) },
            new() { PolygonStipplePattern = null! }];
        foreach (var raster in invalid) Assert.ThrowsAny<ArgumentException>(() => Create(raster));
    }

    /// <summary>Core-safe behavior remains accepted while removed compatibility features reject.</summary>
    [Fact]
    public void ProfileAndClipCapabilitiesAreEnforced()
    {
        var core = Capabilities() with { CoreProfile = true };
        Create(new() { LineSmooth = true, PolygonSmooth = true, PointSpriteOrigin = PointSpriteCoordOriginParameter.LowerLeft }, core);
        RasterizerDesc[] legacy = [new() { AlphaTest = true }, new() { PointSmooth = true },
            new() { LineStipple = true }, new() { PolygonStipple = true }];
        foreach (var raster in legacy) Assert.Throws<NotSupportedException>(() => Create(raster, core));
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
