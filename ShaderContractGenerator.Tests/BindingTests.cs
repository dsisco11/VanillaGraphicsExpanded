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
            { public class GpuProgram { public object ProgramLayout = new(); public int ProgramId = 42; protected virtual void Submit() { } } }
            """;

    #region Public API
    /// <summary>Every independent namespace retains its index and required-resource policy in both consumers.</summary>
    [Fact]
    public void AllBindingNamespacesRoundTripOffline()
    {
        string source = """
            interface IBindings
            {
                [ShaderBinding("position", ShaderBindingKind.UniformLocation, 2, ShaderStageKind.Compute)] ShaderUniformLocationBinding Position { get; }
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute, Required = false)] ShaderSamplerBinding Source { get; }
                [ShaderBinding("output", ShaderBindingKind.Image, 4, ShaderStageKind.Compute)] ShaderImageBinding Output { get; }
                [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 5, ShaderStageKind.Compute)] ShaderUniformBlockBinding Params { get; }
                [ShaderBinding("Work", ShaderBindingKind.StorageBlock, 6, ShaderStageKind.Compute)] ShaderStorageBlockBinding Work { get; }
                [ShaderBinding("uv", ShaderBindingKind.VaryingLocation, 7, ShaderStageKind.Compute)] ShaderVaryingLocationBinding Uv { get; }
                [ShaderBinding("color", ShaderBindingKind.FragmentOutputLocation, 8, ShaderStageKind.Compute)] ShaderFragmentOutputLocationBinding Color { get; }
            }
            """ + Compute.Replace("FIELDS", "").Replace("[ShaderProgram", "[ShaderBindingSet(typeof(IBindings))]\n[ShaderProgram") + """
            public static class Proof { public static string Run() => Shader.Contract.Stages[0].Bindings.UniformLocations["position"] + ":"
                + Shader.Contract.Stages[0].Bindings.Samplers["source"].Required + ":" + Shader.Contract.Stages[0].Bindings.Images["output"].Slot + ":"
                + Shader.Contract.Stages[0].Bindings.UniformBlocks["Params"].Slot + ":" + Shader.Contract.Stages[0].Bindings.StorageBlocks["Work"].Slot + ":"
                + Shader.Contract.Stages[0].Bindings.VaryingLocations["uv"] + ":" + Shader.Contract.Stages[0].Bindings.FragmentOutputLocations["color"]; }
            """;
        Assert.Equal("2:False:4:5:6:7:8", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }

    /// <summary>A fixed sampler contract carries its unit without an authored uniform address.</summary>
    [Fact]
    public void SamplerContractDoesNotEmitUniformLocations()
    {
        string source = """
            interface IBindings
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)]
                ShaderSamplerBinding Source { get; }
            }
            """ + Compute.Replace("FIELDS", "").Replace("[ShaderProgram", "[ShaderBindingSet(typeof(IBindings))]\n[ShaderProgram") + """
            public static class Proof { public static string Run() => Shader.Contract.Stages[0].Bindings.Samplers["source"].Slot + ":"
                + Shader.Contract.Stages[0].Bindings.UniformLocations.Count; }
            """;
        Assert.Equal("3:0", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }

    /// <summary>Legacy class attributes and class imports cannot reintroduce competing binding ownership.</summary>
    [Theory]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private static partial ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] private const int Source = 1;")]
    public void ClassBindingOwnershipIsRejected(string fields)
    {
        foreach (bool offline in new[] { false, true })
        {
            var result = GeneratorFixture.Generate(Compute.Replace("FIELDS", fields), offline);
            Assert.Contains(result.Diagnostics, d => d.Id == "VGEGEN001");
            Assert.Empty(result.Generated);
        }
    }

    /// <summary>Metadata imports require interfaces even when a class happens to expose no binding properties.</summary>
    [Fact]
    public void ClassBindingImportsAreRejected()
    {
        var result = GeneratorFixture.Generate(Compute.Replace("FIELDS", "").Replace("[ShaderProgram", "[ShaderBindingSet(typeof(Legacy))]\n[ShaderProgram") + "internal partial class Legacy { }");
        Assert.Contains(result.Diagnostics, d => d.GetMessage().Contains("interface"));
        Assert.Empty(result.Generated);
    }

    /// <summary>Malformed interface metadata fails before either consumer publishes a partial catalog.</summary>
    [Theory]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, -1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", (ShaderBindingKind)99, 1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", (ShaderBindingKind)(-1), 1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, (ShaderStageKind)99)] ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, Programs = new string[0])] ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, Program = \"Contract\", Programs = new[] { \"Contract\" })] ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"bad-name\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderImageBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; set; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] static ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] float Source { set; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding A { get; } [ShaderBinding(\"source\", ShaderBindingKind.Sampler, 2, ShaderStageKind.Compute)] ShaderSamplerBinding B { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding A { get; } [ShaderBinding(\"other\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding B { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding A { get; } [ShaderBinding(\"source\", ShaderBindingKind.Image, 2, ShaderStageKind.Compute)] ShaderImageBinding B { get; }")]
    public void InvalidPropertiesProduceCompileTimeDiagnostics(string fields)
    {
        string source = "interface IBindings { " + fields + " }" + Compute.Replace("FIELDS", "").Replace("[ShaderProgram", "[ShaderBindingSet(typeof(IBindings))]\n[ShaderProgram");
        foreach (bool offline in new[] { false, true })
        {
            var result = GeneratorFixture.Generate(source, offline);
            Assert.Contains(result.Diagnostics, d => d.Id == "VGEGEN001");
            Assert.Empty(result.Generated);
        }
    }

    /// <summary>Same shader-stage identity cannot acquire a different layout on a second program.</summary>
    [Fact]
    public void IncompatibleSharedStageBindingsFail()
    {
        string source = """
            interface IFirst { [ShaderBinding("source", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; } }
            interface ISecond { [ShaderBinding("source", ShaderBindingKind.Sampler, 2, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; } }
            [ShaderBindingSet(typeof(IFirst))] [ShaderProgram("Contract", "example", 1)] [ShaderStage("Contract", ShaderStageKind.Compute, "example.csh")] internal static partial class Shader { }
            [ShaderBindingSet(typeof(ISecond))] [ShaderProgram("Contract", "other", 1)] [ShaderStage("Contract", ShaderStageKind.Compute, "example.csh")] internal static partial class Other { }
            """;
        var result = GeneratorFixture.Generate(source);
        Assert.Contains(result.Diagnostics, d => d.GetMessage().Contains("Conflicting shared stage"));
        Assert.Empty(result.Generated);
    }

    /// <summary>Recursive interface imports fail without unbounded traversal.</summary>
    [Fact]
    public void CyclicSharedLayoutsFail()
    {
        string source = Compute.Replace("FIELDS", "").Replace("[ShaderProgram", "[ShaderBindingSet(typeof(IOne))]\n[ShaderProgram") + """
            [ShaderBindingSet(typeof(ITwo))] interface IOne { }
            [ShaderBindingSet(typeof(IOne))] interface ITwo { }
            """;
        Assert.Contains(GeneratorFixture.Generate(source).Diagnostics, d => d.GetMessage().Contains("Cyclic binding set"));
    }

    /// <summary>Independent defaults cannot select conflicting slots according to traversal order.</summary>
    [Fact]
    public void ConflictingIncludeDefaultsFail()
    {
        string source = Compute.Replace("FIELDS", "").Replace("[ShaderProgram", "[ShaderBindingSet(typeof(IOne), Defaults = true)]\n[ShaderBindingSet(typeof(ITwo), Defaults = true)]\n[ShaderProgram") + """
            interface IOne { [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 1, ShaderStageKind.Compute)] ShaderUniformBlockBinding Params { get; } }
            interface ITwo { [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 2, ShaderStageKind.Compute)] ShaderUniformBlockBinding Params { get; } }
            """;
        Assert.Contains(GeneratorFixture.Generate(source).Diagnostics, d => d.GetMessage().Contains("Conflicting default binding"));
    }

    /// <summary>Structural variants reuse the same immutable union even when a resource is optimized away.</summary>
    [Fact]
    public void StructuralVariantsShareTheGeneratedBindingContract()
    {
        string source = DeclarationTests.Basic.Replace("internal static partial class Shader", "[ShaderBindingSet(typeof(IShared))]\ninternal static partial class Shader") + """
            interface IShared { [ShaderBinding("source", ShaderBindingKind.Sampler, 7, ShaderStageKind.Fragment, Required = false)] ShaderSamplerBinding Source { get; } }
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
    /// <summary>Resource arrays reject occupied ranges without conflating image and sampler namespaces.</summary>
    [Theory]
    [InlineData("Sampler", true)]
    [InlineData("Image", false)]
    public void ArrayRangesUseIndependentResourceNamespaces(string secondKind, bool conflicts)
    {
        string source = """
            interface IResources
            {
                [ShaderBinding("first", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute, ArrayLength = 2, ShaderType = ShaderResourceType.Sampler2D)]
                ShaderSamplerBinding First { get; }
                [ShaderBinding("second", ShaderBindingKind.KIND, 4, ShaderStageKind.Compute)]
                ShaderKINDBinding Second { get; }
            }
            [ShaderBindingSet(typeof(IResources))]
            """.Replace("KIND", secondKind) + Compute.Replace("FIELDS", "");
        foreach (bool offline in new[] { false, true })
        {
            var result = GeneratorFixture.Generate(source, offline);
            if (conflicts) Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "VGEGEN001");
            else result.Compile();
        }
    }

    /// <summary>Authoritative metadata survives generation and internal indices are independent of unit numbers.</summary>
    [Fact]
    public void ResourceMetadataAndStableIndicesAreGenerated()
    {
        string source = """
            interface IResources
            {
                [ShaderBinding("z", ShaderBindingKind.Sampler, 7, ShaderStageKind.Compute, ArrayLength = 2, ShaderType = ShaderResourceType.Sampler2D, Required = false)]
                ShaderSamplerBinding Last { get; }
                [ShaderBinding("a", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)]
                ShaderSamplerBinding First { get; }
            }
            [ShaderBindingSet(typeof(IResources))]
            """ + Compute.Replace("FIELDS", "") + """
            public static class Proof
            {
                public static string Run()
                {
                    var entries = Shader.Contract.Bindings.Entries;
                    return entries[0].Index + ":" + entries[0].Name + ":" + entries[1].Index + ":" +
                        entries[1].Binding.Slot + ":" + entries[1].Binding.ArrayLength + ":" + entries[1].Binding.ShaderType;
                }
            }
            """;
        Assert.Equal("0:a:1:7:2:Sampler2D", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }
    /// <summary>Explicit target metadata is independent of texture wrapper names.</summary>
    [Theory]
    [InlineData("Texture2D", 3553)]
    [InlineData("DynamicTexture2D", 3553)]
    [InlineData("RenamedTexture", 3553)]
    [InlineData("Texture3D", 0)]
    [InlineData("GpuTexture", 0)]
    public void ExplicitTextureTargetsReachPreparedContract(string textureType, int target)
    {
        string source = """
            interface IResources
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute TARGET)]
                VanillaGraphicsExpanded.Rendering.TEXTURE Source { set; }
            }
            [ShaderBindingSet(typeof(IResources))]
            """.Replace("TEXTURE", textureType).Replace("TARGET", target == 0 ? "" : ", TextureTarget = ShaderTextureTarget.Texture2D") + Compute.Replace("FIELDS", "") + """
            public static class Proof
            {
                public static string Run() => Shader.Contract.Bindings.Samplers["source"].TextureTarget.ToString();
            }
            """;
        string support = RuntimeBindingSupport + """
            namespace VanillaGraphicsExpanded.Rendering
            {
                /// <summary>Models fixed two-dimensional texture storage.</summary>
                public class Texture2D : GpuTexture { }
                /// <summary>Models fixed two-dimensional dynamic texture storage.</summary>
                public class DynamicTexture2D : GpuTexture { }
                /// <summary>Models a wrapper whose name conveys no storage dimension.</summary>
                public class RenamedTexture : GpuTexture { }
                /// <summary>Models runtime-selectable three-dimensional or array texture storage.</summary>
                public class Texture3D : GpuTexture { }
            }
            """;
        Assert.Equal(target.ToString(), GeneratorFixture.Generate(source, supportSource: support).Run());
        GeneratorFixture.Generate(source, true, supportSource: support).Compile();
    }
    #endregion
}
