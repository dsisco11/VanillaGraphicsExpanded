using VanillaGraphicsExpanded.Tests.Support.Cache;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;


using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks bounded driver submission and ownership through the production loaders.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderLinkBatchTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    private readonly CountingDiskCacheFileSystem storage = new();

    private const string Graphics = "tests/render_infrastructure";
    private const string Compute = "tests/GpuUniformRingBufferIntegrationTests_1";

    #region Public API
    #region Completion and fallback
    /// <summary>Both owners consume submitted candidates and refill without exceeding the configured window.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedOwnersCompleteWithinBound(bool disable)
    {
        // Arrange: Submit mixed graphics and compute requests with a fixed maximum outstanding count.
        // Act: Complete each candidate through the batch while observing queued and completed work.
        // Assert: Every owner completes without exceeding the configured bound.
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
        // Action/check: Compile the graphics owner and create the compute owner inside the window; verify bounded completion and output.
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        Assert.Equal(enabled ? 2 : 0, batch.Outstanding);
        Assert.True(ComputeShaderAsset.TryCreateFromAssets(assets.Api, compute, out var pipeline, out var log, ct: TestContext.Current.CancellationToken), log);
        using (pipeline)
        {
            Assert.True(GL.IsProgram(pipeline!.ProgramId));
            Assert.NotNull(pipeline.ProgramLayout.BinaryInterface);
            using var texture = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            Assert.True(RHIUniformRingSystem.TryGetCurrent(out var ring));
            byte[] data = new byte[16];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data, 123);
            var allocation = ring.AllocateAndWrite(data);
            var commands = RHICommandContext.Current;
            using (var boundary = commands.EnterBoundary(new("Tests.ShaderLinkBatch.ComputeOutput", true,
                compute: [pipeline.Pipeline])))
            {
                boundary.Run(() =>
                {
                    commands.SetPipeline(pipeline.Pipeline);
                    commands.BindResources(new(pipeline.Shader,
                        images: [new RHIImageInput(GpuBindingEntry.Identity(ShaderBindingKind.Image, "outImg"),
                            new RHIImageBinding(texture, ImageAccess.WriteOnly, Layered: true, Format: TextureFormat.Rgba32ui))],
                        buffers: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "TestParams"),
                            BufferRole.Uniform, allocation.Buffer, allocation.OffsetBytes, allocation.SizeBytes, Whole: false)]));
                    commands.Dispatch(new(1));
                    commands.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate));
                });
            }
            uint[] actual = new uint[4];
            GL.BindTexture(TextureTarget.Texture3D, texture.TextureId);
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, actual);
            Assert.Equal(123u, actual[0]);
            GL.BindTexture(TextureTarget.Texture3D, 0);
        }
        Assert.Equal(enabled ? 1 : 0, batch.Outstanding);
        int old = program.ProgramId;
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        Assert.False(GL.IsProgram(old));
        Assert.Equal(0, batch.Outstanding);
#if DEBUG
        Assert.Equal(enabled ? 2 : 0, batch.PeakOutstanding);
