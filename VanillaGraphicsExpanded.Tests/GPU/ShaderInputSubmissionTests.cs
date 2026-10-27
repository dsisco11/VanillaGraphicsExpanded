using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises staged inputs against the production upsample program and real uniform allocator.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderInputSubmissionTests : RenderTestBase
{
    /// <summary>Uses the mandatory headless context without starting the game.</summary>
    public ShaderInputSubmissionTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Public API
    /// <summary>Shared CPU contents rebind across programs while ring boundaries and edits require fresh snapshots.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedBlockReuseRespectsPublicationAndAllocatorLifetime(bool persistent)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var first = programs.Create<LumOnUpsampleShaderProgram>();
        var second = programs.Create<LumOnUpsampleShaderProgram>();
        using var block = new PackedUniformBuffer(32);
        GpuSupport.Initialize();
        using var ring = new GpuUniformRingBuffer(4096, 2, persistent);
        Assert.SkipWhen(persistent && !ring.UsesPersistentMapping, "Persistent uniform mapping unavailable.");
        Assert.Equal(persistent, ring.UsesPersistentMapping);
        using var replacement = new GpuUniformRingBuffer(4096, 1, false);
        byte[] bytes = new byte[32];
        BitConverter.GetBytes(0.25f).CopyTo(bytes, 0);
        block.SetBytes(bytes);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            // Capturing a CPU version performs no allocation; the engine slot is explicit.
            var captured = block.Capture();
            Assert.True(block.IsDirty);
            Assert.True(RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
            Assert.False(block.IsDirty);
#if DEBUG
            Assert.Equal(1, ring.AllocationsWritten);
#endif
            Assert.True(RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
#if DEBUG
            Assert.Equal(1, ring.AllocationsWritten);
#endif
            Assert.Equal(0.25f, SubmittedDepthSigma());
            GL.GetInteger((GetIndexedPName)All.UniformBufferStart, 14, out int originalOffset);
            GL.GetInteger((GetIndexedPName)All.UniformBufferBinding, 14, out int originalBuffer);

            // A competing slot publication must not prevent the unchanged owner from restoring its range.
            using var competing = new PackedUniformBuffer(32);
            BitConverter.GetBytes(0.75f).CopyTo(bytes, 0);
            competing.SetBytes(bytes);
            Assert.True(RHICommandContext.Current.OpenGL.BindEngineUniform(14, competing.Capture()));
            Assert.Equal(0.75f, SubmittedDepthSigma());
            Assert.True(RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
            Assert.Equal(0.25f, SubmittedDepthSigma());
#if DEBUG
            Assert.Equal(2, ring.AllocationsWritten);
#endif
            block.SetBytes(bytes);
            Assert.True(RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
            Assert.Equal(0.75f, SubmittedDepthSigma());
#if DEBUG
            Assert.Equal(3, ring.AllocationsWritten);
#endif
            // Earlier submitted snapshots remain intact after a changed publication.
            GL.BindBuffer(BufferTarget.UniformBuffer, originalBuffer);
            float[] original = new float[1];
            GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)originalOffset, sizeof(float), original);
            Assert.Equal(0.25f, original[0]);

            first.InvalidateAssets();
            first.EnsureReady();
            Assert.True(RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
#if DEBUG
            Assert.Equal(3, ring.AllocationsWritten);
#endif
            ring.EndFrame();
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
#if DEBUG
            Assert.Equal(3, ring.AllocationsWritten);
#endif
            // Both another page and wraparound invalidate the borrowed range even when bytes are unchanged.
            for (int frame = 1; frame <= 2; frame++)
            {
                ring.BeginFrame(frame);
                Assert.True(RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
                Assert.Equal(0.75f, SubmittedDepthSigma());
#if DEBUG
                Assert.Equal(3 + frame, ring.AllocationsWritten);
#endif
                ring.EndFrame();
            }
            ring.BeginFrame(2);
            Assert.True(RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
#if DEBUG
            Assert.Equal(6, ring.AllocationsWritten);
#endif
            Assert.Equal(0.75f, SubmittedDepthSigma());
            replacement.BeginFrame(0);
            GpuUniformRingSystem.SetCurrent(replacement);
            Assert.True(RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
#if DEBUG
            Assert.Equal(1, replacement.AllocationsWritten);
#endif
            block.Dispose();
            Assert.True(RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
#if DEBUG
            Assert.Equal(2, replacement.AllocationsWritten);
            Assert.Equal(64, replacement.BytesWritten);
#endif
            Assert.Equal(0.75f, SubmittedDepthSigma());
            replacement.Dispose();
            Assert.Throws<ObjectDisposedException>(() => RHICommandContext.Current.OpenGL.BindEngineUniform(14, block.Capture()));
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }
    /// <summary>Generated validation leaves all bindings and CPU dirty work untouched until a missing required input is corrected.</summary>
    [Fact]
    public void RequiredInputFailureRetainsPendingBlockForRetry()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var previous = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var desired = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var ring = new GpuUniformRingBuffer(4096, 1, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<RasterDepthReductionShaderProgram>();
            using var pipeline = CreateBindingPipeline(shader);
            RHICommandContext.Current.SetPipeline(pipeline);
            shader.SrcMip = 1;
            var parameters = (CpuUniformBuffer)typeof(RasterDepthReductionShaderProgram)
                .GetField("paramsUbo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shader)!;
            OpenGLImageBindings.BindTexture(StateCache.Current, TextureTarget.Texture2D, 0, previous.TextureId);
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindResources(shader.CaptureBindings()));
            Assert.True(parameters.IsDirty);
#if DEBUG
            Assert.Equal(0, ring.AllocationsWritten);
#endif
            Assert.True(StateCache.Current.TryGetCachedBoundTexture(TextureTarget.Texture2D, 0, out int retained));
            Assert.Equal(previous.TextureId, retained);
            shader.HzbDepth = desired;
            { RHICommandContext.Current.BindResources(shader.CaptureBindings()); }
            
            {
                Assert.False(parameters.IsDirty);
#if DEBUG
                Assert.Equal(1, ring.AllocationsWritten);
#endif
                Assert.True(StateCache.Current.TryGetCachedBoundTexture(TextureTarget.Texture2D, 0, out int published));
                Assert.Equal(desired.TextureId, published);
            }
            RHICommandContext.Current.BindResources(shader.CaptureBindings());
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }
    /// <summary>Each HZB draw publishes one source mip and retains resource edits until that draw.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HzbMipDrawsPublishOneBlockAndDeferResourceChanges(bool persistent)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var first = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 3, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (3) - 1, Label = null }).RequireResource();
        using var second = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 3, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (3) - 1, Label = null }).RequireResource();
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        using var draws = new VanillaGraphicsExpanded.Tests.GPU.Helpers.ShaderTestFramework();
        GpuSupport.Initialize();
        Assert.True(GpuSupport.IsInitializedForCurrentContext);
        using var ring = new GpuUniformRingBuffer(4096, 1, persistent);
        Assert.SkipWhen(persistent && !ring.UsesPersistentMapping, "Persistent uniform mapping is unavailable in this headless context.");
        Assert.Equal(persistent, ring.UsesPersistentMapping);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<RasterDepthReductionShaderProgram>();
            using var pipeline = CreateBindingPipeline(shader);
            RHICommandContext.Current.SetPipeline(pipeline);
            int priorTexture = BoundTextures()[0];
            for (int mip = 0; mip < 2; mip++)
            {
                // Repeated assignments remain CPU work even between consecutive draws.
                shader.HzbDepth = first;
                shader.HzbDepth = mip == 0 ? first : second;
                shader.SrcMip = 2;
                shader.SrcMip = mip;
#if DEBUG
                Assert.Equal(mip, ring.AllocationsWritten);
#endif
                Assert.Equal(priorTexture, BoundTextures()[0]);
                draws.RenderQuadTo(shader, target, afterDraw: () =>
                    Assert.Equal(mip, BitConverter.SingleToInt32Bits(SubmittedDepthSigma())));
#if DEBUG
                Assert.Equal(mip + 1, ring.AllocationsWritten);
#endif
                Assert.Equal(priorTexture, BoundTextures()[0]);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>Explicit pipeline and capture selection restores the desired owner's textures and parameter bytes.</summary>
    [Fact]
    public void ExplicitCaptureSwitchRestoresRetainedResources()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var firstTexture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var secondTexture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var frame = RHICommandContext.Current.CreateUniformBuffer(new RHIBufferDescription { Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        frame.Allocate(112);
        using var frameCamera = TestFrameCamera.CreateIdentity(1, 1);
        using var ring = new GpuUniformRingBuffer(4096, 1, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var first = programs.Create<LumOnUpsampleShaderProgram>();
            var second = programs.Create<LumOnUpsampleShaderProgram>();
            using var firstSurface = AssignResources(first, firstTexture, frame, frameCamera, 0.25f);
            using var secondSurface = AssignResources(second, secondTexture, frame, frameCamera, 0.75f);
            using var firstPipeline = CreateBindingPipeline(first);
            using var secondPipeline = CreateBindingPipeline(second);
            var firstCapture = first.CaptureBindings();
            RHICommandContext.Current.SetPipeline(firstPipeline);
            RHICommandContext.Current.BindResources(firstCapture);
            {
                Assert.Equal(firstTexture.TextureId, BoundTextures()[0]);
                Assert.Equal(0.25f, SubmittedDepthSigma());
                Assert.Equal(0f, SubmittedDepthSigma(4));
                Assert.Equal(0f, SubmittedDepthSigma(8));
                Assert.Equal(0f, SubmittedDepthSigma(16));
                RHICommandContext.Current.SetPipeline(secondPipeline);
                RHICommandContext.Current.BindResources(second.CaptureBindings());
                {
                    Assert.Equal(secondTexture.TextureId, BoundTextures()[0]);
                    Assert.Equal(0.75f, SubmittedDepthSigma());
                }
                RHICommandContext.Current.SetPipeline(firstPipeline);
                RHICommandContext.Current.BindResources(firstCapture);
                Assert.Equal(firstTexture.TextureId, BoundTextures()[0]);
                Assert.Equal(0.25f, SubmittedDepthSigma());
                Assert.Equal(0f, SubmittedDepthSigma(4));
                Assert.Equal(0f, SubmittedDepthSigma(8));
                Assert.Equal(0f, SubmittedDepthSigma(16));
            }
#if DEBUG
            Assert.Equal(3, ring.AllocationsWritten);
#endif
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>Ordinary assignments retain partial edits and writes one parameter block at each use boundary.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpsampleAssignmentsDeferUploadsAndRetainValues(bool persistent)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var frame = RHICommandContext.Current.CreateUniformBuffer(new RHIBufferDescription { Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        frame.Allocate(112);
        using var frameCamera = TestFrameCamera.CreateIdentity(1, 1);
        GpuSupport.Initialize();
        Assert.True(GpuSupport.IsInitializedForCurrentContext);
        using var ring = new GpuUniformRingBuffer(4096, 1, persistent);
        Assert.SkipWhen(persistent && !ring.UsesPersistentMapping, "Persistent uniform mapping is unavailable in this headless context.");
        Assert.Equal(persistent, ring.UsesPersistentMapping);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<LumOnUpsampleShaderProgram>();
            using var pipeline = CreateBindingPipeline(shader);
            RHICommandContext.Current.SetPipeline(pipeline);
            int[] priorTextures = BoundTextures();
            shader.FrameUniformBuffer = frame;
            ((ILumOnFrameShader)shader).FrameInputs = frameCamera;
            shader.IndirectHalf = texture;
            shader.PrimaryDepth = texture;
            using var surfaceInput1 = LayeredTestTexture.Create(texture, null, null);
            shader.GBufferSurface = surfaceInput1;
            shader.UpsampleDepthSigma = 0.25f;
            shader.UpsampleNormalSigma = 12f;
            shader.UpsampleSpatialSigma = 2f;
            shader.HoleFillRadius = 3;
            shader.HoleFillMinConfidence = 0.5f;

#if DEBUG
            Assert.Equal(0, ring.AllocationsWritten);
#endif
            Assert.Equal(priorTextures, BoundTextures());
            Assert.Null(ShaderProgramBase.CurrentShaderProgram);
            GpuUniformRingSystem.ClearCurrent();
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindResources(shader.CaptureBindings()));
            Assert.True(Parameters(shader).IsDirty);
            Assert.Null(ShaderProgramBase.CurrentShaderProgram);
            GpuUniformRingSystem.SetCurrent(ring);
            RHICommandContext.Current.BindResources(shader.CaptureBindings());
#if DEBUG
            Assert.Equal(2, ring.AllocationsWritten);
#endif
            shader.UpsampleDepthSigma = 0.75f;
#if DEBUG
            Assert.Equal(2, ring.AllocationsWritten);
#endif
            Assert.Equal(12f, UboPacking.ReadFloat(Parameters(shader).Bytes, 4));
            RHICommandContext.Current.BindResources(shader.CaptureBindings());
#if DEBUG
            Assert.Equal(3, ring.AllocationsWritten);
#endif
            // End the direct use borrow before replacing the executable and reusing its uniform page.
            GL.Finish();
            ring.BeginFrame(1);
            RHICommandContext.Current.BindResources(shader.CaptureBindings());
#if DEBUG
            Assert.Equal(5, ring.AllocationsWritten);
#endif
            Assert.Equal(0.75f, SubmittedDepthSigma());
            Assert.Equal(shader.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal(0.75f, UboPacking.ReadFloat(Parameters(shader).Bytes, 0));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>The generated anchor submission publishes one retained CPU block after ordinary assignments.</summary>
    [Fact]
    public void ProbeAnchorGeneratedSubmissionDefersUploadsAndRetainsValues()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var frame = RHICommandContext.Current.CreateUniformBuffer(new RHIBufferDescription { Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        frame.Allocate(112);
        using var frameCamera = TestFrameCamera.CreateIdentity(1, 1);
        using var ring = new GpuUniformRingBuffer(4096, 1, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<LumOnProbeAnchorShaderProgram>();
            using var pipeline = CreateBindingPipeline(shader);
            RHICommandContext.Current.SetPipeline(pipeline);
            int[] priorTextures = BoundTextures();
            shader.FrameUniformBuffer = frame;
            ((ILumOnFrameShader)shader).FrameInputs = frameCamera;
            shader.PrimaryDepth = texture;
            using var surfaceInput2 = LayeredTestTexture.Create(texture, null, null);
            shader.GBufferSurface = surfaceInput2;
            shader.PmjJitter = texture;
            shader.DepthDiscontinuityThreshold = 0.25f;
            shader.DepthDiscontinuityThreshold = 0.75f;
#if DEBUG
            Assert.Equal(0, ring.AllocationsWritten);
#endif
            Assert.Equal(priorTextures, BoundTextures());
            RHICommandContext.Current.BindResources(shader.CaptureBindings());
#if DEBUG
            Assert.Equal(2, ring.AllocationsWritten);
#endif
            Assert.Equal(0.75f, SubmittedDepthSigma(48));
            RHICommandContext.Current.BindResources(shader.CaptureBindings());
#if DEBUG
            Assert.Equal(2, ring.AllocationsWritten);
#endif
            Assert.Equal(0.75f, SubmittedDepthSigma(48));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }
    /// <summary>Missing required resources fail use before any parameter allocation can be consumed.</summary>
    [Fact]
    public void MissingRequiredUpsampleInputsPreventSubmission()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var frameCamera = TestFrameCamera.CreateIdentity(1, 1);
        using var ring = new GpuUniformRingBuffer(4096, 1, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<LumOnUpsampleShaderProgram>();
            using var pipeline = CreateBindingPipeline(shader);
            RHICommandContext.Current.SetPipeline(pipeline);
            ((ILumOnFrameShader)shader).FrameInputs = frameCamera;
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindResources(shader.CaptureBindings()));
#if DEBUG
            Assert.Equal(0, ring.AllocationsWritten);
#endif
            Assert.Null(ShaderProgramBase.CurrentShaderProgram);
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>An optimized-out normal sampler and unused shared world-probe block need no prepared resource.</summary>
    [Fact]
    public void InactiveUpsampleInputsCanRemainUnset()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var frame = RHICommandContext.Current.CreateUniformBuffer(new RHIBufferDescription { Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        frame.Allocate(112);
        using var frameCamera = TestFrameCamera.CreateIdentity(1, 1);
        using var ring = new GpuUniformRingBuffer(4096, 1, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<LumOnUpsampleShaderProgram>(s =>
            {
                s.DenoiseEnabled = false;
                s.HoleFillEnabled = false;
            });
            using var pipeline = CreateBindingPipeline(shader);
            RHICommandContext.Current.SetPipeline(pipeline);
            Assert.Equal(GpuProgramLayout.ResolutionState.Missing,
                shader.ProgramLayout.ResolveUniformLocation(shader.ProgramId, "gBufferSurface").State);
            Assert.Equal(GpuProgramLayout.ResolutionState.Missing,
                shader.ProgramLayout.ResolveUniformBlockActive(shader.ProgramId, LumOnUpsampleParamsUbo.BlockName).State);
            shader.FrameUniformBuffer = frame;
            ((ILumOnFrameShader)shader).FrameInputs = frameCamera;
            shader.IndirectHalf = texture;
            shader.PrimaryDepth = texture;

            RHICommandContext.Current.BindResources(shader.CaptureBindings());
#if DEBUG
            Assert.Equal(1, ring.AllocationsWritten);
            Assert.Equal(VgeFrameUniformBuffer.PackedSize, ring.BytesWritten);
#endif
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }
    #endregion

    #region Private
    /// <summary>Builds the fixed executable contract for publication-only checks without selecting native state.</summary>
    private static RHIGraphicsPipeline CreateBindingPipeline(GpuProgram program)
    {
        int outputs = program is LumOnProbeAnchorShaderProgram ? 2 : 1;
        return RHICommandContext.Current.CreateGraphicsPipeline(new(program.Shader, EngineFullscreenGeometry.Layout,
            new(Enumerable.Range(0, outputs).Select(_ => new VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions.ColorTargetSlot(TextureFormat.Rgba32f))),
            DynamicPipelineState.Viewport)).RequireResource();
    }

    /// <summary>Supplies required borrowed inputs while giving each owner distinct parameter bytes.</summary>
    private static Texture3D AssignResources(LumOnUpsampleShaderProgram shader, GpuTexture texture, GpuUniformBuffer frame, VgeFrameUniformBuffer frameCamera, float sigma)
    {
        shader.FrameUniformBuffer = frame;
            ((ILumOnFrameShader)shader).FrameInputs = frameCamera;
        shader.IndirectHalf = texture;
        shader.PrimaryDepth = texture;
        var surfaceInput3 = LayeredTestTexture.Create(texture, null, null);
        shader.GBufferSurface = surfaceInput3;
        shader.UpsampleDepthSigma = sigma;
        return surfaceInput3;

    }

    /// <summary>Reads the first packed parameter from the actual driver-bound uniform-buffer range.</summary>
    private static float SubmittedDepthSigma(int parameterOffset = 0)
    {
        const int slot = 14;
        GL.GetInteger((GetIndexedPName)All.UniformBufferBinding, slot, out int buffer);
        GL.GetInteger((GetIndexedPName)All.UniformBufferStart, slot, out int offset);
        int prior = GL.GetInteger(GetPName.UniformBufferBinding);
        GL.BindBuffer(BufferTarget.UniformBuffer, buffer);
        float[] value = new float[1];
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)(offset + parameterOffset), sizeof(float), value);
        GL.BindBuffer(BufferTarget.UniformBuffer, prior);
        return value[0];
    }

    /// <summary>Reads driver bindings for the three upsample units without relying on the production cache.</summary>
    private static int[] BoundTextures()
    {
        int priorUnit = GL.GetInteger(GetPName.ActiveTexture);
        int[] bindings = new int[3];
        for (int unit = 0; unit < bindings.Length; unit++)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            bindings[unit] = GL.GetInteger(GetPName.TextureBinding2D);
        }
        GL.ActiveTexture((TextureUnit)priorUnit);
        return bindings;
    }

    /// <summary>Observes the retained CPU block without exposing test-only production accessors.</summary>
    private static CpuUniformBuffer Parameters(LumOnUpsampleShaderProgram shader) =>
        (CpuUniformBuffer)typeof(LumOnUpsampleShaderProgram).GetField("paramsUbo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shader)!;
    #endregion
}
