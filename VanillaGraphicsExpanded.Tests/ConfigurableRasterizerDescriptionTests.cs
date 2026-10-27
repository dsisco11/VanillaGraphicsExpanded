using VanillaGraphicsExpanded.Tests.GPU;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies configurable raster policy independent of executable interface validation without native state.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ConfigurableRasterizerDescriptionTests : IDisposable
{
    private readonly ComponentShaderPrograms programs;
    private readonly RasterDepthReductionShaderProgram program;
    /// <summary>Supplies a real fixed executable before testing pure descriptions with synthetic device limits.</summary>
    public ConfigurableRasterizerDescriptionTests(HeadlessGLFixture fixture)
    { fixture.MakeCurrent();programs=new();program=programs.Create<RasterDepthReductionShaderProgram>(); }
    /// <summary>Retires fixture-owned executables after description assertions complete.</summary>
    public void Dispose()=>programs.Dispose();
    #region Public API
    /// <summary>Construction retains the former neutral raster defaults.</summary>
    [Fact]
    public void DefaultsAreExplicit()
    {
        var raster = Create(new()).Rasterizer;
        Assert.Equal(ClipCoordinateOrigin.LowerLeft, raster.ClipOrigin);
        Assert.Equal(ClipDepthRange.NegativeOneToOne, raster.ClipDepth);
        Assert.Equal(PointSpriteOrigin.UpperLeft, raster.PointSpriteOrigin);
        Assert.False(raster.LineSmooth || raster.PolygonSmooth);
    }

    /// <summary>Each independent active setting contributes to pipeline identity.</summary>
    [Fact]
    public void ActiveRasterSettingsChangeIdentity()
    {
        var baseline = Create(new());
        RasterizerDesc[] alternatives = [new() { ClipOrigin = ClipCoordinateOrigin.UpperLeft },
            new() { ClipDepth = ClipDepthRange.ZeroToOne }, new() { PointSpriteOrigin = PointSpriteOrigin.LowerLeft },
            new() { LineSmooth = true },
            new() { PolygonSmooth = true }];
        foreach (var raster in alternatives) Assert.NotEqual(baseline, Create(raster));
    }

    /// <summary>Malformed authored values reject even when the corresponding feature is disabled.</summary>
    [Fact]
    public void InvalidInactiveParametersReject()
    {
        RasterizerDesc[] invalid = [new() { ClipOrigin = (ClipCoordinateOrigin)(-1) }, new() { ClipDepth = (ClipDepthRange)(-1) },
            new() { PointSpriteOrigin = (PointSpriteOrigin)(-1) }];
        foreach (var raster in invalid) Assert.ThrowsAny<ArgumentException>(() => Create(raster));
    }

    /// <summary>Core raster behavior remains available independently of legacy extension flags.</summary>
    [Fact]
    public void ProfileAndClipCapabilitiesAreEnforced()
    {
        var core = Capabilities() with { CoreProfile = true };
        Create(new() { LineSmooth = true, PolygonSmooth = true, PointSpriteOrigin = PointSpriteOrigin.LowerLeft }, core);
        var noControl = Capabilities() with { ClipControl = false };
        Create(new(), noControl);
        Create(new() { ClipOrigin = ClipCoordinateOrigin.UpperLeft }, noControl);
        Create(new() { ClipDepth = ClipDepthRange.ZeroToOne }, noControl);
        Create(new(), Capabilities() with { MaxClipDistances = 0 });
    }


    #endregion

    #region Private
    /// <summary>Creates a minimal complete graphics description with deterministic support.</summary>
    private RHIGraphicsPipelineDescription Create(RasterizerDesc raster,GraphicsCapabilities? capabilities=null)
    {
        var description=new RHIGraphicsPipelineDescription(program.Shader,new([]),new([new(TextureFormat.Rgba8)]),DynamicPipelineState.Viewport,rasterizer:raster);
        PipelineDescriptionValidation.Validate(description,capabilities??Capabilities());return description;
    }

    /// <summary>Supplies representative compatibility support independent of installed hardware.</summary>
    private static GraphicsCapabilities Capabilities() => new() {
        Graphics33 = true, ClipControl = true, MaxClipDistances = 8, MaxDrawBuffers = 8,
        MaxVertexAttributes = 16, MaxVertexBindings = 16, MaxVertexStride = 2048, MaxVertexRelativeOffset = 2047,
        MaxPatchVertices = 32, MaxSamples = 8, MaxSampleMaskWords = 1, MinLineWidth = 1, MaxLineWidth = 16, MinPointSize = 1, MaxPointSize = 64 };
    #endregion
}