#endif
        using var target = CreateRenderTarget(8, 8, TextureFormat.Rgba32f);
        SubmitCacheOutput(program, target);
        var pixel = ReadPixel(target, 4, 4);
        Assert.InRange(pixel.R, .55f, .58f);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Asset managers cannot consume another owner's pending executable.</summary>
    [Fact]
    public void AssetOwnersRemainIsolated()
    {
        // Arrange: Create independent asset owners with separate requested program identities.
        // Act: Prepare their candidates through one shared batch coordinator.
        // Assert: Completion transfers each executable only to its originating owner.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var first = new BinaryShaderApiFixture();
        using var second = new BinaryShaderApiFixture();
        using var batch = new ShaderLinkBatch(first.Api.Assets, ShaderImportsSystem.DefaultDomain, [Settings(Graphics)]);
        int pending = batch.Outstanding;
        using var other = new FixtureProgram();
        other.Initialize(second.Api);
        // Action/check: Let the unrelated manager consume first, then the owning manager; only the owner should drain its candidate.
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
        // Arrange: Pause an asset read and retain the original native context registration and current program.
        // Act: Change registration before the pending read completes.
        // Assert: Candidate allocation is rejected before it can disturb the new context or selected program.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var sentinel = new FixtureProgram();
        sentinel.Initialize(assets.Api);
        Assert.True(sentinel.CompileAndLink());
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, sentinel.ProgramId);
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
            return TestAssetSnapshot.ReadBuiltBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path));
        }
        try
        {
            // Action/check: Trigger the read hook that changes registration; stale-context validation must reject before allocation.
            var failure = Assert.Throws<InvalidOperationException>(() => new ShaderAssetCandidate(
                new ShaderLoadPlan(Settings(Graphics)), Read,
                () => ShaderDigestIndexCache.ForAssets(assets.Api.Assets, ShaderImportsSystem.DefaultDomain)));
            Assert.Contains("original RHI context", failure.Message);
            Assert.Equal(sentinel.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.True(GL.IsProgram(sentinel.ProgramId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            lock (registrations) registrations[original.Handle] = registration;
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
        }
    }

    /// <summary>Pending cached and uncached candidates cannot be consumed or deleted under a reused context registration.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextGenerationMismatchRejectsBeforeTransferOrDeletion(bool cached)
    {
        // Arrange: Prepare installed owners and pending cached or uncached candidates under one context generation.
        // Act: Reuse the registration handle, then attempt candidate transfer and disposal.
        // Assert: Stale work cannot replace or delete installed programs and remains outstanding until valid cleanup.
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.BatchContext", Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(cached ? new ProgramBinaryStore(directory, fileSystem: storage) : null);
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
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, sentinel.ProgramId);
            var replacement = new object();
            try
            {
                // Action/check: Retire and reuse the registration, then consume/dispose the old batch without stale transfer or deletion.
                VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(fixture);
                VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
                Assert.False(program.CompileAndLink());
                Assert.Contains("originating native context generation", program.PreparationFailure!.Message);
                Assert.Equal(installed, program.ProgramId);
                Assert.Equal(revision, program.ExecutableRevision);
#if DEBUG
                Assert.Equal(0, batch.ConsumedCount);
#endif
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
                OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            }
            Assert.Equal(0, batch.Outstanding);
            batch.Dispose();
        }
        finally { if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, true); }
    }

    /// <summary>An asset epoch change after batch submission prevents old captured binaries being published as new assets.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssetInvalidationWithdrawsSubmittedCandidates(bool disable)
    {
        // Arrange: Submit one candidate, retain the installed generation, and mutate its source asset epoch.
        // Act: Invalidate and request readiness before restoring the asset and starting a fresh epoch.
        // Assert: Submitted bytes from the old epoch cannot publish; the fresh epoch can replace the prior executable.
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
        // Action/check: Invalidate after submission and verify captured bytes cannot replace the installed generation until a new epoch.
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

    /// <summary>Ready transfers release temporary stages; cancelled requests retire all their unpublished native allocations.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingCandidateOwnsExactlyOneRetirement(bool transfer)
    {
        // Arrange: Capture candidate program and stage allocations before selecting transfer or cancellation.
        // Act: Complete and transfer the ready result, or cancel unpublished work; dispose the pending owner repeatedly.
        // Assert: Each native allocation retires exactly once under its final owner.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        ReadOnlySpan<byte> Read(string path) => TestAssetSnapshot.ReadBuiltBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path));
        var plan = new ShaderLoadPlan(Settings(Graphics));
        var inputs = DriverProgramCache.Prepare(plan, Read,
            () => ShaderDigestIndexCache.ForAssets(assets.Api.Assets, ShaderImportsSystem.DefaultDomain));
        using var capture = new NativeShaderAllocationCapture();
        using var pending = VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BeginCreateShader(ShaderExecutableInputs.Create(plan, inputs));
        int program = capture.Program;
        int[] stages = capture.Stages;
        // Action/check: Transfer a ready result or cancel unpublished work; repeat disposal and verify each native allocation retires once.
        if (transfer)
        {
            while (pending.TryComplete() == VanillaGraphicsExpanded.Rendering.RHI.CompletionStatus.Pending)
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            using var installed = pending.Result.RequireResource();
            Assert.All(stages, stage => Assert.False(GL.IsShader(stage)));
            pending.Dispose(); pending.Dispose();
            Assert.True(GL.IsProgram(program));
            GL.GetProgram(program, GetProgramParameterName.AttachedShaders, out int count);
            Assert.Equal(0, count);
        }
        else
        {
            // Ready results are recipient-owned; pending cancellation retires only unpublished ownership.
            if (pending.Status == VanillaGraphicsExpanded.Rendering.RHI.CompletionStatus.Ready)
                pending.Result.RequireResource().Dispose();
            else pending.Cancel();
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
        // Arrange: Install a valid program and corrupt only the candidate replacement asset.
        // Act: Submit the failed replacement, restore the asset, and retry synchronously.
        // Assert: Failure retains the installed program; recovery publishes a new one and retires the old handle.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var program = new FixtureProgram();
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink());
        int installed = program.ProgramId;
        assets.Overrides["shaders/tests/render_infrastructure.fsh.spv"] = new byte[20];
        // Action/check: Submit the invalid replacement, keep the installed owner, then restore the asset and retry successfully.
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
        // Arrange: Install a program and queue graphics/compute work under a cancellable batch.
        // Act: Cancel pending work, then dispose the batch and request synchronous loading again.
        // Assert: The installed program remains until synchronous recovery succeeds.
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
            // Action/check: Cancel queued work; the owner must retain its current program until disposal restores synchronous loading.
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
        // Arrange: Install one debug variant and queue its requested settings with a competing compute request.
        // Act: Change defines during refill so the pending candidate represents a superseded snapshot.
        // Assert: The obsolete candidate is rejected and the installed program survives until current settings load.
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
        // Action/check: Change the requested defines during refill; the obsolete candidate must fail while the installed owner survives.
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
        // Arrange: Start a one-slot outer batch with graphics and compute requests.
        // Act: Request nested or out-of-window compute work while the outer batch is pending.
        // Assert: Existing work completes before dependent requests continue and outstanding work stays bounded.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var outer = new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain,
            [Settings(Graphics), Settings(Compute)], maximumOutstanding: 1);
        if (!outer.Enabled) return;
        Assert.Equal(0, outer.CompletedOutstanding);
        assets.BeforeRead = _ => Assert.Equal(outer.Outstanding, outer.CompletedOutstanding);
        // Action/check: Request a nested or out-of-window compute dependency and verify completion precedes subsequent work.
        using (var inner = nested ? new ShaderLinkBatch(assets.Api.Assets, ShaderImportsSystem.DefaultDomain, [Settings(Compute)]) : null)
        {
            Assert.True(ComputeShaderAsset.TryCreateFromAssets(assets.Api, Settings(Compute), out var pipeline, out string log,
                ct: TestContext.Current.CancellationToken), log);
            pipeline!.Dispose();
        }
        assets.BeforeRead = null;
        Assert.Equal(1, outer.CompletedOutstanding);
        using var program = new FixtureProgram();
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink());
        Assert.Equal(nested ? 1 : 0, outer.Outstanding);
