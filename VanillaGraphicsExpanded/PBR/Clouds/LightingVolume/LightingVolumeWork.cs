using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Coordinates synchronous GPU observation, invalidation and bounded persistent work demand.</summary>
internal sealed class LightingVolumeWork(Func<int,LightingVolumeWorkProgram> createProgram) : IDisposable
{
    internal const int DirectListWord=64,DiffuseListWord=576;
    internal const int DirectBudget=512,DiffuseBudget=256;
    private readonly LightingVolumeWorkProgram?[] programs=new LightingVolumeWorkProgram?[6];
    private readonly ulong[] revisions=new ulong[6];
    private readonly Vector4[] frustum=new Vector4[6];
    private LightingVolumeDomain? previousGrid;
    private Vector3 sun,moon,solar,lunar,environment;
    private long windowX,windowZ;
    private uint generation,revision,frame;
    private bool history,observed,failed,disposed;
    private int active;
    internal bool Ready { get; private set; }
    internal string? Failure { get; private set; }

    #region Public API
    /// <summary>Observes one certified publication and selects jobs; source values remain physical and unencoded.</summary>
    internal bool Update(LightingVolumeStorage storage,CloudMapInput? input,GpuTexture? map,AtmosphereLighting lighting,
        ReadOnlySpan<Vector4> nativeFrustum,Func<bool> current,bool directEnabled=true,bool diffuseEnabled=true)
    {
        ObjectDisposedException.ThrowIf(disposed,this); Ready=false;
        if(failed || input is not { } borrow || map is null || !storage.Ready || !current()) return false;
        if(nativeFrustum.Length!=6) throw new ArgumentException("Six native-local frustum planes are required.",nameof(nativeFrustum));
        if(map.TextureId!=borrow.Storage.Map || map.Width!=borrow.Storage.Width || map.Height!=borrow.Storage.Width ||
            map.InternalFormat!=PixelInternalFormat.Rgba32f || !storage.Accepts(borrow)) return false;
        Validate(lighting,nativeFrustum);
        // Repeated calls for one frame neither consume fairness again nor fabricate newer metadata.
        if(observed && generation==storage.Generation && borrow.Frame==frame) return false;
        try
        {
            PreparePrograms();
            var format=GpuSupport.GetInternalFormatCapabilities(ImageTarget.Texture2D,SizedInternalFormat.Rgba32f);
            if(format.ImageLoadSupport==0 || format.ImageStoreSupport==0)
                throw new NotSupportedException("Lighting observation requires RGBA32F image load/store support.");
            if(GpuSupport.Graphics.MaxImageUnits<3 || GpuSupport.Graphics.MaxComputeWorkGroupSize[0]<64 ||
                GpuSupport.Graphics.MaxComputeWorkGroupCount[0]<(storage.Domain!.Voxels+63)/64)
                throw new NotSupportedException("Lighting work requires three image slots and 64-wide dispatches.");
            // Counter rollover explicitly replaces metadata and illumination rather than using ambiguous age ordering.
            if(observed && (borrow.Frame<frame || revision==uint.MaxValue))
            {
                var encoding=storage.Encoding;storage.Reset();storage.Ensure(borrow,encoding,current);history=false;observed=false;
            }
            bool same=generation==storage.Generation && previousGrid?.Width==storage.Domain!.Width;
            if(!same) { history=false;observed=false;revision=0;previousGrid=null; }
            long x=(long)borrow.Placement.CenterX-borrow.Placement.WindTileX,z=(long)borrow.Placement.CenterZ-borrow.Placement.WindTileZ;
            bool retain=history && Math.Abs(x-windowX)<borrow.Storage.Width && Math.Abs(z-windowZ)<borrow.Storage.Width;
            int nextActive=directEnabled?((Peak(lighting.Solar)>0?1:0)|(Peak(lighting.Lunar)>0?2:0)):0;
            int newlyActive=nextActive&~(observed?active:0);
            int dirty=observed?((sun!=lighting.Sun?1:0)|(moon!=lighting.Moon?2:0)):3;
            bool diffuseDirty=!observed || dirty!=0 || solar!=lighting.Solar || lunar!=lighting.Lunar || environment!=lighting.Environment;
            nativeFrustum.CopyTo(frustum);revision++;
            Configure(storage,borrow,map,retain,dirty,newlyActive,diffuseDirty,diffuseEnabled,nextActive,0);
            // Every operation is bounded by the admitted descriptor/node/region extents. No readback selects work.
            Run(0,(storage.Domain!.Regions+63)/64);
            Run(1,(borrow.Storage.Width*borrow.Storage.Width+63)/64);
            Run(2,(storage.Domain.Regions+63)/64);
            Run(3,(storage.Domain.Voxels+63)/64);
            Run(4,1);
            Check(current);
            windowX=x;windowZ=z;generation=storage.Generation;previousGrid=storage.Domain;history=true;observed=true;
            frame=borrow.Frame;sun=lighting.Sun;moon=lighting.Moon;solar=lighting.Solar;lunar=lighting.Lunar;
            environment=lighting.Environment;active=nextActive;Ready=true;
            return true;
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            // Descriptor/mask writes are in place. A failed pass cannot leave a reusable history certificate.
            failed=true;history=false;Failure=error.Message;storage.Withdraw();return false;
        }
        finally { foreach(var program in programs) if(program is not null) program.Map=null; }
    }

