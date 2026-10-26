using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns an isolated drawable snapshot, updated only from fenced and validated solver results.</summary>
internal sealed class CloudVolumePublication(Func<int,PackedUniformBuffer,CloudVolumePublishProgram> createProgram):IDisposable
{
    private PackedUniformBuffer? inputs;
    private readonly CloudVolumePublishProgram?[] programs=new CloudVolumePublishProgram?[3];
    private GpuIndirectBuffer? commands;
    private GpuResourceCollection? resources;
    private DynamicTexture3D? density,direct,diffuse;
    private GpuShaderStorageBuffer? regions,occupancy;
    private CloudVolumeFields? source;
    internal CloudVolumeViewFields? Published {get;private set;}
    internal long Bytes=>Published is {} p?Payload(p.Domain):0;
    /// <summary>Exposes GPU-produced counts for deferred diagnostics without reading them during publication.</summary>
    internal GpuIndirectBuffer Arguments=>commands??throw new InvalidOperationException("Cloud publication is not prepared.");
    /// <summary>Reports exceptional channel copies separately from the compact dirty-block list.</summary>
    internal int FullChannels {get;private set;}
    #region Public API
    /// <summary>Counts all drawable images and metadata separately from working/replacement storage.</summary>
    internal static long Payload(CloudVolumeDomain domain)=>checked(14L*domain.Voxels+68L*domain.Blocks);
    /// <summary>Admits the typed publication executable before volume activation.</summary>
    internal void Prepare()
    {
        var caps=GpuSupport.Graphics;
        if(caps.MaxComputeShaderStorageBlocks<6||caps.MaxShaderStorageBufferBindings<6||caps.MaxComputeTextureImageUnits<3||
            caps.MaxComputeWorkGroupInvocations<256||caps.MaxComputeWorkGroupSize.Length<3||caps.MaxComputeWorkGroupCount.Length<3||
            caps.MaxComputeWorkGroupSize[0]<256||
            caps.MaxComputeWorkGroupCount[0]<281||caps.MaxComputeWorkGroupCount[1]<1123||caps.MaxComputeWorkGroupCount[2]<65)
            throw new NotSupportedException("Cloud publication bindings unavailable.");
        var shared=inputs??=new PackedUniformBuffer(16);
        for(int i=0;i<programs.Length;i++)
        {
            programs[i]??=createProgram(i,shared);
            if(!programs[i]!.EnsureReady())throw new InvalidOperationException(programs[i]!.PreparationLog);
        }
        if(commands is null)
        {
            commands=RHICommandContext.Current.CreateIndirectBuffer(new RHIBufferDescription { Role = RHIBufferRole.IndirectDraw, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = "Cloud.Volume.Publication.Arguments" }).RequireResource();
            commands.UploadData(new uint[4]);
        }
    }
    /// <summary>Copies changed blocks after completion, using full channels only for new domains, activation or encoding changes.</summary>
    internal void Publish(CloudVolumeFields fields,int newlyActive,long workingBytes)
    {
        Prepare();var d=fields.Domain;
        // Selection summaries have retired behind the update fence. Reuse only this bounded range,
        // preserving density/light cursors, status and retained diffuse repair history elsewhere.
        if(CloudVolumeWork.SummaryA+d.Blocks>CloudVolumeWork.SummaryB)
            throw new NotSupportedException("Cloud publication list exceeds reusable scratch.");
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
        programs[0]!.Capture(d,full);
        var boundaryResources=new EngineBoundaryResources();
        foreach(var program in programs)
        {
            program!.Density=fields.Density;program.Direct=fields.Direct;program.Diffuse=fields.Diffuse;
            program.TargetDensity=new(density,TextureAccess.WriteOnly,Layered:true);
            program.TargetDirect=new(direct,TextureAccess.WriteOnly,Layered:true);
            program.TargetDiffuse=new(diffuse,TextureAccess.WriteOnly,Layered:true);
            program.Regions=fields.Regions;program.Occupancy=fields.Occupancy;program.TargetRegions=regions;program.TargetOccupancy=occupancy;
            program.Scratch=fields.Scratch;program.Commands=new(commands!,0,16);
            var bindings=program.ProgramLayout.BinaryInterface?.PreparedBindings??throw new InvalidOperationException("Cloud publication bindings unavailable.");
            boundaryResources=boundaryResources.Union(EngineBoundaryResources.From(bindings));
        }
        if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.Volume.Publish"),boundaryResources,_=>
            {
                Barrier();programs[0]!.Dispatch(1);Barrier();
                programs[1]!.Dispatch((d.Blocks+255)/256);Barrier();
                if(full!=0)programs[2]!.Dispatch(d.BlocksX,d.BlocksY,d.BlocksX);
                else programs[2]!.DispatchIndirect(commands!);
                Barrier();
            }))
            throw new InvalidOperationException("Cloud publication boundary rejected.");
        GlDebug.ThrowIfErrors("Cloud volume publication");
        Published=new(d,fields.Encoding,fields.LightingBounds,checked((Published?.Revision??0)+1),density!,direct!,diffuse!,regions!,occupancy!,fields);
        source=fields;
        FullChannels=full;
    }
    /// <summary>Withdraws drawable bindings and retires owned GPU state without waiting on the CPU.</summary>
    public void Dispose()
    {
        // Lighting reconfiguration reuses this owner after retiring its current publication.
        // Detach every executable before cleanup so a later Prepare gets a fresh shared record.
        var retained=programs.ToArray();Array.Clear(programs);
        RenderingCleanup.Run(ResetImages,()=>RenderingCleanup.Run(retained.Select(p=>(Action)(()=>p?.Dispose())).ToArray()),
            ()=>{commands?.Dispose();commands=null;},()=>{inputs?.Dispose();inputs=null;});
    }
    #endregion
    #region Private
    /// <summary>Orders prior drawable reads before overwriting images and exposes completed copies to subsequent draws.</summary>
    private static void Barrier()=>GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|
        MemoryBarrierFlags.TextureFetchBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit|
        MemoryBarrierFlags.TextureUpdateBarrierBit|MemoryBarrierFlags.CommandBarrierBit);
    /// <summary>Allocates hardware-filtered volume storage matching solver addressing.</summary>
    private DynamicTexture3D Volume(CloudVolumeDomain d,PixelInternalFormat format)
    {
        var texture=resources!.Own(DynamicTexture3D.Create(d.Size,66,d.Size,format,textureTarget:TextureTarget.Texture3D));
        texture.SetTexWrap(TextureWrapMode.Repeat,TextureWrapMode.ClampToEdge,TextureWrapMode.Repeat);return texture;
    }
    /// <summary>Allocates exact metadata through its ordinary GPU owner.</summary>
    private GpuShaderStorageBuffer Buffer(int words)
    {var result=resources!.Own(RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource());result.UploadData(new uint[words]);return result;}
    /// <summary>Revokes the snapshot before releasing every acquired image and metadata buffer.</summary>
    private void ResetImages()
    {Published=null;source=null;resources?.Dispose();resources=null;density=direct=diffuse=null;regions=occupancy=null;}
    #endregion
}
