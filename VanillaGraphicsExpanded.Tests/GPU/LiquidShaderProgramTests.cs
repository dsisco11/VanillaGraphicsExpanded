using OpenTK.Graphics.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises actual SPIR-V blocks through the same interface calls made by engine mesh pools.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LiquidShaderProgramTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Measures complete production block copies across repeated nested surface and volume activation.</summary>
    [Fact]
    public void RepeatedLiquidActivationReportsUploadCopies()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        var surface = GpuShaderPrograms.Get<LiquidShaderProgram>(assets.Api, "pbr_liquid")!;
        var volume = GpuShaderPrograms.Get<LiquidShaderProgram>(assets.Api, LiquidShaderProgram.VolumePassName)!;
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var atmosphere = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        AssignTextures(surface, texture, atmosphere, sharedCamera, sharedLights); AssignTextures(volume, texture, atmosphere, sharedCamera, sharedLights);
        surface.Origin = new(1, 2, 3); volume.Origin = new(4, 5, 6);
        using var ring = new GpuUniformRingBuffer(4 * 1024 * 1024, 2, false);
        ring.BeginFrame(0); GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            using (surface.UseScope()) { using (volume.UseScope()) { } }
            long bytesBefore = ring.BytesWritten;
            long before = ring.AllocationsWritten, start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < 32; iteration++)
            { using (surface.UseScope()) { using (volume.UseScope()) { } } }
            long copies = ring.AllocationsWritten - before;
            Assert.Equal(0, copies);
            Assert.Equal(bytesBefore, ring.BytesWritten);
            output.WriteLine($"uniform-publication nestedPairs=32 allocations={copies} copiedBytes={ring.BytesWritten - bytesBefore} elapsedMs={System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:R}");
            using (surface.UseScope())
            {
                Assert.Equal(new float[] { 1, 2, 3 }, ReadDraw(out _, out _).AsSpan(16, 3).ToArray());
                using (volume.UseScope()) Assert.Equal(new float[] { 4, 5, 6 }, ReadDraw(out _, out _).AsSpan(16, 3).ToArray());
                Assert.Equal(new float[] { 1, 2, 3 }, ReadDraw(out _, out _).AsSpan(16, 3).ToArray());
            }
        }
        finally { GpuUniformRingSystem.ClearCurrent(); GpuShaderPrograms.Dispose(assets.Api); }
    }

    /// <summary>Measures actual frame preparation with representative and maximum authored array counts.</summary>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(0, 4, 1)]
    [InlineData(3, 4, 1)]
    [InlineData(0, 100, 3)]
    [InlineData(3, 100, 3)]
    public void FrameCaptureRetainsCommonInputsAcrossArrayLoads(int mode, int lights, int spheres)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        var uniforms = new DefaultShaderUniforms
        {
            PointLightsCount = lights,
            FogSphereQuantity = spheres,
            ColorMapRects4 = Enumerable.Range(0, 160).Select(value => (float)value).ToArray(),
            ZNear = .1f,
            ZFar = 100,
            WaterStillCounter = 2,
            WaterFlowCounter = 3,
            WindWaveCounter = 4
        };
        float[] projection = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var api = CreateFrameCaptureApi(uniforms, projection);
        var program = new LiquidShaderProgram { CaptureMode = mode };
        using var sharedCamera = TestFrameCamera.CreateFromProjection(projection, 512, 512, .1f, 100);
        using var sharedLights = new VgeLightsUniformBuffer();
        program.LightsInputs = sharedLights;
        program.FrameInputs = sharedCamera;
        var tile = new Vec2f(.125f, .125f);
        for (int frame = 0; frame < 8192; frame++) program.CaptureFrameInputs(api, tile);
        const int iterations = 2048;
        for (int sample = 0; sample < 5; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread(), start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int frame = 0; frame < iterations; frame++) program.CaptureFrameInputs(api, tile);
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"frame-capture mode={mode} lights={lights} spheres={spheres} sample={sample} iterations={iterations} bytes={bytes} elapsedMs={elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency:R}");
        }
        var frameBytes = ((ILiquidShaderProgramBindings)program).FrameParameters.Bytes;
        Assert.Equal(new Vector4(2, 3, 0, 4), MemoryMarshal.Read<Vector4>(frameBytes.Slice(128, 16)));
        Assert.Equal(new Vector4(156, 157, 158, 159), MemoryMarshal.Read<Vector4>(frameBytes.Slice(944, 16)));
        Assert.Same(sharedCamera, ((ILiquidShaderProgramBindings)program).FrameInputs);
        Assert.Equal(Matrix4x4.Identity, MemoryMarshal.Read<Matrix4x4>(sharedCamera.Bytes.Slice(128, 64)));
    }

    /// <summary>Projection changes publish a coherent shared GPU matrix pair for liquid consumers.</summary>
    [Fact]
    public void ProjectionUpdatesPublishMatchingInverseForAsymmetricCamera()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var atmosphere = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        AssignTextures(program, texture, atmosphere, sharedCamera, sharedLights);
        foreach (float shift in new[] { .23f, -.17f })
        {
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(shift > 0 ? .8f : 1.3f, 1.7f, .1f, 100);
            projection.M33 = -100.1f / 99.9f; projection.M43 = -20f / 99.9f;
            projection.M31 = shift; projection.M32 = -.11f;
            Assert.True(Matrix4x4.Invert(projection, out var inverse));
            float[] projected = MemoryMarshal.Cast<Matrix4x4, float>(new[] { projection }).ToArray();
            float[] inverseProjected = MemoryMarshal.Cast<Matrix4x4, float>(new[] { inverse }).ToArray();
            float[] identity = Mat4f.Create();
            sharedCamera.Capture(projected, identity, inverseProjected, identity, projected, projected,
                new(512, 512), 0, 0, Vector3.Zero, Vector3.Zero, 0, new(.1f, 100));
            var pair = ReadPair();
            Assert.Equal(projection, pair.Projection);
            // Project independent view-space points, then use the GPU-published
            // inverse to reconstruct them. Off-axis terms expose transposition errors.
            foreach (var point in new[] { new Vector4(.4f, -.2f, -2, 1), new Vector4(-1.2f, .7f, -12, 1) })
            {
                Vector4 clip = Vector4.Transform(point, pair.Projection);
                Vector4 reconstructed = Vector4.Transform(clip / clip.W, pair.Inverse);
                reconstructed /= reconstructed.W;
                Assert.InRange(Vector4.Distance(point, reconstructed), 0, .0002f);
            }
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        /// <summary>Reads the production uniform block after normal shader activation publishes frame inputs.</summary>
        (Matrix4x4 Projection, Matrix4x4 Inverse) ReadPair()
        {
            using var activation = program.UseScope();
            GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.Frame, out int buffer);
            GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.Frame, out int offset);
            byte[] bytes = new byte[VgeFrameUniformBuffer.PackedSize];
            using var binding = StateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
            GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, bytes.Length, bytes);
            return (MemoryMarshal.Read<Matrix4x4>(bytes.AsSpan(0, 64)),
                MemoryMarshal.Read<Matrix4x4>(bytes.AsSpan(128, 64)));
        }
    }

    /// <summary>Alternating volume and surface submissions retain separate executables through quality changes and reload.</summary>
    [Fact]
    public void RegisteredVolumeAndSurfaceRetainIndependentExecutables()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        var surface = Assert.IsType<LiquidShaderProgram>(GpuShaderPrograms.Get<GpuProgram>(assets.Api, "pbr_liquid"));
        var volume = Assert.IsType<LiquidShaderProgram>(GpuShaderPrograms.Get<GpuProgram>(assets.Api, LiquidShaderProgram.VolumePassName));
        Assert.NotSame(surface, volume);
        Assert.Equal(0, surface.CaptureMode);
        Assert.Equal(3, volume.CaptureMode);
        Assert.True(surface.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.True(volume.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.NotEqual(surface.ProgramId, volume.ProgramId);
        TestUniformRing.EnsureFrame();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var atmosphere = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        AssignTextures(surface, texture, atmosphere, sharedCamera, sharedLights);
        AssignTextures(volume, texture, atmosphere, sharedCamera, sharedLights);
        var uniforms = new DefaultShaderUniforms { ColorMapRects4 = new float[160] };
        float[] projection = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var captureApi = CreateFrameCaptureApi(uniforms, projection);
        AssertStableSubmissions();

        // A user quality change replaces only the surface executable. The volume
        // executable has its own installed settings and remains ready for submission.
        int volumeId = volume.ProgramId;
        int surfaceId = surface.ProgramId;
        Assert.True(surface.ConfigureOptions(() => surface.RefractionQuality = 0));
        Assert.True(surface.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.NotEqual(surfaceId, surface.ProgramId);
        Assert.Equal(volumeId, volume.ProgramId);
        Assert.False(volume.RequiresPreparation);
        AssertStableSubmissions();

        // Production reload redeclares the owners. Both retain their selected
        // mode and rebuild on demand, then return to stable alternating use.
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        Assert.Same(surface, GpuShaderPrograms.Get<LiquidShaderProgram>(assets.Api, "pbr_liquid"));
        Assert.Same(volume, GpuShaderPrograms.Get<LiquidShaderProgram>(assets.Api, LiquidShaderProgram.VolumePassName));
        Assert.True(surface.RequiresPreparation);
        Assert.True(volume.RequiresPreparation);
        Assert.True(surface.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.True(volume.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.Equal(0, surface.RefractionQuality);
        Assert.Equal(0, surface.CaptureMode);
        Assert.Equal(3, volume.CaptureMode);
        AssertStableSubmissions();
        // An owner whose requested contract changes must refresh arrays before
        // any stage that consumes them, even after several volume-only frames.
        foreach (int mode in new[] { 0, 1, 3 })
        {
            volume.CaptureMode = mode;
            CaptureAndVerify(volume, 2, 1, 37);
        }
        surfaceId = surface.ProgramId;
        volumeId = volume.ProgramId;
        GpuShaderPrograms.Dispose(assets.Api);
        Assert.False(GL.IsProgram(surfaceId));
        Assert.False(GL.IsProgram(volumeId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        /// <summary>Checks real GL activation without asset reloads or executable replacement between draws.</summary>
        void AssertStableSubmissions()
        {
            int reads = assets.Reads.Count;
            int retainedSurface = surface.ProgramId;
            int retainedVolume = volume.ProgramId;
            for (int frame = 0; frame < 3; frame++)
                foreach (var program in new[] { volume, surface })
                {
                    CaptureAndVerify(program, frame == 0 ? 100 : frame == 1 ? 1 : 0, frame == 0 ? 3 : frame == 1 ? 1 : 0, frame + 11);
                }
            Assert.Equal(retainedSurface, surface.ProgramId);
            Assert.Equal(retainedVolume, volume.ProgramId);
            Assert.Equal(reads, assets.Reads.Count);
        }

        /// <summary>Checks the actual uploaded frame after changed counts, values and owner selection.</summary>
        void CaptureAndVerify(LiquidShaderProgram program, int lights, int spheres, float value)
        {
            uniforms.PointLightsCount = lights; uniforms.FogSphereQuantity = spheres;
            Array.Fill(uniforms.PointLights3, value); Array.Fill(uniforms.PointLightColors3, value + 1);
            Array.Fill(uniforms.FogSpheres, value + 2); Array.Fill(uniforms.ColorMapRects4, value + 3);
            uniforms.WaterStillCounter = value + 4;
            program.ModelViewMatrix = projection;
            program.Origin = new(value, value + 1, value + 2);
            program.WaveFrame = new(new Vector4(value), .5f);
            byte[] retainedArrays = ((ILiquidShaderProgramBindings)program).FrameParameters.Bytes.Slice(960, 384).ToArray();
            sharedLights.Capture(lights, uniforms.PointLights3, uniforms.PointLightColors3);
            program.CaptureFrameInputs(captureApi, new Vec2f(.125f, .125f));
            Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
            using var activation = program.UseScope();
            GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.ShaderInputs, out int buffer);
            GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.ShaderInputs, out int offset);
            byte[] bytes = new byte[LiquidFrameParamsUbo.BlockSize];
            using var binding = StateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
            GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, bytes.Length, bytes);
            bool boundary = program.CaptureMode == 3;
            Assert.Same(sharedLights, ((ILiquidShaderProgramBindings)program).LightsInputs);
            Assert.Equal((uint)(boundary ? 0 : spheres), MemoryMarshal.Read<uint>(bytes.AsSpan(272)));
            Assert.Equal(value + 3, MemoryMarshal.Read<float>(bytes.AsSpan(320)));
            Assert.Equal(value + 4, MemoryMarshal.Read<float>(bytes.AsSpan(128)));
            Assert.Same(sharedCamera, ((ILiquidShaderProgramBindings)program).FrameInputs);
            if (boundary) Assert.Equal(retainedArrays, bytes.AsSpan(960, 384).ToArray());
            if (!boundary && lights > 0)
            {
                Assert.Equal(value, MemoryMarshal.Read<float>(sharedLights.Bytes.Slice(16 + (lights - 1) * 16)));
                Assert.Equal(value + 1, MemoryMarshal.Read<float>(sharedLights.Bytes.Slice(1616 + (lights - 1) * 16)));
            }
            if (!boundary && spheres > 0) Assert.Equal(value + 2, MemoryMarshal.Read<float>(bytes.AsSpan(960 + (spheres * 8 - 1) * 16)));
            Assert.Equal(new Vector4(value), MemoryMarshal.Read<Vector4>(((ILiquidShaderProgramBindings)program).WaveParameters.Bytes));
            float[] draw = ReadDraw(out _, out _);
            Assert.Equal(projection, draw.AsSpan(0, 16).ToArray());
            Assert.Equal(new float[] { value, value + 1, value + 2 }, draw.AsSpan(16, 3).ToArray());
            Assert.Equal(program.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.False(program.RequiresPreparation);
        }
    }

    /// <summary>Water selections load distinct offline fragments while retaining the shared vertex and capture choices.</summary>
    [Fact]
    public void WaterOptionsSelectPrecompiledFragmentsBeforePreparation()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.Equal(3, program.RefractionQuality);
        Assert.Equal(2, program.RefractionBackgroundScale);
        Assert.DoesNotContain("vge_waterRefractionQuality", LiquidShaderProgram.Contract.Bindings.UniformLocations.Keys);
        string? vertex = null;
        var fragments = new HashSet<string>();
        // Exercise generation replacement on one retained owner, rather than only
        // enumerating settings that might never have compiled executable binaries.
        for (int quality = 0; quality <= 3; quality++)
            for (int resolution = 1; resolution <= 2; resolution++)
            {
                Assert.True(program.ConfigureOptions(() =>
                {
                    program.RefractionQuality = quality;
                    program.RefractionBackgroundScale = resolution;
                }));
                Assert.True(program.RequiresPreparation);
                var plan = new ShaderLoadPlan(program.RequestedSettings);
                string selectedVertex = plan.Stages.Single(stage => stage.Stage.Kind == ShaderStageKind.Vertex).BinaryPath;
                vertex ??= selectedVertex;
                Assert.Equal(vertex, selectedVertex);
                Assert.True(fragments.Add(plan.Stages.Single(stage => stage.Stage.Kind == ShaderStageKind.Fragment).BinaryPath));
                Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
                Assert.False(program.RequiresPreparation);
                Assert.Same(program.RequestedSettings, program.InstalledSettings);
                Assert.False(((IShaderProgram)program).HasUniform("vge_waterRefractionQuality"));
                Assert.Equal(quality, program.RefractionQuality);
                Assert.Equal(resolution, program.RefractionBackgroundScale);
            }
        Assert.Equal(8, fragments.Count);
        Assert.True(program.ConfigureOptions(() => program.CaptureMode = 1));
        Assert.Equal(3, program.RefractionQuality);
        Assert.Equal(2, program.RefractionBackgroundScale);
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.Equal(vertex, new ShaderLoadPlan(program.InstalledSettings!).Stages.Single(stage => stage.Stage.Kind == ShaderStageKind.Vertex).BinaryPath);
        foreach (int capture in new[] { 2, 3 })
        {
            Assert.True(program.ConfigureOptions(() => program.CaptureMode = capture));
            Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
            string fragment = new ShaderLoadPlan(program.RequestedSettings).Stages.Single(stage => stage.Stage.Kind == ShaderStageKind.Fragment).BinaryPath;
            Assert.True(program.ConfigureOptions(() =>
            {
                program.RefractionQuality = capture == 2 ? 0 : 2;
                program.RefractionBackgroundScale = capture == 2 ? 1 : 2;
            }));
            Assert.True(program.RequiresPreparation);
            Assert.NotEqual(fragment, new ShaderLoadPlan(program.RequestedSettings).Stages.Single(stage => stage.Stage.Kind == ShaderStageKind.Fragment).BinaryPath);
            Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        }
        // Diagnostic-only output may ignore these settings, but returning to the
        // real liquid path must apply the user's last retained choices.
        Assert.True(program.ConfigureOptions(() => program.CaptureMode = 0));
        Assert.Equal(2, program.RefractionQuality);
        Assert.Equal(2, program.RefractionBackgroundScale);
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.Same(program.RequestedSettings, program.InstalledSettings);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Pool draw boundaries publish staged origin writes and retain prior mini-dimension snapshots.</summary>
    [Fact]
    public void PoolWritesPublishAndRestoreActualGpuParameters()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var volume = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        AssignTextures(program, texture, volume, sharedCamera, sharedLights);
        using var activation = program.UseScope();
        Assert.Same(program, StateCache.ActiveProgram);
        Assert.Null(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
        IShaderProgram engine = new VanillaGraphicsExpanded.Rendering.Integration.LiquidPoolInputAdapter(program);
        int frameBlock = program.ProgramLayout.BinaryInterface!.GetUniformBlockIndex(LiquidFrameParamsUbo.BlockName);
        GL.GetActiveUniformBlock(program.ProgramId, frameBlock, ActiveUniformBlockParameter.UniformBlockDataSize, out int frameSize);
        Assert.Equal(LiquidFrameParamsUbo.BlockSize, frameSize);
        Assert.True(engine.HasUniform("modelViewMatrix"));
        Assert.True(engine.HasUniform("origin"));
        Assert.True(engine.HasUniform("forcedTransparency"));
        Assert.False(engine.HasUniform("mvpMatrix"));
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        engine.UniformMatrix("modelViewMatrix", identity);
        engine.Uniform("forcedTransparency", .25f);
        engine.Uniform("origin", new Vec3f(1, 2, 3));
        program.Use();
        var first = ReadDraw(out int firstOffset, out int firstBuffer);
        Assert.Equal(new float[] { 1, 2, 3, .25f }, first[16..]);
        engine.Uniform("origin", new Vec3f(-4, 5, 6));
        Assert.Equal(first, ReadDraw(out _, out _));
        program.Use();
        var second = ReadDraw(out int secondOffset, out int secondBuffer);
        Assert.True(firstBuffer != secondBuffer || firstOffset != secondOffset);
        Assert.Equal(new float[] { -4, 5, 6, .25f }, second[16..]);
        // Previously submitted draws must keep their old bytes after later parameter writes.
        using (StateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, firstBuffer))
        {
            float[] retained = new float[20];
            GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)firstOffset, 80, retained);
            Assert.Equal(first, retained);
        }
        var moved = (float[])identity.Clone(); moved[12] = 7;
        engine.UniformMatrix("modelViewMatrix", moved);
        program.Use();
        Assert.Equal(7, ReadDraw(out _, out _)[12]);
        engine.UniformMatrix("modelViewMatrix", identity);
        engine.Uniform("forcedTransparency", 0f);
        program.Use();
        var restored = ReadDraw(out _, out _);
        Assert.Equal(identity, restored[..16]);
        Assert.Equal(0, restored[19]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Typed texture inputs bind every declared unit with the intended target and sampling policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedImagesBindDeclaredTargetsAndSamplers(bool useInterface)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        using var terrain = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8);
        using var depth = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        using var material = Texture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var near = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        using var far = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        using var radiance = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture3D);
        using var attenuation = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture3D);
        // Exercise both the preserved engine API and the interface boundary against
        // real linked resources; targets, samplers and uniform units must be identical.
        if (useInterface)
        {
            var bindings = (ILiquidShaderProgramBindings)program;
            bindings.TerrainTexture = terrain.TextureId;
            bindings.DepthTexture = depth.TextureId;
            bindings.MaterialParamsTexture = material;
            bindings.ShadowMapNear = near.TextureId;
            bindings.ShadowMapFar = far.TextureId;
            bindings.AerialRadianceTexture = radiance;
            bindings.AerialAttenuationTexture = attenuation;
        }
        else
        {
            program.TerrainTexture = terrain.TextureId;
            program.DepthTexture = depth.TextureId;
            program.MaterialParamsTexture = material;
            program.ShadowMapNear = near.TextureId;
            program.ShadowMapFar = far.TextureId;
            program.AerialRadianceTexture = radiance;
            program.AerialAttenuationTexture = attenuation;
        }
        program.LightsInputs = sharedLights;
        program.FrameInputs = sharedCamera;
        using var activation = program.UseScope();
        int[] images = [terrain.TextureId, depth.TextureId, material.TextureId, near.TextureId, far.TextureId, radiance.TextureId, attenuation.TextureId];
        int[] samplers = [0, GpuSamplers.NearestClamp.SamplerId, GpuSamplers.NearestClamp.SamplerId,
            GpuSamplers.ShadowCompareLinearClamp.SamplerId, GpuSamplers.ShadowCompareLinearClamp.SamplerId, 0, 0];
        string[] names = ["terrainTex", "depthTex", "vge_materialParamsTex", "shadowMapNear", "shadowMapFar", "vge_atmosphereAerialRadiance", "vge_atmosphereAerialAttenuation"];
        for (int unit = 0; unit < images.Length; unit++)
        {
            StateCache.Current.ActiveTexture(unit);
            Assert.Equal(images[unit], GL.GetInteger(unit < 5 ? GetPName.TextureBinding2D : GetPName.TextureBinding3D));
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, unit, out int sampler);
            Assert.Equal(samplers[unit], sampler);
            var binding = program.ProgramLayout.BinaryInterface!.PreparedBindings.Resolve(
                VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingEntry.Identity(VanillaGraphicsExpanded.Rendering.Contracts.ShaderBindingKind.Sampler, names[unit]));
            Assert.True(binding.Active);
            Assert.Equal(unit, binding.Contract.Binding.Slot);
            Assert.DoesNotContain(names[unit], LiquidShaderProgram.Contract.Bindings.UniformLocations.Keys);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Staged typed frame fields reach GPU memory only at the explicit publication boundary.</summary>
    [Fact]
    public void UsePublishesTypedFrameFields()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var volume = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        AssignTextures(program, texture, volume, sharedCamera, sharedLights);
        program.Animation = new(1, 2, 3, 4);
        program.SetFogSphereCount(1);
        sharedLights.Capture(2, [0, 0, 0, 5, 6, 7], new float[6]);
        program.SetFogSphereComponent(23, .75f);
        using var activation = program.UseScope();
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.ShaderInputs, out int buffer);
        GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.ShaderInputs, out int offset);
        Assert.NotEqual(0, buffer);
        byte[] bytes = new byte[LiquidFrameParamsUbo.BlockSize];
        using var binding = StateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, bytes.Length, bytes);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes.AsSpan(128, 16)).ToArray());
        Assert.Equal(1u, System.Runtime.InteropServices.MemoryMarshal.Read<uint>(bytes.AsSpan(272)));
        Assert.Equal(new float[] { 5, 6, 7 }, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(sharedLights.Bytes.Slice(32, 12)).ToArray());
        Assert.Equal(.75f, System.Runtime.InteropServices.MemoryMarshal.Read<float>(bytes.AsSpan(1328)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Harmony compiles the real patched methods with both new boundaries and atlas interception.</summary>
    [Fact]
    public void InstalledEngineHooksCompose()
    {
        var harmony = new HarmonyLib.Harmony("VGE.Tests.LiquidHookInstallation");
        try
        {
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.TerrainAtlasDrawBindingHook)).Patch();
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.LiquidRenderHooks)).Patch();
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.LiquidMeshSourceHook)).Patch();
            harmony.CreateClassProcessor(typeof(LiquidPoolSubmissionHook)).Patch();
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    #endregion

    #region Private
    /// <summary>Adapts the existing engine API edge without allocating frame inputs inside capture.</summary>
    private static ICoreClientAPI CreateFrameCaptureApi(DefaultShaderUniforms uniforms, float[] projection)
    {
        var render = RuntimeRenderEvents.Adapt<IRenderAPI>((method, _) => method.Name switch
        {
            "get_ShaderUniforms" => uniforms,
            "get_CurrentProjectionMatrix" => projection,
            "get_FrameWidth" => 512,
            "get_FrameHeight" => 512,
            _ => throw new NotSupportedException(method.Name)
        });
        var size = new Size2i(1024, 1024);
        var atlas = RuntimeRenderEvents.Adapt<IBlockTextureAtlasAPI>((method, _) => method.Name == "get_Size"
            ? size : throw new NotSupportedException(method.Name));
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, _) => method.Name switch
        {
            "get_Render" => render,
            "get_BlockTextureAtlas" => atlas,
            _ => throw new NotSupportedException(method.Name)
        });
        return api;
    }


    /// <summary>Supplies required borrowed textures for buffer-only tests that issue no draw.</summary>
    private static void AssignTextures(LiquidShaderProgram program, Texture2D texture, DynamicTexture3D volume, VgeFrameUniformBuffer camera, VgeLightsUniformBuffer lights)
    {
        program.LightsInputs = lights;
        program.FrameInputs = camera;
        program.TerrainTexture = texture.TextureId;
        program.DepthTexture = texture.TextureId;
        program.MaterialParamsTexture = texture;
        program.ShadowMapNear = texture.TextureId;
        program.ShadowMapFar = texture.TextureId;
        program.AerialRadianceTexture = volume;
        program.AerialAttenuationTexture = volume;
    }

    /// <summary>Reads the GPU-bound range rather than the program's CPU staging storage.</summary>
    private static float[] ReadDraw(out int offset, out int buffer)
    {
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.Object, out buffer);
        GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.Object, out offset);
        Assert.NotEqual(0, buffer);
        float[] values = new float[20];
        using var binding = StateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, 80, values);
        return values;
    }
    #endregion
}
