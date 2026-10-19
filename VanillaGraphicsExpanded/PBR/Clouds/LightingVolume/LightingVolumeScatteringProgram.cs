using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Refines selected diffuse regions from a coherent previously published source field.</summary>
[ShaderProgram("Contract","cloud_lighting_volume/scattering",4)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_lighting_volume/scattering.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Detail))]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(SelfShadowing))]
internal sealed partial class LightingVolumeScatteringProgram : GpuComputeProgram, ILightingVolumeScatteringBindings
{
    private readonly LightingVolumeParameters inputs=new();
    private readonly PackedUniformBuffer sources=new(112);
    #region Public API
    /// <summary>Selects the compiled private density path.</summary>
    [ShaderOptionReference(typeof(LightingVolumeShaderOptions),nameof(LightingVolumeShaderOptions.Detail))]
    internal static partial ShaderOption<bool> Detail { get; }
    /// <summary>Removes cached direct reads when self-shadowing is disabled.</summary>
    [ShaderOptionReference(typeof(LightingVolumeShaderOptions),nameof(LightingVolumeShaderOptions.Direct))]
    internal static partial ShaderOption<bool> SelfShadowing { get; }
    /// <summary>Creates the independent typed compute executable with immutable user-path selections.</summary>
    internal static LightingVolumeScatteringProgram Create(ICoreAPI api,bool detail=true,bool selfShadowing=true)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract).With(Detail,detail).With(SelfShadowing,selfShadowing),
            out var pipeline,out string log,"Cloud.LightingVolume.Scattering",layout:layout)) throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Freezes physical source units and separate source/destination encodings for one partial iteration.</summary>
    internal void Capture(LightingVolumeDomain domain,CloudPlacement placement,LightingVolumeEncoding from,
        LightingVolumeEncoding to,AtmosphereLighting lighting)
    {
        RequireInputMutation();inputs.Capture(domain,placement,from);
        Span<byte> data=stackalloc byte[112];data.Clear();
        Vector3 sun=Direction(lighting.Sun,lighting.Solar),moon=Direction(lighting.Moon,lighting.Lunar);
        UboPacking.WriteVec4(data,0,sun.X,sun.Y,sun.Z,0);
        UboPacking.WriteVec4(data,16,moon.X,moon.Y,moon.Z,0);
        UboPacking.WriteVec4(data,32,lighting.Solar.X,lighting.Solar.Y,lighting.Solar.Z,0);
        UboPacking.WriteVec4(data,48,lighting.Lunar.X,lighting.Lunar.Y,lighting.Lunar.Z,0);
        UboPacking.WriteVec4(data,64,lighting.Environment.X,lighting.Environment.Y,lighting.Environment.Z,0);
        UboPacking.WriteIVec4(data,80,(int)(domain.X-4*domain.RegionX),0,(int)(domain.Z-4*domain.RegionZ),domain.RegionSide);
        UboPacking.WriteVec4(data,96,to.Scale,to.Cap,0,0);sources.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Owns parameter blocks while borrowing all images, jobs and native inputs.</summary>
    private LightingVolumeScatteringProgram(GpuComputePipeline pipeline):base(pipeline)
    {OwnUniformBuffer(inputs);OwnUniformBuffer(sources);}
    /// <summary>Normalizes meaningful sources in double precision; inactive directions are irrelevant.</summary>
    private static Vector3 Direction(Vector3 direction,Vector3 energy)
    {
        if(energy==Vector3.Zero) return Vector3.UnitY;
        double length=Math.Sqrt((double)direction.X*direction.X+(double)direction.Y*direction.Y+(double)direction.Z*direction.Z);
        if(!double.IsFinite(length)||length<=0) throw new ArgumentException("Scattering requires a finite nonzero active direction.");
        return new((float)(direction.X/length),(float)(direction.Y/length),(float)(direction.Z/length));
    }
    /// <summary>Publishes native density and readable field parameters.</summary>
    CpuUniformBuffer ILightingVolumeScatteringBindings.Inputs=>inputs;
    /// <summary>Publishes the immutable physical source snapshot.</summary>
    CpuUniformBuffer ILightingVolumeScatteringBindings.Sources=>sources;
    #endregion
}
