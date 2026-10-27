using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises authoritative immediate submission against production shader inputs and real target pixels.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RHICommandContextIntegrationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Depth/stencil clear masks remain valid independently of disabled draw tests and restore disabled incoming masks.</summary>
    [Fact]
    public void PackedClearThenDisabledTestsPreservesClearedAspectsAndExactMasks()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader, Layout(3),
            new([new(TextureFormat.R32f)], TextureFormat.Depth24Stencil8), DynamicPipelineState.Viewport)).RequireResource();
        using var geometry = Geometry();
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 8, Height = 8, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 8, 8), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.R32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((Enumerable.Repeat(.2f, 64).ToArray()).AsSpan()).ToArray())]).RequireResource();
        using var color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depth = new DepthStencilTexture(4, 4);
        using var colorImage = GpuFramebufferAttachment.FromTexture(color);
        using var depthImage = GpuFramebufferAttachment.FromTexture(depth);
        using var target = DynamicGpuFramebuffer.CreateEmpty();
        target.SetAttachment(FramebufferAttachment.ColorAttachment0, colorImage);
        target.SetAttachment(FramebufferAttachment.DepthStencilAttachment, depthImage);
        shader.HzbDepth = source; shader.SrcMip = 0;
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.StencilTest);
        GL.DepthMask(false); GL.StencilMaskSeparate(StencilFace.Front, 0x35); GL.StencilMaskSeparate(StencilFace.Back, 0x53);
        StateCache.Current.InvalidateAll();
        {
    var commands=RHICommandContext.Current;
    using var scope47=commands.EnterBoundary(new("PackedClearAndDraw",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope47.Run(()=>{
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0)], new(DepthLoad: AttachmentLoad.Clear, ClearDepth: .75f,
                StencilLoad: AttachmentLoad.Clear, ClearStencil: 0x59)));
            commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings()); commands.DrawIndexed(geometry.Inputs, new(0, 3));
            Assert.False(GL.IsEnabled(EnableCap.DepthTest)); Assert.False(GL.IsEnabled(EnableCap.StencilTest));
            Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.Equal(uint.MaxValue, unchecked((uint)GL.GetInteger(GetPName.StencilWritemask)));
            Assert.Equal(uint.MaxValue, unchecked((uint)GL.GetInteger(GetPName.StencilBackWritemask)));
        });
}
        Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.Equal(0x35, GL.GetInteger(GetPName.StencilWritemask)); Assert.Equal(0x53, GL.GetInteger(GetPName.StencilBackWritemask));
        Assert.All(color.ReadPixels(), value => Assert.InRange(value, .1999f, .2001f));
        Assert.All(depth.ReadPixels(), value => Assert.InRange(value, .7499f, .7501f));
        Assert.All(depth.ReadStencilPixels(), value => Assert.Equal((byte)0x59, value));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A region clear establishes its own scissor before an unscissored prepared draw covers the entire target.</summary>
    [Fact]
    public void RegionClearThenDrawUsesIndependentStateAndRestoresDormantRectangle()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var pipeline = Pipeline(shader);
        using var geometry = Geometry();
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 8, Height = 8, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 8, 8), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.R32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((Enumerable.Repeat(.2f, 64).ToArray()).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(4, 4, PixelInternalFormat.R32f);
        shader.HzbDepth = source; shader.SrcMip = 0;
        GL.Disable(EnableCap.ScissorTest); GL.Scissor(2, 1, 1, 2); GL.DepthMask(false);
        StateCache.Current.InvalidateAll();
        {
    var commands=RHICommandContext.Current;
    using var scope48=commands.EnterBoundary(new("RegionClearAndDraw",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope48.Run(()=>{
#if DEBUG
            long before = StateCache.Current.FixedFunctionCalls;
#endif
            Assert.Throws<InvalidOperationException>(() => StateCache.Current.SetLineWidth(1));
#if DEBUG
            Assert.Equal(before, StateCache.Current.FixedFunctionCalls);
#endif
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear, Clear: ColorClearValue.Float(.9f, 0, 0, 1))], area: new(1, 1, 2, 2)));
            commands.EndRenderPass();
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0)])); commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings()); commands.DrawIndexed(geometry.Inputs, new(0, 3));
            Assert.False(GL.IsEnabled(EnableCap.ScissorTest));
            int[] rectangle = new int[4]; GL.GetInteger(GetPName.ScissorBox, rectangle);
            Assert.Equal(new[] { 1, 1, 2, 2 }, rectangle);
            Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
        });
}
        Assert.False(GL.IsEnabled(EnableCap.ScissorTest));
        int[] restored = new int[4]; GL.GetInteger(GetPName.ScissorBox, restored);
        Assert.Equal(new[] { 2, 1, 1, 2 }, restored);
        Assert.All(target[0].ReadPixels(), value => Assert.InRange(value, .1999f, .2001f));
        Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Submission ownership rejects nesting and restores authority after a declared external operation.</summary>
    [Fact]
    public void PassNestingAndExternalBoundariesAreExplicit()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var pipeline = Pipeline(shader);
        using var geometry = Geometry();
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 2), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.R32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .2f, .4f, .6f, .8f }).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        shader.HzbDepth = source; shader.SrcMip = 0;
        RHICommandContext? escaped = null;
        {
    var commands=RHICommandContext.Current;
    using var scope49=commands.EnterBoundary(new("SubmissionBoundary",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope49.Run(()=>{
            escaped = commands;
            Assert.ThrowsAny<Exception>(() => commands.DrawIndexed(geometry.Inputs, new(0, 3)));
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0)]));
            Assert.ThrowsAny<Exception>(() => commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0)])));
            Assert.Throws<InvalidOperationException>(() => commands.EnterBoundary(new("Tests.Nested", true)));
            commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings()); commands.DrawIndexed(geometry.Inputs, new(0, 3)); commands.EndRenderPass();
            Assert.ThrowsAny<Exception>(() => commands.DrawIndexed(geometry.Inputs, new(0, 3)));
        });
}
        Assert.Throws<InvalidOperationException>(() => escaped!.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Explicit recapture changes shader inputs while unchanged packets reuse executed state and prepared metadata.</summary>
    [Fact]
    public void SamePipelinePublishesChangedInputsAndSuppressesStableState()
    {
        EnsureContextValid();using var programs=new ComponentShaderPrograms();
        var shader=programs.Create<RasterDepthReductionShaderProgram>();
        using var pipeline=Pipeline(shader);using var geometry=Geometry();
        using var first=RHICommandContext.Current.CreateDynamicTexture2D(new(){Dimension=TextureDimension.Two,
            Width=2,Height=2,Format=TextureFormat.R32f,MipLevels=1,Storage=ResourceStorageMode.Mutable}).RequireResource();
        using var second=RHICommandContext.Current.CreateDynamicTexture2D(new(){Dimension=TextureDimension.Two,
            Width=2,Height=2,Format=TextureFormat.R32f,MipLevels=1,Storage=ResourceStorageMode.Mutable}).RequireResource();
        first.UploadDataImmediate(new float[]{.2f,.4f,.6f,.8f});second.UploadDataImmediate(new float[]{.7f,.8f,.9f,1});
        using var target=CreateRenderTarget(1,1,PixelInternalFormat.R32f);
        shader.HzbDepth=first;shader.SrcMip=0;
        var commands=RHICommandContext.Current;
        using var boundary=commands.EnterBoundary(new("Tests.ChangedBindings",true,graphics:[pipeline],operations:BoundaryOperations.RenderPasses));
        boundary.Run(()=>
        {
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target));commands.SetPipeline(pipeline);
            commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.BindResources(shader.CaptureBindings());
            commands.DrawIndexed(geometry.Inputs,new(0,3));
#if DEBUG
            long calls=StateCache.Current.FixedFunctionCalls,queries=StateCache.Current.BoundaryQueries;
#endif
            commands.DrawIndexed(geometry.Inputs,new(0,3,InstanceCount:2));
#if DEBUG
            Assert.Equal(calls,StateCache.Current.FixedFunctionCalls);Assert.Equal(queries,StateCache.Current.BoundaryQueries);
#endif
            shader.HzbDepth=second;commands.BindResources(shader.CaptureBindings());commands.DrawIndexed(geometry.Inputs,new(0,3));
            commands.EndRenderPass();
        });
        Assert.InRange(target[0].ReadPixels()[0],.6999f,.7001f);
    }

    /// <summary>Missing dynamics, missing resources, incompatible layouts and invalid ranges never submit a draw.</summary>
    [Theory]
    [InlineData("dynamics")]
    [InlineData("resources")]
    [InlineData("layout")]
    [InlineData("range")]
    [InlineData("negative-first")]
    [InlineData("retired")]
    [InlineData("target")]
    public void InvalidDrawContractEmitsNoDraw(string failure)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var pipeline = Pipeline(shader);
        using var geometry = Geometry(failure == "layout" ? 2 : 3, failure == "topology" ? PrimitiveTopology.Lines : PrimitiveTopology.Triangles);
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 2), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.R32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .2f, .4f, .6f, .8f }).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(1, 1, failure == "target" ? PixelInternalFormat.Rgba16f : PixelInternalFormat.R32f);
        if (failure != "resources") shader.HzbDepth = source;
        shader.SrcMip = 0;
