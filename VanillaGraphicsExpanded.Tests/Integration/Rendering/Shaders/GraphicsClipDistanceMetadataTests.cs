using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.Support.Assets;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks compiler-packaged clipping facts as they pass through immutable runtime shader inputs.</summary>
public sealed class GraphicsClipDistanceMetadataTests
{
    #region Public API
    /// <summary>Final producer activity determines the immutable clip-enable count for packaged variants.</summary>
    [Theory]
    [InlineData("tests/clip-distance-vertex", 2)]
    [InlineData("tests/clip-distance-tessellation", 3)]
    [InlineData("tests/clip-distance-tessellation-geometry", 4)]
    [InlineData("tests/clip-distance-geometry", 4)]
    [InlineData("tests/clip-distance-inactive", 0)]
    [InlineData("tests/clip-distance-geometry-inactive", 0)]
    public void PreparedPackagedStagesResolveFinalProducer(string identity, int expected)
    {
        // Arrange: prepare the catalog-selected SPIR-V and its exact build manifest metadata.
        var description = Prepare(identity);

        // Act: resolve clipping from immutable stage facts using a supported context limit.
        int count = GraphicsClipDistances.Resolve(description.Stages, 8);

        // Assert: each producer's compiler-derived activity and declared extent survive preparation.
        Assert.Equal(expected, count);
    }

    /// <summary>Active packaged plane extents beyond the device limit are rejected during executable preparation.</summary>
    [Fact]
    public void RejectsClipExtentAboveDeviceLimit()
    {
        // Arrange: use the real four-plane geometry variant and its matching packaged metadata.
        var description = Prepare("tests/clip-distance-geometry");

        // Act and assert: a device with only three clip distances cannot prepare this executable.
        Assert.Throws<InvalidOperationException>(() => GraphicsClipDistances.Resolve(description.Stages, 3));
    }

    /// <summary>Compiler-active stage input must match the preceding producer's packaged extent.</summary>
    [Fact]
    public void RejectsMismatchedClipDistanceProducerAndConsumerExtents()
    {
        // Arrange: pair a two-plane vertex producer with a tessellation-control input declared for three planes.
        var description = Prepare("tests/clip-distance-mismatched-tessellation");

        // Act and assert: preparation rejects the incompatible packaged stage interface before native linking.
        Assert.Throws<InvalidOperationException>(() => GraphicsClipDistances.Resolve(description.Stages, 8));
    }

    /// <summary>A raw SPIR-V input without its packaged facts cannot be treated as an inactive executable.</summary>
    [Fact]
    public void RawSpirvWithoutPackagedFactsFailsClosed()
    {
        // Arrange: capture a real catalog binary while deliberately omitting its manifest interface.
        var resolver = ShaderBuildTool.Spirv.TestShaderPrograms.Create();
        var contract = resolver.Programs["tests/clip-distance-vertex"];
        var selection = new ShaderLoadPlan(new ShaderSettings(contract)).Stages.Single(stage => stage.Stage.Kind == ShaderStageKind.Vertex);
        var raw = new RHIShaderStageDescription(ShaderStage.Vertex,
            TestAssetSnapshot.Built.Read("shaders/" + selection.BinaryPath), selection.Stage.EntryPoint);

        // Act and assert: an unassociated module has unknown clipping facts and cannot resolve to zero.
        Assert.Throws<InvalidOperationException>(() => GraphicsClipDistances.Resolve([raw], 8));
    }

    /// <summary>Source-only shader descriptions keep the established no-custom-clipping behavior.</summary>
    [Fact]
    public void SourceOnlyStagesDoNotAcquireClipEnablement()
    {
        // Arrange: source text mentions a clip output but carries no compiler-packaged activity facts.
        RHIShaderStageDescription[] stages =
        [
            new(ShaderStage.Vertex, "#version 450 core\nvoid main(){gl_Position=vec4(0);gl_ClipDistance[0]=1.0;}"),
            new(ShaderStage.Fragment, "#version 450 core\nout vec4 color;void main(){color=vec4(1);}")
        ];

        // Act and assert: the unsupported source-only path derives no native clip enables.
        Assert.Equal(0, GraphicsClipDistances.Resolve(stages, 8));
    }
    #endregion

    #region Private
    /// <summary>Uses serialized manifest facts and captured compiled assets to build backend-neutral stage inputs.</summary>
    private static RHIShaderDescription Prepare(string identity)
    {
        var resolver = ShaderBuildTool.Spirv.TestShaderPrograms.Create();
        var contract = resolver.Programs[identity];
        var plan = new ShaderLoadPlan(new ShaderSettings(contract));
        var manifest = ShaderBinaryDigest.Parse(TestAssetSnapshot.Built.Read("shaders/" + ShaderBinaryDigest.FileName));
        var inputs = new PreparedProgramBinary(plan,
            path => TestAssetSnapshot.Built.Read("shaders/" + path), null, "packaged-metadata-test", () => manifest);
        return ShaderExecutableInputs.Create(plan, inputs);
    }
    #endregion
}
