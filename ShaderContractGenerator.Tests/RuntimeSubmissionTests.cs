namespace ShaderContractGenerator.Tests;

/// <summary>Checks contract-generated publication and retained assignments independently of the graphics driver.</summary>
public sealed class RuntimeSubmissionTests
{
    private const string Support = """
        namespace VanillaGraphicsExpanded.Rendering
        {
            public class GpuTexture { }
            public class CpuUniformBuffer { }
            public class GpuShaderStorageBuffer { }
            public class GpuAtomicCounterBuffer { }
            public struct GpuStorageBufferBinding { }
            public struct GpuTextureBinding { }
            public struct ShaderInputValidation
            {
                public ulong Resolve(object owner, ulong identity) => identity;
            }
            public static class ShaderBindingSubmission
            {
                public static string Calls = "";
                public static void ValidateSampler(params object[] args) { Calls += "validate-sampler;"; }
                public static void ValidateUniformBlock(params object[] args) { Calls += "validate-block;"; }
                public static void Sampler(params object[] args) { Calls += "sampler;"; }
                internal static void Sampler(object owner, string name, int value, VanillaGraphicsExpanded.Rendering.Contracts.ShaderTextureTarget target,
                    VanillaGraphicsExpanded.Rendering.Contracts.ShaderSamplerPolicy sampler)
                { Calls += value + ":" + target + ":" + sampler + ";"; }
                public static void UniformBlock(params object[] args) { Calls += "block;"; }
                public static void ValidateImage(params object[] args) { Calls += "validate-image;"; }
                public static void ValidateStorageBlock(params object[] args) { Calls += "validate-storage;"; }
                public static void Image(params object[] args) { Calls += "image;"; }
                public static void StorageBlock(params object[] args) { Calls += "storage;"; }
                public static void ValidateAtomicCounter(params object[] args) { Calls += "validate-counter;"; }
                public static void AtomicCounter(params object[] args) { Calls += "counter;"; }
            }
            public static class ShaderPreparedSubmission
            {
                public static void ValidateSampler(ulong binding, GpuTexture texture, ref ShaderInputValidation history) { ShaderBindingSubmission.Calls += "validate-sampler;"; }
                public static void ValidateImage(ulong binding, GpuTexture texture, ref ShaderInputValidation history) { ShaderBindingSubmission.Calls += "validate-image;"; }
                public static void ValidateImage(ulong binding, GpuTextureBinding image, ref ShaderInputValidation history) { ShaderBindingSubmission.Calls += "validate-image;"; }
                public static void ValidateStorageBlock(ulong binding, GpuStorageBufferBinding range, ref ShaderInputValidation history) { ShaderBindingSubmission.Calls += "validate-storage;"; }
                public static ulong Resolve(object owner, ulong identity) => identity;
                public static int ValidateSampler(ulong binding, int texture) { ShaderBindingSubmission.Calls += "validate-sampler;"; return texture; }
                public static void ValidateSampler(params object[] args) { ShaderBindingSubmission.Calls += "validate-sampler;"; }
                public static void ValidateUniformBlock(params object[] args) { ShaderBindingSubmission.Calls += "validate-block;"; }
                public static void Sampler(params object[] args) { ShaderBindingSubmission.Calls += "sampler;"; }
                public static void UniformBlock(params object[] args) { ShaderBindingSubmission.Calls += "block;"; }
                public static void ValidateImage(params object[] args) { ShaderBindingSubmission.Calls += "validate-image;"; }
                public static void ValidateStorageBlock(params object[] args) { ShaderBindingSubmission.Calls += "validate-storage;"; }
                public static void Image(params object[] args) { ShaderBindingSubmission.Calls += "image;"; }
                public static void StorageBlock(params object[] args) { ShaderBindingSubmission.Calls += "storage;"; }
                public static void ValidateAtomicCounter(params object[] args) { ShaderBindingSubmission.Calls += "validate-counter;"; }
                public static void AtomicCounter(params object[] args) { ShaderBindingSubmission.Calls += "counter;"; }
            }
            public abstract class GpuComputeShader
            {
                protected abstract void Submit();
                protected void RequireInputMutation() { }
                public void Dispatch() { Submit(); }
            }
        }
        namespace VanillaGraphicsExpanded.Rendering.Shaders
        {
            public abstract class GpuProgram
            {
                protected abstract void Submit();
                protected void RequireInputMutation() { }
                public void Use() { Submit(); }
            }
        }
        """;
    private const string Header = """
        [ShaderProgram("Contract", "example", 1)]
        [ShaderStage("Contract", ShaderStageKind.Vertex, "example.vsh")]
        [ShaderStage("Contract", ShaderStageKind.Fragment, "example.fsh")]
        """;

