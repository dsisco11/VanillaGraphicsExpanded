using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises pool-hook publication, shader input staging and engine layout-selector restoration.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LiquidGraphicsSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The active hook consumes the declared draw after changed inputs, restoring UseSsbo on success and exceptions.</summary>
    [Fact]
    public void PoolHookPublishesStagedInputsAndRestoresEngineSelector()
    {
        EnsureContextValid();
        using var storage = new LiquidPoolStorage(); using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnHzbDownsampleShaderProgram>();
        using var first = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.2f, .4f, .6f, .8f]);
        using var second = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.7f, .8f, .9f, 1]);
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        Type[] types;
        try { types = typeof(RenderAPIBase).Assembly.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException error) { types = error.Types.OfType<Type>().ToArray(); }
        var renderType = types.First(type => typeof(RenderAPIBase).IsAssignableFrom(type) && !type.IsAbstract);
        // Only the native layout-selector field is accessed; no window or game-owned API methods execute.
        var render = (RenderAPIBase)RuntimeHelpers.GetUninitializedObject(renderType); GC.SuppressFinalize(render);
        var api = new Mock<ICoreClientAPI>(); api.SetupGet(value => value.Render).Returns(render);
        api.SetupGet(value => value.Event).Returns(new Mock<IClientEventAPI>().Object);
        var manager = (MeshDataPoolManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));
        AccessTools.Field(typeof(MeshDataPoolManager), "pools").SetValue(manager, new List<MeshDataPool> { storage.Pool });
        using var submission = new LiquidGraphicsSubmission(api.Object);
        LiquidMeshSource.UseSsbo(render) = true;
        shader.HzbDepth = first; shader.SrcMip = 0;
        Assert.True(submission.Run(shader, [manager], new(target, [new(0)]), new(), [new ColorBlendDesc()], () =>
        {
            Assert.False(LiquidMeshSource.UseSsbo(render));
            Assert.Same(shader, ShaderProgramBase.CurrentShaderProgram);
            shader.HzbDepth = second;
            Assert.False(LiquidPoolSubmissionHook.Prefix(storage.Pool));
        }));
        Assert.True(LiquidMeshSource.UseSsbo(render));
        Assert.InRange(target[0].ReadPixels()[0], .6999f, .7001f);
        Assert.Throws<ArithmeticException>(() => submission.Run(shader, [manager], new(target, [new(0)]), new(),
            [new ColorBlendDesc()], () => throw new ArithmeticException("Manager fixture failure.")));
        Assert.True(LiquidMeshSource.UseSsbo(render));
        Assert.True(LiquidPoolSubmissionHook.Prefix(storage.Pool));
        using var depth = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent24);
        using var depthTarget = GpuFramebuffer.CreateDepthOnly(depth)!;
        var borrowedDepth = submission.Borrow(new FrameBufferRef { FboId = depthTarget.FboId, Width = 1, Height = 1, DepthTextureId = depth.TextureId });
        Assert.True(submission.Run(shader, [manager], new(borrowedDepth, LiquidPipelineStates.DepthOutputs,
            new(DepthLoad: VanillaGraphicsExpanded.Rendering.Pipeline.Passes.AttachmentLoad.Clear, ClearDepth: 1)),
            LiquidPipelineStates.Depth, LiquidPipelineStates.DepthBlending,
            () => Assert.False(LiquidPoolSubmissionHook.Prefix(storage.Pool))));
        Assert.InRange(depth.ReadPixels()[0], .4999f, .5001f);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
