using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Reduces current valid native heights on the GPU independently of existing cloud acceleration.</summary>
[ShaderProgram("Contract","cloud_lighting_volume/heights",1)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_lighting_volume/heights.csh")]
internal sealed partial class LightingVolumeHeightProgram:GpuComputeProgram,ILightingVolumeHeightBindings
{
    private readonly PackedUniformBuffer inputs=new(16);
    #region Public API
    /// <summary>Creates the typed finite reduction executable.</summary>
    internal static LightingVolumeHeightProgram Create(ICoreAPI api)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract),out var pipeline,out string log,
            "Cloud.LightingVolume.Heights",layout:layout)) throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Stages only the admitted native width; no CPU map readback participates.</summary>
    internal void Capture(int width)
    {
        RequireInputMutation();if(width is <1 or >200) throw new ArgumentOutOfRangeException(nameof(width));
        Span<byte> data=stackalloc byte[16];UboPacking.WriteIVec4(data,0,width,0,0,0);inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Owns its compact parameter record while scratch and native storage remain borrowed.</summary>
    private LightingVolumeHeightProgram(GpuComputePipeline pipeline):base(pipeline)=>OwnUniformBuffer(inputs);
    /// <summary>Publishes the complete native width record.</summary>
    CpuUniformBuffer ILightingVolumeHeightBindings.Inputs=>inputs;
    #endregion
}
