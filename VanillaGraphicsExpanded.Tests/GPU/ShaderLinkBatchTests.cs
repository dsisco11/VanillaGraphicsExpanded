using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks bounded driver submission and ownership through the production loaders.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderLinkBatchTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    private const string Graphics = "tests/render_infrastructure";
    private const string Compute = "tests/GpuUniformRingBufferIntegrationTests_1";

    #region Completion and fallback
    /// <summary>Both owners consume submitted candidates and refill without exceeding the configured window.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedOwnersCompleteWithinBound(bool disable)
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        var graphics = Settings(Graphics);
        var compute = Settings(Compute);
        using var batch = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain,
            [graphics, compute, graphics], maximumOutstanding: 2, disable: disable);
        bool enabled = batch.Enabled;
        if (disable) Assert.False(enabled);
        Assert.Equal(enabled ? 2 : 0, batch.Outstanding);
        using var program = new FixtureProgram();
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        Assert.Equal(enabled ? 2 : 0, batch.Outstanding);
        Assert.True(GpuComputePipeline.TryCreateFromAssets(assets.Api, compute, out var pipeline, out var log, ct: TestContext.Current.CancellationToken), log);
        using (pipeline)
        {
            Assert.True(GL.IsProgram(pipeline!.ProgramId));
            Assert.NotNull(pipeline.ProgramLayout.BinaryInterface);
            using var texture = Texture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D);
            Assert.True(GpuUniformRingSystem.TryGetCurrent(out var ring));
            byte[] data = new byte[16];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data, 123);
            var allocation = ring.AllocateAndWrite(data);
            allocation.Buffer.BindRange(0, allocation.OffsetBytes, allocation.SizeBytes);
            GL.BindImageTexture(0, texture.TextureId, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32ui);
            GL.UseProgram(pipeline.ProgramId);
            GL.DispatchCompute(1, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureUpdateBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit);
            uint[] actual = new uint[4];
            GL.BindTexture(TextureTarget.Texture3D, texture.TextureId);
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, actual);
            Assert.Equal(123u, actual[0]);
            GL.UseProgram(0);
            GL.BindTexture(TextureTarget.Texture3D, 0);
            GL.BindImageTexture(0, 0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32ui);
        }
        Assert.Equal(enabled ? 1 : 0, batch.Outstanding);
        int old = program.ProgramId;
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        Assert.False(GL.IsProgram(old));
        Assert.Equal(0, batch.Outstanding);
        Assert.Equal(enabled ? 2 : 0, batch.PeakOutstanding);
        using var target = CreateRenderTarget(8, 8, PixelInternalFormat.Rgba32f);
        target.BindWithViewport();
        RenderFullscreenQuad(program.ProgramId);
        var pixel = ReadPixel(target, 4, 4);
        Assert.InRange(pixel.R, .55f, .58f);
        GL.UseProgram(0);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Asset managers cannot consume another owner's pending executable.</summary>
    [Fact]
    public void AssetOwnersRemainIsolated()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var first = new BinaryShaderApiFixture();
        using var second = new BinaryShaderApiFixture();
        using var batch = new ShaderLinkBatch(first.Api.Assets, ShaderImportsSystem.DefaultDomain, [Settings(Graphics)]);
        int pending = batch.Outstanding;
        using var other = new FixtureProgram();
        other.Initialize(second.Api);
        Assert.True(other.CompileAndLink());
        Assert.Equal(pending, batch.Outstanding);
        using var intended = new FixtureProgram();
        intended.Initialize(first.Api);
        Assert.True(intended.CompileAndLink());
        Assert.Equal(0, batch.Outstanding);
        Assert.NotEqual(other.ProgramId, intended.ProgramId);
    }
    #endregion

    #region Failure and cancellation
    /// <summary>Context changes during external asset reads reject the candidate before any native allocation.</summary>
    [Fact]
    public void PendingAssetReadsCannotAllocateOnChangedContextRegistration()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var sentinel = new FixtureProgram();
        sentinel.Initialize(assets.Api);
        Assert.True(sentinel.CompileAndLink());
        StateCache.Current.UseProgram(sentinel.ProgramId);
        var original = VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Current();
        var registrations = (System.Collections.IDictionary)typeof(VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry)
            .GetField("registrations", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        object registration = registrations[original.Handle]!;
        var replacement = new object();
        bool changed = false;
        ReadOnlySpan<byte> Read(string path)
        {
            if (!changed)
            {
                changed = true;
                VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(fixture);
                VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            }
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path));
        }
        try
        {
            var failure = Assert.Throws<InvalidOperationException>(() => new PendingShaderProgram(
                new ShaderLoadPlan(Settings(Graphics)), Read,
                () => ShaderDigestIndexCache.ForAssets(assets.Api.Assets, ShaderImportsSystem.DefaultDomain),
                GetProgramParameterName.LinkStatus));
            Assert.Contains("originating native context generation", failure.Message);
            Assert.Equal(sentinel.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.True(GL.IsProgram(sentinel.ProgramId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            lock (registrations) registrations[original.Handle] = registration;
            StateCache.Current.UnbindProgram();
        }
    }

    /// <summary>Pending cached and uncached candidates cannot be consumed or deleted under a reused context registration.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextGenerationMismatchRejectsBeforeTransferOrDeletion(bool cached)
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.BatchContext", Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(cached ? new ProgramBinaryStore(directory) : null);
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram();
            using var sentinel = new FixtureProgram();
            program.Initialize(assets.Api); sentinel.Initialize(assets.Api);
            Assert.True(program.CompileAndLink());
            Assert.True(sentinel.CompileAndLink());
            int installed = program.ProgramId;
            ulong revision = program.ExecutableRevision;
            using var batch = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain, [program.RequestedSettings]);
            int outstanding = batch.Outstanding;
            var original = VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Current();
            var registrations = (System.Collections.IDictionary)typeof(VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry)
                .GetField("registrations", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            object registration = registrations[original.Handle]!;
            StateCache.Current.UseProgram(sentinel.ProgramId);
            var replacement = new object();
            try
            {
                VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(fixture);
                VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
                Assert.False(program.CompileAndLink());
                Assert.Contains("originating native context generation", program.PreparationFailure!.Message);
                Assert.Equal(installed, program.ProgramId);
                Assert.Equal(revision, program.ExecutableRevision);
                Assert.Equal(0, batch.ConsumedCount);
                Assert.Throws<InvalidOperationException>(batch.Dispose);
                Assert.Equal(outstanding, batch.Outstanding);
                Assert.Equal(sentinel.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
                Assert.True(GL.IsProgram(installed));
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
            finally
            {
                // Reuse the original live registration receipt to retry cleanup; destroyed-context abandonment is separate.
                lock (registrations) registrations[original.Handle] = registration;
                batch.Dispose();
                StateCache.Current.UnbindProgram();
            }
            Assert.Equal(0, batch.Outstanding);
            batch.Dispose();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>An asset epoch change after batch submission prevents old captured binaries being published as new assets.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssetInvalidationWithdrawsSubmittedCandidates(bool disable)
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var program = new FixtureProgram();
        program.Initialize(assets.Api);
        Assert.True(program.EnsureReady());
        int installed = program.ProgramId;
        ulong revision = program.ExecutableRevision;
        using var batch = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain,
            [program.RequestedSettings], disable: disable);
        assets.Overrides["shaders/tests/render_infrastructure.fsh.spv"] = new byte[20];
        program.InvalidateAssets();
        Assert.False(program.EnsureReady());
        Assert.Equal(installed, program.ProgramId);
        Assert.Equal(revision, program.ExecutableRevision);
        Assert.True(program.RequiresPreparation);
        Assert.True(GL.IsProgram(installed));
        assets.Overrides.Clear();
        program.InvalidateAssets();
        Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
        Assert.False(GL.IsProgram(installed));
        Assert.Equal(revision + 1, program.ExecutableRevision);
    }

    /// <summary>Successful transfers release attached stages; abandoned and cancelled candidates retire every native allocation once.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingCandidateOwnsExactlyOneRetirement(bool transfer)
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        ReadOnlySpan<byte> Read(string path) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path));
        var pending = new PendingShaderProgram(new ShaderLoadPlan(Settings(Graphics)), Read,
            () => ShaderDigestIndexCache.ForAssets(assets.Api.Assets, ShaderImportsSystem.DefaultDomain),
            GetProgramParameterName.LinkStatus);
        int program = pending.Program;
        int[] stages = pending.Stages.Select(stage => stage.Shader).ToArray();
        Assert.Equal(2, stages.Length);
        if (transfer)
        {
            pending.Complete(TestContext.Current.CancellationToken);
            using var installed = GpuProgramObject.Adopt(pending.DetachProgram());
            Assert.Empty(pending.Stages);
            Assert.Equal(0, pending.Program);
            Assert.All(stages, stage => Assert.False(GL.IsShader(stage)));
            pending.Dispose(); pending.Dispose();
            Assert.True(GL.IsProgram(program));
            GL.GetProgram(program, GetProgramParameterName.AttachedShaders, out int count);
            Assert.Equal(0, count);
        }
        else
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => pending.Complete(cancellation.Token));
            pending.Dispose(); pending.Dispose();
            Assert.False(GL.IsProgram(program));
            Assert.All(stages, stage => Assert.False(GL.IsShader(stage)));
        }
        Assert.False(GL.IsProgram(program));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A deferred submission failure retains the installed executable and permits later recovery.</summary>
    [Fact]
    public void FailedSubmissionPreservesInstalledProgram()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var program = new FixtureProgram();
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink());
        int installed = program.ProgramId;
        assets.Overrides["shaders/tests/render_infrastructure.fsh.spv"] = new byte[20];
        using (var batch = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain, [Settings(Graphics)]))
        {
            Assert.False(program.CompileAndLink());
            Assert.Equal(installed, program.ProgramId);
            Assert.True(GL.IsProgram(installed));
        }
        assets.Overrides.Clear();
        Assert.True(program.CompileAndLink());
        Assert.False(GL.IsProgram(installed));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Cancellation prevents consumption and disposal restores the ordinary creation path.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationAndAbandonmentRestoreSynchronousLoading(bool disable)
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var cancellation = new CancellationTokenSource();
        using var program = new FixtureProgram();
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink());
        int installed = program.ProgramId;
        using (var batch = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain,
            [Settings(Graphics), Settings(Compute)], cancellation: cancellation.Token, disable: disable))
        {
            cancellation.Cancel();
            Assert.False(program.CompileAndLink());
            Assert.Equal(installed, program.ProgramId);
        }
        Assert.True(program.CompileAndLink());
        Assert.False(GL.IsProgram(installed));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    /// <summary>Changes raised while refilling cannot publish an obsolete requested generation.</summary>
    [Fact]
    public void SupersededSettingsPreserveInstalledProgram()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var program = new FixtureProgram("lumon_debug_view_direct_diffuse");
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink());
        int installed = program.ProgramId;
        using var batch = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain,
            [program.RequestedSettings, Settings(Compute)], maximumOutstanding: 1);
        if (!batch.Enabled) return;
        bool changed = false;
        assets.BeforeRead = _ =>
        {
            if (changed) return;
            changed = true;
            Assert.True(program.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" }));
        };
        Assert.False(program.CompileAndLink());
        Assert.True(changed);
        Assert.Equal(installed, program.ProgramId);
        Assert.True(GL.IsProgram(installed));
        assets.BeforeRead = null;
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        Assert.False(GL.IsProgram(installed));
    }
    /// <summary>Unexpected dependencies and nested scopes complete the outer window before issuing more work.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutOfWindowAndNestedWorkRespectPendingCompletion(bool nested)
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var outer = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain,
            [Settings(Graphics), Settings(Compute)], maximumOutstanding: 1);
        if (!outer.Enabled) return;
        Assert.Equal(0, outer.CompletedOutstanding);
        assets.BeforeRead = _ => Assert.Equal(outer.Outstanding, outer.CompletedOutstanding);
        using (var inner = nested ? new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain, [Settings(Compute)]) : null)
        {
            Assert.True(GpuComputePipeline.TryCreateFromAssets(assets.Api, Settings(Compute), out var pipeline, out string log,
                ct: TestContext.Current.CancellationToken), log);
            pipeline!.Dispose();
        }
        assets.BeforeRead = null;
        Assert.Equal(1, outer.CompletedOutstanding);
        using var program = new FixtureProgram();
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink());
        Assert.Equal(nested ? 1 : 0, outer.Outstanding);
        Assert.Equal(1, outer.PeakOutstanding);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #region Binary cache coordination
    /// <summary>Warm entries and rejected driver payloads remain consumable through a batch.</summary>
    [Fact]
    public void CacheHitsAndRejectedEntriesComplete()
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.ParallelLinkTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProgramBinaryStore(directory);
            using var cache = DriverProgramCache.UseStoreForTesting(store);
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram();
            program.Initialize(assets.Api);
            Assert.True(program.CompileAndLink());
            string key = Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(directory, "*.bin")));
            using (var batch = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain, [Settings(Graphics)]))
            {
                Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
                Assert.True(DriverProgramCache.LastLoadWasHit);
                Assert.Equal(0, batch.Outstanding);
            }
            store.Write(key, -1, [1, 2, 3, 4]);
            using (var batch = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain, [Settings(Graphics)]))
            {
                Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
                Assert.False(DriverProgramCache.LastLoadWasHit);
                Assert.Equal(0, batch.Outstanding);
            }
            Assert.True(store.TryRead(key, out int repairedFormat, out _));
            Assert.NotEqual(-1, repairedFormat);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    #endregion
    #region Timing evidence
    /// <summary>Records matched executable submission, consumption, and first draw costs without claiming a cold driver cache.</summary>
    [Fact]
    public void MatchedLoadingTimings()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var target = CreateRenderTarget(8, 8, PixelInternalFormat.Rgba32f);
        // ABBA retains the same binaries and work, exposing order effects from the driver cache.
        foreach (bool disable in new[] { true, false, false, true })
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            using var batch = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain,
                [Settings(Graphics), Settings(Compute), Settings(Graphics), Settings(Compute)], maximumOutstanding: 4, disable: disable);
            double submitted = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            using var first = new FixtureProgram();
            using var second = new FixtureProgram();
            first.Initialize(assets.Api);
            second.Initialize(assets.Api);
            Assert.True(first.CompileAndLink());
            Assert.True(second.CompileAndLink());
            Assert.True(GpuComputePipeline.TryCreateFromAssets(assets.Api, Settings(Compute), out var firstCompute, out var firstLog,
                ct: TestContext.Current.CancellationToken), firstLog);
            Assert.True(GpuComputePipeline.TryCreateFromAssets(assets.Api, Settings(Compute), out var secondCompute, out var secondLog,
                ct: TestContext.Current.CancellationToken), secondLog);
            using (firstCompute)
            using (secondCompute)
            {
                double loaded = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long use = System.Diagnostics.Stopwatch.GetTimestamp();
                target.BindWithViewport();
                RenderFullscreenQuad(first.ProgramId);
                RenderFullscreenQuad(second.ProgramId);
                GL.Finish();
                double firstUse = System.Diagnostics.Stopwatch.GetElapsedTime(use).TotalMilliseconds;
                GL.UseProgram(0);
                output.WriteLine($"Mode={(disable ? "sync" : "batch")}, enabled={batch.Enabled}, count=4, submit={submitted:F3} ms, load={loaded:F3} ms, completion={batch.CompletionMilliseconds:F3} ms, first two draws+finish={firstUse:F3} ms, total={loaded + firstUse:F3} ms");
            }
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Fixture declarations
    /// <summary>Captures immutable defaults for the declared binary variant.</summary>
    private static ShaderSettings Settings(string identity) => new(GpuShaderContracts.Registry.FindProgram(identity));

    /// <summary>Uses the existing deterministic fullscreen contract.</summary>
    private sealed class FixtureProgram : Rendering.Shaders.GpuProgram
    {
        #region Submission
        /// <summary>Retains this owner's explicit external input publication contract.</summary>
        protected override void Submit() { }
        #endregion

        /// <summary>Supplies engine wrappers for the production owner.</summary>
        public FixtureProgram(string name = Graphics)
        {
            PassName = name;
        }

        /// <summary>Builds the declared interface for both linking paths.</summary>
        protected override GpuProgramLayout CreateLayout()
        {
            var layout = new GpuProgramLayout();
            layout.RegisterContract(GpuShaderContracts.Create(ShaderName));
            return layout;
        }
    }
    #endregion
}