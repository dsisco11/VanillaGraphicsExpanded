
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Pipelines;

/// <summary>Verifies raster-description rules using a ready native shader interface and synthetic device capabilities.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RasterDescriptionRulesTests : IDisposable
{
    private readonly ComponentShaderPrograms programs;
    private readonly RasterDepthReductionShaderProgram program;
    #region Public API
    /// <summary>Supplies a real fixed executable before testing pure descriptions with synthetic device limits.</summary>
    public RasterDescriptionRulesTests(HeadlessGLFixture fixture)
    {
        fixture.MakeCurrent();
        programs = new();
        try
        {
            program = programs.Create<RasterDepthReductionShaderProgram>();
        }
        catch
        {
            programs.Dispose();
            throw;
        }
    }
    /// <summary>Retires fixture-owned executables after description assertions complete.</summary>
    public void Dispose()=>programs.Dispose();
    /// <summary>Construction retains the former neutral raster defaults.</summary>
    [Fact]
    public void DefaultsAreExplicit()
    {
        // Arrange and act
        var raster = Create(new()).Rasterizer;
        // Assert
        Assert.Equal(ClipCoordinateOrigin.LowerLeft, raster.ClipOrigin);
        Assert.Equal(ClipDepthRange.NegativeOneToOne, raster.ClipDepth);
        Assert.Equal(PointSpriteOrigin.UpperLeft, raster.PointSpriteOrigin);
        Assert.False(raster.LineSmooth || raster.PolygonSmooth);
    }

    /// <summary>Each independent active setting contributes to pipeline identity.</summary>
    [Fact]
    public void ActiveRasterSettingsChangeIdentity()
    {
        // Arrange
        var baseline = Create(new());
        RasterizerDesc[] alternatives = [new() { ClipOrigin = ClipCoordinateOrigin.UpperLeft },
            new() { ClipDepth = ClipDepthRange.ZeroToOne }, new() { PointSpriteOrigin = PointSpriteOrigin.LowerLeft },
            new() { LineSmooth = true },
            new() { PolygonSmooth = true }];
        // Act and assert each active setting contributes to description identity.
        foreach (var raster in alternatives) Assert.NotEqual(baseline, Create(raster));
    }

    /// <summary>Malformed authored values reject even when the corresponding feature is disabled.</summary>
    [Fact]
    public void InvalidInactiveParametersReject()
    {
        // Arrange
        RasterizerDesc[] invalid = [new() { ClipOrigin = (ClipCoordinateOrigin)(-1) }, new() { ClipDepth = (ClipDepthRange)(-1) },
            new() { PointSpriteOrigin = (PointSpriteOrigin)(-1) }];
        // Act and assert malformed inactive fields are rejected.
        foreach (var raster in invalid) Assert.ThrowsAny<ArgumentException>(() => Create(raster));
    }

    /// <summary>Supported profiles accept raster descriptions with and without clip-control support.</summary>
    [Fact]
    public void SupportedProfilesAcceptRasterDescriptions()
    {
        // Arrange and act through the validator: these supported capability profiles must construct successfully.
        // Assert: Core and compatibility profiles accept supported raster options, including absent clip control.
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
    /// <summary>Creates a complete description using the ready executable and deterministic capability inputs.</summary>
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
