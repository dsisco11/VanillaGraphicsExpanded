using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using FluffyClouds;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Tests.Integration.Clouds;

    /// <summary>Qualifies native cloud resource acquisition and publication through real GL boundaries.</summary>
    [Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CloudNativeAdapterTests(HeadlessGLFixture fixture)
{
    #region Public API


/// <summary>Target recreation preserves selected scene inputs, while screen retirement releases their borrowers.</summary>
    [Fact]
    public void VolumeTargetRetirementPreservesSelectedDepth()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var depth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
            using var runtime = new CloudNativeRuntime(api.Object);
            using var consumer = new VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudStoredVolumeConsumer(api.Object, runtime,
                () => VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeViewSettings.Default);
            var borrower = new BorrowedTexture(depth.TextureId);
            var field = AccessTools.Field(consumer.GetType(), "visibilityTexture");
            field.SetValue(consumer, borrower);
            // Act: retire the capture target while its selected scene-depth input remains borrowed.
            AccessTools.Method(consumer.GetType(), "RetireTarget").Invoke(consumer, null);
        // Assert the borrowed scene depth survives target retirement, while screen retirement releases only its borrow.
        // Assert: compare the observed result with the expected contract.
            Assert.Same(borrower, field.GetValue(consumer)); Assert.True(borrower.IsValid);
            // Act: retire the owning screen resources after preserving the selected scene-depth input.
            AccessTools.Method(consumer.GetType(), "RetireScreenResources").Invoke(consumer, null);
            Assert.Null(field.GetValue(consumer)); Assert.False(borrower.IsValid);
            Assert.True(GL.IsTexture(depth.TextureId));
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

/// <summary>Storage revisions revoke content certificates until a new observed upload qualifies them.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageChangesRequireFreshUploadAndRetirementDoesNotDelete(bool external)
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var native = new NativeMap();
            var adapter = new CloudNativeMapAdapter(native.Owner, 5);
        // Act: publish one uploaded generation before replacing its backing storage.
            adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(10, true);
        // Assert: storage replacement withdraws certification until a fresh upload completes.
            Assert.True(adapter.TryGet(5, 10, out _));
            // Restore the same descriptor after replacing storage: dimensions alone cannot certify contents.
            if (external)
            {
                using var binding = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, native.Raw1.TextureId);
                GL.TexImage2D(
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
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

/// <summary>Observed producer deletion rejects an otherwise current map without another generation callback.</summary>
    [Fact]
    public void DeletedBackingRejectsCurrentPublication()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var native = new NativeMap();
            var adapter = new CloudNativeMapAdapter(native.Owner, 5);
        // Act: publish the backing map before simulating producer deletion.
            adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(10, true);
        // Assert: the current publication is valid before deletion and withdrawn afterward.
            Assert.True(adapter.TryGet(5, 10, out _));
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, native.Raw1.TextureId));
            Assert.False(adapter.TryGet(5, 10, out _));
            adapter.Retire();
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

/// <summary>Stable generations preserve publication, restored bindings and external ownership.</summary>
    [Fact]
    public void StableNativeMapPublicationPreservesBindings()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid();
        using var native = new NativeMap();
        int previous = GL.GetInteger(GetPName.TextureBinding2D);
        int draw = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int read = GL.GetInteger(GetPName.ReadFramebufferBinding);
        // Act: construction inspects the native storage while preserving caller bindings.
        var adapter = new CloudNativeMapAdapter(native.Owner, 5);
        // Assert: native texture and framebuffer bindings are restored after validation.
        Assert.Equal(previous, GL.GetInteger(GetPName.TextureBinding2D));
        Assert.Equal(draw, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(read, GL.GetInteger(GetPName.ReadFramebufferBinding));
        // Act: publish the map and retain it through metadata-only generations.
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
    public void StorageUploadsAndReportedFailuresControlPublication()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid();
        using var native = new NativeMap();
        var adapter = new CloudNativeMapAdapter(native.Owner, 5);
        // Arrange: complete a generation without uploading its required storage as a negative control.
        adapter.BeginGeneration(); adapter.CompleteGeneration(10, true);
        // Assert: incomplete storage cannot be published.
        Assert.False(adapter.Publication.TryGet(native.Owner, 5, 10, out _));
        // Act: upload current map storage, then exercise reported failures and successful recovery.
        adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true);
        adapter.CompleteGeneration(10, true);
        Assert.True(adapter.Publication.TryGet(native.Owner, 5, 10, out var first));
        // A failed in-place second upload must not leave the prior map usable.
        adapter.BeginGeneration(); adapter.BeginUpload();
        adapter.CompleteUpload(false); adapter.CompleteGeneration(11, true);
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
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid();
        using var native = new NativeMap();
        int draw = GL.GenFramebuffer(), read = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw); GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
        GL.BindTexture(TextureTarget.Texture2D, native.Raw2.TextureId);
        try
        {
            // Act: construct the adapter while distinct caller framebuffer and texture bindings are active.
            var adapter = new CloudNativeMapAdapter(native.Owner, 5);
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int actualDraw);
            GL.GetInteger(GetPName.ReadFramebufferBinding, out int actualRead);
            GL.GetInteger(GetPName.TextureBinding2D, out int actualTexture);
            // Assert construction restores all caller bindings before testing malformed storage.
            Assert.Equal(draw, actualDraw); Assert.Equal(read, actualRead); Assert.Equal(native.Raw2.TextureId, actualTexture);
            native.Owner.CloudTileLength = 40;
            // Act: report an allocation with mismatched native dimensions, then restore a valid allocation.
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
    public void ReloadAndReportedDrawFailuresWithdrawUntilSuccessfulGeneration()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid(); using var native = new NativeMap();
        var adapter = new CloudNativeMapAdapter(native.Owner, 5);
        // Act: publish the completed generation, then exercise shader reload, failed completion and recovery.
        adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(10, true);
        // Assert the baseline publication is available before injected reload and draw failures.
        Assert.True(adapter.Publication.TryGet(native.Owner, 5, 10, out _));
        int unlinked = GL.CreateProgram();
        // Act: invalidate the shader, report a failed generation, then restore a successful generation.
        native.SetProgram(unlinked); adapter.InvalidateShader();
        adapter.BeginGeneration(); adapter.CompleteGeneration(11, true);
        Assert.False(adapter.Publication.TryGet(native.Owner, 5, 11, out _));
        native.SetProgram(native.Program);
        GL.DeleteProgram(unlinked);
        adapter.BeginGeneration(); adapter.CompleteGeneration(12, false);
        Assert.False(adapter.Publication.TryGet(native.Owner, 5, 12, out _));
        adapter.BeginGeneration(); adapter.CompleteGeneration(13, true);
        Assert.True(adapter.Publication.TryGet(native.Owner, 5, 13, out _));
        // Act: withdraw publication after the native producer disables map rendering.
        AccessTools.Field(typeof(CloudRendererMap), "renderCloudMap").SetValue(native.Owner, false);
        adapter.BeginGeneration(); adapter.CompleteGeneration(14, true);
        Assert.False(adapter.Publication.TryGet(native.Owner, 5, 14, out _));
    }

