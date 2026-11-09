using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Checks atomic publication through actual generated properties and typed/name batch APIs.</summary>
public sealed class ShaderOptionBatchTests(ITestOutputHelper output)
{
    #region Public API
    #region Publication and failure
    /// <summary>Measures warmed unchanged batches through production liquid and generated property owners.</summary>
    [Fact]
    public void RepeatedUnchangedBatchesRetainSelectionsWithoutPreparation()
    {
        // Arrange: Capture existing liquid settings and generated-owner defaults.
        var liquid = new LiquidShaderProgram();
        int quality = liquid.RefractionQuality, resolution = liquid.RefractionBackgroundScale;
        Action liquidBatch = () => { liquid.RefractionQuality = quality; liquid.RefractionBackgroundScale = resolution; };
        // Act: Warm and measure unchanged option batches through both production owners.
        MeasureUnchanged(liquid,liquidBatch,"liquid-quality");
        // Assert: The original selections and no-preparation behavior remain intact.
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
        // Arrange: Capture the prior immutable requested-settings snapshot.
        var shader = new GeneratedAccessorShader();
        var prior = shader.RequestedSettings;
        // Act: Apply a generated property and name-based update inside one outer batch.
        Assert.True(shader.ConfigureOptions(() =>
        {
            shader.Enabled = true;
            shader.SetDefines(new Dictionary<string, string?> { ["GENERATED_STEPS"] = "20" });
            Assert.Same(prior, shader.RequestedSettings);
            Assert.Equal(0, shader.ReloadRequests);
        }));
        // Assert: Nothing publishes from inside the callback; the completed batch publishes once.
        Assert.True(shader.Enabled);
        Assert.Equal(20, shader.Steps);
        Assert.Equal(1, shader.ReloadRequests);
        Assert.Null(shader.InstalledSettings);
        var retained = shader.RequestedSettings;
        // Act: Apply changes that return to the retained selection.
        Assert.False(shader.ConfigureOptions(() => { shader.Enabled = false; shader.Enabled = true; }));
        // Assert: The net no-op keeps the existing snapshot and scheduling count.
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
        // Arrange: Capture the original requested snapshot and select one failure source from the theory row.
        var shader = new GeneratedAccessorShader();
        var prior = shader.RequestedSettings;
        // Act: Mutate a valid option before triggering scalar, name, or callback failure.
        Assert.ThrowsAny<Exception>(() => shader.ConfigureOptions(() =>
        {
            shader.Enabled = true;
            if (failure == 0) shader.Steps = 0;
            else if (failure == 1) shader.SetDefines(new Dictionary<string, string?> { ["UNKNOWN"] = "1" });
            else throw new InvalidOperationException("callback failure");
        }));
        // Assert: Failure rolls back the staged prefix and permits a later valid update.
        Assert.Same(prior, shader.RequestedSettings);
        Assert.Equal(0, shader.ReloadRequests);
        Assert.Null(shader.InstalledSettings);
        Assert.True(shader.ConfigureOptions(() => shader.Enabled = true));
    }

    /// <summary>Swallowing a validation exception inside a callback cannot publish the valid prefix.</summary>
    [Fact]
    public void SwallowedNestedFailureStillAborts()
    {
        // Arrange: Capture the owner before entering nested configuration.
        var shader = new GeneratedAccessorShader();
        var prior = shader.RequestedSettings;
        // Act: Catch the inner validation error while the outer callback remains active.
        Assert.Throws<InvalidOperationException>(() => shader.ConfigureOptions(() =>
        {
            shader.Enabled = true;
            Assert.Throws<ArgumentException>(() => shader.Steps = 0);
        }));
        // Assert: The outer transaction still aborts its valid prefix.
        Assert.Same(prior, shader.RequestedSettings);
        Assert.Equal(0, shader.ReloadRequests);
    }