    #region Public API
    /// <summary>Concrete texture owners are accepted and retain deferred sampler publication.</summary>
    [Theory]
    [InlineData("Texture2D")]
    [InlineData("DynamicTexture2D")]
    [InlineData("DynamicTexture3D")]
    public void ConcreteTextureSamplerPublishesAtActivation(string textureType)
    {
        // Use inheritance in the semantic compilation so validation cannot rely on a class-name whitelist.
        string support = Support + "namespace VanillaGraphicsExpanded.Rendering { public class " + textureType + " : GpuTexture { public static implicit operator int(" + textureType + " value) => 1; } }";
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.TEXTURE Source { set; }
            }
            """.Replace("TEXTURE", textureType) + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); shader.Source = new();
                    string before = VanillaGraphicsExpanded.Rendering.ShaderBindingSubmission.Calls;
                    shader.Use();
                    return before + "|" + VanillaGraphicsExpanded.Rendering.ShaderBindingSubmission.Calls;
                }
            }
            """;
        Assert.Equal("|validate-sampler;sampler;", GeneratorFixture.Generate(source, supportSource: support).Run());
    }

    /// <summary>Compute owners retain storage ranges and atomic counters and publish them at every dispatch.</summary>
    [Fact]
    public void ComputeRangeAndCounterAssignmentsPublishOnlyAtDispatch()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("Work", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
                VanillaGraphicsExpanded.Rendering.GpuStorageBufferBinding Work { set; }
                [ShaderBinding("Count", ShaderBindingKind.AtomicCounter, 0, ShaderStageKind.Compute)]
                VanillaGraphicsExpanded.Rendering.GpuAtomicCounterBuffer Count { set; }
            }
            [ShaderProgram("Contract", "example", 1)]
            [ShaderStage("Contract", ShaderStageKind.Compute, "example.csh")]
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.GpuComputeShader, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader();
                    shader.Work = new(); shader.Count = new();
                    string before = VanillaGraphicsExpanded.Rendering.ShaderBindingSubmission.Calls;
                    shader.Dispatch(); shader.Dispatch();
                    return before + "|" + VanillaGraphicsExpanded.Rendering.ShaderBindingSubmission.Calls;
                }
            }
            """;
        var result = GeneratorFixture.Generate(source, supportSource: Support);
        string calls = result.Run();
        Assert.StartsWith("|", calls);
        Assert.Equal(2, calls.Split(';').Count(call => call == "counter"));
        Assert.Equal(2, calls.Split(';').Count(call => call == "storage"));
        Assert.True(calls.IndexOf("validate-counter;", StringComparison.Ordinal) < calls.IndexOf(";storage;", StringComparison.Ordinal));
    }

    /// <summary>Declared enum policies retain their actual values in generated runtime publication.</summary>
    [Theory]
    [InlineData("Texture1D")]
    [InlineData("Texture2D")]
    [InlineData("Texture3D")]
    [InlineData("TextureCubeMap")]
    [InlineData("Texture2DArray")]
    [InlineData("TextureBuffer")]
    public void RawSamplerPublishesTypedTargetAndPolicy(string target)
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment,
                    TextureTarget = ShaderTextureTarget.TARGET,
                    Sampler = ShaderSamplerPolicy.ShadowCompareLinearClamp)]
                int Source { set; }
            }
            """.Replace("TARGET", target) + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); shader.Source = 12; shader.Use();
                    var binding = Shader.Contract.Bindings.Samplers["source"];
                    return VanillaGraphicsExpanded.Rendering.ShaderBindingSubmission.Calls + "|" +
                        (ShaderTextureTarget)binding.TextureTarget + ":" + (ShaderSamplerPolicy)binding.Sampler;
                }
            }
            """;
        var result = GeneratorFixture.Generate(source, supportSource: Support);
        Assert.All(result.Generated, text => Assert.DoesNotContain("OpenTK", text));
        Assert.Equal($"validate-sampler;sampler;|{target}:ShadowCompareLinearClamp", result.Run());
        string submission = Assert.Single(result.Generated, text => text.Contains("#region Submission", StringComparison.Ordinal)).Split("#region Submission")[1];
        Assert.DoesNotContain("ResolveUniformLocation", submission);
        Assert.DoesNotContain("\"source\"", submission);
    }

    /// <summary>Undefined enum casts fail generation instead of creating invalid runtime policy expressions.</summary>
    [Theory]
    [InlineData("TextureTarget = (VanillaGraphicsExpanded.Rendering.Contracts.ShaderTextureTarget)999999", "TextureTarget")]
    [InlineData("Sampler = (ShaderSamplerPolicy)999999", "Sampler")]
    public void UndefinedSamplerMetadataIsRejected(string metadata, string property)
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment, METADATA)]
                VanillaGraphicsExpanded.Rendering.GpuTexture Source { set; }
            }
            """.Replace("METADATA", metadata) + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs { }
            """;
        foreach (bool offline in new[] { false, true })
        {
            var result = GeneratorFixture.Generate(source, offline, supportSource: Support);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "VGEGEN001" && diagnostic.GetMessage().Contains(property, StringComparison.Ordinal));
            Assert.Empty(result.Generated);
        }
    }

    /// <summary>A concrete runtime owner receives publication directly, without a generated edit wrapper.</summary>
    [Fact]
    public void ConcreteOwnerGetsSubmitWithoutPrepare()
    {
        string source = Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram { }
            public static class Proof { public static string Run() { new Shader().Use(); return "used"; } }
            """;
        var result = GeneratorFixture.Generate(source, supportSource: Support);
        Assert.Contains(result.Generated, text => text.Contains("protected override void Submit()"));
        Assert.All(result.Generated, text => Assert.DoesNotContain("void Prepare(", text));
        Assert.Equal("used", result.Run());
    }

    /// <summary>Interface setters retain values, while the generated method validates and publishes on every use.</summary>
    [Fact]
    public void ResourceAssignmentsPublishOnlyOnUseAndRetainCpuBlock()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuTexture Source { set; }
                [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 6, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.CpuUniformBuffer Params { get; }
                [ShaderBinding("Output", ShaderBindingKind.Image, 1, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuTextureBinding Output { set; }
                [ShaderBinding("Work", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuShaderStorageBuffer Work { set; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs
            {
                public VanillaGraphicsExpanded.Rendering.CpuUniformBuffer Params { get; } = new();
            }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader();
                    shader.Source = new(); shader.Output = new(); shader.Work = new();
                    string before = VanillaGraphicsExpanded.Rendering.ShaderBindingSubmission.Calls;
                    shader.Use(); shader.Use();
                    return before + "|" + VanillaGraphicsExpanded.Rendering.ShaderBindingSubmission.Calls;
                }
            }
            """;
        var result = GeneratorFixture.Generate(source, supportSource: Support);
        string calls = result.Run();
        Assert.StartsWith("|", calls);
        Assert.Equal(2, calls.Split(';').Count(call => call == "sampler"));
        Assert.Equal(2, calls.Split("validate-block;").Length - 1);
        Assert.Equal(2, calls.Split(';').Count(call => call == "image"));
        Assert.Equal(2, calls.Split(';').Count(call => call == "storage"));
        Assert.True(calls.IndexOf("validate-storage;", StringComparison.Ordinal) < calls.IndexOf(";sampler;", StringComparison.Ordinal));
    }

    /// <summary>Generated shader state owns every non-UBO resource and ignores equal reassignment.</summary>
    [Fact]
    public void GeneratedStateRetainsNonUboBindingsAndSkipsEqualAssignments()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuTexture Source { get; set; }
                [ShaderBinding("Output", ShaderBindingKind.Image, 1, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuTexture Output { get; set; }
                [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 6, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.CpuUniformBuffer Params { get; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs
            {
                public VanillaGraphicsExpanded.Rendering.CpuUniformBuffer Params { get; } = new();
            }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader();
                    var source = new VanillaGraphicsExpanded.Rendering.GpuTexture();
                    shader.Source = source; shader.Source = source;
                    shader.Output = source; shader.Output = source;
                    return object.ReferenceEquals(shader.Source, source) + ":" + object.ReferenceEquals(shader.Output, source);
                }
            }
            """;
        var result = GeneratorFixture.Generate(source, supportSource: Support);
        Assert.Equal("True:True", result.Run());
        string generated = Assert.Single(result.Generated, text => text.Contains("private struct ShaderState", StringComparison.Ordinal));
        Assert.Contains("GpuTexture Source = default!;", generated, StringComparison.Ordinal);
        Assert.Contains("GpuTexture Output = default!;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("CpuUniformBuffer Params;", generated, StringComparison.Ordinal);
        Assert.Contains("EqualityComparer<global::VanillaGraphicsExpanded.Rendering.GpuTexture>.Default.Equals", generated, StringComparison.Ordinal);
    }

    /// <summary>An authored write-only resource cannot supply retained state to generated publication.</summary>
    [Fact]
    public void AuthoredWriteOnlyBindingIsRejected()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuTexture Source { set; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs
            {
                public VanillaGraphicsExpanded.Rendering.GpuTexture Source { set { } }
            }
            """;
        var result = GeneratorFixture.Generate(source, supportSource: Support);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.GetMessage().Contains("Source", StringComparison.Ordinal));
    }
    #endregion
}
