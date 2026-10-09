using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Observes production buffer interfaces and traversal policies at the actual driver binding boundary.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnShaderBufferInterfaceTests : RenderTestBase
{
    /// <summary>Uses the mandatory graphics context and production uniform-ring lifecycle.</summary>
    public LumOnShaderBufferInterfaceTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region External buffer ownership
    /// <summary>Interface setters retain independent slots until use and never take ownership of caller buffers.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExternalBuffersBindIndependentlyAndRemainCallerOwned(int consumer)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = CreateConsumer(programs, consumer);
        using var frameCamera = TestFrameCamera.CreateIdentity(1, 1);
        ((ILumOnFrameShader)program).FrameInputs = frameCamera;
        using var frame = GpuUniformBuffer.Create();
        using var world = GpuUniformBuffer.Create();
        using var replacement = GpuUniformBuffer.Create();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var inputs = AssignRequiredTextures(program, texture);
        // Contents are irrelevant to this binding-only contract; no draw reads these buffers.
        frame.UploadOrResize(new byte[16]);
        world.UploadOrResize(new byte[16]);
        replacement.UploadOrResize(new byte[16]);
        ((ILumOnFrameShader)program).FrameUniformBuffer = frame;
        ((ILumOnWorldProbeShader)program).WorldProbeUniformBuffer = world;
        bool consumesEffectFrame = program.ProgramLayout.BinaryInterface!.GetUniformBlockIndex(LumOnUniformBuffers.FrameBlockName) >= 0;
        int previousFrameBinding = BoundBuffer(LumOnUniformBuffers.FrameBinding);
        using (program.UseScope())
        {
            Assert.Equal(consumesEffectFrame ? frame.BufferId : previousFrameBinding, BoundBuffer(LumOnUniformBuffers.FrameBinding));
            Assert.Equal(world.BufferId, BoundBuffer(LumOnUniformBuffers.WorldProbeBinding));
            ((ILumOnFrameShader)program).FrameUniformBuffer = replacement;
            Assert.Equal(consumesEffectFrame ? frame.BufferId : previousFrameBinding, BoundBuffer(LumOnUniformBuffers.FrameBinding));
            program.Use();
            Assert.Equal(consumesEffectFrame ? replacement.BufferId : previousFrameBinding, BoundBuffer(LumOnUniformBuffers.FrameBinding));
            Assert.Equal(world.BufferId, BoundBuffer(LumOnUniformBuffers.WorldProbeBinding));
        }
        program.Dispose();
        Assert.True(GL.IsBuffer(frame.BufferId));
        Assert.True(GL.IsBuffer(world.BufferId));
        Assert.True(GL.IsBuffer(replacement.BufferId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An absent world-probe block must not disturb an independently bound buffer.</summary>
    [Fact]
    public void AbsentWorldBlockPreservesExistingBinding()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<LumOnVelocityShaderProgram>();
        Assert.Equal(-1, program.ProgramLayout.BinaryInterface!.GetUniformBlockIndex(LumOnUniformBuffers.WorldProbeBlockName));
        using var sentinel = GpuUniformBuffer.Create();
        using var ignored = GpuUniformBuffer.Create();
        sentinel.UploadOrResize(new byte[16]);
        ignored.UploadOrResize(new byte[16]);
        // Deliberate low-level precondition: the absent block's setter must preserve this slot.
        sentinel.BindBase(LumOnUniformBuffers.WorldProbeBinding);
        ((ILumOnWorldProbeShader)program).WorldProbeUniformBuffer = ignored;
        Assert.Equal(sentinel.BufferId, BoundBuffer(LumOnUniformBuffers.WorldProbeBinding));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Scene-owned traversal parameters
    /// <summary>Overrides traversal policy without changing ring/domain data, then restores defaults and unavailable state.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TraversalPolicyPreservesSceneMappingAndDefaultRestoration(int consumer)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = CreateConsumer(programs, consumer);
        using var frameCamera = TestFrameCamera.CreateIdentity(1, 1);
        ((ILumOnFrameShader)program).FrameInputs = frameCamera;
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var frame = GpuUniformBuffer.Create();
        frame.Allocate(112);
        program.FrameUniformBuffer = frame;
        AssignRequiredTextures(program, texture);
        using var scene = new TraceGeometryGpuScene(32);
        var window = new PartitionBounds(new(0, 0, 0), new(32, 32, 32));
        var near = new PartitionBounds(new(0, 0, 0), new(16, 16, 16));
        var surface = new PartitionBounds(new(16, 0, 0), new(32, 32, 32));
        scene.SetWindow(new(near, surface, window, 32, 32));
        BindScene(program, scene);
        using var active = program.UseScope();
        byte[] defaults = ReadNearFieldParameters();
        Assert.Equal(32, BitConverter.ToInt32(defaults, 12));
        Assert.Equal(256, BitConverter.ToInt32(defaults, 16));
        Assert.Equal(1, BitConverter.ToInt32(defaults, 24));
        Assert.Equal(float.MaxValue, BitConverter.ToSingle(defaults, 44));

        var origins = new PartitionBounds(new(2, 3, 4), new(8, 9, 10));
        var policy = new LumOnNearFieldTraceSettings(7, origins, 12);
        BindScene(program, scene, policy);
        program.Use();
        byte[] configured = ReadNearFieldParameters();
        Assert.Equal(defaults[..16], configured[..16]);
        Assert.Equal(defaults[64..], configured[64..]);
        Assert.Equal(7, BitConverter.ToInt32(configured, 16));
        Assert.Equal(2f, BitConverter.ToSingle(configured, 32));
        Assert.Equal(10f, BitConverter.ToSingle(configured, 56));
        Assert.Equal(12f, BitConverter.ToSingle(configured, 44));

        BindScene(program, scene, new LumOnNearFieldTraceSettings(9));
        program.Use();
        byte[] unrestricted = ReadNearFieldParameters();
        Assert.Equal(0, BitConverter.ToInt32(unrestricted, 24));
        Assert.Equal(defaults[64..], unrestricted[64..]);

        BindScene(program, scene);
        program.Use();
        Assert.Equal(defaults, ReadNearFieldParameters());
        BindScene(program, null, policy);
        program.Use();
        byte[] unavailable = ReadNearFieldParameters();
        Assert.Equal(0, BitConverter.ToInt32(unavailable, 12));
        Assert.Equal(0, BitConverter.ToInt32(unavailable, 76));
        Assert.Equal(0, BitConverter.ToInt32(unavailable, 108));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Production consumers and driver observations
    /// <summary>Supplies valid required samplers for binding-only tests that issue no draw.</summary>
    private static GpuResourceCollection AssignRequiredTextures(LumOnShaderProgram program, GpuTexture texture)
    {
        var inputs = new GpuResourceCollection();
        // These cases exercise the actual production declaration while buffer contents stay irrelevant.
        switch (program)
        {
            case LumOnScreenProbeAtlasTraceShaderProgram trace:
                trace.PrimaryDepth = texture.TextureId; var surfaceInput1 = inputs.Own(LayeredTestTexture.Create(null, texture, null));
        trace.GBufferSurface = surfaceInput1;
                var anchorInputs1 = inputs.Own(LayeredTestTexture.Create(texture, texture));
        trace.ProbeAnchors = anchorInputs1;
                trace.SurfaceAlbedo = texture; trace.ScreenProbeAtlasHistory = texture;
                trace.HzbDepth = texture; trace.ScreenProbeAtlasMetaHistory = texture;
                trace.ProbeTraceMask = texture;
                break;
            case LumOnScreenProbeAtlasGatherShaderProgram gather:
                gather.PrimaryDepth = texture.TextureId; var surfaceInput2 = inputs.Own(LayeredTestTexture.Create(texture, null, null));
        gather.GBufferSurface = surfaceInput2;
                var anchorInputs2 = inputs.Own(LayeredTestTexture.Create(texture, texture));
        gather.ProbeAnchors = anchorInputs2;
                gather.ScreenProbeAtlas = texture;
                break;
            case LumOnProbeSh9GatherShaderProgram sh9:
                sh9.PrimaryDepth = texture.TextureId; var surfaceInput3 = inputs.Own(LayeredTestTexture.Create(texture, null, null));
        sh9.GBufferSurface = surfaceInput3;
                var anchorInputs3 = inputs.Own(LayeredTestTexture.Create(texture, texture));
        sh9.ProbeAnchors = anchorInputs3;
                var shInputs4 = inputs.Own(LayeredTestTexture.Create(texture, texture, texture, texture, texture, texture, texture));
        sh9.ProbeSh9 = shInputs4;
                break;
            case LumOnDebugShaderProgram debug:
                debug.PrimaryDepth = texture.TextureId; var surfaceInput4 = inputs.Own(LayeredTestTexture.Create(texture, null, null));
        debug.GBufferSurface = surfaceInput4;
                break;
        }
        return inputs;
    }

    /// <summary>Loads the actual trace, atlas gather, SH9 gather or world-probe debug consumer.</summary>
    private static LumOnShaderProgram CreateConsumer(ComponentShaderPrograms programs, int consumer) => consumer switch
    {
        0 => programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(shader => { shader.WorldProbes = true; shader.NearField = true; }),
        1 => programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>(shader => { shader.WorldProbeEnabled = true; shader.DirectVisibility = true; }),
        2 => programs.Create<LumOnProbeSh9GatherShaderProgram>(shader => { shader.WorldProbeEnabled = true; shader.DirectVisibility = true; }),
        3 => programs.Create<LumOnDebugShaderProgram>(shader => { shader.WorldProbeEnabled = true; shader.DirectVisibility = true; },
            identity: LumOnDebugShaderProgramFamily.GetProgramName(LumOnDebugMode.WorldProbeIrradianceCombined)),
        _ => throw new ArgumentOutOfRangeException(nameof(consumer))
    };

    /// <summary>Invokes each consumer's production scene-binding operation without accessing its private parameter buffer.</summary>
    private static void BindScene(LumOnShaderProgram program, TraceGeometryGpuScene? scene, LumOnNearFieldTraceSettings? settings = null)
    {
        switch (program)
        {
            case LumOnScreenProbeAtlasTraceShaderProgram trace: trace.BindNearFieldScene(scene, settings); break;
            case LumOnScreenProbeAtlasGatherShaderProgram gather: gather.NearFieldVisibility.Stage(gather, scene, settings); break;
            case LumOnProbeSh9GatherShaderProgram sh9: sh9.NearFieldVisibility.Stage(sh9, scene, settings); break;
            case LumOnDebugShaderProgram debug: debug.NearFieldVisibility.Stage(debug, scene, settings); break;
            default: throw new ArgumentException("Consumer has no near-field scene binding.", nameof(program));
        }
    }

    /// <summary>Queries actual indexed state rather than assuming that the interface setter bound the requested buffer.</summary>
    private static int BoundBuffer(int binding)
    {
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, binding, out int buffer);
        return buffer;
    }

    /// <summary>Reads the installed std140 contract, respecting the production allocator's subrange offset.</summary>
    private static byte[] ReadNearFieldParameters()
    {
        int buffer = BoundBuffer(LumOnNearFieldParamsUbo.Binding);
        Assert.NotEqual(0, buffer);
        GL.GetInteger(GetIndexedPName.UniformBufferStart, LumOnNearFieldParamsUbo.Binding, out int offset);
        var bytes = new byte[128];
        using var binding = StateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, bytes.Length, bytes);
        return bytes;
    }
    #endregion
}