    /// <summary>Inactive values survive without scheduling until their controlling option becomes active.</summary>
    [Fact]
    public void InactiveBatchAndDefaultsRetainExistingSemantics()
    {
        // Arrange: Capture the default requested-settings snapshot.
        var shader = new GeneratedAccessorShader();
        var initial = shader.RequestedSettings;
        // Act: Change an inactive numeric choice, then repeat it without changes.
        Assert.False(shader.ConfigureOptions(() => shader.Steps = 22));
        // Assert: The value is retained without scheduling until a controlling option becomes active.
        Assert.NotSame(initial,shader.RequestedSettings);
        var inactive = shader.RequestedSettings;
        Assert.False(shader.ConfigureOptions(() => shader.Steps = 22));
        Assert.Same(inactive,shader.RequestedSettings);
        Assert.Equal(22, shader.Steps);
        // Act: Enable the option, then reset both values to defaults.
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
        // Arrange: Use one owner so nested setters share the outer editor.
        var shader = new GeneratedAccessorShader();
        // Act: Change the selector outside and the numeric value inside a nested call.
        Assert.True(shader.ConfigureOptions(() =>
        {
            shader.Enabled = true;
            Assert.False(shader.ConfigureOptions(() => shader.Steps = 24));
            Assert.Equal(0, shader.ReloadRequests);
        }));
        // Assert: Nested configuration publishes once after the outer scope completes.
        Assert.Equal(24, shader.Steps);
        Assert.Equal(1, shader.ReloadRequests);
        // Arrange: Capture the published snapshot before a transaction that returns to its current value.
        var retained = shader.RequestedSettings;
        // Act: Stage a temporary step count and restore it through a nested no-op update.
        Assert.False(shader.ConfigureOptions(() =>
        {
            shader.Steps = 20;
            Assert.False(shader.ConfigureOptions(() => shader.Steps = 24));
        }));
        // Assert: The net no-op retains the snapshot and does not schedule another reload.
        Assert.Same(retained,shader.RequestedSettings);
        Assert.Equal(24,shader.Steps);
        Assert.Equal(1,shader.ReloadRequests);
    }
    /// <summary>Coupled structural choices may cross an unsupported intermediate combination within one batch.</summary>
    [Fact]
    public void StructuralAssignmentValidatesOnlyTheFinalCombination()
    {
        // Arrange: The owner permits only both structural flags off or both on.
        var shader = new CoupledProgram();
        var prior = shader.RequestedSettings;
        // Act: A single unsupported intermediate update must fail; a paired update is valid.
        Assert.Throws<ArgumentException>(() => shader.SetShaderOptions(options => options.Set(CoupledProgram.First, true)));
        Assert.Same(prior, shader.RequestedSettings);
        Assert.True(shader.SetShaderOptions(options =>
        {
            options.Set(CoupledProgram.First, true);
            options.Set(CoupledProgram.Second, true);
        }));
        // Assert: The valid pair publishes once and exposes both final values.
        Assert.Equal(1, shader.ReloadRequests);
        Assert.True(shader.GetShaderOption(CoupledProgram.First));
        Assert.True(shader.GetShaderOption(CoupledProgram.Second));
        prior = shader.RequestedSettings;
        // Act: Reject a later update that leaves the final pair unsupported.
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
        // Arrange: Capture the original snapshot of the coupled owner.
        var shader = new CoupledProgram();
        var prior = shader.RequestedSettings;
        // Act: Swallow an unknown-name error inside the editor callback.
        Assert.Throws<InvalidOperationException>(() => shader.SetShaderOptions(options =>
        {
            options.Set(CoupledProgram.First, true);
            options.Set(CoupledProgram.Second, true);
            Assert.Throws<ArgumentException>(() => options.Set("UNKNOWN", "1"));
        }));
        // Assert: The editor's failure marker aborts the otherwise valid prefix.
        Assert.Same(prior, shader.RequestedSettings);
        Assert.Equal(0, shader.ReloadRequests);
    }
    #endregion
    #endregion

    #region Private
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
        #region Protected
        /// <summary>Retains this owner's explicit external input publication contract.</summary>
        protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
        /// <summary>Counts option notifications at the existing preparation boundary.</summary>
        protected override void RequestRecompile() => ReloadRequests++;
        #endregion

        #region Internal
        internal static readonly ShaderOption<bool> First = new("FIRST", false);
        internal static readonly ShaderOption<bool> Second = new("SECOND", false);
        internal override GpuShaderContract ProgramContract => contract;
        internal int ReloadRequests { get; private set; }
        #endregion

        #region Private
        private static readonly GpuShaderContract contract = ShaderContractFixture.Program(
            fragment: ShaderContractFixture.Stage(structural: [First, Second]), options: [First, Second],
            assignments: [ShaderContractFixture.Row(("FIRST", "0"), ("SECOND", "0")),
                ShaderContractFixture.Row(("FIRST", "1"), ("SECOND", "1"))]);
        #endregion
    }
    #endregion
    #endregion
}
