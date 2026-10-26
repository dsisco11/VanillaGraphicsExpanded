using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Common;
namespace VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
/// <summary>Owns bounded current-frame log/base images under the existing postprocess lifecycle.</summary>
internal sealed class LocalExposureRenderer : IDisposable
{
    private GpuResourceCollection resources=new();
    private LocalExposureReduceProgram? reduce;
    private LocalExposureFilterProgram? filter;
    private LocalExposureInputs? inputs;
    private DynamicTexture2D? logarithms,baseImage;
    private GpuShaderStorageBuffer? lighting;
    private (nint Handle,long Generation) context;
    private bool published;
    #region Public API
    /// <summary>Counts storage replacements independently of current-frame dispatches.</summary>
    internal long Generation { get; private set; }
    /// <summary>Supplies alternate-view frozen storage without owning that publication.</summary>
    internal GpuShaderStorageBuffer? FrameLightingInputs { set=>lighting=value; }
    /// <summary>Exposes only a completely submitted current-frame reduction.</summary>
    internal DynamicTexture2D Logarithms=>published ? logarithms! : throw new InvalidOperationException("Local luminance is not published.");
    /// <summary>Exposes only a completely submitted current-frame spatial base.</summary>
    internal DynamicTexture2D BaseTexture=>published ? baseImage! : throw new InvalidOperationException("Local exposure base is not published.");
    /// <summary>Reduces and filters the complete unexposed scene without CPU statistics or image history.</summary>
    internal DynamicTexture2D Render(ICoreAPI api,GpuTexture scene,LocalExposureParameters settings)
    {
        published=false;
        if(!settings.Enabled) throw new InvalidOperationException("Disabled local exposure must not allocate or dispatch.");
        Validate(settings);
        var current=RenderContextRegistry.Current();
        if(current==default) throw new InvalidOperationException("Local exposure requires a live rendering context.");
        if(current!=context) { Dispose(); context=current; }
        GpuSupport.EnsureCurrentContext();
        var capabilities=GpuSupport.Graphics;
        if(!capabilities.SupportsArbComputeShader || !capabilities.SupportsArbShaderImageLoadStore
            || capabilities.MaxComputeWorkGroupInvocations<64 || capabilities.MaxComputeWorkGroupSize.Length<2
            || capabilities.MaxComputeWorkGroupSize[0]<8 || capabilities.MaxComputeWorkGroupSize[1]<8)
            throw new InvalidOperationException("Local exposure requires an admitted 8x8 compute footprint.");
        int width=Math.Min(scene.Width,settings.Width), height=Math.Min(scene.Height,settings.Height);
        if(width<=0 || height<=0) throw new ArgumentException("Local exposure requires complete scene storage.",nameof(scene));
        Prepare(api,width,height);
        reduce!.FrameLightingInputs=lighting;
        reduce.SceneRadiance=scene;
        reduce.LogOutput=new(logarithms!,TextureAccess.WriteOnly,Format:SizedInternalFormat.Rg32f);
        inputs!.Capture(settings.RangeWidth);
        filter!.Inputs=inputs;
        filter.LogImage=logarithms!;
        filter.BaseOutput=new(baseImage!,TextureAccess.WriteOnly,Format:SizedInternalFormat.Rg32f);
        var borrowed=EngineBoundaryResources.From(reduce.ProgramLayout.BinaryInterface!.PreparedBindings)
            .Union(EngineBoundaryResources.From(filter.ProgramLayout.BinaryInterface!.PreparedBindings));
        if(!EngineBoundaryExecution.TryRun(new("Postprocess.LocalExposure"),borrowed,_=>
        {
            reduce.Dispatch((width+7)/8,(height+7)/8);
            RHICommandContext.Current.Barrier(new(RHIAccess.Image|RHIAccess.TextureFetch));
            filter.Dispatch((width+7)/8,(height+7)/8);
            RHICommandContext.Current.Barrier(new(RHIAccess.Image|RHIAccess.TextureFetch));
        })) throw new InvalidOperationException("Local exposure compute boundary was rejected.");
        published=true;
        return baseImage!;
    }
    /// <summary>Withdraws publication before retiring executable inputs and their textures.</summary>
    public void Dispose()
    {
        published=false;
        // Disabled frames repeatedly retire this optional owner. Once empty,
        // retain its empty collection rather than allocating replacement sets.
        if(context==default && reduce is null && filter is null && inputs is null
            && logarithms is null && baseImage is null) return;
        context=default;
        RenderingCleanup.Run(
            ()=>{reduce?.Dispose(); reduce=null;},
            ()=>{filter?.Dispose(); filter=null;},
            ()=>{inputs?.Dispose(); inputs=null;},
            ()=>{resources.Dispose(); resources=new(); logarithms=baseImage=null;});
    }
    #endregion
    #region Private
    /// <summary>Reuses stable storage and retires old-size resources before declaring replacements.</summary>
    private void Prepare(ICoreAPI api,int width,int height)
    {
        if(logarithms is not null && (logarithms.Width!=width || logarithms.Height!=height))
        {
            var current=context; Dispose(); context=current;
        }
        try
        {
            if(reduce is null)
            {
                if(!GpuComputePipeline.TryCreateFromAssets(api,LocalExposureReduceProgram.Contract.Identity,
                    out var pipeline,out _,out string log,preferSpirv:true))
                    throw new InvalidOperationException(log);
                reduce=new LocalExposureReduceProgram(pipeline!);
            }
            if(filter is null)
            {
                if(!GpuComputePipeline.TryCreateFromAssets(api,LocalExposureFilterProgram.Contract.Identity,
                    out var pipeline,out _,out string log,preferSpirv:true))
                    throw new InvalidOperationException(log);
                filter=new LocalExposureFilterProgram(pipeline!);
            }
            if(!reduce.EnsureReady() || !filter.EnsureReady()) throw new InvalidOperationException("Local exposure executable unavailable.");
            inputs??=new LocalExposureInputs();
            if(logarithms is null)
            {
                logarithms=resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rg32f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "LocalExposure.Log" }).RequireResource());
                baseImage=resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rg32f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "LocalExposure.Base" }).RequireResource());
                Generation++;
            }
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Admits explicit candidate parameters before any allocation or GPU arithmetic.</summary>
    private static void Validate(LocalExposureParameters settings)
    {
        if(settings.Width<1 || settings.Width>512 || settings.Height<1 || settings.Height>512
            || !float.IsFinite(settings.RangeWidth) || settings.RangeWidth<=0 || settings.RangeWidth>8
            || !InRange(settings.ShadowStrength,1) || !InRange(settings.HighlightStrength,1)
            || !InRange(settings.ShadowLimit,8) || !InRange(settings.HighlightLimit,8))
            throw new ArgumentOutOfRangeException(nameof(settings));
    }
    /// <summary>Checks a finite nonnegative candidate coefficient against its declared numerical domain.</summary>
    private static bool InRange(float value,float maximum)=>float.IsFinite(value)&&value>=0&&value<=maximum;
    #endregion
}
