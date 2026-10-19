using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Integrates selected source-node transmission using the candidate's private density field.</summary>
[ShaderProgram("Contract","cloud_lighting_volume/direct",2)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_lighting_volume/direct.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Detail))]
internal sealed partial class LightingVolumeDirectProgram : GpuComputeProgram, ILightingVolumeDirectBindings
{
    private readonly LightingVolumeParameters inputs=new();
    private readonly PackedUniformBuffer source=new(32);
    #region Public API
    /// <summary>Selects the candidate's actual compiled density-detail variant.</summary>
    [ShaderOptionReference(typeof(LightingVolumeShaderOptions),nameof(LightingVolumeShaderOptions.Detail))]
    internal static partial ShaderOption<bool> Detail { get; }
    /// <summary>Creates the typed executable without borrowing current cloud transport programs.</summary>
    internal static LightingVolumeDirectProgram Create(ICoreAPI api,bool detail=true)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract).With(Detail,detail),out var pipeline,
            out string log,"Cloud.LightingVolume.Direct",layout:layout)) throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Stages one source and its exact region alignment; illuminance never enters stored transmission.</summary>
    internal void Capture(LightingVolumeDomain domain,CloudPlacement placement,LightingVolumeEncoding encoding,Vector3 direction,int channel)
    {
        RequireInputMutation();
        if(channel is <0 or >1) throw new ArgumentOutOfRangeException(nameof(channel));
        inputs.Capture(domain,placement,encoding);
        Span<byte> data=stackalloc byte[32];
        UboPacking.WriteVec4(data,0,direction.X,direction.Y,direction.Z,channel);
        UboPacking.WriteIVec4(data,16,(int)(domain.X-4*domain.RegionX),0,(int)(domain.Z-4*domain.RegionZ),domain.RegionSide);
        source.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Owns both parameter blocks while borrowing selected lists and lighting images.</summary>
    private LightingVolumeDirectProgram(GpuComputePipeline pipeline):base(pipeline)
    {OwnUniformBuffer(inputs);OwnUniformBuffer(source);}
    /// <summary>Publishes physical native density and placement parameters.</summary>
    CpuUniformBuffer ILightingVolumeDirectBindings.Inputs=>inputs;
    /// <summary>Publishes the normalized direction and selected channel.</summary>
    CpuUniformBuffer ILightingVolumeDirectBindings.Source=>source;
    #endregion
}
