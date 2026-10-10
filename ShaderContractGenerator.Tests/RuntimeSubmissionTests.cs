namespace ShaderContractGenerator.Tests;

/// <summary>Checks contract-generated publication and retained assignments independently of the graphics driver.</summary>
public sealed class RuntimeSubmissionTests
{
    private const string Support = """
        namespace VanillaGraphicsExpanded.Rendering
        {
            public static class ShaderInputSnapshot
            {
                public static T Copy<T>(T value) => value is System.Array array ? (T)(object)array.Clone() : value;
            }
            public class GpuTexture { }
            public class CpuUniformBuffer { }
            public class GpuShaderStorageBuffer { }
            public class GpuAtomicCounterBuffer { }
            public readonly record struct GpuStorageBufferBinding(GpuShaderStorageBuffer Buffer, int Offset = 0, int Size = 0);
            public readonly record struct GpuTextureBinding(GpuTexture Texture, int Level = 0, int Layer = 0, bool Layered = false, int Access = 0, int Format = 0);
            public struct ShaderInputValidation
            {
                public ulong Resolve(object owner, ulong identity) => identity;
            }
            public static class SubmissionRecorder
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
                public static void ValidateSampler(ulong binding, GpuTexture texture, ref ShaderInputValidation history) { SubmissionRecorder.Calls += "validate-sampler;"; }
                public static void ValidateImage(ulong binding, GpuTexture texture, ref ShaderInputValidation history) { SubmissionRecorder.Calls += "validate-image;"; }
                public static void ValidateImage(ulong binding, GpuTextureBinding image, ref ShaderInputValidation history) { SubmissionRecorder.Calls += "validate-image;"; }
                public static void ValidateStorageBlock(ulong binding, GpuStorageBufferBinding range, ref ShaderInputValidation history) { SubmissionRecorder.Calls += "validate-storage;"; }
                public static ulong Resolve(object owner, ulong identity) => identity;
                public static int ValidateSampler(ulong binding, int texture) { SubmissionRecorder.Calls += "validate-sampler;"; return texture; }
                public static void ValidateSampler(params object[] args) { SubmissionRecorder.Calls += "validate-sampler;"; }
                public static void ValidateUniformBlock(params object[] args) { SubmissionRecorder.Calls += "validate-block;"; }
                public static void Sampler(params object[] args) { SubmissionRecorder.Calls += "sampler;"; }
                public static void UniformBlock(params object[] args) { SubmissionRecorder.Calls += "block;"; }
                public static void ValidateImage(params object[] args) { SubmissionRecorder.Calls += "validate-image;"; }
                public static void ValidateStorageBlock(params object[] args) { SubmissionRecorder.Calls += "validate-storage;"; }
                public static void Image(params object[] args) { SubmissionRecorder.Calls += "image;"; }
                public static void StorageBlock(params object[] args) { SubmissionRecorder.Calls += "storage;"; }
                public static void ValidateAtomicCounter(params object[] args) { SubmissionRecorder.Calls += "validate-counter;"; }
                public static void AtomicCounter(params object[] args) { SubmissionRecorder.Calls += "counter;"; }
            }
            public interface IGpuProgram : System.IDisposable
            {
                void PublishInputs();
                void Activate() { PublishInputs(); }
            }
            public abstract class GpuComputeProgram : IGpuProgram
            {
                private bool retired;
                protected abstract void Submit();
                protected void RequireInputMutation() { if (retired) throw new System.ObjectDisposedException("owner"); }
                void IGpuProgram.PublishInputs() { RequireInputMutation(); Submit(); }
                public void Dispatch() { ((IGpuProgram)this).Activate(); }
                public void Dispose() { retired = true; }
            }
        }
        namespace VanillaGraphicsExpanded.Rendering.Shaders
        {
            public abstract class GpuProgram : VanillaGraphicsExpanded.Rendering.IGpuProgram
            {
                private bool retired;
                protected abstract void Submit();
                protected void RequireInputMutation() { if (retired) throw new System.ObjectDisposedException("owner"); }
                void VanillaGraphicsExpanded.Rendering.IGpuProgram.PublishInputs() { RequireInputMutation(); Submit(); }
                public void Use() { ((VanillaGraphicsExpanded.Rendering.IGpuProgram)this).Activate(); }
                public void Dispose() { retired = true; }
            }
        }
        """;
    private const string Header = """
        [ShaderProgram("Contract", "example", 1)]
        [ShaderStage("Contract", ShaderStageKind.Vertex, "example.vsh")]
        [ShaderStage("Contract", ShaderStageKind.Fragment, "example.fsh")]
        """;

