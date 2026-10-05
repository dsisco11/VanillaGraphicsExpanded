using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Checks atomic publication through actual generated properties and typed/name batch APIs.</summary>
public sealed class ShaderOptionBatchTests(ITestOutputHelper output)
{
    #region Publication and failure
    /// <summary>Measures warmed unchanged batches through production liquid and generated property owners.</summary>
    [Fact]
    public void RepeatedUnchangedBatchesRetainSelectionsWithoutPreparation()
    {
        var liquid = new LiquidShaderProgram();
        int quality = liquid.RefractionQuality, resolution = liquid.RefractionBackgroundScale;
        Action liquidBatch = () => { liquid.RefractionQuality = quality; liquid.RefractionBackgroundScale = resolution; };
        MeasureUnchanged(liquid,liquidBatch,"liquid-quality");
        Assert.Equal(quality,liquid.RefractionQuality);
        Assert.Equal(resolution,liquid.RefractionBackgroundScale);
        Assert.Null(liquid.InstalledSettings);
        var generated = new GeneratedAccessorShader();
        Action generatedBatch = () => { generated.Enabled = false; generated.Steps = 10; };
        MeasureUnchanged(generated,generatedBatch,"generated");
        Assert.Equal(0,generated.ReloadRequests);
    }

    /// <summary>Generated properties and name updates share one publication, with no intermediate reload requests.</summary>
    [Fact]
    public void MixedConfigurationPublishesOnce()
    {
        var shader = new GeneratedAccessorShader();
        var prior = shader.RequestedSettings;
        Assert.True(shader.ConfigureOptions(() =>
        {
            shader.Enabled = true;
            shader.SetDefines(new Dictionary<string, string?> { ["GENERATED_STEPS"] = "20" });
            Assert.Same(prior, shader.RequestedSettings);
            Assert.Equal(0, shader.ReloadRequests);
        }));
        Assert.True(shader.Enabled);
        Assert.Equal(20, shader.Steps);
        Assert.Equal(1, shader.ReloadRequests);
        Assert.Null(shader.InstalledSettings);
        var retained = shader.RequestedSettings;
        Assert.False(shader.ConfigureOptions(() => { shader.Enabled = false; shader.Enabled = true; }));
        Assert.Same(retained,shader.RequestedSettings);
        Assert.Equal(1, shader.ReloadRequests);
    }

    /// <summary>Invalid scalar/name batches and callback failures preserve the exact requested snapshot.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FailureDiscardsAllEdits(int failure)
    {
        var shader = new GeneratedAccessorShader();
        var prior = shader.RequestedSettings;
        Assert.ThrowsAny<Exception>(() => shader.ConfigureOptions(() =>
        {
            shader.Enabled = true;
            if (failure == 0) shader.Steps = 0;
            else if (failure == 1) shader.SetDefines(new Dictionary<string, string?> { ["UNKNOWN"] = "1" });
            else throw new InvalidOperationException("callback failure");
        }));
        Assert.Same(prior, shader.RequestedSettings);
        Assert.Equal(0, shader.ReloadRequests);
        Assert.Null(shader.InstalledSettings);
        Assert.True(shader.ConfigureOptions(() => shader.Enabled = true));
    }

    /// <summary>Swallowing a validation exception inside a callback cannot publish the valid prefix.</summary>
    [Fact]
    public void SwallowedNestedFailureStillAborts()
    {
        var shader = new GeneratedAccessorShader();
        var prior = shader.RequestedSettings;
        Assert.Throws<InvalidOperationException>(() => shader.ConfigureOptions(() =>
        {
            shader.Enabled = true;
            Assert.Throws<ArgumentException>(() => shader.Steps = 0);
        }));
        Assert.Same(prior, shader.RequestedSettings);
        Assert.Equal(0, shader.ReloadRequests);
    }

    /// <summary>Inactive values survive without scheduling until their controlling option becomes active.</summary>
    [Fact]
    public void InactiveBatchAndDefaultsRetainExistingSemantics()
    {
        var shader = new GeneratedAccessorShader();
        var initial = shader.RequestedSettings;
        Assert.False(shader.ConfigureOptions(() => shader.Steps = 22));
        Assert.NotSame(initial,shader.RequestedSettings);
        var inactive = shader.RequestedSettings;
        Assert.False(shader.ConfigureOptions(() => shader.Steps = 22));
        Assert.Same(inactive,shader.RequestedSettings);
        Assert.Equal(22, shader.Steps);
        Assert.True(shader.SetDefines(new Dictionary<string, string?> { ["GENERATED_LEGACY"] = "1" }));
        Assert.Equal(1, shader.ReloadRequests);
        Assert.True(shader.SetDefines(new Dictionary<string, string?> { ["GENERATED_LEGACY"] = null, ["GENERATED_STEPS"] = null }));
        Assert.False(shader.Enabled);
        Assert.Equal(10, shader.Steps);
        Assert.False(shader.SetDefines(new Dictionary<string, string?>()));
        var defaults = shader.RequestedSettings;
        Assert.False(shader.SetDefines(new Dictionary<string, string?> { ["GENERATED_LEGACY"] = null, ["GENERATED_STEPS"] = "10" }));
        Assert.Same(defaults,shader.RequestedSettings);
    }