#if DEBUG
        long before = StateCache.Current.DrawSubmissions;
#endif
        {
    var commands=RHICommandContext.Current;
    using var scope52=commands.EnterBoundary(new("SubmissionFailure",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope52.Run(()=>{
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0)])); commands.SetPipeline(pipeline);
            if (failure != "dynamics") commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            if (failure != "resources") commands.BindResources(shader.CaptureBindings());
            if (failure == "retired") geometry.Dispose();
            Assert.ThrowsAny<Exception>(() => commands.DrawIndexed(geometry.Inputs, new(failure == "negative-first" ? -1 : 0, failure == "range" ? 4 : 3)));
#if DEBUG
            Assert.Equal(before, StateCache.Current.DrawSubmissions);
#endif
        });
}
#if DEBUG
        Assert.Equal(before, StateCache.Current.DrawSubmissions);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Scope disposal restores engine shader and drawing ownership when user work throws.</summary>
    [Fact]
    public void ExceptionsRestoreOwnershipAndAllowAnotherSubmission()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var pipeline = Pipeline(shader);
        using var geometry = Geometry();
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 2), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.R32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .2f, .4f, .6f, .8f }).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        shader.HzbDepth = source; shader.SrcMip = 0;
        var previous = programs.Create<RasterDepthReductionShaderProgram>();
        previous.HzbDepth = source; previous.SrcMip = 0;
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLProgramSelection.Select(StateCache.Current, previous.ProgramId);
        int owner = previous.ProgramId;
        int framebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport);
        var context = RHICommandContext.Current;
        Assert.Throws<ArithmeticException>(() =>
        {
            using var boundary = context.EnterBoundary(new("Tests.SubmissionThrow", true, graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
            boundary.Run(() =>
            {
                context.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target));
                context.SetPipeline(pipeline); context.SetDynamicState(new() { Viewport = context.PassViewport });
                context.BindResources(shader.CaptureBindings()); context.DrawIndexed(geometry.Inputs, new(0, 3));
                throw new ArithmeticException("Fixture failure after submission.");
            });
        });
        Assert.Equal(owner, GL.GetInteger(GetPName.CurrentProgram));
        Assert.Equal(framebuffer, GL.GetInteger(GetPName.DrawFramebufferBinding));
        int[] restored = new int[4]; GL.GetInteger(GetPName.Viewport, restored); Assert.Equal(viewport, restored);
        {
    var commands=RHICommandContext.Current;
    using var scope53=commands.EnterBoundary(new("SubmissionRetry",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope53.Run(()=>{
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0)]));
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings()); commands.DrawIndexed(geometry.Inputs, new(0, 3));
        });
}
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Declares the exact production HZB vertex and single floating-point output contract.</summary>
    private static RHIGraphicsPipeline Pipeline(RasterDepthReductionShaderProgram shader) =>
        RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader, Layout(3), new([new(TextureFormat.R32f)]), DynamicPipelineState.Viewport)).RequireResource();

    /// <summary>Declares one tightly packed floating position stream.</summary>
    private static RHIVertexLayout Layout(int components) =>
        new([new(0, components, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, components * sizeof(float))]);

    /// <summary>Owns a fullscreen triangle with retained CPU index bounds and explicit vertex metadata.</summary>
    private static ManagedGraphicsGeometry Geometry(int components = 3, PrimitiveTopology topology = PrimitiveTopology.Triangles)
    {
        float[] values = components == 3 ? [-1, -1, 0, 3, -1, 0, -1, 3, 0] : [-1, -1, 3, -1, -1, 3];
        return ManagedGraphicsGeometry.Create(Layout(components), topology,
            [new(0, MemoryMarshal.AsBytes(values.AsSpan()).ToArray())], [0, 1, 2]);
    }
    #endregion
}
