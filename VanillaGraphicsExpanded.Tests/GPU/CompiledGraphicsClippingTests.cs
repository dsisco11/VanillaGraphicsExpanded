using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks final-stage clipping with actual linked optimized binary shader programs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CompiledGraphicsClippingTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Later vertex-producing stages determine clipping even when the vertex stage declares a different extent.</summary>
    [Theory]
    [InlineData("absent", false, false, 0, 2)]
    [InlineData("present", false, false, 3, 2)]
    [InlineData("present", true, false, 2, 2)]
    [InlineData("present", false, true, 1, 2)]
    [InlineData("present", true, true, 2, 2)]
    [InlineData("specialized", false, false, 2, 1)]
    [InlineData("specialized", false, false, 6, 5)]
    public void FinalProducerDeterminesEnabledClipCoverage(string vertex, bool geometry, bool tessellation, int extent, int count)
    {
        fixture.MakeCurrent();
        var stages = new List<ShaderStageContract> { Stage(vertex, ShaderStageKind.Vertex), Stage("fragment", ShaderStageKind.Fragment) };
        var option = new ShaderOption<int>("count", 2);
        if (vertex == "specialized") stages[0] = new(vertex, vertex + ".glsl", ShaderStageKind.Vertex,
            new GpuBindingContract(), specializations: [new ShaderSpecialization(7, option)]);
        if (geometry) stages.Add(Stage(tessellation ? "geometry-triangles" : "geometry", ShaderStageKind.Geometry));
        if (tessellation)
        {
            stages.Add(Stage("control", ShaderStageKind.TessellationControl));
            stages.Add(Stage("evaluation", ShaderStageKind.TessellationEvaluation));
        }
        var plan = new ShaderLoadPlan(new ShaderSettings(new GpuShaderContract("clipping", stages, 1,
            options: vertex == "specialized" ? [option] : []),
            vertex == "specialized" ? new Dictionary<string, string?> { ["count"] = count.ToString() } : null));
        var modules = new List<GpuShaderModule>();
        int program = 0;
        try
        {
            foreach (var selection in plan.Stages)
            {
                var type = selection.Stage.Kind switch
                {
                    ShaderStageKind.Vertex => ShaderType.VertexShader,
                    ShaderStageKind.Fragment => ShaderType.FragmentShader,
                    ShaderStageKind.Geometry => ShaderType.GeometryShader,
                    ShaderStageKind.TessellationControl => ShaderType.TessControlShader,
                    _ => ShaderType.TessEvaluationShader
                };
                var specialization = selection.Specializations.Select(s => new GpuShaderModule.SpirvSpecializationConstant((int)s.Id, (int)s.Value.Bits)).ToArray();
                Assert.True(GpuShaderModule.TryLoadSpirv(type, Binary(selection.BinaryPath), "main", specialization, out var module, out string log), log);
                modules.Add(module!);
            }
            program = ShaderProgramLink.Submit(modules.Select(m => m.ShaderId).ToArray(), false, out _);
            Assert.True(ShaderProgramLink.Validate(program, out string linkLog), linkLog);
            var metadata = new GraphicsExecutableInterface(program, plan, path => Binary(path));
            Assert.Equal(extent, metadata.ClipDistanceExtent);
            Assert.Equal(geometry ? ShaderStageKind.Geometry : tessellation ? ShaderStageKind.TessellationEvaluation : ShaderStageKind.Vertex,
                metadata.FinalVertexStage);
            // Validation uses the retained compiled extent and must reject the first undeclared output.
            GraphicsPipelineDesc Description(uint mask, bool wrongTopology = false) => new(new ShaderPipelineIdentity("tests", plan), new([]),
                new([new(PixelInternalFormat.Rgba8)]), DynamicPipelineState.Viewport,
                rasterizer: new() { ClipDistances = mask },
                assembly: new() { Topology = tessellation ? PrimitiveType.Patches : geometry && !wrongTopology ? PrimitiveType.Points : PrimitiveType.Triangles });
            GraphicsInterfaceValidation.Validate(Description(extent == 0 ? 0 : (1u << extent) - 1), metadata);
            Assert.Throws<InvalidOperationException>(() => GraphicsInterfaceValidation.Validate(Description(1u << extent), metadata));
            if (geometry && !tessellation) Assert.Throws<InvalidOperationException>(() => GraphicsInterfaceValidation.Validate(Description(0, true), metadata));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (program != 0) GL.DeleteProgram(program);
            foreach (var module in modules) module.Dispose();
        }
    }
    #endregion

    #region Private
    /// <summary>Declares one test binary without authored clipping metadata.</summary>
    private static ShaderStageContract Stage(string name, ShaderStageKind kind) => new(name, name + ".glsl", kind, new GpuBindingContract());

    /// <summary>Reads the same embedded bytes for both native linking and retained executable reflection.</summary>
    private static byte[] Binary(string path)
    {
        using var source = typeof(CompiledGraphicsClippingTests).Assembly.GetManifestResourceStream(
            "VanillaGraphicsExpanded.Tests.Fixtures.ClipDistance." + path)!;
        using var result = new MemoryStream();
        source.CopyTo(result);
        return result.ToArray();
    }
    #endregion
}
