using System;
using System.Buffers.Binary;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Owns one structural work operation while sharing typed inputs across barrier-separated submissions.</summary>
[ShaderProgram("Contract","cloud_lighting_volume/work",6)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_lighting_volume/work.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Operation))]
internal sealed partial class LightingVolumeWorkProgram : GpuComputeProgram, ILightingVolumeWorkBindings
{
    private readonly PackedUniformBuffer inputs=new(208);
    #region Public API
    /// <summary>Selects preparation, observation, demand merge, node invalidation, selection or completion.</summary>
    [ShaderOption("VGE_LIGHTING_WORK",0,Domain=new object[]{0,1,2,3,4,5})]
    internal static partial ShaderOption<int> Operation { get; }
    /// <summary>Loads an independently discovered operation with unused bindings optimized away.</summary>
    internal static LightingVolumeWorkProgram Create(ICoreAPI api,int operation)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract).With(Operation,operation),out var pipeline,
            out string log,"Cloud.LightingVolume.Work",layout:layout)) throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Publishes complete work parameters without retaining the native input or source snapshot.</summary>
    internal void Capture(LightingVolumeDomain domain,CloudMapInput input,long oldX,long oldZ,bool history,
        LightingVolumeDomain? previousGrid,uint revision,int active,int directDirty,int newlyActive,bool diffuseDirty,
        bool diffuseEnabled,int completed,ReadOnlySpan<Vector4> planes)
    {
        RequireInputMutation();
        Span<byte> data=stackalloc byte[208]; data.Clear();
        long x=(long)input.Placement.CenterX-input.Placement.WindTileX,z=(long)input.Placement.CenterZ-input.Placement.WindTileZ;
        UboPacking.WriteIVec4(data,0,domain.Size,domain.Width,domain.RegionSide,domain.Interior);
        UboPacking.WriteIVec4(data,16,(int)(domain.X-4*domain.RegionX),(int)(domain.Z-4*domain.RegionZ),Mod(x,domain.Width),Mod(z,domain.Width));
        UboPacking.WriteIVec4(data,32,history?checked((int)(oldX-x)):0,history?checked((int)(oldZ-z)):0,history?1:0,diffuseEnabled?1:0);
        UboPacking.WriteIVec4(data,48,previousGrid is null?0:checked((int)(domain.RegionX-previousGrid.RegionX)),
            previousGrid is null?0:checked((int)(domain.RegionZ-previousGrid.RegionZ)),0,completed);
        Vector3 first=domain.FirstNode;
        UboPacking.WriteVec4(data,64,first.X,first.Y,first.Z,0);
        UboPacking.WriteIVec4(data,80,active,directDirty,newlyActive,diffuseDirty?1:0);
        UboPacking.WriteIVec4(data,96,unchecked((int)input.Frame),unchecked((int)revision),unchecked((int)domainGeneration),0);
        for(int i=0;i<6;i++) UboPacking.WriteVec4(data,112+i*16,planes[i].X,planes[i].Y,planes[i].Z,planes[i].W);
        inputs.SetBytes(data);
    }
    /// <summary>Sets the current storage generation before capturing parameters.</summary>
    internal uint Generation { set { RequireInputMutation(); domainGeneration=value; } }
    /// <summary>Reuses the frozen frame record while admitting only a completed channel batch.</summary>
    internal void SetCompleted(int channels)
    {
        RequireInputMutation();
        Span<byte> data=stackalloc byte[208];inputs.Bytes.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(60,4),channels);inputs.SetBytes(data);
    }
    #endregion
    #region Private
    private uint domainGeneration;
    /// <summary>Adopts the executable and its bounded packed input block.</summary>
    private LightingVolumeWorkProgram(GpuComputePipeline pipeline):base(pipeline)=>OwnUniformBuffer(inputs);
    /// <summary>Publishes the complete staged observation/scheduler record.</summary>
    CpuUniformBuffer ILightingVolumeWorkBindings.Inputs=>inputs;
    /// <summary>Uses positive integer modulo without converting absolute material keys to floats.</summary>
    private static int Mod(long value,int width)=>(int)((value%width+width)%width);
    #endregion
}