    #region Public API
    /// <summary>Complete view/range payloads participate in generated change tracking while equal assignments skip.</summary>
    [Fact]
    public void GeneratedViewAndRangeStateRecordsOnlyActualChanges()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("image", ShaderBindingKind.Image, 1, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuTextureBinding Image { get; set; }
                [ShaderBinding("storage", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuStorageBufferBinding Storage { get; set; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs
            {
                public ulong Changes => __inputRevision;
            }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader();
                    var view = new VanillaGraphicsExpanded.Rendering.GpuTextureBinding(new());
                    shader.Image = view; shader.Image = view;
                    shader.Image = view = view with { Level = 1 };
                    shader.Image = view = view with { Layer = 1 };
                    shader.Image = view = view with { Layered = true };
                    shader.Image = view = view with { Access = 1 };
                    shader.Image = view = view with { Format = 1 };
                    shader.Image = view; shader.Image = view with { Texture = new() };
                    var range = new VanillaGraphicsExpanded.Rendering.GpuStorageBufferBinding(new(), 0, 4);
                    shader.Storage = range; shader.Storage = range;
                    shader.Storage = range = range with { Offset = 4 };
                    shader.Storage = range = range with { Size = 8 };
                    shader.Storage = range; shader.Storage = range with { Buffer = new() };
                    return shader.Changes.ToString();
                }
            }
            """;
        Assert.Equal("11", GeneratorFixture.Generate(source, supportSource: Support).Run());
    }

    /// <summary>Authored getters are sampled once and retained even when the scene source changes.</summary>
    [Fact]
    public void AuthoredSourcesUpdateTheGeneratedStateOncePerUse()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment)]
                VanillaGraphicsExpanded.Rendering.GpuTexture Source { get; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs
            {
                public int Reads;
                public VanillaGraphicsExpanded.Rendering.GpuTexture Current;
                public VanillaGraphicsExpanded.Rendering.GpuTexture Source { get { Reads++; return Current; } }
                public bool Retained() => object.ReferenceEquals(__activeState.Source, Current);
            }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader { Current = new() };
                    shader.Use(); bool first = shader.Retained();
                    shader.Current = new(); shader.Use();
                    return first + ":" + shader.Retained() + ":" + shader.Reads;
                }
            }
            """;
        Assert.Equal("True:True:2", GeneratorFixture.Generate(source, supportSource: Support).Run());
    }

    /// <summary>Numeric runtime inputs must use blocks rather than generated ordinary GL uploads.</summary>
    [Theory]
    [InlineData("float")]
    [InlineData("float[]")]
    [InlineData("int")]
    [InlineData("bool")]
    [InlineData("System.Numerics.Vector2")]
    [InlineData("System.Numerics.Vector3")]
    [InlineData("System.Numerics.Vector4")]
    [InlineData("System.Numerics.Matrix4x4")]
    public void OrdinaryNumericBindingsRequireUniformBlocks(string type)
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("value", ShaderBindingKind.UniformLocation, 120, ShaderStageKind.Fragment)]
                float Value { get; set; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs { }
            """;
        var result = GeneratorFixture.Generate(source.Replace("float Value", type + " Value", StringComparison.Ordinal), supportSource: Support);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "VGEGEN001" &&
            diagnostic.GetMessage().Contains("must use a uniform block", StringComparison.Ordinal));
    }

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
                    string before = VanillaGraphicsExpanded.Rendering.SubmissionRecorder.Calls;
                    shader.Use();
                    return before + "|" + VanillaGraphicsExpanded.Rendering.SubmissionRecorder.Calls;
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
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.GpuComputeProgram, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader();
                    shader.Work = new(); shader.Count = new();
                    string before = VanillaGraphicsExpanded.Rendering.SubmissionRecorder.Calls;
                    shader.Dispatch(); shader.Dispatch();
                    return before + "|" + VanillaGraphicsExpanded.Rendering.SubmissionRecorder.Calls;
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

    /// <summary>Abstract compute families retain generated input guards and default workflow dispatch on concrete shells.</summary>
    [Fact]
    public void ComputeIntermediateFamilyUsesDefaultWorkflowAndRetirementGuard()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("Work", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
                VanillaGraphicsExpanded.Rendering.GpuStorageBufferBinding Work { get; set; }
            }
            internal abstract class Family : VanillaGraphicsExpanded.Rendering.GpuComputeProgram { }
            [ShaderProgram("Contract", "example", 1)]
            [ShaderStage("Contract", ShaderStageKind.Compute, "example.csh")]
            internal partial class Shader : Family, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); shader.Work = new();
                    ((VanillaGraphicsExpanded.Rendering.IGpuProgram)shader).Activate();
                    shader.Dispatch();
                    ((System.IDisposable)shader).Dispose();
                    try { shader.Work = new(); return "unguarded"; }
                    catch (System.ObjectDisposedException) { return VanillaGraphicsExpanded.Rendering.SubmissionRecorder.Calls; }
                }
            }
            """;
        var runtime = GeneratorFixture.Generate(source, supportSource: Support);
        Assert.Equal("validate-storage;storage;validate-storage;storage;", runtime.Run());
        GeneratorFixture.Generate(source, true, supportSource: Support).Compile();
    }

