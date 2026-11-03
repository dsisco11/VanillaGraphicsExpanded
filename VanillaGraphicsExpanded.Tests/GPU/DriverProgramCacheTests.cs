using VanillaGraphicsExpanded.Tests.TestSupport;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises disposable driver executables through the production graphics and compute loaders.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class DriverProgramCacheTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    private readonly CountingDiskCacheFileSystem storage = new();

    #region Public API
    #region Graphics execution
    /// <summary>Invalid packaged declarations cannot replace a working generation, including a cached native executable.</summary>
    [Theory]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    [InlineData(false, "schema")]
    [InlineData(true, "schema")]
    [InlineData(false, "shape")]
    [InlineData(true, "shape")]
    [InlineData(false, "absent-output")]
    [InlineData(true, "absent-output")]
    public void PackagedMetadataFailurePreservesInstalledGeneration(bool useCache, string failure)
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DriverCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(useCache ? new ProgramBinaryStore(directory, fileSystem: storage) : null);
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram();
            program.Initialize(assets.Api);
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            Assert.Equal(useCache, DriverProgramCache.LastLoadWasHit);
            int installed = program.ProgramId;
            ulong revision = program.ExecutableRevision;
            var bindings = program.ResourceBindings;
            var graphics = program.GraphicsInterface;
            var manifest = ShaderBinaryDigest.Parse(TestAssetSnapshot.ReadBuiltBytes(Path.Combine(AppContext.BaseDirectory,
                "assets", "shaders", ShaderBinaryDigest.FileName)))!;
            const string fragment = "tests/render_infrastructure.fsh.spv";
            var entry = manifest.Binaries[fragment];
            var metadata = entry.Interface!;
            // Keep the binary hash valid so declaration tests reach linked-interface comparison.
            metadata = failure switch
            {
                "missing" => null,
                "schema" => metadata with { Version = -1 },
                "shape" => metadata with { Interface = metadata.Interface with
                    { Outputs = [metadata.Interface.Outputs[0] with { VectorSize = 3 }] } },
                _ => metadata with { Interface = metadata.Interface with { Outputs = [] } }
            };
            manifest.Binaries[fragment] = entry with { Interface = metadata };
            assets.Overrides["shaders/" + ShaderBinaryDigest.FileName] = ShaderBinaryDigest.Encode(manifest.Binaries);
            ShaderDigestIndexCache.Clear();
            Assert.False(program.CompileAndLink());
            Assert.Equal(installed, program.ProgramId);
            Assert.Equal(revision, program.ExecutableRevision);
            Assert.Same(bindings, program.ResourceBindings);
            Assert.Same(graphics, program.GraphicsInterface);
            Assert.True(GL.IsProgram(installed));
            Assert.Contains(assets.Logs, message => message.Contains(
                failure is "shape" or "absent-output" ? "packaged compiler declarations" : "interface schema/identity",
                StringComparison.OrdinalIgnoreCase));
            assets.Overrides.Clear();
            ShaderDigestIndexCache.Clear();
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            Assert.Equal(useCache, DriverProgramCache.LastLoadWasHit);
            Assert.True(program.ExecutableRevision > revision);
        }
        finally
        {
            ShaderDigestIndexCache.Clear();
            if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, true);
        }
    }

    /// <summary>Cold, warm, and reload paths produce the same pixels and retire replaced programs.</summary>
    [Fact]
    public void GraphicsCacheRetainsRenderingAndReloadLifetime()
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DriverCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory, fileSystem: storage));
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram();
            program.Initialize(assets.Api);
            using var target = CreateRenderTarget(8, 8, TextureFormat.Rgba32f);
            int previous = 0;
            for (int generation = -1; generation < 3; generation++)
            {
                using var bypass = generation < 0 ? DriverProgramCache.UseStoreForTesting(null) : null;
                long start = Stopwatch.GetTimestamp();
                Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
                Assert.Equal(generation > 0, DriverProgramCache.LastLoadWasHit);
                double load = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (previous != 0) Assert.False(GL.IsProgram(previous));
                previous = program.ProgramId;
                start = Stopwatch.GetTimestamp();
                SubmitCacheOutput(program, target);
                RHICommandContext.Current.WaitIdle();
                double firstUse = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var pixel = ReadPixel(target, 4, 4);
                Assert.InRange(pixel.R, .55f, .58f);
                Assert.InRange(pixel.G, .55f, .58f);
                output.WriteLine($"Graphics generation {generation}: load {load:F3} ms, first RHI draw/completion {firstUse:F3} ms");
                if (generation == 1) program.InvalidateAssets();
            }
            Assert.NotEmpty(storage.EnumerateFiles(directory, "*.json"));
            program.Dispose();
            Assert.False(GL.IsProgram(previous));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, true); }
    }

    /// <summary>Compute cached executables retain uniform and image bindings and their first dispatch result.</summary>
    [Fact]
    public void ComputeCacheRetainsBindingsAndDispatch()
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DriverCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory, fileSystem: storage));
            using var assets = new BinaryShaderApiFixture();
            const string identity = "tests/GpuUniformRingBufferIntegrationTests_1";
            var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram(identity));
            using var texture = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            for (int generation = -1; generation < 3; generation++)
            {
                using var bypass = generation < 0 ? DriverProgramCache.UseStoreForTesting(null) : null;
                long start = Stopwatch.GetTimestamp();
                Assert.True(ComputeShaderAsset.TryCreateFromAssets(assets.Api, settings, out var pipeline, out string log,
                    ct: TestContext.Current.CancellationToken), log);
                double load = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                using (pipeline)
                {
                    Assert.Equal(generation > 0, DriverProgramCache.LastLoadWasHit);
                    Assert.NotNull(pipeline!.ProgramLayout.BinaryInterface);
                    Assert.True(RHIUniformRingSystem.TryGetCurrent(out var ring));
                    byte[] data = new byte[16];
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)(100 + generation));
                    var allocation = ring.AllocateAndWrite(data);
                    var commands = RHICommandContext.Current;
                    start = Stopwatch.GetTimestamp();
                    using (var boundary = commands.EnterBoundary(new("Tests.DriverCache.ComputeOutput", true,
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
                            commands.WaitIdle();
                        });
                    }
                    double firstUse = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    uint[] actual = new uint[4];
                    GL.BindTexture(TextureTarget.Texture3D, texture.TextureId);
                    GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, actual);
                    Assert.Equal((uint)(100 + generation), actual[0]);
                    output.WriteLine($"Compute generation {generation}: load {load:F3} ms, first dispatch/completion {firstUse:F3} ms");
                }
            }
            GL.BindTexture(TextureTarget.Texture3D, 0);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            OpenGlStateCache.Current.InvalidateAll();
            if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, true);
        }
    }

    /// <summary>A validly indexed but unsupported executable format falls back and is repaired by linking.</summary>
    [Fact]
    public void RejectedBinaryFallsBackAndBypassPreservesNormalLoading()
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DriverCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProgramBinaryStore(directory, fileSystem: storage);
            using var cache = DriverProgramCache.UseStoreForTesting(store);
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram();
            program.Initialize(assets.Api);
            Assert.True(program.CompileAndLink());
            string key = Path.GetFileNameWithoutExtension(Assert.Single(storage.EnumerateFiles(directory, "*.bin")));
            store.Write(key, -1, [1, 2, 3, 4]);
            Assert.True(program.CompileAndLink());
            Assert.False(DriverProgramCache.LastLoadWasHit);
            Assert.True(program.CompileAndLink());
            Assert.True(DriverProgramCache.LastLoadWasHit);
            Assert.True(store.TryRead(key, out int supportedFormat, out _));
            store.Write(key, supportedFormat, [1, 2, 3, 4]);
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            Assert.False(DriverProgramCache.LastLoadWasHit);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Assert.True(program.CompileAndLink());
            Assert.True(DriverProgramCache.LastLoadWasHit);
            int installed = program.ProgramId;
            assets.Overrides["shaders/tests/render_infrastructure.fsh.spv"] = new byte[20];
            Assert.False(program.CompileAndLink());
            Assert.Equal(installed, program.ProgramId);
            Assert.True(GL.IsProgram(installed));
            assets.Overrides.Clear();
            using (DriverProgramCache.UseStoreForTesting(null))
            {
                Assert.True(program.CompileAndLink());
                Assert.False(DriverProgramCache.LastLoadWasHit);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, true); }
    }

    /// <summary>Production lighting samplers and block declarations retain their compiled slots on a hit.</summary>
    [Fact]
    public void LightingContractSurvivesCachedExecutable()
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DriverCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory, fileSystem: storage));
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram("pbr_direct_lighting");
            program.Initialize(assets.Api);
            var contract = GpuShaderContracts.Create("pbr_direct_lighting");
            for (int generation = 0; generation < 2; generation++)
            {
                Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
                Assert.Equal(generation > 0, DriverProgramCache.LastLoadWasHit);
                int checkedSamplers = 0;
                foreach (var pair in contract.Samplers)
                {
                    int location = program.ResourceBindings.BinaryInterface!.Uniforms[pair.Key];
                    if (location < 0) continue;
                    GL.GetUniform(program.ProgramId, location, out int actual);
                    Assert.Equal(pair.Value.Slot, actual);
                    checkedSamplers++;
                }
                Assert.True(checkedSamplers > 1);
                GL.GetProgram(program.ProgramId, GetProgramParameterName.ActiveUniformBlocks, out int blocks);
                for (int index = 0; index < blocks; index++)
                {
                    GL.GetActiveUniformBlock(program.ProgramId, index, ActiveUniformBlockParameter.UniformBlockBinding, out int actual);
                    Assert.Contains(contract.UniformBlocks.Values, item => item.Slot == actual);
                }
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, true); }
    }

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
        using var boundary = commands.EnterBoundary(new("Tests.DriverCache.Output", true,
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
    #endregion

    /// <summary>Selects packaged declarations while leaving binary loading and linking untouched.</summary>
    private sealed class FixtureProgram : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        #region Submission
        /// <summary>Retains this owner's explicit external input publication contract.</summary>
        protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
        #endregion

        /// <summary>Selects the existing deterministic fullscreen fixture.</summary>
        public FixtureProgram(string name = "tests/render_infrastructure")
        {
            PassName = name;
        }

        /// <summary>Registers the production declaration used by both link paths.</summary>
        protected override GpuProgramLayout CreateLayout()
        {
            var layout = new GpuProgramLayout();
            layout.RegisterContract(GpuShaderContracts.Create(ShaderName));
            return layout;
        }
    }
    #endregion
}
