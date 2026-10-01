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
            public struct GpuTextureBinding { }
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
                public static string Run() { var shader = new Shader(); shader.Source = 12; shader.Use(); return VanillaGraphicsExpanded.Rendering.ShaderBindingSubmission.Calls; }
            }
            """;
        var result = GeneratorFixture.Generate(source, supportSource: Support);
        Assert.All(result.Generated, text => Assert.DoesNotContain("OpenTK", text));
        Assert.Equal($"validate-sampler;12:{target}:ShadowCompareLinearClamp;", result.Run());
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
