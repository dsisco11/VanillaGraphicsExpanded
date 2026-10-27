using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks compute publication against actual indexed resources and nested graphics ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ComputeInputSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>A missing required counter fails closed and retained inputs remain available for a corrected retry.</summary>
    [Fact]
    public void RequiredCounterFailureCanRetryRetainedInputs()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var usage = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var counter = new ComponentAtomicCounters(23, 3);
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var loaded, out string log), log);
        using var shader = loaded!;
        shader.PatchIdG = texture;
        shader.BindPageUsageStampImage(usage);
        var commands=RHICommandContext.Current;commands.SetPipeline(shader.Pipeline);
        int previous=GL.GetInteger(GetPName.CurrentProgram);
        Assert.Throws<InvalidOperationException>(()=>commands.BindResources(shader.CaptureBindings()));
        Assert.Equal(previous,GL.GetInteger(GetPName.CurrentProgram));
        shader.BindDebugCounters(counter.Buffer);
        commands.BindResources(shader.CaptureBindings());AssertBound(shader.ProgramId,texture.TextureId,counter.Buffer.BufferId);
        Assert.Equal(23u, counter.Read());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Native compute execution guards the original owner and rejects recursive dispatch before effects.</summary>
    [Fact]
    public void ExecutingComputeRejectsRetirementAndReentry()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var assets = new BinaryShaderApiFixture();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var usage = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var counters = new ComponentAtomicCounters(0, 3);
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var loaded, out string log), log);
        using var outer = loaded!;
        outer.PatchIdG = texture;
        outer.BindPageUsageStampImage(usage);
        outer.BindDebugCounters(counters.Buffer);
        var commands=RHICommandContext.Current;
        var harmony=new Harmony("Tests.ComputeOwnerDuringDispatch");
        bool observed=false;
        DuringDispatch=()=>
        {
            observed=true;
            Assert.Throws<InvalidOperationException>(outer.Dispose);
            Assert.Throws<InvalidOperationException>(()=>commands.Dispatch(new(0)));
        };
        try
        {
            harmony.Patch(((Action<int,int,int>)OpenTK.Graphics.OpenGL4.GL.DispatchCompute).Method,
                prefix:new HarmonyMethod(typeof(ComputeInputSubmissionTests),nameof(ObserveDispatch)));
            using var boundary=commands.EnterBoundary(new("Tests.ComputeOwner",true,compute:[outer.Pipeline]));
            boundary.Run(()=>{commands.SetPipeline(outer.Pipeline);commands.BindResources(outer.CaptureBindings());commands.Dispatch(new(0));});
            Assert.True(observed);Assert.True(outer.IsValid);
        }
        finally{DuringDispatch=null;harmony.UnpatchAll(harmony.Id);}
    }

    /// <summary>Explicit captured compute sets restore their retained inputs without resetting counters; boundary exit restores the native caller.</summary>
    [Fact]
    public void ExplicitComputeSwitchesPreserveCountersAndRestoreOriginalExecutable()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var assets = new BinaryShaderApiFixture();
        using var firstTexture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var secondTexture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var usage = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var firstCounters = new ComponentAtomicCounters(17, 3);
        using var secondCounters = new ComponentAtomicCounters(29, 3);
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var firstOwner, out string firstLog), firstLog);
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var secondOwner, out string secondLog), secondLog);
        using var first = firstOwner!;
        using var second = secondOwner!;
        first.PatchIdG = firstTexture;
        first.BindPageUsageStampImage(usage);
        first.BindDebugCounters(firstCounters.Buffer);
        second.PatchIdG = secondTexture;
        second.BindPageUsageStampImage(usage);
        second.BindDebugCounters(secondCounters.Buffer);
        using var graphicsDepth = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        var graphics = programs.Create<RasterDepthCopyShaderProgram>();
        graphics.PrimaryDepth = graphicsDepth;
        var commands=RHICommandContext.Current;
        OpenGLProgramSelection.Select(OpenGlStateCache.Current,graphics.ProgramId);
        using(var boundary=commands.EnterBoundary(new("Tests.ComputeSwitch",true,compute:[first.Pipeline,second.Pipeline])))
        boundary.Run(()=>
        {
            commands.SetPipeline(first.Pipeline);commands.BindResources(first.CaptureBindings());
            AssertBound(first.ProgramId,firstTexture.TextureId,firstCounters.Buffer.BufferId);
            commands.SetPipeline(second.Pipeline);commands.BindResources(second.CaptureBindings());
            AssertBound(second.ProgramId,secondTexture.TextureId,secondCounters.Buffer.BufferId);
            commands.SetPipeline(first.Pipeline);commands.BindResources(first.CaptureBindings());
            AssertBound(first.ProgramId,firstTexture.TextureId,firstCounters.Buffer.BufferId);
        });
        Assert.Equal(graphics.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
        OpenGLProgramSelection.Select(OpenGlStateCache.Current,0);
        Assert.Equal(17u, firstCounters.Read());
        Assert.Equal(29u, secondCounters.Read());
        Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Range publication exposes initialized records only and rejects an overrun before binding.</summary>
    [Fact]
    public void StoragePublicationPreservesExactOffsetAndLength()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var owner, out string log), log);
        using var shader = owner!;
        Assert.True(shader.EnsureReady());
        using var storage = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        int alignment = GL.GetInteger(GetPName.ShaderStorageBufferOffsetAlignment);
        storage.EnsureCapacity(alignment + 64, growExponentially: false);
        var range = new GpuStorageBufferBinding(storage, alignment, 32);
        using var source=RHICommandContext.Current.CreateTexture3D(new(){Dimension=TextureDimension.TwoArray,
            Width=1,Height=1,Depth=1,Format=TextureFormat.R32ui,MipLevels=1,Storage=ResourceStorageMode.Mutable}).RequireResource();
        using var counters=new ComponentAtomicCounters(0);
        shader.PageUsageStamp=source;shader.PageTableMip0=source;shader.BindRequestCounter(counters.Buffer);shader.BindRequestsSsbo(storage);
        var captured=shader.CaptureBindings();
        ulong identity=VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingEntry.Identity(VanillaGraphicsExpanded.Rendering.Contracts.ShaderBindingKind.StorageBlock,"VgePageRequests");
        var buffers=captured.Buffers.Select(value=>value.Identity==identity?value with{Offset=alignment,Length=32,Whole=false}:value).ToArray();
        var inputs=new RHIResourceBindings(captured.Shader,captured.Samplers,captured.Images,buffers,captured.Uniforms);
        var commands=RHICommandContext.Current;commands.SetPipeline(shader.Pipeline);commands.BindResources(inputs);
        GL.GetInteger(GetIndexedPName.ShaderStorageBufferBinding, 0, out int buffer);
        GL.GetInteger(GetIndexedPName.ShaderStorageBufferStart, 0, out int offset);
        GL.GetInteger(GetIndexedPName.ShaderStorageBufferSize, 0, out int length);
        Assert.Equal(storage.BufferId, buffer);
        Assert.Equal(alignment, offset);
        Assert.Equal(32, length);
        var invalid=new RHIResourceBindings(captured.Shader,captured.Samplers,captured.Images,
            buffers.Select(value=>value.Identity==identity?value with{Length=128}:value),captured.Uniforms);
        Assert.Throws<ArgumentOutOfRangeException>(()=>commands.BindResources(invalid));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    private static Action? DuringDispatch;
    /// <summary>Runs lifetime assertions while the original native dispatch still holds its resource guards.</summary>
    private static void ObserveDispatch()=>DuringDispatch?.Invoke();

    /// <summary>Observes executable and indexed bindings independently of the state cache.</summary>
    private static void AssertBound(int program, int texture, int counters)
    {
        Assert.Equal(program, GL.GetInteger(GetPName.CurrentProgram));
        GL.GetInteger((GetIndexedPName)All.AtomicCounterBufferBinding, 0, out int actualCounters);
        Assert.Equal(counters, actualCounters);
        int prior = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        Assert.Equal(texture, GL.GetInteger(GetPName.TextureBinding2D));
        GL.ActiveTexture((TextureUnit)prior);
    }
    #endregion
}
