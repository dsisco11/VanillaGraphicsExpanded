using System.Runtime.CompilerServices;
using FluffyClouds;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies real GL storage/link/error boundaries using tiny synthetic native owners, without a game.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CloudNativeAdapterTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Target recreation preserves selected scene inputs, while screen retirement releases their borrowers.</summary>
    [Fact]
    public void VolumeTargetRetirementPreservesSelectedDepth()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var depth = DynamicTexture2D.Create(4, 4, PixelInternalFormat.R32f);
            var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
            using var runtime = new CloudNativeRuntime(api.Object);
            using var consumer = new VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudStoredVolumeConsumer(api.Object, runtime,
                () => VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeViewSettings.Default);
            var borrower = new BorrowedTexture(depth.TextureId);
            var field = AccessTools.Field(consumer.GetType(), "visibilityTexture");
            field.SetValue(consumer, borrower);
            AccessTools.Method(consumer.GetType(), "RetireTarget").Invoke(consumer, null);
            Assert.Same(borrower, field.GetValue(consumer)); Assert.True(borrower.IsValid);
            AccessTools.Method(consumer.GetType(), "RetireScreenResources").Invoke(consumer, null);
            Assert.Null(field.GetValue(consumer)); Assert.False(borrower.IsValid);
            Assert.True(GL.IsTexture(depth.TextureId));
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Storage revisions revoke content certificates until a new observed upload qualifies them.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageChangesRequireFreshUploadAndRetirementDoesNotDelete(bool external)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var native = new NativeMap();
            var adapter = new CloudNativeMapAdapter(native.Owner, 5);
            adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(10, true);
            Assert.True(adapter.TryGet(5, 10, out _));
            // Restore the same descriptor after replacing storage: dimensions alone cannot certify contents.
            if (external)
            {
                using var binding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, native.Raw1.TextureId);
                VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures.Core.TexImage2D(
                    TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16, 20, 20, 0, PixelFormat.Rgba, PixelType.Short, IntPtr.Zero);
            }
            else { native.Raw1.Resize(21, 20); native.Raw1.Resize(20, 20); }
            Assert.False(adapter.TryGet(5, 10, out _));
            adapter.BeginGeneration(); adapter.CompleteGeneration(11, true);
            Assert.False(adapter.TryGet(5, 11, out _));
            adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(12, true);
            Assert.True(adapter.TryGet(5, 12, out _));
            adapter.Retire(); Assert.False(adapter.TryGet(5, 12, out _));
            adapter.BeginAllocation(); adapter.CompleteAllocation();
            adapter.BeginGeneration(); adapter.BeginUpload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(13, true);
            Assert.False(adapter.TryGet(5, 13, out _));
            Assert.Empty((BorrowedTexture[])AccessTools.Field(typeof(CloudNativeMapAdapter), "storageTextures").GetValue(adapter));
            Assert.True(GL.IsTexture(native.Map.TextureId)); Assert.True(GL.IsTexture(native.Raw1.TextureId));
            Assert.True(GL.IsProgram(native.Program));
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Observed producer deletion rejects an otherwise current map without another generation callback.</summary>
    [Fact]
    public void DeletedBackingRejectsCurrentPublication()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var native = new NativeMap();
            var adapter = new CloudNativeMapAdapter(native.Owner, 5);
            adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(10, true);
            Assert.True(adapter.TryGet(5, 10, out _));
            StateCache.Current.DeleteTexture(native.Raw1.TextureId);
            Assert.False(adapter.TryGet(5, 10, out _));
            adapter.Retire();
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Stable generations preserve publication, restored bindings and external ownership.</summary>
    [Fact]
    public void StableNativeMapPublicationPreservesBindings()
    {
        fixture.EnsureContextValid();
        using var native = new NativeMap();
        int previous = GL.GetInteger(GetPName.TextureBinding2D);
        int draw = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int read = GL.GetInteger(GetPName.ReadFramebufferBinding);
        var adapter = new CloudNativeMapAdapter(native.Owner, 5);
        Assert.Equal(previous, GL.GetInteger(GetPName.TextureBinding2D));
        Assert.Equal(draw, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(read, GL.GetInteger(GetPName.ReadFramebufferBinding));
        adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true);
        adapter.CompleteGeneration(10, true);
        for (uint frame = 11; frame < 19; frame++)
        {
            adapter.BeginGeneration(); adapter.CompleteGeneration(frame, true);
            Assert.True(adapter.Publication.TryGet(native.Owner, 5, frame, out var input));
            Assert.Equal(native.Map.TextureId, input.Storage.Map);
        }
        adapter.Retire();
        Assert.True(GL.IsTexture(native.Map.TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Late acquisition waits for uploads, observes errors and copies current placement while preserving native resources.</summary>
    [Fact]
    public void StorageUploadsAndGlErrorsControlPublication()
    {
        fixture.EnsureContextValid();
        using var native = new NativeMap();
        var adapter = new CloudNativeMapAdapter(native.Owner, 5);
        adapter.BeginGeneration(); adapter.CompleteGeneration(10, true);
        Assert.False(adapter.Publication.TryGet(native.Owner, 5, 10, out _));
        adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true);
        adapter.CompleteGeneration(10, true);
        Assert.True(adapter.Publication.TryGet(native.Owner, 5, 10, out var first));
        // A failed in-place second upload must not leave the prior map usable.
        adapter.BeginGeneration(); adapter.BeginUpload();
        GL.BindTexture(TextureTarget.Texture2D, native.Raw1.TextureId);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, -1, 0, 1, 1, PixelFormat.Rgba, PixelType.Float, new float[4]);
        adapter.CompleteUpload(true); adapter.CompleteGeneration(11, true);
        Assert.False(adapter.Publication.TryGet(native.Owner, 5, 11, out _));
        Assert.False(adapter.Publication.IsCurrent(first, 10));
        adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(12, true);
        Assert.True(adapter.Publication.TryGet(native.Owner, 5, 12, out var recovered));
        native.Owner.offset.X = 999;
        Assert.Equal(1, recovered.Placement.Offset.X);
        adapter.BeginGeneration(); adapter.CompleteGeneration(13, true);
        Assert.True(adapter.Publication.TryGet(native.Owner, 5, 13, out var moved));
        Assert.Equal(999, moved.Placement.Offset.X);
        adapter.Retire();
        Assert.True(GL.IsTexture(native.Map.TextureId)); Assert.True(GL.IsProgram(native.Program));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Validation restores distinct read/draw framebuffer bindings and texture binding, and rejects malformed native storage.</summary>
    [Fact]
    public void AllocationValidationPreservesBindingsAndRejectsWrongDimensions()
    {
        fixture.EnsureContextValid();
        using var native = new NativeMap();
        int draw = GL.GenFramebuffer(), read = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw); GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
        GL.BindTexture(TextureTarget.Texture2D, native.Raw2.TextureId);
        try
        {
            var adapter = new CloudNativeMapAdapter(native.Owner, 5);
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int actualDraw);
            GL.GetInteger(GetPName.ReadFramebufferBinding, out int actualRead);
            GL.GetInteger(GetPName.TextureBinding2D, out int actualTexture);
            Assert.Equal(draw, actualDraw); Assert.Equal(read, actualRead); Assert.Equal(native.Raw2.TextureId, actualTexture);
            native.Owner.CloudTileLength = 40;
            adapter.BeginAllocation(); adapter.CompleteAllocation();
            adapter.BeginGeneration(); adapter.BeginUpload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(10, true);
            Assert.False(adapter.Publication.TryGet(native.Owner, 5, 10, out _));
            native.Owner.CloudTileLength = 20;
            adapter.BeginAllocation(); adapter.CompleteAllocation();
            adapter.BeginGeneration(); adapter.CompleteGeneration(11, true);
            Assert.False(adapter.Publication.TryGet(native.Owner, 5, 11, out _));
            adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(12, true);
            Assert.True(adapter.Publication.TryGet(native.Owner, 5, 12, out _));
        }
        finally { GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.DeleteFramebuffer(draw); GL.DeleteFramebuffer(read); }
    }

    /// <summary>Failed native compilation and generation errors revoke publication; a fresh linked generation can recover.</summary>
    [Fact]
    public void ReloadAndDrawErrorsWithdrawUntilSuccessfulGeneration()
    {
        fixture.EnsureContextValid(); using var native = new NativeMap();
        var adapter = new CloudNativeMapAdapter(native.Owner, 5);
        adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(10, true);
        Assert.True(adapter.Publication.TryGet(native.Owner, 5, 10, out _));
        int unlinked = GL.CreateProgram();
        native.SetProgram(unlinked); adapter.InvalidateShader();
        adapter.BeginGeneration(); adapter.CompleteGeneration(11, true);
        Assert.False(adapter.Publication.TryGet(native.Owner, 5, 11, out _));
        native.SetProgram(native.Program);
        GL.DeleteProgram(unlinked);
        adapter.BeginGeneration(); GL.Enable((EnableCap)(-1)); adapter.CompleteGeneration(12, true);
        Assert.False(adapter.Publication.TryGet(native.Owner, 5, 12, out _));
        adapter.BeginGeneration(); adapter.CompleteGeneration(13, true);
        Assert.True(adapter.Publication.TryGet(native.Owner, 5, 13, out _));
        AccessTools.Field(typeof(CloudRendererMap), "renderCloudMap").SetValue(native.Owner, false);
        adapter.BeginGeneration(); adapter.CompleteGeneration(14, true);
        Assert.False(adapter.Publication.TryGet(native.Owner, 5, 14, out _));
    }

    /// <summary>Real event withdrawal prevents old-world roots from reacquiring adapters while leaving native registrations/resources intact.</summary>
    [Fact]
    public void RuntimeRejectsForeignAndOldWorldRoots()
    {
        fixture.EnsureContextValid(); using var native = new NativeMap(); using var foreign = new NativeMap();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var events = new Mock<IClientEventAPI>(); api.SetupGet(value => value.Event).Returns(events.Object);
        var system = new FluffyCloudsModSystem();
        AccessTools.Field(typeof(FluffyCloudsModSystem), "map").SetValue(system, native.Owner);
        api.Setup(value => value.ModLoader.GetModSystem<FluffyCloudsModSystem>(It.IsAny<bool>())).Returns(system);
        api.Setup(value => value.Settings.Int.Get("cloudRenderMode", 0)).Returns(1);
        using var runtime = new CloudNativeRuntime(api.Object);
        Assert.Null(runtime.ForMap(native.Owner));
        events.Raise(value => value.LevelFinalize += null); runtime.Update();
        Assert.False(runtime.TryGetFrame(out _)); Assert.False(runtime.TryGetMap(out _));
        Assert.Null(runtime.ForMap(foreign.Owner));
        var adapter = runtime.ForMap(native.Owner); Assert.NotNull(adapter);
        long world = runtime.World;
        adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(5, true);
        Assert.True(adapter.Publication.TryGet(native.Owner, world, 5, out _));
        // Another mod may suppress the void native upload. A normal Harmony return is not proof of work.
        var harmony = new Harmony("vge.tests.cloud.skippedupload");
        try
        {
            harmony.CreateClassProcessor(typeof(CloudMapUploadHook)).Patch();
            var upload = AccessTools.Method(typeof(CloudRendererMap), "WriteTexture");
            harmony.Patch(upload, prefix: new HarmonyMethod(AccessTools.Method(typeof(CloudNativeAdapterTests), nameof(SkipNativeUpload))));
            upload.Invoke(native.Owner, null);
            adapter.BeginGeneration(); adapter.CompleteGeneration(5, true);
            Assert.False(adapter.Publication.TryGet(native.Owner, world, 5, out _));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(5, true);
        events.Raise(value => value.ReloadShader += null);
        Assert.False(adapter.Publication.TryGet(native.Owner, world, 5, out _));
        adapter.BeginGeneration(); adapter.CompleteGeneration(6, true);
        Assert.True(adapter.Publication.TryGet(native.Owner, world, 6, out _));
        events.Raise(value => value.LeaveWorld += null);
        Assert.Null(runtime.ForMap(native.Owner));
        Assert.False(adapter.Publication.TryGet(native.Owner, world, 6, out _));
        events.Raise(value => value.LevelFinalize += null); runtime.Update();
        Assert.NotEqual(world, runtime.World); Assert.Null(runtime.ForMap(native.Owner));
        // A newly constructed root is acquired independently of retained old roots.
        AccessTools.Field(typeof(FluffyCloudsModSystem), "map").SetValue(system, foreign.Owner);
        Assert.NotNull(runtime.ForMap(foreign.Owner));
        events.Raise(value => value.LeaveWorld += null); Assert.Null(runtime.ForMap(foreign.Owner));
        runtime.Dispose(); Assert.Null(CloudNativeRuntime.Active);
        Assert.True(GL.IsTexture(native.Map.TextureId)); Assert.True(GL.IsTexture(foreign.Map.TextureId));
        events.Verify(value => value.RegisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>(), It.IsAny<string>()), Times.Never);
        events.Verify(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>()), Times.Never);
    }

    /// <summary>Prepared ownership gates only the current native draw, follows the live setting and leaves every native registration intact.</summary>
    [Fact]
    public void DrawGateFollowsCurrentModeWithoutRegistrationChanges()
    {
        fixture.EnsureContextValid();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var events = new Mock<IClientEventAPI>(); api.SetupGet(value => value.Event).Returns(events.Object);
        var system = new FluffyCloudsModSystem();
        var nativeDraw = (CloudRendererVolumetric)RuntimeHelpers.GetUninitializedObject(typeof(CloudRendererVolumetric));
        AccessTools.Field(typeof(FluffyCloudsModSystem), "volumetric").SetValue(system, nativeDraw);
        api.Setup(value => value.ModLoader.GetModSystem<FluffyCloudsModSystem>(It.IsAny<bool>())).Returns(system);
        int mode = 1; api.Setup(value => value.Settings.Int.Get("cloudRenderMode", 0)).Returns(() => mode);
        using var runtime = new CloudNativeRuntime(api.Object);
        events.Raise(value => value.LevelFinalize += null); runtime.Update();
        var resources = new Mock<IDisposable>();
        Assert.True(runtime.Ownership.TryActivate(CloudMode.Volumetric, resources.Object, true));
        Assert.True(runtime.SuppressVolume(nativeDraw));
        Assert.True(runtime.SuppressVolume(nativeDraw)); // A native watcher may re-register the same draw freely.
        var foreign = (CloudRendererVolumetric)RuntimeHelpers.GetUninitializedObject(typeof(CloudRendererVolumetric));
        Assert.False(runtime.SuppressVolume(foreign));
        mode = 2; Assert.False(runtime.SuppressVolume(nativeDraw));
        resources.Verify(value => value.Dispose(), Times.Once);
        events.Verify(value => value.RegisterRenderer(nativeDraw, EnumRenderStage.OIT, "cloudsvolumetric"), Times.Never);
        mode = 1; runtime.Update();
        var replacement = new Mock<IDisposable>();
        Assert.True(runtime.Ownership.TryActivate(CloudMode.Volumetric, replacement.Object, true));
        runtime.Ownership.Dispose();
        Assert.False(runtime.SuppressVolume(nativeDraw));
        replacement.Verify(value => value.Dispose(), Times.Once);
        events.Verify(value => value.RegisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>(), It.IsAny<string>()), Times.Never);
        events.Verify(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>()), Times.Never);
        api.VerifySet(value => value.Settings.Int["cloudRenderMode"] = It.IsAny<int>(), Times.Never);
    }
    /// <summary>The independent volume callback never advances native weather and retains ownership across unavailable maps and reload.</summary>
    [Fact]
    public void StoredRendererRetainsItsGateUntilCurrentModeRelease()
    {
        fixture.EnsureContextValid();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var events = new Mock<IClientEventAPI>(); api.SetupGet(value => value.Event).Returns(events.Object);
        var system = new FluffyCloudsModSystem();
        var nativeDraw = (CloudRendererVolumetric)RuntimeHelpers.GetUninitializedObject(typeof(CloudRendererVolumetric));
        AccessTools.Field(typeof(FluffyCloudsModSystem), "volumetric").SetValue(system, nativeDraw);
        api.Setup(value => value.ModLoader.GetModSystem<FluffyCloudsModSystem>(It.IsAny<bool>())).Returns(system);
        int mode = 1; api.Setup(value => value.Settings.Int.Get("cloudRenderMode", 0)).Returns(() => mode);
        using var runtime = new CloudNativeRuntime(api.Object);
        events.Raise(value => value.LevelFinalize += null); runtime.Update();
        var renderer = new VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudStoredVolumeConsumer(api.Object, runtime,()=>VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeViewSettings.Default);
        AccessTools.Field(typeof(CloudNativeRuntime), "volume").SetValue(runtime, renderer);
        Assert.Equal(.31, renderer.RenderOrder);
        // Executable arithmetic is exercised separately by the real binary GPU fixtures.
        Assert.True(runtime.Ownership.TryActivate(CloudMode.Volumetric, renderer, true));
        api.Object.Event.RegisterRenderer(renderer, EnumRenderStage.OIT, "vge_cloud_volume");
        renderer.OnRenderFrame(.1f, EnumRenderStage.OIT);
        Assert.True(runtime.SuppressVolume(nativeDraw)); Assert.False(runtime.TryGetMap(out _));
        events.Raise(value => value.ReloadShader += null);
        Assert.True(runtime.SuppressVolume(nativeDraw));
        mode = 0; renderer.OnRenderFrame(.1f, EnumRenderStage.OIT);
        Assert.False(runtime.SuppressVolume(nativeDraw));
        renderer.Dispose();
        events.Verify(value => value.UnregisterRenderer(renderer, EnumRenderStage.OIT), Times.Once);
        events.Verify(value => value.UnregisterRenderer(nativeDraw, It.IsAny<EnumRenderStage>()), Times.Never);
        api.VerifySet(value => value.Settings.Int["cloudRenderMode"] = It.IsAny<int>(), Times.Never);
    }
    #endregion

    #region Private
    /// <summary>Simulates a foreign prefix that bypasses the native void upload body.</summary>
    private static bool SkipNativeUpload() => false;

    /// <summary>Owns fixture GL allocations only; production adapters must leave them alive until fixture disposal.</summary>
    private sealed class NativeMap : IDisposable
    {
        internal readonly CloudRendererMap Owner = (CloudRendererMap)RuntimeHelpers.GetUninitializedObject(typeof(CloudRendererMap));
        internal readonly DynamicTexture2D Map = DynamicTexture2D.Create(20, 20, PixelInternalFormat.Rgba32f);
        internal readonly DynamicTexture2D Color = DynamicTexture2D.Create(20, 20, PixelInternalFormat.Rgba32f);
        internal readonly DynamicTexture2D Raw1 = DynamicTexture2D.Create(20, 20, PixelInternalFormat.Rgba16);
        internal readonly DynamicTexture2D Raw2 = DynamicTexture2D.Create(20, 20, PixelInternalFormat.Rgba16);
        internal readonly int Program;
        private readonly int framebuffer;

        #region Public API
        /// <summary>Creates exactly the inspected native texture/attachment contract with a linked diagnostic program.</summary>
        internal NativeMap()
        {
            Owner.CloudTileLength = 20; Owner.CloudTileSize = 50; Owner.TextureMap = Map.TextureId; Owner.TextureCol = Color.TextureId;
            Owner.offset = new Vec3f(1, 2, 3); Owner.committedState = new FluffyClouds.CloudTilesState();
            framebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, Map.TextureId, 0);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, Color.TextureId, 0);
            GL.DrawBuffers(2, new[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1 }); StateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            int vertex = Compile(ShaderType.VertexShader, "#version 150\nvoid main(){gl_Position=vec4(0,0,0,1);}");
            int fragment = Compile(ShaderType.FragmentShader, "#version 150\nout vec4 color; void main(){color=vec4(1);}");
            Program = GL.CreateProgram(); GL.AttachShader(Program, vertex); GL.AttachShader(Program, fragment); GL.LinkProgram(Program);
            GL.DeleteShader(vertex); GL.DeleteShader(fragment);
            GL.GetProgram(Program, GetProgramParameterName.LinkStatus, out int linked); Assert.Equal(1, linked);
            SetProgram(Program);
            AccessTools.Field(typeof(CloudRendererMap), "renderCloudMap").SetValue(Owner, true);
            AccessTools.Field(typeof(CloudRendererMap), "Framebuffer").SetValue(Owner, framebuffer);
            AccessTools.Field(typeof(CloudRendererMap), "TextureData1").SetValue(Owner, Raw1.TextureId);
            AccessTools.Field(typeof(CloudRendererMap), "TextureData2").SetValue(Owner, Raw2.TextureId);
        }

        /// <summary>Submits both raw uploads at the observed boundary without a worker or game runtime.</summary>
        internal void Upload()
        {
            foreach (int texture in new[] { Raw1.TextureId, Raw2.TextureId })
            {
                GL.BindTexture(TextureTarget.Texture2D, texture);
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 20, 20, PixelFormat.Rgba, PixelType.Short, new short[20 * 20 * 4]);
            }
        }

        /// <summary>Replaces the native program reference as native LoadShader does, without installing a shader-loading hook.</summary>
        internal void SetProgram(int id)
        {
            var program = new Mock<IShaderProgram>(); program.SetupGet(value => value.ProgramId).Returns(id);
            AccessTools.Field(typeof(CloudRendererMap), "prog").SetValue(Owner, program.Object);
        }

        /// <summary>Deletes fixture-owned names; no production native Dispose method is invoked.</summary>
        public void Dispose()
        {
            GL.DeleteFramebuffer(framebuffer); GL.DeleteProgram(Program);
            Map.Dispose(); Color.Dispose(); Raw1.Dispose(); Raw2.Dispose();
        }
        #endregion

        #region Private
        /// <summary>Compiles a minimal diagnostic stage used only to exercise native link validation.</summary>
        private static int Compile(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type); GL.ShaderSource(shader, source); GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled); Assert.Equal(1, compiled); return shader;
        }
        #endregion
    }
    #endregion
}
