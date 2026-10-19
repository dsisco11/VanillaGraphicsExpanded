using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Publishes bounded partial diffuse iterations while retaining the old coherent field on failure.</summary>
internal sealed class LightingVolumeScattering(Func<bool,LightingVolumeScatteringProgram> createProgram) : IDisposable
{
    private LightingVolumeScatteringProgram? program;
    private ulong executable;
    private uint generation,frame;
    private bool completed,failed,disposed,shadowVariant;
    internal LightingVolumeBounds Bounds { get; }=new();
    internal bool Ready { get; private set; }
    internal string? Failure { get; private set; }

    #region Public API
    /// <summary>Copies unchanged RGBA, refines selected nodes from the old field, then publishes the complete texture/scale pair.</summary>
    internal bool Update(LightingVolumeStorage storage,LightingVolumeWork work,CloudMapInput input,GpuTexture map,Func<bool> current)
    {
        ObjectDisposedException.ThrowIf(disposed,this);Ready=false;
        if(failed || !current() || !work.TryDiffuseBatch(storage,input,out var lighting,out bool enabled,out bool shadows)) return false;
        if(map.TextureId!=input.Storage.Map || map.Width!=input.Storage.Width || map.Height!=input.Storage.Width ||
            map.TextureTarget!=TextureTarget.Texture2D || map.InternalFormat!=PixelInternalFormat.Rgba32f) return false;
        if(completed && generation==storage.Generation && frame==input.Frame &&
            (!enabled || (program?.IsValid==true && ((IGpuProgram)program).ExecutableRevision==executable)))
        {Ready=true;return true;}
        try
        {
            if(completed && generation!=storage.Generation) Bounds.Reset();
            Bounds.Observe(lighting!);
            // Disabled refinement has no GPU jobs, copy or swap; direct metadata keeps its mask-bearing storage.
            if(enabled)
            {
                if(program is null) {program=createProgram(shadows);shadowVariant=shadows;}
                if(shadows!=shadowVariant) throw new InvalidOperationException("Scattering configuration changed; reset the solution.");
                if(!program.EnsureReady()) throw new InvalidOperationException(program.PreparationLog);
                ulong revision=((IGpuProgram)program).ExecutableRevision;
                if(executable!=0 && generation==storage.Generation && executable!=revision)
                    throw new InvalidOperationException("Scattering executable changed; reset the lighting solution.");
                ValidateLimits();
                var target=storage.Encoding.Grow(Bounds.Encoding.Cap);
                program.Capture(storage.Domain!,input.Placement,storage.Encoding,target,lighting!);
                program.Map=map;program.Direct=storage.Direct;program.Previous=storage.Diffuse;
                program.Regions=storage.Regions;program.Jobs=storage.Scratch;
                // No selected writer can see its own output. Metadata commits only after the entire dispatch is visible.
                if(!storage.CopyDiffuse(target,()=>current()&&program.IsValid&&((IGpuProgram)program).ExecutableRevision==revision,
                    spare=>{program.Target=new(spare,TextureAccess.ReadWrite,Layered:true);Run();},
                    ()=>work.Complete(storage,input.Frame,4,current)))
                    throw new InvalidOperationException("Diffuse iteration lost its publication certificate.");
                executable=revision;
            }
            generation=storage.Generation;frame=input.Frame;completed=true;Ready=true;return true;
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;storage.Withdraw();return false;}
        finally {if(program is not null) {program.Map=null;program.Direct=null;program.Previous=null;}}
    }
    /// <summary>Retires the executable and source envelopes together with explicit lighting-solution recovery.</summary>
    internal void Reset()
    {
        Ready=completed=failed=false;Failure=null;executable=0;generation=frame=0;Bounds.Reset();
        program?.Dispose();program=null;
    }
    /// <summary>Releases owned program state; borrowed storage and native maps remain with their owners.</summary>
    public void Dispose() {if(disposed)return;disposed=true;Reset();}
    #endregion
    #region Private
    /// <summary>Separates copy, source sampling, spare image writes and completion with existing engine boundaries.</summary>
    private void Run()
    {
        var bindings=program!.ProgramLayout.BinaryInterface?.PreparedBindings??throw new InvalidOperationException("Scattering bindings unavailable.");
        if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.LightingVolume.Scattering"),EngineBoundaryResources.From(bindings),_=>
        {
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.TextureFetchBarrierBit);
            program.Dispatch(LightingVolumeWork.DiffuseBudget);
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit|
                MemoryBarrierFlags.TextureFetchBarrierBit|MemoryBarrierFlags.TextureUpdateBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        })) throw new InvalidOperationException("Scattering compute boundary rejected.");
        GlDebug.ThrowIfErrors("Cloud lighting volume scattering");
    }
    /// <summary>Admits the fixed refinement job shape and its parameter, sampler, image and storage bindings.</summary>
    private static void ValidateLimits()
    {
        var caps=GpuSupport.Graphics;
        if(caps.MaxComputeWorkGroupInvocations<64 || caps.MaxComputeWorkGroupSize[0]<64 ||
            caps.MaxComputeWorkGroupCount[0]<LightingVolumeWork.DiffuseBudget || caps.MaxUniformBufferBindings<2 ||
            caps.MaxUniformBlockSize<112 || caps.MaxComputeImageUniforms<1 || caps.MaxComputeShaderStorageBlocks<2 ||
            caps.MaxComputeTextureImageUnits<3 || caps.MaxCombinedTextureImageUnits<3)
            throw new NotSupportedException("Diffuse scattering exceeds compute capabilities.");
    }
    #endregion
}
