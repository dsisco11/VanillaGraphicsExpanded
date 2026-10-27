namespace ShaderContractGenerator.Tests;

/// <summary>Verifies immutable generated capture without native state, allocation or execution stubs.</summary>
public sealed class RuntimeSubmissionTests
{
    private const string Header = """
        [ShaderProgram("Contract", "example", 1)]
        [ShaderStage("Contract", ShaderStageKind.Vertex, "example.vsh")]
        [ShaderStage("Contract", ShaderStageKind.Fragment, "example.fsh")]
        """;

    #region Public API
    /// <summary>Setter assignments preserve resource identity while already captured tables remain unchanged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedSamplersCaptureOneFixedGeneration(bool compute)
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, STAGE)]
                VanillaGraphicsExpanded.Rendering.GpuTextureRef Source { get; set; }
            }
            """ + (compute ? Header.Replace("ShaderStageKind.Vertex, \"example.vsh\"", "ShaderStageKind.Compute, \"example.csh\"").Replace("[ShaderStage(\"Contract\", ShaderStageKind.Fragment, \"example.fsh\")]", "") : Header) + """
            internal partial class Shader : BASE, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader();
                    var first = new VanillaGraphicsExpanded.Rendering.GpuTextureRef(new object());
                    var second = new VanillaGraphicsExpanded.Rendering.GpuTextureRef(new object());
                    shader.Source = first;
                    var captured = shader.CaptureBindings();
                    bool reused = object.ReferenceEquals(captured, shader.CaptureBindings());
                    shader.Source = second;
                    var changed = shader.CaptureBindings();
                    var originalShader = captured.Shader;
                    shader.Shader = new(new object());
                    var replacement = shader.CaptureBindings();
                    return reused + ":" + (captured.Samplers[0].Texture == first) + ":" + (changed.Samplers[0].Texture == second)
                        + ":" + (captured.Shader == originalShader) + ":" + (replacement.Shader != originalShader);
                }
            }
            """;
        source = source.Replace("STAGE", compute ? "ShaderStageKind.Compute" : "ShaderStageKind.Fragment")
            .Replace("BASE", compute ? "VanillaGraphicsExpanded.Rendering.GpuComputeProgram" : "VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram");
        Assert.Equal("True:True:True:True:True", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
    }

    /// <summary>Caller array edits cannot mutate retained assignments or earlier captured element tables.</summary>
    [Fact]
    public void SamplerArraysCopyAssignmentsAndFreezeElementOrder()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("sources", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, ArrayLength = 2)]
                VanillaGraphicsExpanded.Rendering.GpuTextureRef[] Sources { get; set; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); var first = new VanillaGraphicsExpanded.Rendering.GpuTextureRef(new object());
                    var second = new VanillaGraphicsExpanded.Rendering.GpuTextureRef(new object());
                    var values = new[] { first, second }; shader.Sources = values; values[0] = second;
                    var captured = shader.CaptureBindings();
                    shader.Sources = new[] { second, first }; var next = shader.CaptureBindings();
                    return (captured.Samplers[0].Texture == first) + ":" + (captured.Samplers[1].Texture == second)
                        + ":" + captured.Samplers[1].Element + ":" + (next.Samplers[0].Texture == second);
                }
            }
            """;
        Assert.Equal("True:True:1:True", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
    }

    /// <summary>CPU edits create a new captured version without changing the bytes version retained by earlier tables.</summary>
    [Fact]
    public void UniformSnapshotsFollowVersionedCapture()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("Parameters", ShaderBindingKind.UniformBlock, 1, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.CpuUniformBuffer Parameters { get; set; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var block = new VanillaGraphicsExpanded.Rendering.CpuUniformBuffer(); var shader = new Shader { Parameters = block };
                    block.Edit(7); var first = shader.CaptureBindings();
                    bool reused = object.ReferenceEquals(first, shader.CaptureBindings());
                    block.Edit(8); var second = shader.CaptureBindings();
                    return reused + ":" + first.Uniforms[0].Snapshot.Version + ":" + second.Uniforms[0].Snapshot.Version;
                }
            }
            """;
        Assert.Equal("True:7:8", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
    }

    /// <summary>Authored resource getters are sampled once per capture and equal values reuse the frozen table.</summary>
    [Fact]
    public void AuthoredGetterCaptureIsSingleRead()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuTextureRef Source { get; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs
            {
                public int Reads;
                public VanillaGraphicsExpanded.Rendering.GpuTextureRef Current = new(new object());
                public VanillaGraphicsExpanded.Rendering.GpuTextureRef Source { get { Reads++; return Current; } }
            }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); var first = shader.CaptureBindings(); var second = shader.CaptureBindings();
                    return shader.Reads + ":" + object.ReferenceEquals(first, second);
                }
            }
            """;
        Assert.Equal("2:True", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
    }

    /// <summary>Retirement rejects further input mutation before any new capture can be produced.</summary>
    [Fact]
    public void RetiredOwnerRejectsGeneratedSetters()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuTextureRef Source { get; set; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); shader.Dispose();
                    try { shader.Source = new(new object()); return "failed"; }
                    catch (System.ObjectDisposedException) { return "retired"; }
                }
            }
            """;
        Assert.Equal("retired", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
    }
    /// <summary>Image selectors and buffer intervals remain frozen when later desired inputs select different subresources.</summary>
    [Fact]
    public void ImageAndRangeCapturePreservesCompleteSelections()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("image", ShaderBindingKind.Image, 1, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.RHI.RHIImageBinding Image { get; set; }
                [ShaderBinding("work", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuStorageBufferBinding Work { get; set; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var texture = new VanillaGraphicsExpanded.Rendering.GpuTextureRef(new object());
                    var buffer = new VanillaGraphicsExpanded.Rendering.GpuShaderStorageBuffer();
                    var shader = new Shader { Image = new(texture, 2, true, 0), Work = new(buffer, 16, 32) };
                    var first = shader.CaptureBindings();
                    shader.Image = new(texture, 3, false, 4); shader.Work = new(buffer, 48, 64);
                    var second = shader.CaptureBindings();
                    return first.Images[0].View.Level + ":" + first.Images[0].View.Layered + ":" + first.Buffers[0].Offset + ":" + first.Buffers[0].Length
                        + ":" + second.Images[0].View.Layer + ":" + second.Buffers[0].Offset;
                }
            }
            """;
        Assert.Equal("2:True:16:32:4:48", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
    }

    /// <summary>An inherited authored capture owns its implementation; reabstraction restores concrete generated capture.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthoredCaptureOwnershipRespectsInheritance(bool reabstract)
    {
        string source = """
            internal abstract class Family : VanillaGraphicsExpanded.Rendering.GpuComputeProgram
            {
                protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
            }
            ABSTRACT
            """ + Header.Replace("ShaderStageKind.Vertex, \"example.vsh\"", "ShaderStageKind.Compute, \"example.csh\"").Replace("[ShaderStage(\"Contract\", ShaderStageKind.Fragment, \"example.fsh\")]", "") + """
            internal partial class Shader : PARENT { }
            public static class Proof { public static string Run() => new Shader().CaptureBindings().Samplers.Length.ToString(); }
            """;
        source = source.Replace("ABSTRACT", reabstract
            ? "internal abstract class Required : Family { protected abstract override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore(); }" : "")
            .Replace("PARENT", reabstract ? "Required" : "Family");
        var result = GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source);
        Assert.Equal("0", result.Run());
        Assert.Equal(reabstract, result.Generated.Any(text => text.Contains("protected override global::VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore()")));
    }
    #endregion
}
