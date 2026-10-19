using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Computes and certifies selected persistent transmission channels without retaining native texture borrows.</summary>
internal sealed class LightingVolumeDirect(Func<LightingVolumeDirectProgram> createProgram) : IDisposable
{
    private LightingVolumeDirectProgram? program;
    private ulong executable;
    private uint generation,frame;
    private bool completed,failed,disposed;
    internal bool Ready { get; private set; }
    internal string? Failure { get; private set; }

    #region Public API
    /// <summary>Submits the frozen selected solar/lunar batches in order, then commits their actual update metadata.</summary>
    internal bool Update(LightingVolumeStorage storage,LightingVolumeWork work,CloudMapInput input,GpuTexture map,Func<bool> current)
    {
        ObjectDisposedException.ThrowIf(disposed,this);Ready=false;
        if(failed || !current() || !work.TryDirectBatch(storage,input,out var sun,out var moon,out int sources)) return false;
        if(map.TextureId!=input.Storage.Map || map.Width!=input.Storage.Width || map.Height!=input.Storage.Width ||
            map.TextureTarget!=TextureTarget.Texture2D || map.InternalFormat!=PixelInternalFormat.Rgba32f) return false;
        if(completed && generation==storage.Generation && frame==input.Frame &&
            (sources==0 || (program?.IsValid==true && ((IGpuProgram)program).ExecutableRevision==executable)))
        {Ready=true;return true;}
        try
        {
            // No executable, dispatch or metadata completion is needed for inactive direct sources.
            if(sources!=0)
            {
                program??=createProgram();
                if(!program.EnsureReady()) throw new InvalidOperationException(program.PreparationLog);
                ulong revision=((IGpuProgram)program).ExecutableRevision;
                if(executable!=0 && generation==storage.Generation && executable!=revision)
                    throw new InvalidOperationException("Direct density executable changed; reset the lighting solution.");
                ValidateLimits();
                program.Map=map;program.Regions=storage.Regions;program.Jobs=storage.Scratch;
                program.Direct=new(storage.Direct,TextureAccess.ReadWrite,Layered:true);
                program.Nodes=new(storage.Diffuse,TextureAccess.ReadWrite,Layered:true);
                for(int channel=0;channel<2;channel++) if((sources&(1<<channel))!=0)
                {
                    if(!current()) throw new InvalidOperationException("Direct source lost its native certificate.");
                    Vector3 direction=Normalize(channel==0?sun:moon);
                    program.Capture(storage.Domain!,input.Placement,storage.Encoding,direction,channel);
                    Run();
                    if(!program.IsValid || ((IGpuProgram)program).ExecutableRevision!=revision || !current())
                        throw new InvalidOperationException("Direct work lost its executable or input certificate.");
                }
                if(!work.Complete(storage,input.Frame,sources,current)) throw new InvalidOperationException("Direct completion rejected.");
                executable=revision;
            }
            if(!current()) throw new InvalidOperationException("Direct publication lost its native certificate.");
            generation=storage.Generation;frame=input.Frame;completed=true;Ready=true;return true;
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;storage.Withdraw();return false;}
        finally {if(program is not null) program.Map=null;}
    }
    /// <summary>Releases the solver executable and clears its failure latch at explicit lifecycle recovery.</summary>
    internal void Reset()
    {
        Ready=completed=failed=false;Failure=null;executable=0;generation=frame=0;
        program?.Dispose();program=null;
    }
    /// <summary>Retires this solver permanently while native maps and lighting textures remain with their owners.</summary>
    public void Dispose() {if(disposed)return;disposed=true;Reset();}
    #endregion
    #region Private
    /// <summary>Runs one source with visibility barriers before the next source or any lighting reader.</summary>
    private void Run()
    {
        var bindings=program!.ProgramLayout.BinaryInterface?.PreparedBindings??throw new InvalidOperationException("Direct bindings unavailable.");
        if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.LightingVolume.Direct"),EngineBoundaryResources.From(bindings),_=>
        {
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.TextureFetchBarrierBit);
            program.Dispatch(LightingVolumeWork.DirectBudget);
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit|
                MemoryBarrierFlags.TextureFetchBarrierBit|MemoryBarrierFlags.TextureUpdateBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        })) throw new InvalidOperationException("Direct compute boundary rejected.");
        GlDebug.ThrowIfErrors("Cloud lighting volume direct transmission");
    }
    /// <summary>Admits the fixed per-source job shape and the two UBO/image/storage bindings before dispatch.</summary>
    private static void ValidateLimits()
    {
        var caps=GpuSupport.Graphics;
        if(caps.MaxComputeWorkGroupInvocations<64 || caps.MaxComputeWorkGroupSize[0]<64 ||
            caps.MaxComputeWorkGroupCount[0]<LightingVolumeWork.DirectBudget || caps.MaxUniformBufferBindings<2 ||
            caps.MaxComputeImageUniforms<2 || caps.MaxComputeShaderStorageBlocks<2)
            throw new NotSupportedException("Direct transmission exceeds compute capabilities.");
    }
    /// <summary>Normalizes a finite meaningful source in double precision without overflowing a float norm.</summary>
    private static Vector3 Normalize(Vector3 direction)
    {
        double length=Math.Sqrt((double)direction.X*direction.X+(double)direction.Y*direction.Y+(double)direction.Z*direction.Z);
        if(!double.IsFinite(length)||length<=0) throw new ArgumentException("Direct source requires a finite nonzero direction.");
        return new((float)(direction.X/length),(float)(direction.Y/length),(float)(direction.Z/length));
    }
    #endregion
}
