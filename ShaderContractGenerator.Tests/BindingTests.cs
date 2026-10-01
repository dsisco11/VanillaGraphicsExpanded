namespace ShaderContractGenerator.Tests;

/// <summary>Exercises property-driven bindings in normal and semantic-only offline compilations.</summary>
public sealed class BindingTests
{
    private const string Compute = """
        [ShaderProgram("Contract", "example", 1)]
        [ShaderStage("Contract", ShaderStageKind.Compute, "example.csh")]
        internal static partial class Shader
        {
            FIELDS
        }
        """;

    internal const string RuntimeBindingSupport = """
            namespace VanillaGraphicsExpanded.Rendering
            {
                public class GpuTexture { }
                public struct GpuTextureBinding { }
                public class GpuUniformBuffer { }
                public class GpuShaderStorageBuffer { }
                public class GpuComputePipeline { public object ProgramLayout = new(); public int ProgramId = 42; }
                public static class ShaderBindingAccess
                {
                    public static string Calls = "";
                    public static object ExpectedLayout;
                    public static object ExpectedTexture;
                    public static object ExpectedUniform;
                    public static object ExpectedStorage;
                    public static void Sampler(object layout, int program, string name, GpuTexture value)
                    { Calls += (object.ReferenceEquals(layout, ExpectedLayout) && program == 42 && object.ReferenceEquals(value, ExpectedTexture)) + ":" + name + ";"; }
                    public static void Image(object layout, int program, string name, GpuTextureBinding value)
                    { Calls += (object.ReferenceEquals(layout, ExpectedLayout) && program == 42) + ":" + name + ";"; }
                    public static void Image(object layout, int program, string name, GpuTexture value)
                    { Calls += (object.ReferenceEquals(layout, ExpectedLayout) && program == 42 && object.ReferenceEquals(value, ExpectedTexture)) + ":" + name + ";"; }
                    public static void UniformBlock(object layout, int program, string name, GpuUniformBuffer value)
                    { Calls += (object.ReferenceEquals(layout, ExpectedLayout) && program == 42 && object.ReferenceEquals(value, ExpectedUniform)) + ":" + name + ";"; }
                    public static void StorageBlock(object layout, int program, string name, GpuShaderStorageBuffer value)
                    { Calls += (object.ReferenceEquals(layout, ExpectedLayout) && program == 42 && object.ReferenceEquals(value, ExpectedStorage)) + ":" + name + ";"; }
                }
            }
            namespace VanillaGraphicsExpanded.Rendering.Shaders
            { public class GpuProgram { public object ProgramLayout = new(); public int ProgramId = 42; } }
            """;

