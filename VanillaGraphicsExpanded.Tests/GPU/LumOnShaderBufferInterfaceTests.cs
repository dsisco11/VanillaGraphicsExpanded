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
    /// <summary>Interface setters bind independent declared slots and never take ownership of caller buffers.</summary>
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
        using var frame = GpuUniformBuffer.Create();
        using var world = GpuUniformBuffer.Create();
        using var replacement = GpuUniformBuffer.Create();
        // Contents are irrelevant to this binding-only contract; no draw reads these buffers.
        frame.UploadOrResize(new byte[16]);
        world.UploadOrResize(new byte[16]);
        replacement.UploadOrResize(new byte[16]);
        using (program.UseScope())
        {
            ((ILumOnFrameShader)program).FrameUniformBuffer = frame;
            ((ILumOnWorldProbeShader)program).WorldProbeUniformBuffer = world;
            Assert.Equal(frame.BufferId, BoundBuffer(LumOnUniformBuffers.FrameBinding));
            Assert.Equal(world.BufferId, BoundBuffer(LumOnUniformBuffers.WorldProbeBinding));
            ((ILumOnFrameShader)program).FrameUniformBuffer = replacement;
            Assert.Equal(replacement.BufferId, BoundBuffer(LumOnUniformBuffers.FrameBinding));
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
        using var scene = new TraceGeometryGpuScene(32);
        var window = new PartitionBounds(new(0, 0, 0), new(32, 32, 32));
        var near = new PartitionBounds(new(0, 0, 0), new(16, 16, 16));
        var surface = new PartitionBounds(new(16, 0, 0), new(32, 32, 32));
        scene.SetWindow(new(near, surface, window, 32, 32));
        using var active = program.UseScope();
        BindScene(program, scene);
        byte[] defaults = ReadNearFieldParameters();
        Assert.Equal(32, BitConverter.ToInt32(defaults, 12));
        Assert.Equal(256, BitConverter.ToInt32(defaults, 16));
        Assert.Equal(1, BitConverter.ToInt32(defaults, 24));
        Assert.Equal(float.MaxValue, BitConverter.ToSingle(defaults, 44));

        var origins = new PartitionBounds(new(2, 3, 4), new(8, 9, 10));
        var policy = new LumOnNearFieldTraceSettings(7, origins, 12);
        BindScene(program, scene, policy);
        byte[] configured = ReadNearFieldParameters();
        Assert.Equal(defaults[..16], configured[..16]);
        Assert.Equal(defaults[64..], configured[64..]);
        Assert.Equal(7, BitConverter.ToInt32(configured, 16));
        Assert.Equal(2f, BitConverter.ToSingle(configured, 32));
        Assert.Equal(10f, BitConverter.ToSingle(configured, 56));
        Assert.Equal(12f, BitConverter.ToSingle(configured, 44));

        BindScene(program, scene, new LumOnNearFieldTraceSettings(9));
        byte[] unrestricted = ReadNearFieldParameters();
        Assert.Equal(0, BitConverter.ToInt32(unrestricted, 24));
        Assert.Equal(defaults[64..], unrestricted[64..]);

        BindScene(program, scene);
        Assert.Equal(defaults, ReadNearFieldParameters());
        BindScene(program, null, policy);
        byte[] unavailable = ReadNearFieldParameters();
        Assert.Equal(0, BitConverter.ToInt32(unavailable, 12));
        Assert.Equal(0, BitConverter.ToInt32(unavailable, 76));
        Assert.Equal(0, BitConverter.ToInt32(unavailable, 108));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Production consumers and driver observations
    /// <summary>Loads the actual trace, atlas gather, SH9 gather or world-probe debug consumer.</summary>
    private static LumOnShaderProgram CreateConsumer(ComponentShaderPrograms programs, int consumer) => consumer switch
    {
        0 => programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(shader => { shader.WorldProbes = true; shader.NearField = true; }),
        1 => programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>(shader => { shader.WorldProbeEnabled = true; shader.DirectVisibility = true; }),
        2 => programs.Create<LumOnProbeSh9GatherShaderProgram>(shader => { shader.WorldProbeEnabled = true; shader.DirectVisibility = true; }),
        3 => programs.Create<LumOnDebugShaderProgram>(shader => { shader.WorldProbeEnabled = true; shader.DirectVisibility = true; },
            identity: LumOnDebugShaderProgram.WorldprobeContract.Identity),
        _ => throw new ArgumentOutOfRangeException(nameof(consumer))
    };

    /// <summary>Invokes each consumer's production scene-binding operation without accessing its private parameter buffer.</summary>
    private static void BindScene(LumOnShaderProgram program, TraceGeometryGpuScene? scene, LumOnNearFieldTraceSettings? settings = null)
    {
        switch (program)
        {
            case LumOnScreenProbeAtlasTraceShaderProgram trace: trace.BindNearFieldScene(scene, settings); break;
            case LumOnScreenProbeAtlasGatherShaderProgram gather: gather.NearFieldVisibility.Bind(gather, scene, settings); break;
            case LumOnProbeSh9GatherShaderProgram sh9: sh9.NearFieldVisibility.Bind(sh9, scene, settings); break;
            case LumOnDebugShaderProgram debug: debug.NearFieldVisibility.Bind(debug, scene, settings); break;
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
        using var binding = GlStateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, bytes.Length, bytes);
        return bytes;
    }
    #endregion
}