/// <summary>Real event withdrawal prevents old-world roots from reacquiring adapters while leaving native registrations/resources intact.</summary>
    [Fact]
    public void RuntimeRejectsForeignAndOldWorldRoots()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid(); using var native = new NativeMap(); using var foreign = new NativeMap();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        // Arrange runtime event mocks before constructing the runtime that subscribes to them.
        var events = new Mock<IClientEventAPI>(); api.SetupGet(value => value.Event).Returns(events.Object);
        var system = new FluffyCloudsModSystem();
        AccessTools.Field(typeof(FluffyCloudsModSystem), "map").SetValue(system, native.Owner);
        api.Setup(value => value.ModLoader.GetModSystem<FluffyCloudsModSystem>(It.IsAny<bool>())).Returns(system);
        api.Setup(value => value.Settings.Int.Get("cloudRenderMode", 0)).Returns(1);
        using var runtime = new CloudNativeRuntime(api.Object);
        // Act: acquire the current map, publish one generation, then apply world, reload, and ownership transitions.
        // Assert the runtime admits only current roots and rejects stale publications after each transition.
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

/// <summary>Covered allocation and executable facts are reused without invoking their acquisition readers.</summary>
    [Fact]
    public void CoveredFactsAreReusedAndAmbientErrorsRemainWithTheirCaller()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var native = new NativeMap();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            _ = RHIResourceManager.AcquireExternalFacts(GpuResourceKind.Program, native.Program, GpuProgramMetadataReader.Instance);
            var program = (GpuResourceMetadataEntry<GpuProgramMetadata>)manager.FindResource(GpuResourceKind.Program, native.Program)!;
            program.Reader = new RejectRead<GpuProgramMetadata>();
            foreach (var texture in new[] { native.Map, native.Color, native.Raw1, native.Raw2 })
                ((GpuResourceMetadataEntry<GpuTextureMetadata>)manager.GetResourceRegistration(texture)!).Reader = new RejectRead<GpuTextureMetadata>();
            var adapter = new CloudNativeMapAdapter(native.Owner, 5);
            try
            {
                GL.Viewport(0, 0, -1, 1);
        // Act: certify the covered resources through the adapter while preserving the caller's pending GL error.
                adapter.BeginGeneration(); adapter.BeginUpload(); native.Upload(); adapter.CompleteUpload(true); adapter.CompleteGeneration(10, true);
        // Assert: covered metadata was reused and the adapter left the pre-existing error for its caller.
                Assert.True(adapter.TryGet(5, 10, out _));
                Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
            finally { adapter.Retire(); }
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Fails if an available canonical resource is unnecessarily queried again.</summary>
    private sealed class RejectRead<T> : IGpuResourceMetadataReader<T> where T : struct
    {
        #region Public API
        /// <inheritdoc />
        public bool IsResource(nint id) => throw new InvalidOperationException("Covered validity was queried.");
        /// <inheritdoc />
        public bool TryRead(nint id, out T data) => throw new InvalidOperationException("Covered metadata was queried.");
        #endregion
    }
    /// <summary>Simulates a foreign prefix that bypasses the native void upload body.</summary>
    private static bool SkipNativeUpload() => false;

    /// <summary>Owns fixture GL allocations only; production adapters must leave them alive until fixture disposal.</summary>
    private sealed class NativeMap : IDisposable
    {
        internal readonly CloudRendererMap Owner = (CloudRendererMap)RuntimeHelpers.GetUninitializedObject(typeof(CloudRendererMap));
        internal DynamicTexture2D Map = null!;
        internal DynamicTexture2D Color = null!;
        internal DynamicTexture2D Raw1 = null!;
        internal DynamicTexture2D Raw2 = null!;
        internal int Program;
        private int framebuffer;
        #region Public API
        /// <summary>Creates exactly the inspected native texture/attachment contract with a linked diagnostic program.</summary>
        internal NativeMap()
        {
            // Arrange owned handles locally so any failed allocation, compile, or link releases every prior handle.
            DynamicTexture2D? map = null, color = null, raw1 = null, raw2 = null;
            int framebufferHandle = 0, vertex = 0, fragment = 0, program = 0;
            try
            {
                // Act: allocate the exact observed native storage, attach it, and link its diagnostic program.
                map = CreateTexture(TextureFormat.Rgba32f);
                color = CreateTexture(TextureFormat.Rgba32f);
                raw1 = CreateTexture(TextureFormat.Rgba16);
                raw2 = CreateTexture(TextureFormat.Rgba16);
                Owner.CloudTileLength = 20; Owner.CloudTileSize = 50; Owner.TextureMap = map.TextureId; Owner.TextureCol = color.TextureId;
                Owner.offset = new Vec3f(1, 2, 3); Owner.committedState = new FluffyClouds.CloudTilesState();
                framebufferHandle = GL.GenFramebuffer();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebufferHandle);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, map.TextureId, 0);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, color.TextureId, 0);
                GL.DrawBuffers(2, new[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1 }); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                vertex = Compile(ShaderType.VertexShader, "#version 150\nvoid main(){gl_Position=vec4(0,0,0,1);}");
                fragment = Compile(ShaderType.FragmentShader, "#version 150\nout vec4 color; void main(){color=vec4(1);}");
                program = GL.CreateProgram(); GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked); Assert.Equal(1, linked);
                GL.DeleteShader(vertex); vertex = 0;
                GL.DeleteShader(fragment); fragment = 0;
                SetProgram(program);
                AccessTools.Field(typeof(CloudRendererMap), "renderCloudMap").SetValue(Owner, true);
                AccessTools.Field(typeof(CloudRendererMap), "Framebuffer").SetValue(Owner, framebufferHandle);
                AccessTools.Field(typeof(CloudRendererMap), "TextureData1").SetValue(Owner, raw1.TextureId);
                AccessTools.Field(typeof(CloudRendererMap), "TextureData2").SetValue(Owner, raw2.TextureId);
                Map = map; Color = color; Raw1 = raw1; Raw2 = raw2;
                Program = program; framebuffer = framebufferHandle;
            }
            catch
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
                if (program != 0) GL.DeleteProgram(program);
                if (vertex != 0) GL.DeleteShader(vertex);
                if (fragment != 0) GL.DeleteShader(fragment);
                if (framebufferHandle != 0) GL.DeleteFramebuffer(framebufferHandle);
                map?.Dispose(); color?.Dispose(); raw1?.Dispose(); raw2?.Dispose();
                throw;
            }
        }

        /// <summary>Creates one owned native texture for the synthetic storage fixture.</summary>
        private static DynamicTexture2D CreateTexture(TextureFormat format) =>
            RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = format, Width = System.Math.Max(1, 20), Height = System.Math.Max(1, 20), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

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
            int shader = GL.CreateShader(type);
            try
            {
                GL.ShaderSource(shader, source); GL.CompileShader(shader);
                GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
                Assert.Equal(1, compiled);
                return shader;
            }
            catch
            {
                if (shader != 0) GL.DeleteShader(shader);
                throw;
            }
        }
        #endregion
    }
    #endregion
}
