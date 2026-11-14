using System;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Readiness;

namespace VanillaGraphicsExpanded.PBR.Shadows;

/// <summary>Offers one borrowed shadow publication and safe disabled resources through ordinary consumer binding contracts.</summary>
internal static class SurfaceShadowBindings
{
    private static Func<ShadowFramePublication?>? provider;
    private static SurfaceShadowInputSet? currentInputs;
    private static GpuShaderStorageBuffer? empty;
    private static Texture3D? depth;
    private static (nint Handle,long Generation) context;
    private static ShadowPageSettings policy=new();
    private static RHISampledTextureBindings? sampled;

    #region Public API
    #region Publications
    /// <summary>Borrowing never extends the gate interval, source identity or physical-page lifetime.</summary>
    internal static ShadowFramePublication? CurrentPublication=>provider?.Invoke() is {IsCurrent:true} current &&
        ReferenceEquals(current.Lights,VgeLightsRenderer.ShadowInputs)?current:null;

    /// <summary>Installs a lifetime-owned provider; reconfiguration withdraws through the gate before new bindings can be captured.</summary>
    internal static void Attach(Func<ShadowFramePublication?>? source,ShadowPageSettings settings)
    {provider=source;policy=settings.Freeze();currentInputs?.UpdatePolicy(policy);}

    /// <summary>Updates and returns the shared current-frame input set without replacing its packed-buffer owner.</summary>
    internal static SurfaceShadowInputSet CurrentInputs
    {
        get
        {
            Ensure();
            var publication=CurrentPublication;
            var lights=publication?.Lights??VgeLightsRenderer.ShadowInputs;
            if(currentInputs is null)
                currentInputs=new(lights,publication,AtmosphereModSystem.Lighting,policy);
            else
                currentInputs.Update(lights,publication,AtmosphereModSystem.Lighting);
            return currentInputs;
        }
    }
    /// <summary>Provides initialized storage for bindings whose shadow publication is unavailable.</summary>
    internal static GpuShaderStorageBuffer DisabledBuffer{get{Ensure();return empty!;}}
    /// <summary>Provides an initialized one-layer integer array for disabled shadow sampling.</summary>
    internal static GpuTextureRef DisabledDepth{get{Ensure();return depth!;}}
    #endregion

    #region Native program binding and lifetime
    /// <summary>Rebinds the complete shared set after native state mutation without taking over the engine's drawing pipeline.</summary>
    internal static void BindNative(SurfaceShadowInputSet inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var commands=RHICommandContext.Current;
        if(!commands.BindUniformSnapshot(18,inputs.Parameters.Capture()))throw new InvalidOperationException("Surface light snapshot publication failed.");
        inputs.Requests.BindBase(0);inputs.Entries.BindBase(1);inputs.Readiness.BindBase(2);inputs.Diagnostics.BindBase(5);
        var selected=inputs.Depth;
        // Resource identity may change at a gate even when the native program did not.
        if(sampled is null || sampled.RequiresRefresh || sampled.Inputs[0].Texture!=selected)
            sampled=[new(33,selected,GpuSamplers.NearestClamp,TextureDimension.TwoArray)];
        commands.BindSampledTextures(sampled);
    }

    /// <summary>Withdraws consumer access before releasing only owned sentinel resources.</summary>
    internal static void Retire()
    {
        provider=null;sampled=null;
        currentInputs?.Dispose();currentInputs=null;empty?.Dispose();empty=null;depth?.Dispose();depth=null;context=default;
        RenderContextRegistry.Retiring-=OnContextRetiring;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Allocates a tiny disabled set once per context; occupancy never changes the shadow pool size.</summary>
    private static void Ensure()
    {
        var current=RenderContextRegistry.Current();
        if(empty is not null && current==context)return;
        // Context teardown owns the prior namespace. Never carry handles into another one.
        var requestedProvider=provider;
        Retire();provider=requestedProvider;context=current;
        var caps=GpuSupport.Graphics;
        if(caps.MaxCombinedTextureImageUnits<=33 || caps.MaxTextureImageUnits<16 ||
            caps.MaxFragmentShaderStorageBlocks<5 || caps.MaxShaderStorageBufferBindings<=5 || caps.MaxUniformBufferBindings<=18)
            throw new InvalidOperationException("Shared surface lighting resource interface is unsupported.");
        var commands=RHICommandContext.Current;
        try
        {
            empty=commands.CreateShaderStorageBuffer(new RHIBufferDescription{Role=BufferRole.ShaderStorage,Mode=ResourceStorageMode.Mutable,
                Capacity=16,Usage=BufferUsage.DynamicDraw,Label="SurfaceShadows.Disabled"}).RequireResource();
            empty.UploadSubData<uint>(new uint[4],0);
            depth=commands.CreateTexture3D(new RHITextureDescription{Dimension=TextureDimension.TwoArray,Format=TextureFormat.R32ui,
                Width=1,Height=1,Depth=1,MipLevels=1,Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest,
                BaseLevel=0,MaxLevel=0,Label="SurfaceShadows.DisabledDepth"}).RequireResource();
            commands.ClearTexture(depth,new(new(0),0,0,0,1,1,1),
                new(PixelChannelLayout.RedInteger,PixelComponentType.UnsignedInt),BitConverter.GetBytes(0x3f800000u));
            RenderContextRegistry.Retiring+=OnContextRetiring;
        }
        catch{Retire();throw;}
    }

    /// <summary>Ends owned sentinels before their originating context is replaced.</summary>
    private static void OnContextRetiring((nint Handle,long Generation) retiring){if(retiring==context)Retire();}
    #endregion
}