    /// <summary>Specialized compute publication remains authored rather than receiving a conflicting generated override.</summary>
    [Fact]
    public void AuthoredComputeSubmitPreservesFamilyFacade()
    {
        string source = """
            [ShaderProgram("Contract", "example", 1)]
            [ShaderStage("Contract", ShaderStageKind.Compute, "example.csh")]
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.GpuComputeProgram
            {
                public int Calls;
                protected override void Submit() { Calls++; }
            }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); shader.Dispatch();
                    ((VanillaGraphicsExpanded.Rendering.IGpuProgram)shader).Activate();
                    return shader.Calls.ToString();
                }
            }
            """;
        var runtime = GeneratorFixture.Generate(source, supportSource: Support);
        Assert.Equal("2", runtime.Run());
        Assert.All(runtime.Generated, text => Assert.DoesNotContain("protected override void Submit()", text));
        GeneratorFixture.Generate(source, true, supportSource: Support).Compile();
    }

    /// <summary>A sealed authored Submit inherited through an abstract compute family remains the publication owner.</summary>
    [Fact]
    public void InheritedAuthoredComputeSubmitIsPreserved()
    {
        string source = """
            internal abstract class Family : VanillaGraphicsExpanded.Rendering.GpuComputeProgram
            {
                public int Calls;
                protected sealed override void Submit() { Calls++; }
            }
            [ShaderProgram("Contract", "example", 1)]
            [ShaderStage("Contract", ShaderStageKind.Compute, "example.csh")]
            internal partial class Shader : Family { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); shader.Dispatch();
                    ((VanillaGraphicsExpanded.Rendering.IGpuProgram)shader).Activate();
                    return shader.Calls.ToString();
                }
            }
            """;
        var runtime = GeneratorFixture.Generate(source, supportSource: Support);
        Assert.Equal("2", runtime.Run());
        Assert.All(runtime.Generated, text => Assert.DoesNotContain("protected override void Submit()", text));
        GeneratorFixture.Generate(source, true, supportSource: Support).Compile();
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
                    return VanillaGraphicsExpanded.Rendering.SubmissionRecorder.Calls + "|" +
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
                    string before = VanillaGraphicsExpanded.Rendering.SubmissionRecorder.Calls;
                    shader.Use(); shader.Use();
                    return before + "|" + VanillaGraphicsExpanded.Rendering.SubmissionRecorder.Calls;
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
        Assert.Contains("global::System.Object.ReferenceEquals", generated, StringComparison.Ordinal);
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
