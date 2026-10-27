namespace ShaderContractGenerator.Tests;

/// <summary>Verifies interface composition, implementation ownership and pure offline binding contracts.</summary>
public sealed class InterfaceBindingTests
{
    private const string Header = """
        [ShaderProgram("Contract", "example", 1)]
        [ShaderStage("Contract", ShaderStageKind.Compute, "example.csh")]
        """;

    #region Public API
    /// <summary>Graphics and compute owners retain legal inputs and freeze them without immediate native execution.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourceInterfacesGenerateRetainedCapture(bool compute)
    {
        string source = """
            internal interface IResources
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)]
                VanillaGraphicsExpanded.Rendering.GpuTextureRef Source { get; set; }
                [ShaderBinding("output", ShaderBindingKind.Image, 4, ShaderStageKind.Compute)]
                VanillaGraphicsExpanded.Rendering.RHI.RHIImageBinding Output { get; set; }
                [ShaderBinding("Work", ShaderBindingKind.StorageBlock, 7, ShaderStageKind.Compute)]
                VanillaGraphicsExpanded.Rendering.GpuShaderStorageBuffer Work { get; set; }
            }
            """ + Header + "internal partial class Shader : " + (compute ? "VanillaGraphicsExpanded.Rendering.GpuComputeProgram" : "VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram") + ", IResources { }" + """
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); var source = new VanillaGraphicsExpanded.Rendering.GpuTextureRef(new object());
                    var work = new VanillaGraphicsExpanded.Rendering.GpuShaderStorageBuffer();
                    shader.Source = source; shader.Output = new(source, 2, false, 3); shader.Work = work;
                    var captured = shader.CaptureBindings();
                    return (captured.Samplers[0].Texture == source) + ":" + captured.Images[0].View.Level + ":"
                        + captured.Images[0].View.Layer + ":" + object.ReferenceEquals(captured.Buffers[0].Buffer.Resource, work);
                }
            }
            """;
        Assert.Equal("True:2:3:True", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
        var offline = GeneratorFixture.Generate(source, true, supportSource: RuntimeCaptureSupport.Source);
        offline.Compile();
        Assert.All(offline.Generated, text => Assert.DoesNotContain("CaptureBindingsCore", text));
    }

    /// <summary>Capture recognizes a concrete owner through an abstract family, without inspecting fields for a pipeline.</summary>
    [Fact]
    public void DerivedComputeOwnerCapturesInheritedAuthoredResources()
    {
        string source = """
            internal interface IResources
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)]
                VanillaGraphicsExpanded.Rendering.GpuTextureRef Source { get; }
            }
            internal abstract partial class Family : VanillaGraphicsExpanded.Rendering.GpuComputeProgram, IResources
            {
                public VanillaGraphicsExpanded.Rendering.GpuTextureRef Source { get; } = new(new object());
            }
            """ + Header + """
            internal partial class Shader : Family { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); return (shader.CaptureBindings().Samplers[0].Texture == shader.Source).ToString();
                }
            }
            """;
        Assert.Equal("True", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
    }

    /// <summary>Diamonds retain one owner and derived redeclarations explicitly replace the inherited index.</summary>
    [Fact]
    public void InterfaceDiamondsAndDerivedOverridesWork()
    {
        string source = """
            internal interface IBase
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; }
            }
            internal interface ILeft : IBase { }
            internal interface IRight : IBase { }
            internal interface IDiamond : ILeft, IRight
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 2, ShaderStageKind.Compute)] new ShaderSamplerBinding Source { get; }
            }
            """ + Header + """
            internal partial class Shader : IDiamond { }
            public static class Proof { public static string Run() => new Shader().Source.Index + ":" + Shader.Contract.Stages[0].Bindings.Samplers["source"].Slot; }
            """;
        Assert.Equal("2:2", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }

    /// <summary>Metadata imports support defaults, stage filters and static owners without emitting runtime setters.</summary>
    [Fact]
    public void InterfaceMetadataImportsRespectStagesAndDefaults()
    {
        string source = """
            internal interface IShared
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)] ShaderSamplerBinding Source { get; }
            }
            [ShaderBindingSet(typeof(IShared), Defaults = true)] internal interface IDefaults { }
            """ + DeclarationTests.Basic.Replace("internal static partial class Shader",
                "[ShaderBindingSet(typeof(IDefaults), Stages = new[] { ShaderStageKind.Fragment })] internal static partial class Shader") + """
            public static class Proof { public static string Run() => Shader.Contract.Stages[0].Bindings.Samplers.Count + ":" + Shader.Contract.Stages[1].Bindings.Samplers["source"].Required; }
            """;
        Assert.Equal("0:False", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }

    /// <summary>Existing concrete bodies and default interface bodies retain their behavior without generated duplicates.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AuthoredAndDefaultBodiesArePreserved(bool defaultBody, bool explicitImplementation)
    {
        string source = """
            public static class Observation { public static int Calls; }
            internal interface ISource
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)]
                VanillaGraphicsExpanded.Rendering.GpuTexture Source { ACCESSOR }
            }
            """.Replace("ACCESSOR", defaultBody ? "set { Observation.Calls++; }" : "set;") + Header +
            "internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, ISource { protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader); " +
            (defaultBody ? "" : explicitImplementation
                ? "VanillaGraphicsExpanded.Rendering.GpuTexture ISource.Source { set { Observation.Calls++; } }"
                : "public VanillaGraphicsExpanded.Rendering.GpuTexture Source { set { Observation.Calls++; } }") +
            "}" + """
            public static class Proof { public static string Run() { ISource shader = new Shader(); shader.Source = new(); return Observation.Calls.ToString(); } }
            """;
        Assert.Equal("1", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
        GeneratorFixture.Generate(source, true, supportSource: RuntimeCaptureSupport.Source).Compile();
    }

    /// <summary>An unannotated defining partial property preserves its authored name and is completed from interface metadata.</summary>
    [Fact]
    public void ExistingPartialPropertyIsCompletedFromInterface()
    {
        string source = """
            internal interface ISource
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)] VanillaGraphicsExpanded.Rendering.GpuTexture Source { set; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, ISource
            { public partial VanillaGraphicsExpanded.Rendering.GpuTexture Source { set; } }
            """;
        var result = GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source);
        result.Compile();
        Assert.Contains(result.Generated, s => s.Contains("public partial global::VanillaGraphicsExpanded.Rendering.GpuTexture Source"));
    }

    /// <summary>Derived interface overrides replace inherited metadata while concrete partial properties implement the API.</summary>
    [Fact]
    public void DerivedInterfaceOverridesMetadataForConcretePartial()
    {
        string source = """
            internal interface ISource
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute, Required = false)] ShaderSamplerBinding Source { get; }
            }
            interface IOverride : ISource
            {
                [ShaderBinding("source", ShaderBindingKind.Sampler, 4, ShaderStageKind.Compute)] new ShaderSamplerBinding Source { get; }
            }
            """ + Header + """
            internal partial class Shader : IOverride
            {
                public partial ShaderSamplerBinding Source { get; }
            }
            public static class Proof { public static string Run() => new Shader().Source.Index + ":" + Shader.Contract.Stages[0].Bindings.Samplers["source"].Slot + ":" + Shader.Contract.Stages[0].Bindings.Samplers["source"].Required; }
            """;
        Assert.Equal("4:4:True", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }

    /// <summary>Inherited class implementations are generated once and remain usable through derived shader owners.</summary>
    [Fact]
    public void GeneratedBaseImplementationIsNotDuplicated()
    {
        string source = """
            interface ISource { [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; } }
            internal partial class Base : ISource { }
            """ + Header + """
            internal partial class Shader : Base { }
            public static class Proof { public static string Run() => new Shader().Source.Index.ToString(); }
            """;
        var result = GeneratorFixture.Generate(source);
        Assert.Equal("3", result.Run());
        Assert.Single(result.Generated, s => s.Contains("get => new global::VanillaGraphicsExpanded.Rendering.Contracts.ShaderSamplerBinding"));
    }

    /// <summary>Private or wrong-type concrete members cannot silently block interface implementation generation.</summary>
    [Theory]
    [InlineData("private ShaderSamplerBinding Source { get; }")]
    [InlineData("public int Source { get; }")]
    public void IncompatibleConcreteMembersAreDiagnosed(string member)
    {
        string source = "interface ISource { [ShaderBinding(\"source\", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; } }" + Header +
            "internal partial class Shader : ISource { " + member + " }";
        var result = GeneratorFixture.Generate(source);
        Assert.Contains(result.Diagnostics, d => d.GetMessage().Contains("incompatible concrete member"));
        Assert.Empty(result.Generated);
    }

    /// <summary>Defining partial implementations must retain the interface accessor shape.</summary>
    [Fact]
    public void ExtraPartialAccessorsAreDiagnosed()
    {
        string source = "interface ISource { [ShaderBinding(\"source\", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; } }" + Header +
            "internal partial class Shader : ISource { public partial ShaderSamplerBinding Source { get; set; } }";
        var result = GeneratorFixture.Generate(source);
        Assert.Contains(result.Diagnostics, d => d.GetMessage().Contains("incompatible defining partial accessors"));
        Assert.Empty(result.Generated);
    }

    /// <summary>Interface resource declarations remain engine-free when imported by a static metadata owner.</summary>
    [Fact]
    public void StaticOwnersImportResourceInterfaceMetadataOnly()
    {
        string source = """
            interface ISource { [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)] VanillaGraphicsExpanded.Rendering.GpuTexture Source { set; } }
            """ + Header + """
            [ShaderBindingSet(typeof(ISource))] internal static partial class Shader { }
            public static class Proof { public static string Run() => Shader.Contract.Stages[0].Bindings.Samplers["source"].Slot.ToString(); }
            """;
        Assert.Equal("3", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
        var offline = GeneratorFixture.Generate(source, true, supportSource: RuntimeCaptureSupport.Source);
        offline.Compile();
        Assert.All(offline.Generated, s => Assert.DoesNotContain("GpuTexture", s));
    }

    /// <summary>Interface program filters select a stable binding union separately for each declared program.</summary>
    [Fact]
    public void InterfaceProgramSubsetsAndExplicitDefaultsRemainStable()
    {
        string source = """
            interface IDefaults { [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 1, ShaderStageKind.Compute, Required = false)] ShaderUniformBlockBinding Params { get; } }
            [ShaderBindingSet(typeof(IDefaults), Defaults = true)]
            interface IPrograms { [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 4, ShaderStageKind.Compute, Programs = new[] { "One", "Two" })] ShaderUniformBlockBinding Parameters { get; } }
            [ShaderProgram("One", "one", 1)] [ShaderStage("One", ShaderStageKind.Compute, "one.csh")]
            [ShaderProgram("Two", "two", 1)] [ShaderStage("Two", ShaderStageKind.Compute, "two.csh")]
            [ShaderProgram("Three", "three", 1)] [ShaderStage("Three", ShaderStageKind.Compute, "three.csh")]
            [ShaderBindingSet(typeof(IPrograms))] internal static partial class Shader { }
            public static class Proof { public static string Run() => Shader.One.Stages[0].Bindings.UniformBlocks["Params"].Slot + ":" + Shader.Two.Stages[0].Bindings.UniformBlocks["Params"].Required + ":" + Shader.Three.Stages[0].Bindings.UniformBlocks["Params"].Slot; }
            """;
        Assert.Equal("4:True:1", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }

    /// <summary>Malformed interfaces and ambiguous inherited API ownership fail before any catalog or implementations are published.</summary>
    [Theory]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] float Source { set; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, -1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; set; }")]
    [InlineData("[ShaderBinding(\"source\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; } [ShaderBinding(\"other\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding Other { get; }")]
    public void InvalidInterfaceBindingsAreDiagnosed(string properties)
    {
        string source = "interface ISource { " + properties + " }" + Header +
            "[ShaderBindingSet(typeof(ISource))] internal static partial class Shader { }";
        foreach (bool offline in new[] { false, true })
        {
            var result = GeneratorFixture.Generate(source, offline);
            Assert.Contains(result.Diagnostics, d => d.Id == "VGEGEN001");
            Assert.Empty(result.Generated);
        }
    }

    /// <summary>Unrelated interfaces cannot silently select one of two attributed declarations for one API name.</summary>
    [Fact]
    public void UnrelatedInterfacePropertiesAreAmbiguous()
    {
        string source = """
            interface IOne { [ShaderBinding("source", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; } }
            interface ITwo { [ShaderBinding("source", ShaderBindingKind.Sampler, 2, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; } }
            """ + Header + "internal partial class Shader : IOne, ITwo { }";
        Assert.Contains(GeneratorFixture.Generate(source).Diagnostics, d => d.GetMessage().Contains("Ambiguous interface binding"));
    }

    /// <summary>A composite redeclaration resolves sibling metadata independently of interface name and traversal order.</summary>
    [Theory]
    [InlineData("IComposite")]
    [InlineData("IZComposite")]
    public void CompositeOverridesResolveSiblingBindings(string composite)
    {
        string source = """
            interface IA { [ShaderBinding("source", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; } }
            interface IB { [ShaderBinding("source", ShaderBindingKind.Sampler, 2, ShaderStageKind.Compute)] ShaderSamplerBinding Source { get; } }
            interface COMPOSITE : IA, IB { [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)] new ShaderSamplerBinding Source { get; } }
            """ + Header + """
            internal partial class Shader : COMPOSITE { }
            public static class Proof { public static string Run() => new Shader().Source.Index + ":" + Shader.Contract.Stages[0].Bindings.Samplers["source"].Slot; }
            """;
        source = source.Replace("COMPOSITE", composite);
        Assert.Equal("3:3", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }

    /// <summary>Expression-bodied default descriptor getters retain their authored implementation.</summary>
    [Fact]
    public void ExpressionBodiedDefaultInterfaceGetterIsPreserved()
    {
        string source = """
            interface ISource { [ShaderBinding("source", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)] ShaderSamplerBinding Source => new("source", 3, true); }
            """ + Header + """
            internal partial class Shader : ISource { }
            public static class Proof { public static string Run() => ((ISource)new Shader()).Source.Index.ToString(); }
            """;
        Assert.Equal("3", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, true).Compile();
    }
    /// <summary>Authored resource getters preserve nullable state while capture freezes the supplied identity.</summary>
    [Fact]
    public void AuthoredTextureAccessorsPreserveNullBehavior()
    {
        string source = """
            internal interface IInputs
            {
                [ShaderBinding("history", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)]
                VanillaGraphicsExpanded.Rendering.GpuTexture History { get; set; }
            }
            """ + Header + """
            internal partial class Shader : VanillaGraphicsExpanded.Rendering.GpuComputeProgram, IInputs
            {
                public VanillaGraphicsExpanded.Rendering.GpuTexture History { get; set; }
            }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader(); shader.History = new(); var first = shader.CaptureBindings();
                    shader.History = null; var cleared = shader.CaptureBindings();
                    return (first.Samplers[0].Texture.Resource != null) + ":" + (cleared.Samplers[0].Texture.Resource == null);
                }
            }
            """;
        Assert.Equal("True:True", GeneratorFixture.Generate(source, supportSource: RuntimeCaptureSupport.Source).Run());
    }

    /// <summary>A texture ID requires authored target/sampler selection instead of a guessed generated binding.</summary>
    [Fact]
    public void MissingTextureIdImplementationIsRejected()
    {
        string source = "interface IInputs { [ShaderBinding(\"depth\", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)] int PrimaryDepth { set; } }" +
            Header + "internal partial class Shader : IInputs { }";
        Assert.Contains(GeneratorFixture.Generate(source).Diagnostics, d => d.GetMessage().Contains("unsupported type/accessors for Sampler", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Public GPU buffer types produce directly callable properties on public shader owners.</summary>
    [Fact]
    public void PublicBufferResourcesHaveDirectlyCallableShaderProperties()
    {
        string source = """
            interface IInputs
            {
                [ShaderBinding("Params", ShaderBindingKind.UniformBlock, 1, ShaderStageKind.Compute)] VanillaGraphicsExpanded.Rendering.GpuUniformBuffer Parameters { set; }
                [ShaderBinding("Work", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)] VanillaGraphicsExpanded.Rendering.GpuShaderStorageBuffer Work { set; }
            }
            """ + Header + """
            public partial class Shader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram, IInputs { }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader();
                    var buffer = new VanillaGraphicsExpanded.Rendering.GpuUniformBuffer();
                    shader.Parameters = buffer;
                    var storage = new VanillaGraphicsExpanded.Rendering.GpuShaderStorageBuffer();
                    shader.Work = storage;
                    var captured = shader.CaptureBindings();
                    return object.ReferenceEquals(captured.Buffers[0].Buffer.Resource, buffer) + ":" + object.ReferenceEquals(captured.Buffers[1].Buffer.Resource, storage);
                }
            }
            """;
        string support = RuntimeCaptureSupport.Source;
        Assert.Equal("True:True", GeneratorFixture.Generate(source, supportSource: support).Run());
        GeneratorFixture.Generate(source, true, supportSource: support).Compile();
    }
    #endregion
}