    #region Public API
    /// <summary>Resource assignments invoke the owner's runtime boundary with the original value and GLSL name.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedSettersUseTheOwningLayout(bool computePipeline)
    {
        string support = RuntimeBindingSupport;
        string source = """
            [ShaderProgram("Contract", "example", 1)]
            [ShaderStage("Contract", ShaderStageKind.Compute, "example.csh")]
            internal partial class Shader BASE
            {
                TARGET
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)] public partial VanillaGraphicsExpanded.Rendering.GpuTexture Source { set; }
                [ShaderBinding("output", ShaderBindingKind.Image, 4, ShaderStageKind.Compute)] public partial VanillaGraphicsExpanded.Rendering.GpuTextureBinding Output { set; }
                [ShaderBinding("direct", ShaderBindingKind.Image, 5, ShaderStageKind.Compute)] public partial VanillaGraphicsExpanded.Rendering.GpuTexture Direct { set; }
                [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 5, ShaderStageKind.Compute)] public partial VanillaGraphicsExpanded.Rendering.GpuUniformBuffer Params { set; }
                [ShaderBinding("Work", ShaderBindingKind.StorageBlock, 6, ShaderStageKind.Compute)] public partial VanillaGraphicsExpanded.Rendering.GpuShaderStorageBuffer Work { set; }
                public void ObserveLayout() => VanillaGraphicsExpanded.Rendering.ShaderBindingAccess.ExpectedLayout = LAYOUT;
            }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); shader.ObserveLayout();
                    var texture = new VanillaGraphicsExpanded.Rendering.GpuTexture();
                    var uniform = new VanillaGraphicsExpanded.Rendering.GpuUniformBuffer();
                    var storage = new VanillaGraphicsExpanded.Rendering.GpuShaderStorageBuffer();
                    VanillaGraphicsExpanded.Rendering.ShaderBindingAccess.ExpectedTexture = texture;
                    VanillaGraphicsExpanded.Rendering.ShaderBindingAccess.ExpectedUniform = uniform;
                    VanillaGraphicsExpanded.Rendering.ShaderBindingAccess.ExpectedStorage = storage;
                    shader.Source = texture; shader.Output = new(); shader.Direct = texture; shader.Params = uniform; shader.Work = storage;
                    return VanillaGraphicsExpanded.Rendering.ShaderBindingAccess.Calls;
                }
            }
            """;
        source = source.Replace("BASE", computePipeline ? "" : ": VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram")
            .Replace("TARGET", computePipeline ? "private readonly VanillaGraphicsExpanded.Rendering.GpuComputePipeline pipeline = new();" : "")
            .Replace("LAYOUT", computePipeline ? "pipeline.ProgramLayout" : "ProgramLayout");
        Assert.Equal("True:source;True:output;True:direct;True:Params;True:Work;", GeneratorFixture.Generate(source, supportSource: support).Run());
        var offline = GeneratorFixture.Generate(source, offline: true, supportSource: support);
        offline.Compile();
        Assert.All(offline.Generated, generated => Assert.DoesNotContain("ShaderBindingAccess", generated));
    }

    /// <summary>Attributed partial properties populate all namespaces identically in both consumers.</summary>
    [Fact]
    public void OfflineAndRuntimeBindingsAreIdenticalAndImmutable()
    {
        string source = Compute.Replace("FIELDS", """
            [ShaderBinding("source", ShaderBindingKind.UniformLocation, 17, ShaderStageKind.Compute)] private static partial ShaderUniformLocationBinding Location { get; }
            [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute, Required = false)] private static partial ShaderSamplerBinding Sampler { get; }
            [ShaderBinding("output", ShaderBindingKind.Image, 3, ShaderStageKind.Compute)] private static partial ShaderImageBinding Image { get; }
            [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 3, ShaderStageKind.Compute)] private static partial ShaderUniformBlockBinding Uniform { get; }
            [ShaderBinding("Work", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Compute)] private static partial ShaderStorageBlockBinding Storage { get; }
            """) + """
            public static class Proof
            {
                public static string Run()
                {
                    var b = Shader.Contract.Stages[0].Bindings;
                    bool frozen = false;
                    try { b.Samplers.Clear(); } catch (System.NotSupportedException) { frozen = true; }
                    return b.UniformLocations["source"] + ":" + b.Samplers["source"].Slot + ":" + b.Samplers["source"].Required
                        + ":" + b.Images["output"].Slot + ":" + b.UniformBlocks["Params"].Slot + ":" + b.StorageBlocks["Work"].Slot + ":" + frozen;
                }
            }
            """;
        Assert.Equal("17:3:False:3:3:3:True", GeneratorFixture.Generate(source).Run());
        var runtime = GeneratorFixture.Generate(source);
        var offline = GeneratorFixture.Generate(source, true);
        runtime.Compile(); offline.Compile();
        Assert.Equal(runtime.Generated.Select(s => s.Replace("static partial global::", "static global::")), offline.Generated);
    }

