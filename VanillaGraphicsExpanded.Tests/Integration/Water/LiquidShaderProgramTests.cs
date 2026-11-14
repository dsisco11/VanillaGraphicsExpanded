using TestUniformRing = VanillaGraphicsExpanded.Tests.Support.GPU.Native.TestUniformRing;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.Water;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Integration.Water;

/// <summary>Exercises liquid shader bindings and installed engine integration through real GPU programs.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LiquidShaderProgramTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Repeated publication reuses each program's unchanged captured uniform storage.</summary>
    [Fact]
    public void RepeatedLiquidPublicationReusesUnchangedCopies()
    {
        // Arrange independently owned surface and volume programs with shared immutable frame inputs.
        EnsureContextValid();
        using var publication = new TestFrameLightingPublication();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        var surface = GpuShaderProgramLibraries.Get<LiquidShaderProgram>(assets.Api, "pbr_liquid")!;
        var volume = GpuShaderProgramLibraries.Get<LiquidShaderProgram>(assets.Api, LiquidShaderProgram.VolumePassName)!;
        surface.FrameLightingInputs = publication.Buffer; volume.FrameLightingInputs = publication.Buffer;
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var atmosphere = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var shadowInputs1=AssignTextures(surface, texture, atmosphere, sharedCamera, sharedLights); using var shadowInputs2=AssignTextures(volume, texture, atmosphere, sharedCamera, sharedLights);
        surface.Origin = new(1, 2, 3); volume.Origin = new(4, 5, 6);
        using var ring = new GpuUniformRingBuffer(4 * 1024 * 1024, 2, false);
        try
        {
            ring.BeginFrame(0); RHIUniformRingSystem.SetCurrent(ring);
            using var surfacePipeline=CreateBindingPipeline(surface);
            using var volumePipeline=CreateBindingPipeline(volume);
            var commands=RHICommandContext.Current;
            // Act by binding each program repeatedly after publishing the initial uniform data.
            commands.SetPipeline(surfacePipeline);commands.BindResources(surface.CaptureBindings());
            commands.SetPipeline(volumePipeline);commands.BindResources(volume.CaptureBindings());
#if DEBUG
            long bytesBefore=ring.BytesWritten,before=ring.AllocationsWritten;
#endif
            for(int iteration=0;iteration<32;iteration++)
            {
                commands.SetPipeline(surfacePipeline);commands.BindResources(surface.CaptureBindings());
                commands.SetPipeline(volumePipeline);commands.BindResources(volume.CaptureBindings());
            }
#if DEBUG
            // Assert repeated capture performs no additional uniform-ring allocation or writes.
            Assert.Equal(before,ring.AllocationsWritten);Assert.Equal(bytesBefore,ring.BytesWritten);
#endif
            commands.SetPipeline(surfacePipeline);commands.BindResources(surface.CaptureBindings());
            Assert.Equal(new float[]{1,2,3},ReadDraw(out _,out _).AsSpan(LiquidDrawParamsUbo.OriginOffset/sizeof(float),3).ToArray());
            commands.SetPipeline(volumePipeline);commands.BindResources(volume.CaptureBindings());
            Assert.Equal(new float[]{4,5,6},ReadDraw(out _,out _).AsSpan(LiquidDrawParamsUbo.OriginOffset/sizeof(float),3).ToArray());
            commands.SetPipeline(surfacePipeline);commands.BindResources(surface.CaptureBindings());
            Assert.Equal(new float[]{1,2,3},ReadDraw(out _,out _).AsSpan(LiquidDrawParamsUbo.OriginOffset/sizeof(float),3).ToArray());
        }
        finally { RHIUniformRingSystem.ClearCurrent(); GpuShaderProgramLibraries.Dispose(assets.Api); }
    }

    /// <summary>Projection changes publish a coherent shared GPU matrix pair for liquid consumers.</summary>
    [Fact]
    public void ProjectionUpdatesPublishMatchingInverseForAsymmetricCamera()
    {
        // Arrange one real liquid program and camera whose projection receives off-axis terms.
        EnsureContextValid();
        using var publication = new TestFrameLightingPublication();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        var program = GpuShaderProgramLibraries.Declare(assets.Api, LiquidShaderProgram.Contract.Identity, () => new LiquidShaderProgram());
        program.FrameLightingInputs = publication.Buffer;
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var atmosphere = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var shadowInputs3=AssignTextures(program, texture, atmosphere, sharedCamera, sharedLights);
        // Act with two asymmetric projections and publish each resulting matrix pair through normal bindings.
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
            // Assert the GPU-published projection and inverse reconstruct independent view-space points.
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
            using var publicationPipeline = CreateBindingPipeline(program);
        RHICommandContext.Current.SetPipeline(publicationPipeline);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
            GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.Frame, out int buffer);
            GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.Frame, out int offset);
            byte[] bytes = new byte[VgeFrameUniformBuffer.PackedSize];
            using var binding = OpenGlStateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
            GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, bytes.Length, bytes);
            return (MemoryMarshal.Read<Matrix4x4>(bytes.AsSpan(VgeFrameUniformBuffer.ProjectionOffset, VgeFrameUniformBuffer.MatrixSizeBytes)),
                MemoryMarshal.Read<Matrix4x4>(bytes.AsSpan(VgeFrameUniformBuffer.InverseProjectionOffset, VgeFrameUniformBuffer.MatrixSizeBytes)));
        }
    }

    /// <summary>Alternating volume and surface submissions retain separate executables through quality changes and reload.</summary>
    [Fact]
    public void RegisteredVolumeAndSurfaceRetainIndependentExecutables()
    {
        // Arrange separately registered surface and volume programs with real packaged shader resources.
        EnsureContextValid();
        using var publication = new TestFrameLightingPublication();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        var surface = Assert.IsType<LiquidShaderProgram>(GpuShaderProgramLibraries.Get<GpuProgram>(assets.Api, "pbr_liquid"));
        var volume = Assert.IsType<LiquidShaderProgram>(GpuShaderProgramLibraries.Get<GpuProgram>(assets.Api, LiquidShaderProgram.VolumePassName));
        surface.FrameLightingInputs = publication.Buffer; volume.FrameLightingInputs = publication.Buffer;
        Assert.NotSame(surface, volume);
        Assert.Equal(0, surface.CaptureMode);
        Assert.Equal(3, volume.CaptureMode);
        Assert.True(surface.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.True(volume.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.NotEqual(surface.ProgramId, volume.ProgramId);
        TestUniformRing.EnsureFrame();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var atmosphere = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var shadowInputs4=AssignTextures(surface, texture, atmosphere, sharedCamera, sharedLights); using var shadowInputs5=AssignTextures(volume, texture, atmosphere, sharedCamera, sharedLights);
        var uniforms = new DefaultShaderUniforms { ColorMapRects4 = new float[160] };
        float[] projection = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var captureApi = LiquidFrameCaptureApi.Create(uniforms, projection);
        AssertStableSubmissions();

        // A user quality change replaces only the surface executable. The volume
        // executable has its own installed settings and remains ready for submission.
        // Act by changing surface quality while both programs continue through real submissions.
        int volumeId = volume.ProgramId;
        int surfaceId = surface.ProgramId;
        Assert.True(surface.ConfigureOptions(() => surface.RefractionQuality = 0));
        Assert.True(surface.EnsureReady(), string.Join("\n", assets.Logs));
        Assert.NotEqual(surfaceId, surface.ProgramId);
        Assert.Equal(volumeId, volume.ProgramId);
        Assert.False(volume.RequiresPreparation);
        AssertStableSubmissions();

        // Repeated declarations preserve readiness; explicit reload withdraws both independent selections.
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        Assert.Same(surface, GpuShaderProgramLibraries.Get<LiquidShaderProgram>(assets.Api, "pbr_liquid"));
        Assert.Same(volume, GpuShaderProgramLibraries.Get<LiquidShaderProgram>(assets.Api, LiquidShaderProgram.VolumePassName));
        Assert.False(surface.RequiresPreparation); Assert.False(volume.RequiresPreparation);
        var registry = GpuShaderProgramLibraries.Require(assets.Api);
        registry.BeginReload(); registry.CompleteReload();
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
        // Assert both real GL executables are retired when their owning library is disposed.
        GpuShaderProgramLibraries.Dispose(assets.Api);
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
            var frameParameters = Assert.IsType<LiquidFrameParamsUbo>(((ILiquidShaderProgramBindings)program).FrameParameters);
            byte[] retainedFogSpheres = frameParameters.Bytes.Slice(LiquidFrameParamsUbo.FogSphereComponentsOffset,
                LiquidFrameParamsUbo.FogSphereComponentsSize).ToArray();
            sharedLights.Capture(lights, uniforms.PointLights3, uniforms.PointLightColors3);
            program.CaptureFrameInputs(captureApi, new Vec2f(.125f, .125f));
            Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
            using var publicationPipeline = CreateBindingPipeline(program);
            RHICommandContext.Current.SetPipeline(publicationPipeline);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
            GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.ShaderInputs, out int buffer);
            GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.ShaderInputs, out int offset);
            byte[] bytes = new byte[LiquidFrameParamsUbo.BlockSize];
            using var binding = OpenGlStateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
            GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, bytes.Length, bytes);
            bool boundary = program.CaptureMode == 3;
            Assert.Same(sharedLights, ((ILiquidShaderProgramBindings)program).LightsInputs);
            Assert.Equal((uint)(boundary ? 0 : spheres), MemoryMarshal.Read<uint>(bytes.AsSpan(LiquidFrameParamsUbo.FogSphereCountOffset)));
            Assert.Equal(value + 3, MemoryMarshal.Read<float>(bytes.AsSpan(LiquidFrameParamsUbo.ColorMapRectsOffset)));
            Assert.Equal(value + 4, MemoryMarshal.Read<float>(bytes.AsSpan(LiquidFrameParamsUbo.AnimationOffset)));
            Assert.Same(sharedCamera, ((ILiquidShaderProgramBindings)program).FrameInputs);
            if (boundary) Assert.Equal(retainedFogSpheres, bytes.AsSpan(LiquidFrameParamsUbo.FogSphereComponentsOffset,
                LiquidFrameParamsUbo.FogSphereComponentsSize).ToArray());
            if (!boundary && lights > 0)
            {
                Assert.Equal(value, MemoryMarshal.Read<float>(sharedLights.Bytes.Slice(
                    VgeLightsUniformBuffer.PositionsOffset + (lights - 1) * VgeLightsUniformBuffer.VectorStride)));
                Assert.Equal(value + 1, MemoryMarshal.Read<float>(sharedLights.Bytes.Slice(
                    VgeLightsUniformBuffer.ColorsOffset + (lights - 1) * VgeLightsUniformBuffer.VectorStride)));
            }
            if (!boundary && spheres > 0) Assert.Equal(value + 2, MemoryMarshal.Read<float>(bytes.AsSpan(
                LiquidFrameParamsUbo.FogSphereComponentsOffset + (spheres * LiquidFrameParamsUbo.FogSphereComponentsPerSphere - 1)
                * LiquidFrameParamsUbo.FogSphereComponentStride)));
            Assert.Equal(new Vector4(value), MemoryMarshal.Read<Vector4>(((ILiquidShaderProgramBindings)program).WaveParameters.Bytes));
            float[] draw = ReadDraw(out _, out _);
            Assert.Equal(projection, draw.AsSpan(LiquidDrawParamsUbo.ModelOffset/sizeof(float), VgeFrameUniformBuffer.MatrixSizeBytes/sizeof(float)).ToArray());
            Assert.Equal(new float[] { value, value + 1, value + 2 }, draw.AsSpan(LiquidDrawParamsUbo.OriginOffset/sizeof(float), 3).ToArray());
            Assert.Equal(program.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.False(program.RequiresPreparation);
        }
    }

    /// <summary>Water selections load distinct offline fragments while retaining the shared vertex and capture choices.</summary>
    [Fact]
    public void WaterOptionsSelectPrecompiledFragmentsBeforePreparation()
    {
        // Arrange the production liquid program before selecting its packaged shader fragments.
        EnsureContextValid();
        using var publication = new TestFrameLightingPublication();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        var program = GpuShaderProgramLibraries.Declare(assets.Api, LiquidShaderProgram.Contract.Identity, () => new LiquidShaderProgram());
        program.FrameLightingInputs = publication.Buffer;
        // Assert the user's default selections and contract remain available before changing options.
        Assert.Equal(2, program.RefractionQuality);
        Assert.Equal(2, program.RefractionBackgroundScale);
        Assert.DoesNotContain("vge_waterRefractionQuality", LiquidShaderProgram.Contract.Bindings.UniformLocations.Keys);
        string? vertex = null;
        var fragments = new HashSet<string>();
        var preparationPairs = new HashSet<(int Quality, int Resolution)>
        {
            (0, 1), (1, 2), (2, 1), (3, 2)
        };
        // Act by traversing every authored quality/resolution selection and capture-mode change.
        // Exercise generation replacement on one retained owner, rather than only
        // enumerating settings that might never have compiled executable binaries.
        // Selection remains exhaustive; native preparation samples each quality and resolution.
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
                Assert.Equal(quality, program.RefractionQuality);
                Assert.Equal(resolution, program.RefractionBackgroundScale);
                if (preparationPairs.Contains((quality, resolution)))
                {
                    Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
                    Assert.False(program.RequiresPreparation);
                    Assert.Same(program.RequestedSettings, program.InstalledSettings);
                    Assert.False(program.ResourceBindings.BinaryInterface!.GetUniformLocation("vge_waterRefractionQuality") >= 0);
                }
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
        // Assert all selections leave the GL context clean.
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Pool draw boundaries publish staged origin writes and retain prior mini-dimension snapshots.</summary>
    [Fact]
    public void PoolWritesPublishAndRestoreActualGpuParameters()
    {
        // Arrange a real liquid program, native uniform storage, and its compatibility engine adapter.
        EnsureContextValid();
        using var publication = new TestFrameLightingPublication();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        var program = GpuShaderProgramLibraries.Declare(assets.Api, LiquidShaderProgram.Contract.Identity, () => new LiquidShaderProgram());
        program.FrameLightingInputs = publication.Buffer;
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var volume = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var shadowInputs6=AssignTextures(program, texture, volume, sharedCamera, sharedLights);
        using var publicationPipeline = CreateBindingPipeline(program);
        RHICommandContext.Current.SetPipeline(publicationPipeline);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
        Assert.Equal(program.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
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
        // Act by binding after each staged write, then restore the values submitted by the first draw.
        RHICommandContext.Current.BindResources(program.CaptureBindings());
        var first = ReadDraw(out int firstOffset, out int firstBuffer);
        // Assert new bindings publish changed bytes without mutating the prior draw's retained range.
        Assert.Equal(new float[] { 1, 2, 3, .25f }, first[(LiquidDrawParamsUbo.OriginOffset / sizeof(float))..]);
        engine.Uniform("origin", new Vec3f(-4, 5, 6));
        Assert.Equal(first, ReadDraw(out _, out _));
        RHICommandContext.Current.BindResources(program.CaptureBindings());
        var second = ReadDraw(out int secondOffset, out int secondBuffer);
        Assert.True(firstBuffer != secondBuffer || firstOffset != secondOffset);
        Assert.Equal(new float[] { -4, 5, 6, .25f }, second[(LiquidDrawParamsUbo.OriginOffset / sizeof(float))..]);
        // Previously submitted draws must keep their old bytes after later parameter writes.
        using (OpenGlStateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, firstBuffer))
        {
            float[] retained = new float[LiquidDrawParamsUbo.BlockSize / sizeof(float)];
            GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)firstOffset, LiquidDrawParamsUbo.BlockSize, retained);
            Assert.Equal(first, retained);
        }
        var moved = (float[])identity.Clone(); moved[12] = 7;
        engine.UniformMatrix("modelViewMatrix", moved);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
        Assert.Equal(7, ReadDraw(out _, out _)[LiquidDrawParamsUbo.ModelOffset / sizeof(float) + 12]);
        engine.UniformMatrix("modelViewMatrix", identity);
        engine.Uniform("forcedTransparency", 0f);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
        var restored = ReadDraw(out _, out _);
        Assert.Equal(identity, restored[..(LiquidDrawParamsUbo.ModelOffset / sizeof(float) + VgeFrameUniformBuffer.MatrixSizeBytes / sizeof(float))]);
        Assert.Equal(0, restored[LiquidDrawParamsUbo.TransparencyOffset / sizeof(float)]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Typed texture inputs bind every declared unit with the intended target and sampling policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedImagesBindDeclaredTargetsAndSamplers(bool useInterface)
    {
        // Arrange real images and the production shader, selecting either supported typed setter surface.
        EnsureContextValid();
        using var publication = new TestFrameLightingPublication();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        var program = GpuShaderProgramLibraries.Declare(assets.Api, LiquidShaderProgram.Contract.Identity, () => new LiquidShaderProgram());
        program.FrameLightingInputs = publication.Buffer;
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        using var terrain = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depth = new DepthTexture(1, 1, TextureFormat.DepthComponent32f);
        using var material = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        // Keep the common shadow binding on an explicit disabled snapshot while this test focuses on liquid-owned image slots.
        using var surfaceShadows = SurfaceShadowTestInputs.Create(Vector3.UnitY, Vector3.Zero);
        program.ShadowInputs = surfaceShadows;
        using var radiance = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var attenuation = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        // Exercise both the preserved engine API and the interface boundary against
        // real linked resources; targets, samplers and uniform units must be identical.
        if (useInterface)
        {
            var bindings = (ILiquidShaderProgramBindings)program;
            bindings.TerrainTexture = terrain;
            bindings.DepthTexture = depth;
            bindings.MaterialParamsTexture = material;
            bindings.AerialRadianceTexture = radiance;
            bindings.AerialAttenuationTexture = attenuation;
        }
        else
        {
            program.TerrainTexture = terrain;
            program.DepthTexture = depth;
            program.MaterialParamsTexture = material;
            program.AerialRadianceTexture = radiance;
            program.AerialAttenuationTexture = attenuation;
        }
        program.LightsInputs = sharedLights;
        program.FrameInputs = sharedCamera;
        using var publicationPipeline = CreateBindingPipeline(program);
        // Act by publishing the selected inputs through the real program binding path.
        RHICommandContext.Current.SetPipeline(publicationPipeline);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
        int[] images = [terrain.TextureId, depth.TextureId, material.TextureId, radiance.TextureId, attenuation.TextureId];
        int[] samplers = [0, GpuSamplers.NearestClamp.SamplerId, GpuSamplers.NearestClamp.SamplerId, 0, 0];
        string[] names = ["terrainTex", "depthTex", "vge_materialParamsTex", "vge_atmosphereAerialRadiance", "vge_atmosphereAerialAttenuation"];
        // Assert each actual unit binding and sampler matches its declared target contract.
        for (int index = 0; index < images.Length; index++)
        {
            var binding = program.ProgramLayout.BinaryInterface!.PreparedBindings.Resolve(
                VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingEntry.Identity(VanillaGraphicsExpanded.Rendering.Contracts.ShaderBindingKind.Sampler, names[index]));
            Assert.True(binding.Active);
            int unit = binding.Contract.Binding.Slot;
            OpenGLImageBindings.ActiveTexture(OpenGlStateCache.Current, unit);
            Assert.Equal(images[index], GL.GetInteger(index < 3 ? GetPName.TextureBinding2D : GetPName.TextureBinding3D));
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, unit, out int sampler);
            Assert.Equal(samplers[index], sampler);
            Assert.DoesNotContain(names[index], LiquidShaderProgram.Contract.Bindings.UniformLocations.Keys);
        }
        OpenGLImageBindings.ActiveTexture(OpenGlStateCache.Current, 33);
        Assert.Equal(((GpuTextureRef)surfaceShadows.Depth).TextureId, GL.GetInteger(GetPName.TextureBinding2DArray));
        GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 33, out int shadowSampler);
        Assert.Equal(GpuSamplers.NearestClamp.SamplerId, shadowSampler);
        var shadowBinding = program.ProgramLayout.BinaryInterface!.PreparedBindings.Resolve(
            VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingEntry.Identity(VanillaGraphicsExpanded.Rendering.Contracts.ShaderBindingKind.Sampler, "shadowDepthPool"));
        Assert.True(shadowBinding.Active);
        Assert.Equal(33, shadowBinding.Contract.Binding.Slot);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Staged typed frame fields reach GPU memory only at the explicit publication boundary.</summary>
    [Fact]
    public void UsePublishesTypedFrameFields()
    {
        // Arrange a ready program with distinct staged animation, fog, and light values.
        EnsureContextValid();
        using var publication = new TestFrameLightingPublication();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var sharedCamera = TestFrameCamera.CreateIdentity(512, 512);
        using var sharedLights = new VgeLightsUniformBuffer();
        var program = GpuShaderProgramLibraries.Declare(assets.Api, LiquidShaderProgram.Contract.Identity, () => new LiquidShaderProgram());
        program.FrameLightingInputs = publication.Buffer;
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var volume = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var shadowInputs7=AssignTextures(program, texture, volume, sharedCamera, sharedLights);
        program.SetAnimation(1, 2, 4);
        program.SetFogSphereCount(1);
        sharedLights.Capture(2, [0, 0, 0, 5, 6, 7], new float[6]);
        program.SetFogSphereComponent(23, .75f);
        using var publicationPipeline = CreateBindingPipeline(program);
        // Act by publishing staged resources and reading the resulting GPU uniform block.
        RHICommandContext.Current.SetPipeline(publicationPipeline);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.ShaderInputs, out int buffer);
        GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.ShaderInputs, out int offset);
        // Assert the actual uploaded fields match their typed staging values.
        Assert.NotEqual(0, buffer);
        byte[] bytes = new byte[LiquidFrameParamsUbo.BlockSize];
        using var binding = OpenGlStateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, bytes.Length, bytes);
        Assert.Equal(new float[] { 1, 2, 0, 4 }, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes.AsSpan(
            LiquidFrameParamsUbo.AnimationOffset, 4 * sizeof(float))).ToArray());
        Assert.Equal(1u, System.Runtime.InteropServices.MemoryMarshal.Read<uint>(bytes.AsSpan(LiquidFrameParamsUbo.FogSphereCountOffset)));
        Assert.Equal(new float[] { 5, 6, 7 }, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(sharedLights.Bytes.Slice(
            VgeLightsUniformBuffer.PositionsOffset + VgeLightsUniformBuffer.VectorStride, 3 * sizeof(float))).ToArray());
        Assert.Equal(.75f, System.Runtime.InteropServices.MemoryMarshal.Read<float>(bytes.AsSpan(
            LiquidFrameParamsUbo.FogSphereComponentsOffset + (LiquidFrameParamsUbo.FogSphereComponentCapacity - 1)
            * LiquidFrameParamsUbo.FogSphereComponentStride)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Harmony compiles the real patched methods with both new boundaries and atlas interception.</summary>
    [Fact]
    public void InstalledEngineHooksCompose()
    {
        // Arrange a unique Harmony owner for the real engine hook implementations.
        var harmony = new HarmonyLib.Harmony("VGE.Tests.LiquidHookInstallation");
        try
        {
            // Act by applying each production patch together; patch-time method generation must complete cleanly.
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.TerrainAtlasDrawBindingHook)).Patch();
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.LiquidRenderHooks)).Patch();
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.LiquidMeshSourceHook)).Patch();
            harmony.CreateClassProcessor(typeof(LiquidPoolSubmissionHook)).Patch();
            // Assert Harmony reports installed engine methods; patch-generation failures also propagate from Patch().
            Assert.NotEmpty(harmony.GetPatchedMethods());
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    #endregion

    #region Private
    /// <summary>Creates a binding-only pipeline over the liquid vertex layout and floating output targets.</summary>
    private static RHIGraphicsPipeline CreateBindingPipeline(LiquidShaderProgram program)
    {
        Assert.True(program.EnsureReady(), program.PreparationFailure?.ToString());
        return RHICommandContext.Current.CreateGraphicsPipeline(new(program.Shader, EngineLiquidPoolGeometry.Layout,
            new(Enumerable.Range(0, 6).Select(_ => new VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions.ColorTargetSlot(TextureFormat.Rgba32f))),
            VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions.DynamicPipelineState.Viewport)).RequireResource();
    }

    /// <summary>Supplies borrowed image inputs used by binding-capture tests that do not draw.</summary>
    private static SurfaceShadowInputSet AssignTextures(LiquidShaderProgram program, Texture2D texture, DynamicTexture3D volume, VgeFrameUniformBuffer camera, VgeLightsUniformBuffer lights)
    {
        program.LightsInputs = lights;
        program.FrameInputs = camera;
        program.TerrainTexture = texture;
        program.DepthTexture = texture;
        program.MaterialParamsTexture = texture;
        var shadowInputs = SurfaceShadowTestInputs.Create(Vector3.UnitY, Vector3.Zero);
        program.ShadowInputs = shadowInputs;
        program.AerialRadianceTexture = volume;
        program.AerialAttenuationTexture = volume;
        return shadowInputs;
    }

    /// <summary>Reads the GPU-bound range rather than the program's CPU staging storage.</summary>
    private static float[] ReadDraw(out int offset, out int buffer)
    {
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.Object, out buffer);
        GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.Object, out offset);
        Assert.NotEqual(0, buffer);
        float[] values = new float[LiquidDrawParamsUbo.BlockSize / sizeof(float)];
        using var binding = OpenGlStateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, LiquidDrawParamsUbo.BlockSize, values);
        return values;
    }
    #endregion
}
