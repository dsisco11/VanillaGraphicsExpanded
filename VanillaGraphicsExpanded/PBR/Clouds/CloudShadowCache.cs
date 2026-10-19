using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns same-frame attenuation storage and bounded generation for the active reduced cloud trace.</summary>
internal sealed class CloudShadowCache(Func<CloudShadowAttenuationComputeShader> createProgram) : IDisposable
{
    internal const int Quadrature=64;
    private CloudShadowAttenuationComputeShader? program;
    private DynamicTexture2D? texture;
    private BorrowedTexture? map;
    private CloudMapInput? allocation;
    private int resolution;
    private bool failed,disposed;

    #region Public API
    /// <summary>Reports optional generation failure while the trace retains bounded marching.</summary>
    internal string? Failure { get; private set; }
    /// <summary>Builds the dominant source's attenuation from the current certified map, never a previous frame.</summary>
    internal DynamicTexture2D? Build(CloudMapInput input,AtmosphereLighting lighting,CloudVolumeParameters parameters,
        int width,DynamicTexture2D? bounds,Func<bool> current,out int source,GpuTexture? perturbation=null)
    {
        source=Peak(lighting.Solar)>=Peak(lighting.Lunar)?0:1;
        if(disposed) return null;
        if(width is not (0 or 256 or 512 or 1024)) throw new ArgumentOutOfRangeException(nameof(width));
        // Quality changes retire incompatible allocations and failed-generation state.
        // Disabling self-shadowing skips compute; the shared flag also bypasses marching.
        if(resolution!=width) Reset();
        if(width==0) return null;
        if(allocation is { } previous && !SameLifetime(previous,input)) Reset();
        resolution=width;
        if(bounds?.IsValid!=true || Math.Max(Peak(lighting.Solar),Peak(lighting.Lunar))<=0 || !current())
        { Reset(); return null; }
        if(failed) return null;
        try
        {
            GlDebug.ThrowIfErrors("Before cloud attenuation generation");
            // Compatible storage persists, but every draw generates new contents from its complete
            // map/placement/light publication. There is no cross-frame attenuation certificate.
            program??=createProgram();
            if(!program.EnsureReady()) throw new InvalidOperationException(program.PreparationLog);
            // RGB carries front depth, inverse-metre mean extinction and maximum tau.
            // RGBA32F supports image writes and later linear sampling; alpha is unused.
            texture??=DynamicTexture2D.Create(width,width,PixelInternalFormat.Rgba32f);
            map??=new BorrowedTexture(input.Storage.Map);
            allocation=input;
            if(!current()) { Reset(); return null; }
            program.Capture(parameters,bounds,width,Quadrature,source);
            program.Perturbation=perturbation;
            program.Map=map;program.Target=new(texture,TextureAccess.WriteOnly);
            var prepared=program.ProgramLayout.BinaryInterface?.PreparedBindings
                ??throw new InvalidOperationException("Cloud attenuation bindings are unavailable.");
            ulong revision=((IGpuProgram)program).ExecutableRevision;
            if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.ShadowCache"),
                EngineBoundaryResources.From(prepared),_=>
                {
                    // Finish prior-frame sampling before overwriting persistent attenuation storage.
                    GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
                    program.Dispatch(width/8,width/8);
                    GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.TextureFetchBarrierBit);
                })) throw new InvalidOperationException("Cloud attenuation compute boundary rejected.");
            GlDebug.ThrowIfErrors("Cloud attenuation generation");
            // Revoke partial work or a changed native borrow before exposing the texture to tracing.
            if(!current() || !program.IsValid || ((IGpuProgram)program).ExecutableRevision!=revision)
            { Reset(); return null; }
            return texture;
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            Reset();allocation=input;resolution=width;failed=true;Failure=error.Message;
            return null;
        }
    }
    /// <summary>Releases owned storage and executable while leaving native textures with their producer.</summary>
    internal void Reset()
    {
        allocation=null;resolution=0;failed=false;Failure=null;
        RenderingCleanup.Run(()=>{texture?.Dispose();texture=null;},()=>{map?.Dispose();map=null;},()=>{program?.Dispose();program=null;});
    }
    /// <summary>Ends this consumer lifetime permanently before retiring its GPU resources.</summary>
    public void Dispose() { if(disposed)return;disposed=true;Reset(); }
    #endregion

    #region Private
    /// <summary>Compares source lifetimes independently of ordinary same-allocation content generations.</summary>
    private static bool SameLifetime(CloudMapInput a,CloudMapInput b)=>ReferenceEquals(a.Owner,b.Owner)
        && a.World==b.World && a.Allocation==b.Allocation && a.Shader==b.Shader && a.Storage==b.Storage;
    /// <summary>Selects a nonnegative physical source without changing either published contribution.</summary>
    private static float Peak(Vector3 value)=>Math.Max(value.X,Math.Max(value.Y,value.Z));
    #endregion
}
