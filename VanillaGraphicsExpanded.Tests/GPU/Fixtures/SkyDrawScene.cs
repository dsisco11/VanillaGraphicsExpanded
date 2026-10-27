using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns matched two-image surrogate sky submissions with four linked programs and independently authored inherited state.</summary>
internal sealed class SkyDrawScene : IDisposable
{
    private readonly List<IDisposable> owned = new();
    private readonly DebugTextureShaderProgram[] shaders;
    private readonly DebugTextureShaderProgram native;
    private readonly DynamicTexture2D[,] inputs = new DynamicTexture2D[4, 2];
    private readonly DynamicTexture2D[] outputs;
    private readonly DynamicTexture2D sentinel, clearImage;
    private readonly GpuSampler sampler;
    private readonly GpuFramebuffer target, incoming, read, auxiliary;
    private readonly RHIGraphicsPipeline[] pipelines;
    private readonly ArrayGraphicsGeometry geometry;
    private readonly RHIRenderPassDescription[,] passes = new RHIRenderPassDescription[4, 2];
    private readonly RHIRenderPassDescription clearPass;
    private static readonly DrawBuffersEnum[] NativeRouting =
        [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1];
    internal readonly List<string> Events = new();
    internal readonly List<int> Rendered = new();
    internal readonly HashSet<RHICommandContext> Contexts = new();
    internal SkyDrawTestPass[] Passes { get; }
    internal int FailAt = -1;