    /// <summary>Shared properties, including diamond imports, retain one owner and explicit stage filtering.</summary>
    [Fact]
    public void SharedLayoutsAndInheritanceRespectStages()
    {
        string source = """
            internal partial class Base
            {
                [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment)] private static partial ShaderUniformBlockBinding Params { get; }
            }
            [ShaderBindingSet(typeof(Base))] internal static partial class Shared { }
            [ShaderBindingSet(typeof(Shared))]
            [ShaderBindingSet(typeof(Base))]
            [ShaderProgram("Contract", "example", 1)]
            [ShaderStage("Contract", ShaderStageKind.Vertex, "shared.vsh")]
            [ShaderStage("Contract", ShaderStageKind.Fragment, "example.fsh")]
            internal partial class Shader : Base
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment)] private static partial ShaderSamplerBinding Source { get; }
            }
            public static class Proof { public static string Run() => Shader.Contract.Stages[0].Bindings.Samplers.Count + ":"
                + Shader.Contract.Stages[1].Bindings.Samplers["source"].Slot + ":" + Shader.Contract.Stages[1].Bindings.UniformBlocks.Count; }
            """;
        Assert.Equal("0:2:1", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }

    /// <summary>A consumer's import filter retains vertex declarations while excluding the same properties from its fragment.</summary>
    [Fact]
    public void SharedStageImportDoesNotLeakIntoOtherStages()
    {
        string source = DeclarationTests.Basic.Replace("internal static partial class Shader",
            "[ShaderBindingSet(typeof(Shared), Stages = new[] { ShaderStageKind.Vertex })]\ninternal static partial class Shader") + """
            internal static partial class Shared
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment)] private static partial ShaderSamplerBinding Source { get; }
            }
            public static class Proof { public static string Run() => Shader.Contract.Stages[0].Bindings.Samplers["source"].Slot
                + ":" + Shader.Contract.Stages[1].Bindings.Samplers.Count; }
            """;
        Assert.Equal("7:0", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }

    /// <summary>One property selects several programs and defaults retain explicit required-resource behavior.</summary>
    [Fact]
    public void ProgramSubsetsAndIncludeDefaultsPreserveOwnership()
    {
        string source = """
            internal static partial class Defaults
            {
                [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 1, ShaderStageKind.Compute, Required = false)] private static partial ShaderUniformBlockBinding Params { get; }
            }
            [ShaderBindingSet(typeof(Defaults), Defaults = true)]
            [ShaderProgram("One", "one", 1)] [ShaderStage("One", ShaderStageKind.Compute, "one.csh")]
            [ShaderProgram("Two", "two", 1)] [ShaderStage("Two", ShaderStageKind.Compute, "two.csh")]
            [ShaderProgram("Three", "three", 1)] [ShaderStage("Three", ShaderStageKind.Compute, "three.csh")]
            internal static partial class Shader
            {
                [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 4, ShaderStageKind.Compute, Programs = new[] { "One", "Two" })] private static partial ShaderUniformBlockBinding Params { get; }
            }
            public static class Proof { public static string Run() => Shader.One.Stages[0].Bindings.UniformBlocks["Params"].Slot + ":"
                + Shader.Two.Stages[0].Bindings.UniformBlocks["Params"].Required + ":" + Shader.Three.Stages[0].Bindings.UniformBlocks["Params"].Required; }
            """;
        Assert.Equal("4:True:False", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }

    /// <summary>Unsupported field storage, invalid indices, kinds, stages and conflicting namespaces fail before emission.</summary>
    [Theory]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private string Source = \"x\";")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static int Source = 1;")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static partial int Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static partial ShaderImageBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding Source { get; set; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, -1, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", (ShaderBindingKind)99, 1, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler)] private const int Source = 1;")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, (ShaderStageKind)99)] private static partial ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex)] private static partial ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, Program = \"Missing\")] private static partial ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, Programs = new string[0])] private static partial ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, Program = \"Contract\", Programs = new[] { \"Contract\" })] private static partial ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"bad-name\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding A { get; } [ShaderBinding(\"source\", ShaderBindingKind.Sampler, 2, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding B { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding A { get; } [ShaderBinding(\"other\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding B { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding A { get; } [ShaderBinding(\"source\", ShaderBindingKind.Image, 2, ShaderStageKind.Compute)] private static partial ShaderImageBinding B { get; }")]
    public void InvalidPropertiesProduceCompileTimeDiagnostics(string fields)
    {
        foreach (bool offline in new[] { false, true })
        {
            var result = GeneratorFixture.Generate(Compute.Replace("FIELDS", fields), offline);
            Assert.Contains(result.Diagnostics, d => d.Id == "VGEGEN001");
            Assert.Empty(result.Generated);
        }
    }