    /// <summary>Nested configuration shares the editor and does not notify until the outer scope completes.</summary>
    [Fact]
    public void NestedConfigurationJoinsOuterBatch()
    {
        var shader = new GeneratedAccessorShader();
        Assert.True(shader.ConfigureOptions(() =>
        {
            shader.Enabled = true;
            Assert.False(shader.ConfigureOptions(() => shader.Steps = 24));
            Assert.Equal(0, shader.ReloadRequests);
        }));
        Assert.Equal(24, shader.Steps);
        Assert.Equal(1, shader.ReloadRequests);
        var retained = shader.RequestedSettings;
        Assert.False(shader.ConfigureOptions(() =>
        {
            shader.Steps = 20;
            Assert.False(shader.ConfigureOptions(() => shader.Steps = 24));
        }));
        Assert.Same(retained,shader.RequestedSettings);
        Assert.Equal(24,shader.Steps);
        Assert.Equal(1,shader.ReloadRequests);
    }
    /// <summary>Coupled structural choices may cross an unsupported intermediate combination within one batch.</summary>
    [Fact]
    public void StructuralAssignmentValidatesOnlyTheFinalCombination()
    {
        var shader = new CoupledProgram();
        var prior = shader.RequestedSettings;
        Assert.Throws<ArgumentException>(() => shader.SetShaderOptions(options => options.Set(CoupledProgram.First, true)));
        Assert.Same(prior, shader.RequestedSettings);
        Assert.True(shader.SetShaderOptions(options =>
        {
            options.Set(CoupledProgram.First, true);
            options.Set(CoupledProgram.Second, true);
        }));
        Assert.Equal(1, shader.ReloadRequests);
        Assert.True(shader.GetShaderOption(CoupledProgram.First));
        Assert.True(shader.GetShaderOption(CoupledProgram.Second));
        prior = shader.RequestedSettings;
        Assert.Throws<ArgumentException>(() => shader.SetShaderOptions(options =>
        {
            options.Set(CoupledProgram.First, false);
            options.Set(new ShaderOption<int>("SECOND", 1), 0);
        }));
        Assert.Same(prior, shader.RequestedSettings);
        Assert.Equal(1, shader.ReloadRequests);
    }

    /// <summary>Direct editor errors cannot be swallowed to publish a partial typed batch.</summary>
    [Fact]
    public void SwallowedEditorFailureStillAborts()
    {
        var shader = new CoupledProgram();
        var prior = shader.RequestedSettings;
        Assert.Throws<InvalidOperationException>(() => shader.SetShaderOptions(options =>
        {
            options.Set(CoupledProgram.First, true);
            options.Set(CoupledProgram.Second, true);
            Assert.Throws<ArgumentException>(() => options.Set("UNKNOWN", "1"));
        }));
        Assert.Same(prior, shader.RequestedSettings);
        Assert.Equal(0, shader.ReloadRequests);
    }
    #endregion

    #region Allocation measurement
    /// <summary>Excludes delegate creation and warmup from thread-local allocation evidence.</summary>
    private void MeasureUnchanged(GpuProgram shader, Action batch, string owner)
    {
        const int iterations = 4096;
        for (int i = 0; i < 1024; i++) shader.ConfigureOptions(batch);
        var prior = shader.RequestedSettings;
        long before = GC.GetAllocatedBytesForCurrentThread();
        int changes = 0;
        for (int i = 0; i < iterations; i++) if (shader.ConfigureOptions(batch)) changes++;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"unchanged-batch owner={owner} iterations={iterations} bytes={bytes} bytesPerBatch={(double)bytes/iterations:R} snapshotRetained={ReferenceEquals(prior,shader.RequestedSettings)} changes={changes}");
        Assert.Equal(0,changes);
        Assert.Same(prior,shader.RequestedSettings);
    }
    #endregion

    #region Coupled contract fixture
    /// <summary>Asset-free owner with only two jointly enabled or disabled structural assignments.</summary>
    private sealed class CoupledProgram : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        #region Submission
        /// <summary>Retains this owner's explicit external input publication contract.</summary>
        protected override void Submit() { }
        #endregion

        internal static readonly ShaderOption<bool> First = new("FIRST", false);
        internal static readonly ShaderOption<bool> Second = new("SECOND", false);
        private static readonly GpuShaderContract contract = ShaderContractFixture.Program(
            fragment: ShaderContractFixture.Stage(structural: [First, Second]), options: [First, Second],
            assignments: [ShaderContractFixture.Row(("FIRST", "0"), ("SECOND", "0")),
                ShaderContractFixture.Row(("FIRST", "1"), ("SECOND", "1"))]);
        internal override GpuShaderContract ProgramContract => contract;
        internal int ReloadRequests { get; private set; }

        /// <summary>Counts notifications through the existing overridable scheduler boundary.</summary>
        protected override void RequestRecompile() => ReloadRequests++;
    }
    #endregion
}
