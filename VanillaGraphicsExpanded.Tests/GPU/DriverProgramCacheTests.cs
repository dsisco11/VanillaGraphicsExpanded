using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises disposable driver executables through the production graphics and compute loaders.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class DriverProgramCacheTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Graphics execution
    /// <summary>Cold, warm, and reload paths produce the same pixels and retire replaced programs.</summary>
    [Fact]
    public void GraphicsCacheRetainsRenderingAndReloadLifetime()
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DriverCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory));
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram();
            program.Initialize(assets.Api);
            using var target = CreateRenderTarget(8, 8, PixelInternalFormat.Rgba32f);
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
                target.BindWithViewport();
                start = Stopwatch.GetTimestamp();
                RenderFullscreenQuad(program.ProgramId);
                GL.Finish();
                double firstUse = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var pixel = ReadPixel(target, 4, 4);
                Assert.InRange(pixel.R, .55f, .58f);
                Assert.InRange(pixel.G, .55f, .58f);
                output.WriteLine($"Graphics generation {generation}: load {load:F3} ms, first draw/completion {firstUse:F3} ms");
                if (generation == 1)
                {
                    GL.UseProgram(0);
                    ((Vintagestory.Client.NoObf.ShaderProgramBase)program).Dispose();
                    Assert.False(GL.IsProgram(previous));
                    previous = 0;
                }
            }
            Assert.NotEmpty(Directory.GetFiles(directory, "*.json"));
            GL.UseProgram(0);
            program.Dispose();
            Assert.False(GL.IsProgram(previous));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>Compute cached executables retain uniform and image bindings and their first dispatch result.</summary>
    [Fact]
    public void ComputeCacheRetainsBindingsAndDispatch()
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DriverCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory));
            using var assets = new BinaryShaderApiFixture();
            const string identity = "tests/GpuUniformRingBufferIntegrationTests_1";
            var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram(identity));
            using var texture = Texture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D);
            for (int generation = -1; generation < 3; generation++)
            {
                using var bypass = generation < 0 ? DriverProgramCache.UseStoreForTesting(null) : null;
                long start = Stopwatch.GetTimestamp();
                Assert.True(GpuComputePipeline.TryCreateFromAssets(assets.Api, settings, out var pipeline, out string log,
                    ct: TestContext.Current.CancellationToken), log);
                double load = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                using (pipeline)
                {
                    Assert.Equal(generation > 0, DriverProgramCache.LastLoadWasHit);
                    Assert.NotNull(pipeline!.ProgramLayout.BinaryInterface);
                    Assert.True(GpuUniformRingSystem.TryGetCurrent(out var ring));
                    byte[] data = new byte[16];
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)(100 + generation));
                    var allocation = ring.AllocateAndWrite(data);
                    allocation.Buffer.BindRange(0, allocation.OffsetBytes, allocation.SizeBytes);
                    GL.BindImageTexture(0, texture.TextureId, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32ui);
                    GL.UseProgram(pipeline.ProgramId);
                    start = Stopwatch.GetTimestamp();
                    GL.DispatchCompute(1, 1, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureUpdateBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit);
                    GL.Finish();
                    double firstUse = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    uint[] actual = new uint[4];
                    GL.BindTexture(TextureTarget.Texture3D, texture.TextureId);
                    GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, actual);
                    Assert.Equal((uint)(100 + generation), actual[0]);
                    GL.UseProgram(0);
                    output.WriteLine($"Compute generation {generation}: load {load:F3} ms, first dispatch/completion {firstUse:F3} ms");
                }
            }
            GL.BindTexture(TextureTarget.Texture3D, 0);
            GL.BindImageTexture(0, 0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32ui);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GlStateCache.Current.InvalidateAll();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
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
            var store = new ProgramBinaryStore(directory);
            using var cache = DriverProgramCache.UseStoreForTesting(store);
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram();
            program.Initialize(assets.Api);
            Assert.True(program.CompileAndLink());
            string key = Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(directory, "*.bin")));
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
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>Production lighting samplers and block declarations retain their compiled slots on a hit.</summary>
    [Fact]
    public void LightingContractSurvivesCachedExecutable()
    {
        EnsureContextValid();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DriverCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory));
            using var assets = new BinaryShaderApiFixture();
            using var program = new FixtureProgram("lumon_debug");
            program.Initialize(assets.Api);
            var contract = GpuShaderContracts.Create("lumon_debug");
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
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>Provides the engine stage wrappers while leaving binary loading and linking untouched.</summary>
    private sealed class FixtureProgram : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        /// <summary>Selects the existing deterministic fullscreen fixture.</summary>
        public FixtureProgram(string name = "tests/render_infrastructure")
        {
            PassName = name;
            VertexShader = new Vintagestory.Client.NoObf.Shader();
            FragmentShader = new Vintagestory.Client.NoObf.Shader();
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
