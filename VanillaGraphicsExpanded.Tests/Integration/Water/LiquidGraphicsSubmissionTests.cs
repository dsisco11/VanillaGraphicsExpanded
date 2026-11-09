using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Integration.Water.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.Water;

/// <summary>Exercises pool-hook publication, shader input staging and engine layout-selector restoration.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LiquidGraphicsSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The active hook consumes the declared draw after changed inputs, restoring UseSsbo on success and exceptions.</summary>
    [Fact]
    public void PoolHookPublishesStagedInputsAndRestoresEngineSelector()
    {
        // Arrange real pool storage, staged textures and an engine selector shell with only the accessed API edges mocked.
        EnsureContextValid();
        using var storage = new LiquidPoolStorage(); using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var first = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 2), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .2f, .4f, .6f, .8f }).AsSpan()).ToArray())]).RequireResource();
        using var second = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 2), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .7f, .8f, .9f, 1 }).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(1, 1, TextureFormat.R32f);
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
        // Act by submitting staged inputs through the installed pool hook and drawing the initial color pass.
        Assert.True(submission.Run(shader, [manager], FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex: 0)]), new(), [new ColorBlendDesc()], () =>
        {
            Assert.False(LiquidMeshSource.UseSsbo(render));
            Assert.Null(ShaderProgramBase.CurrentShaderProgram);
            shader.HzbDepth = second;
            Assert.False(LiquidPoolSubmissionHook.Prefix(storage.Pool));
        }));
        // Assert the hook observed the new texture and restored the engine selector after execution.
        Assert.True(LiquidMeshSource.UseSsbo(render));
        Assert.InRange(target[0].ReadTexel()[0], .6999f, .7001f);
        var retainedTargets = AccessTools.Field(typeof(LiquidGraphicsSubmission), "targets").GetValue(submission);
        var retainedPipeline = AccessTools.Field(typeof(LiquidGraphicsSubmission), "pipeline").GetValue(submission);
        // Act by submitting an equivalent freshly authored pass with the same attachments.
        Assert.True(submission.Run(shader, [manager], FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex: 0)]),
            new(), [new ColorBlendDesc()], () => { }));
        // Assert pass value equality preserves both retained preparation objects.
        Assert.Same(retainedTargets, AccessTools.Field(typeof(LiquidGraphicsSubmission), "targets").GetValue(submission));
        Assert.Same(retainedPipeline, AccessTools.Field(typeof(LiquidGraphicsSubmission), "pipeline").GetValue(submission));
        // Act by submitting an invalid mip-level candidate.
        Assert.ThrowsAny<Exception>(() => submission.Run(shader, [manager],
            new([new(Image: RHIAttachment.FromTexture(first, new(MipLevel: 8)))]), new(), [new ColorBlendDesc()], () => { }));
        // Assert candidate failure preserves the last successful preparation.
        Assert.Same(retainedTargets, AccessTools.Field(typeof(LiquidGraphicsSubmission), "targets").GetValue(submission));
        Assert.Same(retainedPipeline, AccessTools.Field(typeof(LiquidGraphicsSubmission), "pipeline").GetValue(submission));
        // Act by making the manager callback fail after submission begins.
        Assert.Throws<ArithmeticException>(() => submission.Run(shader, [manager], FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex: 0)]), new(),
            [new ColorBlendDesc()], () => throw new ArithmeticException("Manager fixture failure.")));
        // Assert the exceptional pass restores the engine selector and normal pool admission.
        Assert.True(LiquidMeshSource.UseSsbo(render));
        Assert.True(LiquidPoolSubmissionHook.Prefix(storage.Pool));
        using var depth = new DepthTexture(1, 1, TextureFormat.DepthComponent24);
        using var depthTarget = DynamicGpuFramebuffer.CreateDepthOnly(depth)!;
        var borrowedDepth = submission.Borrow(new FrameBufferRef { FboId = depthTarget.FboId, Width = 1, Height = 1, DepthTextureId = depth.TextureId });
        // Act by submitting the depth pass while the hook temporarily disables ordinary pool admission.
        Assert.True(submission.Run(shader, [manager], FramebufferTestSetup.DescribeAttachments(borrowedDepth, LiquidPipelineStates.DepthOutputs,
            new(DepthLoad: AttachmentLoad.Clear, ClearDepth: 1)),
            LiquidPipelineStates.Depth, LiquidPipelineStates.DepthBlending,
            () => Assert.False(LiquidPoolSubmissionHook.Prefix(storage.Pool))));
        // Assert the pass writes the expected depth value.
        Assert.InRange(depth.ReadTexel()[0], .4999f, .5001f);
        var beforeContext = AccessTools.Field(typeof(LiquidGraphicsSubmission), "pipeline").GetValue(submission);
        // Act by supplying an obsolete preparation identity and running in the actual current context.
        AccessTools.Field(typeof(LiquidGraphicsSubmission), "preparedContext").SetValue(submission,
            RuntimeHelpers.GetUninitializedObject(typeof(RHICommandContext)));
        Assert.True(submission.Run(shader, [manager], FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex: 0)]),
            new(), [new ColorBlendDesc()], () => { }));
        // Assert the current context replaces the obsolete preparation.
        Assert.NotSame(beforeContext, AccessTools.Field(typeof(LiquidGraphicsSubmission), "pipeline").GetValue(submission));
        // Act by publishing leave-world to retire the borrowed surface and cached preparation.
        Mock.Get(api.Object.Event).Raise(value => value.LeaveWorld += null);
        // Assert retirement clears the borrowed resource and both cached preparation objects.
        Assert.True(borrowedDepth.IsDisposed);
        Assert.Null(AccessTools.Field(typeof(LiquidGraphicsSubmission), "pipeline").GetValue(submission));
        Assert.Null(AccessTools.Field(typeof(LiquidGraphicsSubmission), "targets").GetValue(submission));
        // Act by submitting again after world retirement.
        Assert.True(submission.Run(shader, [manager], FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex: 0)]),
            new(), [new ColorBlendDesc()], () => { }));
        // Assert the fresh submission completes without GL errors.
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
