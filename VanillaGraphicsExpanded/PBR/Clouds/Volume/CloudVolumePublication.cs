using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns an isolated drawable snapshot, updated only from fenced and validated solver results.</summary>
internal sealed class CloudVolumePublication(Func<CloudVolumePublishProgram> createProgram):IDisposable
{
    private GpuResourceCollection? resources;
    private DynamicTexture3D? density,direct,diffuse;
    private GpuShaderStorageBuffer? regions,occupancy;
    private CloudVolumePublishProgram? program;
    private CloudVolumeFields? source;
    internal CloudVolumeViewFields? Published {get;private set;}
    internal long Bytes=>Published is {} p?Payload(p.Domain):0;
    #region Public API
    /// <summary>Counts all drawable images and metadata separately from working/replacement storage.</summary>
    internal static long Payload(CloudVolumeDomain domain)=>checked(14L*domain.Voxels+68L*domain.Blocks);
    /// <summary>Admits the typed publication executable before candidate activation.</summary>
    internal void Prepare()
    {
        program??=createProgram();
        if(!program.EnsureReady())throw new InvalidOperationException(program.PreparationLog);
        if(GpuSupport.Graphics.MaxComputeShaderStorageBlocks<4||GpuSupport.Graphics.MaxShaderStorageBufferBindings<4||
            GpuSupport.Graphics.MaxComputeTextureImageUnits<3)throw new NotSupportedException("Cloud publication bindings unavailable.");
    }
    /// <summary>Copies changed blocks after completion, using full channels only for new domains, activation or encoding changes.</summary>
    internal void Publish(CloudVolumeFields fields,int newlyActive,long workingBytes)
    {
        Prepare();var d=fields.Domain;
        if(workingBytes+Payload(d)+640000>CloudVolumeDomain.PeakLimit)throw new NotSupportedException("Cloud publication exceeds peak storage allowance.");
        int full=Published is null||!ReferenceEquals(source,fields)||Published.Domain!=d?7:0;
        if(newlyActive!=0)full|=2;
        if(Published is {} old&&old.Encoding!=fields.Encoding)full|=4;
        if(density is null||density.Width!=d.Size)
        {
            ResetImages();resources=new();
            try
            {
                density=Volume(d,PixelInternalFormat.R16f);direct=Volume(d,PixelInternalFormat.Rg16f);diffuse=Volume(d,PixelInternalFormat.Rgba16f);
                regions=Buffer(d.Blocks*16);occupancy=Buffer(d.Blocks);
            }
            catch{ResetImages();throw;}
            full=7;
        }
        program!.Capture(d,full);program.Density=fields.Density;program.Direct=fields.Direct;program.Diffuse=fields.Diffuse;
        program.TargetDensity=new(density,TextureAccess.WriteOnly,Layered:true);
        program.TargetDirect=new(direct,TextureAccess.WriteOnly,Layered:true);
        program.TargetDiffuse=new(diffuse,TextureAccess.WriteOnly,Layered:true);
        program.Regions=fields.Regions;program.Occupancy=fields.Occupancy;program.TargetRegions=regions;program.TargetOccupancy=occupancy;
        var bindings=program.ProgramLayout.BinaryInterface?.PreparedBindings??throw new InvalidOperationException("Cloud publication bindings unavailable.");
        if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.Volume.Publish"),EngineBoundaryResources.From(bindings),_=>
            {Barrier();program.Dispatch(d.BlocksX,d.BlocksY,d.BlocksX);Barrier();}))
            throw new InvalidOperationException("Cloud publication boundary rejected.");
        GlDebug.ThrowIfErrors("Cloud volume publication");
        Published=new(d,fields.Encoding,fields.LightingBounds,checked((Published?.Revision??0)+1),density!,direct!,diffuse!,regions!,occupancy!);
        source=fields;
    }
    /// <summary>Withdraws drawable bindings and retires owned GPU state without waiting on the CPU.</summary>
    public void Dispose()=>RenderingCleanup.Run(ResetImages,()=>{program?.Dispose();program=null;});
    #endregion
    #region Private
    /// <summary>Orders prior drawable reads before overwriting images and exposes completed copies to subsequent draws.</summary>
    private static void Barrier()=>GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|
        MemoryBarrierFlags.TextureFetchBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
    /// <summary>Allocates hardware-filtered volume storage matching solver addressing.</summary>
    private DynamicTexture3D Volume(CloudVolumeDomain d,PixelInternalFormat format)
    {
        var texture=resources!.Own(DynamicTexture3D.Create(d.Size,66,d.Size,format,textureTarget:TextureTarget.Texture3D));
        texture.SetTexWrap(TextureWrapMode.Repeat,TextureWrapMode.ClampToEdge,TextureWrapMode.Repeat);return texture;
    }
    /// <summary>Allocates exact metadata through its ordinary GPU owner.</summary>
    private GpuShaderStorageBuffer Buffer(int words)
    {var result=resources!.Own(GpuShaderStorageBuffer.Create());result.UploadData(new uint[words]);return result;}
    /// <summary>Revokes the snapshot before releasing every acquired image and metadata buffer.</summary>
    private void ResetImages()
    {Published=null;source=null;resources?.Dispose();resources=null;density=direct=diffuse=null;regions=occupancy=null;}
    #endregion
}