#if DEBUG
        Assert.Equal(1, outer.PeakOutstanding);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #region Binary cache coordination
    /// <summary>Warm entries and rejected driver payloads remain consumable through a batch.</summary>
    [Fact]
    public void CacheHitsAndRejectedEntriesComplete()
    {
        // Arrange: Warm a binary cache with one graphics program and retain its key.
        // Act: Complete a warm hit, corrupt the payload, then complete the rejected-entry recovery.
        // Assert: Both requests finish and recovery repairs the cached binary metadata.
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.ParallelLinkTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProgramBinaryStore(directory, fileSystem: storage);
            using var cache = DriverProgramCache.UseStoreForTesting(store);
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram();
            program.Initialize(assets.Api);
            Assert.True(program.CompileAndLink());
            string key = Path.GetFileNameWithoutExtension(Assert.Single(storage.EnumerateFiles(directory, "*.bin")));
            // Action/check: Reuse the warm entry, then reject a corrupt payload and verify batch completion plus cache repair.
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
        finally { if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, true); }
    }
    #endregion
    #region Timing evidence
    /// <summary>Records matched executable submission, consumption, and first draw costs without claiming a cold driver cache.</summary>
    [Fact]
    [Trait("Purpose", "Diagnostic")]
    public void MatchedLoadingTimings()
    {
        // Arrange: Use the same graphics/compute requests and output target for an ABBA synchronous/batched sequence.
        // Act: Measure submission, loading, completion, and first-use intervals for each run.
        // Assert: Preserve the native GL error check; timings are reported without a performance threshold.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var target = CreateRenderTarget(8, 8, TextureFormat.Rgba32f);
        // ABBA retains the same binaries and work, exposing order effects from the driver cache.
        // Action/check: Run matched synchronous and batched requests through consumption and first draw, reporting each interval.
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
            Assert.True(ComputeShaderAsset.TryCreateFromAssets(assets.Api, Settings(Compute), out var firstCompute, out var firstLog,
                ct: TestContext.Current.CancellationToken), firstLog);
            using var firstComputeOwner = firstCompute!;
            Assert.True(ComputeShaderAsset.TryCreateFromAssets(assets.Api, Settings(Compute), out var secondCompute, out var secondLog,
                ct: TestContext.Current.CancellationToken), secondLog);
            using var secondComputeOwner = secondCompute!;
            double loaded = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long use = System.Diagnostics.Stopwatch.GetTimestamp();
            SubmitCacheOutput(first, target);
            SubmitCacheOutput(second, target);
            RHICommandContext.Current.WaitIdle();
            double firstUse = System.Diagnostics.Stopwatch.GetElapsedTime(use).TotalMilliseconds;
#if DEBUG
            output.WriteLine($"Mode={(disable ? "sync" : "batch")}, enabled={batch.Enabled}, count=4, submit={submitted:F3} ms, load={loaded:F3} ms, completion={batch.CompletionMilliseconds:F3} ms, first two RHI output submissions+wait={firstUse:F3} ms, total={loaded + firstUse:F3} ms");
#endif
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Submits the cache fixture through its declared position and UV attributes using the production command context.</summary>
    private static void SubmitCacheOutput(FixtureProgram program, GpuFramebuffer target)
    {
        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([
            new(0, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 16),
            new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 8, 16)]);
        using var vertices = commands.CreateVertexBuffer(new() { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable,
            Capacity = 96, Usage = BufferUsage.StaticDraw }).RequireResource();
        vertices.UploadData<float>([
            -1, -1, 0, 0, 1, -1, 1, 0, 1, 1, 1, 1,
            -1, -1, 0, 0, 1, 1, 1, 1, -1, 1, 0, 1]);
        var geometry = new RHIGeometryInputs(layout, [new(0, new(vertices, 0, vertices.SizeBytes), 16)]);
        var pass = FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex: 0, Load: AttachmentLoad.Preserve)]);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        using var boundary = commands.EnterBoundary(new("Tests.ShaderLinkBatch.Output", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry, new(0, 6));
            commands.EndRenderPass();
        });
    }
    #region Fixture declarations
    /// <summary>Captures immutable defaults for the declared binary variant.</summary>
    private static ShaderSettings Settings(string identity) => new(GpuShaderContracts.Registry.FindProgram(identity));

    /// <summary>Uses the existing deterministic fullscreen contract.</summary>
    private sealed class FixtureProgram : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        #region Public API
        /// <summary>Supplies engine wrappers for the production owner.</summary>
        public FixtureProgram(string name = Graphics)
        {
            PassName = name;
        }
        #endregion

        #region Protected
        /// <summary>Retains this owner's explicit external input publication contract.</summary>
        protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);

        /// <summary>Builds the declared interface for both linking paths.</summary>
        protected override GpuProgramLayout CreateLayout()
        {
            var layout = new GpuProgramLayout();
            layout.RegisterContract(GpuShaderContracts.Create(ShaderName));
            return layout;
        }
        #endregion
    }
    #endregion
    #endregion
}