    #region Public API
    #region Setup
    /// <summary>Uses the same offline production debug executable in four distinct linked objects; blending supplies ordered composition.</summary>
    internal SkyDrawScene()
    {
        var programs = Own(new ComponentShaderPrograms());
        shaders = Enumerable.Range(0, 4).Select(_ => programs.Create<DebugTextureShaderProgram>()).ToArray();
        native = programs.Create<DebugTextureShaderProgram>();
        float[][] colors = [[.125f, .25f, .5f, 1], [.25f, .125f, 0, .25f],
            [.5f, .25f, .125f, .5f], [.0625f, .125f, .25f, .5f]];
        for (int index = 0; index < 4; index++)
            for (int layer = 0; layer < 2; layer++)
            {
                var color = colors[index].Select((value, channel) => layer == 1 && channel < 3 ? value * .5f : value).ToArray();
                inputs[index, layer] = Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((color).AsSpan()).ToArray())]).RequireResource());
                shaders[index].Scene = inputs[index, 0];
            }
        sentinel = Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 0f, 0f, 0f, 1f }).AsSpan()).ToArray())]).RequireResource());
        sampler = Own(RHICommandContext.Current.CreateSampler(new RHISamplerDescription {  }).RequireResource());
        native.Scene = sentinel;
        outputs = Enumerable.Range(0, 2).Select(_ => Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 3), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource())).ToArray();
        target = Own(DynamicGpuFramebuffer.CreateMRT(outputs)!);
        incoming = CreateTarget();
        read = CreateTarget();
        clearImage = Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
        auxiliary = Own(DynamicGpuFramebuffer.CreateSingle(clearImage)!);
        clearPass = FramebufferTestSetup.DescribeAttachments(auxiliary, [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(.125f, .25f, .5f, 1))]);

        var layout = new RHIVertexLayout([new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 20),
            new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 12, 20)]);
        var vertices = Own(RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource());
        vertices.UploadData(new float[] { -1, -1, 0, 0, 0, 3, -1, 0, 2, 0, -1, 3, 0, 0, 2 });
        geometry = Own(new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo> { { 0, vertices } }));
        
        pipelines = new RHIGraphicsPipeline[4];
        for (int index = 0; index < 4; index++)
        {
            var blend = index == 0 ? new ColorBlendDesc() : new ColorBlendDesc
            {
                Enabled = true,
                DestinationRgb = index == 3 ? BlendFactor.SourceAlpha : BlendFactor.One,
                DestinationAlpha = index == 3 ? BlendFactor.SourceAlpha : BlendFactor.One
            };
            pipelines[index] = Own(RHICommandContext.Current.CreateGraphicsPipeline(new(shaders[index].Shader, layout, new([new(TextureFormat.Rgba32f)]),
                    DynamicPipelineState.Viewport, blending: [blend])).RequireResource());
            for (int layer = 0; layer < 2; layer++)
                passes[index, layer] = FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex: layer, Load: index == 0 ? AttachmentLoad.Clear : AttachmentLoad.Preserve,
                    Clear: index == 0 ? ColorClearValue.Float(0, 0, 0, 0) : null)]);
        }
        Passes = Enumerable.Range(0, 4).Select(index => new SkyDrawTestPass(index,
            [pipelines[index]], commands => Draw(commands, index), Events)).ToArray();
    }

    /// <summary>Authors the same hostile native baseline before each matched submission, excluding setup from captures.</summary>
    internal void Initialize()
    {
        Events.Clear();
        Rendered.Clear();
        Contexts.Clear();
        foreach (var image in outputs) image.UploadDataImmediate(new float[4 * 3 * 4]);
        target.Bind();
        OpenGLFramebufferRouting.SetDrawBuffers(OpenGlStateCache.Current, NativeRouting.Length, NativeRouting);
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, native.ProgramId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, incoming.FboId);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read.FboId);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, sentinel.TextureId);
        GL.BindSampler(0, sampler.SamplerId);
        GL.ActiveTexture(TextureUnit.Texture5);
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Never);
        GL.DepthMask(true);
        GL.Enable(EnableCap.ScissorTest);
        GL.Scissor(1, 1, 1, 1);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.Zero, BlendingFactor.One);
        GL.Viewport(1, 1, 2, 1);
        OpenGlStateCache.Current.InvalidateAll();
        OpenGlStateCache.Current.NotifyProgramBound(native.ProgramId);
    }
    #endregion

    #region Submission
    /// <summary>Submits separately prepared work through independent graphics boundaries for a matched baseline.</summary>
    internal void DrawStandalone()
    {
        foreach (var source in Passes)
            if (source.PrepareDraw() is { } work)
                {
    var commands=RHICommandContext.Current;
    using var scope42=commands.EnterBoundary(new(work.Name,true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>(work.Pipelines,value=>value),operations:BoundaryOperations.RenderPasses));
    scope42.Run(()=>work.Execute(commands));
}
    }

    /// <summary>Prepares all optional passes before entering the one production shared graphics boundary.</summary>
    internal void DrawShared() => SkyDraw.Run(Passes.Select(source => source.PrepareDraw()).OfType<SkyDraw>().ToArray());
    /// <summary>Exercises clear-only work and a different destination inside the same declared context.</summary>
    internal void DrawClear(RHICommandContext commands)
    {
        Contexts.Add(commands);
        commands.BeginRenderPass(clearPass);
        commands.EndRenderPass();
    }

    /// <summary>Retires only the executable to exercise per-submission preflight isolation.</summary>
    internal void InvalidateProgram(int index) => shaders[index].Dispose();
    #endregion

    #region Inspection and disposal
    /// <summary>Verifies native draw/read/program/resource/fixed-state handoff before any readback disturbs it.</summary>
    internal void AssertRestored()
    {
        Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
        Assert.Equal(native.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        Assert.True(OpenGlStateCache.Current.TryGetCachedCurrentProgram(out int restoredProgram));
        Assert.Equal(native.ProgramId, restoredProgram);
        Assert.Equal((int)TextureUnit.Texture5, GL.GetInteger(GetPName.ActiveTexture));
        GL.ActiveTexture(TextureUnit.Texture0);
        Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
        Assert.Equal(sampler.SamplerId, GL.GetInteger(GetPName.SamplerBinding));
        GL.ActiveTexture(TextureUnit.Texture5);
        Assert.True(GL.IsEnabled(EnableCap.DepthTest));
        Assert.Equal((int)DepthFunction.Never, GL.GetInteger(GetPName.DepthFunc));
        Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.True(GL.IsEnabled(EnableCap.ScissorTest));
        Assert.True(GL.IsEnabled(EnableCap.Blend));
        Assert.Equal((int)BlendingFactorSrc.Zero, GL.GetInteger(GetPName.BlendSrcRgb));
        int[] viewport = new int[4];
        GL.GetInteger(GetPName.Viewport, viewport);
        Assert.Equal(new[] { 1, 1, 2, 1 }, viewport);
        // Inspect local routing without introducing an owner write or changing its inherited binding.
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, target.FboId);
        for (int slot = 0; slot < NativeRouting.Length; slot++)
            Assert.Equal((int)NativeRouting[slot], GL.GetInteger(GetPName.DrawBuffer0 + slot));
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, incoming.FboId);
        OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferBindings);
    }

    /// <summary>Reads both independently routed output images only after handoff/capture checks.</summary>
    internal float[][] ReadOutput() => outputs.Select(image => image.ReadPixels()).ToArray();

    /// <summary>Reads the clear-only destination independently of its subsequent shader target.</summary>
    internal float[] ReadClear() => clearImage.ReadPixels();

    /// <summary>Checks that a failed active pass retained no mutable target or image authority.</summary>
    internal void AssertMutable() => Assert.True(outputs[0].Resize(5, 4));

    /// <summary>Releases resources after coordinator/engine state has returned to the fixture's native program.</summary>
    public void Dispose()
    {
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
        for (int index = owned.Count - 1; index >= 0; index--) owned[index].Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Writes sky and glow with distinct sources, and optionally interrupts an already-active pass.</summary>
    private void Draw(RHICommandContext commands, int index)
    {
        Contexts.Add(commands);
        Events.Add("draw:sky" + index);
        for (int layer = 0; layer < 2; layer++)
        {
            commands.BeginRenderPass(passes[index, layer]);
            if (FailAt == index) throw new ArithmeticException("Injected sky draw failure.");
            shaders[index].Scene = inputs[index, layer];
            commands.SetPipeline(pipelines[index]);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shaders[index].CaptureBindings());
            commands.Draw(geometry.Inputs, new(0, 3));
            commands.EndRenderPass();
        }
        Rendered.Add(index);
    }

    /// <summary>Tracks fixture ownership in dependency construction order.</summary>
    private T Own<T>(T value) where T : IDisposable
    {
        owned.Add(value);
        return value;
    }

    /// <summary>Creates a separately owned inherited target without borrowing any output image.</summary>
    private GpuFramebuffer CreateTarget()
    {
        var image = Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
        return Own(DynamicGpuFramebuffer.CreateSingle(image)!);
    }
    #endregion
}