    /// <summary>Commits metadata only after every emitted job in the specified channels completed its lighting writes.</summary>
    /// <remarks>Bits zero/one/two select solar/lunar/diffuse. Callers must validate the same frozen publication;
    /// partial or failed solver batches must never call this method. Selection alone is never completion.</remarks>
    internal bool Complete(LightingVolumeStorage storage,uint completedFrame,int channels,Func<bool> current)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!Ready || !storage.Ready || completedFrame!=frame || storage.Generation!=generation ||
            storage.Domain!=previousGrid || channels is <1 or >7) return false;
        try
        {
            Check(current);
            // Capture only the completion bitmask in the previously staged work input; no old native map is retained.
            programs[5]!.SetCompleted(channels);
            programs[5]!.Regions=storage.Regions;programs[5]!.Scratch=storage.Scratch;
            Run(5,(storage.Domain!.Regions+63)/64);Check(current);
            return true;
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        { failed=true;history=false;Ready=false;Failure=error.Message;storage.Withdraw();return false; }
    }

    /// <summary>Releases operation executables and revokes observation identity at explicit recovery boundaries.</summary>
    internal void Reset()
    {
        Ready=history=observed=failed=false;Failure=null;previousGrid=null;generation=revision=frame=0;
        // Attempt every retirement even when an executable's release fails.
        Action[] releases=new Action[programs.Length];
        for(int i=0;i<programs.Length;i++) {int slot=i;releases[i]=()=>{programs[slot]?.Dispose();programs[slot]=null;};}
        RenderingCleanup.Run(releases);
    }
    /// <summary>Retires this coordinator without acquiring native texture ownership.</summary>
    public void Dispose() {if(disposed)return;disposed=true;Reset();}
    #endregion

    #region Private
    #region Executable preparation and submission
    /// <summary>Prepares all bounded operations and rejects executable replacement while retaining history.</summary>
    private void PreparePrograms()
    {
        for(int i=0;i<programs.Length;i++)
        {
            var program=programs[i]??=createProgram(i);
            if(!program.EnsureReady()) throw new InvalidOperationException(program.PreparationLog);
            ulong next=((IGpuProgram)program).ExecutableRevision;
            if(history && revisions[i]!=next) throw new InvalidOperationException("Lighting work executable changed; reset is required.");
            revisions[i]=next;
        }
    }
    /// <summary>Stages current borrows and scalar identity into every structural operation before its submission.</summary>
    private void Configure(LightingVolumeStorage storage,CloudMapInput input,GpuTexture map,bool retain,int dirty,
        int newlyActive,bool diffuseDirty,bool diffuseEnabled,int nextActive,int completed)
    {
        foreach(var program in programs)
        {
            program!.Generation=storage.Generation;
            program.Capture(storage.Domain!,input,windowX,windowZ,retain,previousGrid,revision,nextActive,dirty,
                newlyActive,diffuseDirty,diffuseEnabled,completed,frustum);
            program.Map=map;program.Previous=new(storage.PreviousObservation!,TextureAccess.ReadWrite);
            program.Reference=new(storage.HardReference!,TextureAccess.ReadWrite);
            program.Nodes=new(storage.Diffuse,TextureAccess.ReadWrite,Layered:true);
            program.Regions=storage.Regions;program.Scratch=storage.Scratch;
        }
    }
    /// <summary>Submits through the existing boundary and separates image, sampling and storage consumers.</summary>
    private void Run(int operation,int groups)
    {
        var program=programs[operation]!;
        var bindings=program.ProgramLayout.BinaryInterface?.PreparedBindings??throw new InvalidOperationException("Lighting work bindings unavailable.");
        if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.LightingVolume.Work"),EngineBoundaryResources.From(bindings),_=>
        {
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.TextureFetchBarrierBit);
            program.Dispatch(groups);
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit|
                MemoryBarrierFlags.TextureFetchBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit|MemoryBarrierFlags.TextureUpdateBarrierBit);
        })) throw new InvalidOperationException("Lighting work boundary rejected.");
        GlDebug.ThrowIfErrors("Cloud lighting volume work");
    }
    /// <summary>Rejects changed input/executable certificates before publishing completed metadata.</summary>
    private void Check(Func<bool> current)
    {
        if(!current()) throw new InvalidOperationException("Lighting work publication changed during submission.");
        for(int i=0;i<programs.Length;i++)
            if(!programs[i]!.IsValid || ((IGpuProgram)programs[i]!).ExecutableRevision!=revisions[i])
                throw new InvalidOperationException("Lighting work executable changed during submission.");
    }
    #endregion
    #region Input admission
    /// <summary>Rejects nonphysical frozen sources or malformed frustum planes before GPU mutation.</summary>
    private static void Validate(AtmosphereLighting lighting,ReadOnlySpan<Vector4> planes)
    {
        ReadOnlySpan<Vector3> vectors=stackalloc Vector3[]{lighting.Sun,lighting.Moon,lighting.Solar,lighting.Lunar,lighting.Environment};
        foreach(var vector in vectors)
            if(!float.IsFinite(vector.X)||!float.IsFinite(vector.Y)||!float.IsFinite(vector.Z)) throw new ArgumentException("Nonfinite lighting work input.");
        if(Vector3.Min(lighting.Solar,Vector3.Zero)!=Vector3.Zero || Vector3.Min(lighting.Lunar,Vector3.Zero)!=Vector3.Zero ||
            Vector3.Min(lighting.Environment,Vector3.Zero)!=Vector3.Zero) throw new ArgumentException("Negative lighting work input.");
        foreach(var plane in planes) if(!float.IsFinite(plane.X)||!float.IsFinite(plane.Y)||!float.IsFinite(plane.Z)||!float.IsFinite(plane.W))
            throw new ArgumentException("Nonfinite lighting work frustum.");
    }
    /// <summary>Admits every nonzero physical source without an arbitrary brightness cutoff.</summary>
    private static float Peak(Vector3 vector)=>Math.Max(vector.X,Math.Max(vector.Y,vector.Z));
    #endregion
    #endregion
}
