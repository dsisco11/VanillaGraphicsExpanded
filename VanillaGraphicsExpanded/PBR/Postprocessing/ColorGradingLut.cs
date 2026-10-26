using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.Common;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Caches complete display transforms independently of scene exposure, size and lighting.</summary>
internal sealed class ColorGradingLut : IDisposable
{
    internal const int Size=64;
    private ColorGradingProgram? generator;
    private ColorGradingInputs? inputs;
    private DynamicTexture3D? completed,spare;
    private Vector4? key;
    private (nint Handle,long Generation) context;
    /// <summary>Counts successful replacements without exposing storage ownership.</summary>
    internal long Generation { get; private set; }
    #region Public API
    /// <summary>Returns matching complete storage, rebuilding only for changed grading or an explicit invalidation.</summary>
    internal DynamicTexture3D Prepare(ICoreAPI api,Vector4 controls)
    {
        if(!float.IsFinite(controls.X)||!float.IsFinite(controls.Y)||!float.IsFinite(controls.Z)||!float.IsFinite(controls.W))
            throw new ArgumentException("Display grading requires finite controls.");
        var current=RenderContextRegistry.Current();
        if(current==default) throw new InvalidOperationException("Display lookup requires a live rendering context.");
        if(context!=default&&context!=current) Dispose();
        context=current;
        if(generator is null)
        {
            if(!GpuComputePipeline.TryCreateFromAssets(api,ColorGradingProgram.Contract.Identity,
                out var pipeline,out _,out string log,preferSpirv:true))
                throw new InvalidOperationException("Display lookup executable unavailable: "+log);
            generator=new(pipeline!);
        }
        if(!generator.EnsureReady()) throw new InvalidOperationException(generator.PreparationLog);
        if(key==controls&&completed is { IsValid:true }) return completed;
        inputs??=new(); inputs.Capture(controls);
        spare??=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.Three, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, Size), Height = System.Math.Max(1, Size), Depth = System.Math.Max(1, Size), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        spare.SetTexWrap(RHISamplerAddress.ClampToEdge,RHISamplerAddress.ClampToEdge,RHISamplerAddress.ClampToEdge);
        generator.GradingInputs=inputs;
        generator.GradedOutput=new(spare,TextureAccess.WriteOnly,Layered:true,Format:SizedInternalFormat.Rgba16f);
        generator.Dispatch(Size/4,Size/4,Size/4);
        // The following final draw is ordered after generation; no fence polling or CPU readback is needed.
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.TextureFetchBarrierBit);
        (completed,spare)=(spare,completed); key=controls; Generation++;
        return completed;
    }
    /// <summary>Rebakes after shader reload while retaining completed storage until replacement submission succeeds.</summary>
    internal void Invalidate() => key=null;
    /// <summary>Retires generator and persistent images through their existing owning-context lifecycles.</summary>
    public void Dispose()
    {
        key=null; context=default;
        RenderingCleanup.Run(
            ()=>{generator?.Dispose();generator=null;},()=>{inputs?.Dispose();inputs=null;},
            ()=>{completed?.Dispose();completed=null;},()=>{spare?.Dispose();spare=null;});
    }
    #endregion
}