    /// <summary>Same shader-stage identity cannot acquire a different binding layout on a second program.</summary>
    [Fact]
    public void IncompatibleSharedStageBindingsFail()
    {
        string first = Compute.Replace("FIELDS", "[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding Source { get; }");
        string second = first.Replace("class Shader", "class Other").Replace("\"example\", 1", "\"other\", 1").Replace("Sampler, 1,", "Sampler, 2,");
        var result = GeneratorFixture.Generate(first + second);
        Assert.Contains(result.Diagnostics, d => d.GetMessage().Contains("Conflicting shared stage"));
        Assert.Empty(result.Generated);
    }

    /// <summary>Explicit sets reject recursive references instead of recursing during generator execution.</summary>
    [Fact]
    public void CyclicSharedLayoutsFail()
    {
        string source = Compute.Replace("[ShaderProgram", "[ShaderBindingSet(typeof(Shared))]\n[ShaderProgram").Replace("FIELDS", "")
            + "[ShaderBindingSet(typeof(Shader))] internal static partial class Shared { }";
        Assert.Contains(GeneratorFixture.Generate(source).Diagnostics, d => d.GetMessage().Contains("Cyclic binding set"));
    }

    /// <summary>Distinct defaults cannot select conflicting slots according to import traversal order.</summary>
    [Fact]
    public void ConflictingIncludeDefaultsFail()
    {
        string source = Compute.Replace("FIELDS", "").Replace("[ShaderProgram", "[ShaderBindingSet(typeof(One), Defaults = true)]\n[ShaderBindingSet(typeof(Two), Defaults = true)]\n[ShaderProgram") + """
            internal static partial class One
            {
                [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 1, ShaderStageKind.Compute)] private static partial ShaderUniformBlockBinding Params { get; }
            }
            internal static partial class Two
            {
                [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 2, ShaderStageKind.Compute)] private static partial ShaderUniformBlockBinding Params { get; }
            }
            """;
        Assert.Contains(GeneratorFixture.Generate(source).Diagnostics, d => d.GetMessage().Contains("Conflicting default binding"));
    }

    /// <summary>Enabled and disabled structural variants keep the same immutable resource union.</summary>
    [Fact]
    public void StructuralVariantsShareTheGeneratedBindingContract()
    {
        string source = DeclarationTests.Basic.Replace("internal static partial class Shader", "[ShaderBindingSet(typeof(Shared))]\ninternal static partial class Shader") + """
            internal static partial class Shared
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 7, ShaderStageKind.Fragment, Required = false)] private static partial ShaderSamplerBinding Source { get; }
            }
            public static class Proof
            {
                public static string Run()
                {
                    var resolver = new ShaderVariantResolver(new[] { Shader.Contract });
                    var variants = System.Linq.Enumerable.Where(resolver.Binaries, s => s.Stage.Kind == ShaderStageKind.Fragment);
                    return System.Linq.Enumerable.Count(variants) + ":" + System.Linq.Enumerable.All(variants,
                        s => object.ReferenceEquals(s.Stage.Bindings, Shader.Contract.Stages[1].Bindings) && s.Stage.Bindings.Samplers["source"].Slot == 7);
                }
            }
            """;
        Assert.Equal("2:True", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }
    #endregion
}
