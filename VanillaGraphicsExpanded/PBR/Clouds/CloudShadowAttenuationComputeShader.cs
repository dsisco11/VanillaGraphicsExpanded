using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns bounded same-frame light-space attenuation generation for cloud tracing.</summary>
[ShaderProgram("Contract","cloud_shadow_cache",4)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_shadow_cache.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(GenerationPolicy))]
internal sealed partial class CloudShadowAttenuationComputeShader : GpuComputeProgram,ICloudShadowAttenuationComputeBindings
{
    private readonly PackedUniformBuffer traversal=new(16),inputs=new(16);
    private CpuUniformBuffer? volume;
    #region Public API
    /// <summary>Selects bounded support allocation by default, with retained uniform and area diagnostic variants.</summary>
    [ShaderOption("VGE_CLOUD_SHADOW_POLICY",1,Domain=new object[]{0,1,2})]
    internal static partial ShaderOption<int> GenerationPolicy {get;}
    /// <summary>Creates the cloud executable using production compute preparation.</summary>
    internal static CloudShadowAttenuationComputeShader Create(ICoreAPI api,int policy=1)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract).With(GenerationPolicy,policy),out var pipeline,out string log,
            "Cloud.ShadowAttenuation",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Stages one certified source and bounded generation schedule before dispatch.</summary>
    internal void Capture(CpuUniformBuffer parameters,DynamicTexture2D bounds,int width,int count,int source)
    {
        RequireInputMutation();
        if(width<1||count is <1 or >128||source is <0 or >1)throw new ArgumentOutOfRangeException(nameof(count));
        volume=parameters;HeightBounds=bounds;
        Span<byte> bytes=stackalloc byte[16];
        UboPacking.WriteIVec4(bytes,0,1,bounds.MipLevels-1,0,0);traversal.SetBytes(bytes);
        UboPacking.WriteIVec4(bytes,0,width,count,source,0);inputs.SetBytes(bytes);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and generation blocks while borrowing source textures.</summary>
    private CloudShadowAttenuationComputeShader(GpuComputePipeline pipeline):base(pipeline)
    {OwnUniformBuffer(traversal);OwnUniformBuffer(inputs);}
    /// <summary>Requires a staged coherent volume before generated submission.</summary>
    CpuUniformBuffer ICloudShadowAttenuationComputeBindings.VolumeInputs=>volume??throw new InvalidOperationException("Volume not staged.");
    /// <summary>Publishes certified root selection.</summary>
    CpuUniformBuffer ICloudShadowAttenuationComputeBindings.TraversalInputs=>traversal;
    /// <summary>Publishes the bounded generation schedule.</summary>
    CpuUniformBuffer ICloudShadowAttenuationComputeBindings.CacheInputs=>inputs;
    #endregion
}
