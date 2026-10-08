using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns typed inputs for the explicitly registered aerial lookup test executable.</summary>
internal sealed class AerialLookupProgram : GpuProgram
{
    private static readonly GpuShaderContract contract=ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/aerial-lookup"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes=new byte[16];
    private GpuTexture? radiance,attenuation,occlusion;
    #region Public API
    /// <summary>Registers the shared fixture contract and retained parameter block.</summary>
    public AerialLookupProgram()
    {
        inputs=OwnUniformBuffer(new PackedUniformBuffer(16));
        foreach(var stage in contract.Stages)ProgramLayout.RegisterContract(stage.Bindings);
    }
    /// <summary>Selects the packaged test executable and its explicit resource layout.</summary>
    internal override GpuShaderContract ProgramContract=>contract;
    /// <summary>Supplies the published atmospheric radiance volume.</summary>
    internal GpuTexture? Radiance {get=>radiance;set{RequireInputMutation();radiance=value;}}
    /// <summary>Supplies atmospheric loss without transferring its ownership.</summary>
    internal GpuTexture? Attenuation {get=>attenuation;set{RequireInputMutation();attenuation=value;}}
    /// <summary>Supplies current-frame screen-space occlusion.</summary>
    internal GpuTexture? Occlusion {get=>occlusion;set{RequireInputMutation();occlusion=value;}}
    /// <summary>Stages physical displacement and receiver sky availability in the fixture block.</summary>
    internal void Capture(Vector3 displacement,float visibility)
    {
        RequireInputMutation();UboPacking.WriteVec3(bytes,0,displacement.X,displacement.Y,displacement.Z);
        UboPacking.WriteFloat(bytes,12,visibility);inputs.SetBytes(bytes);
    }
    #endregion
    #region Protected API
    /// <summary>Validates the complete typed input set before publishing prepared descriptors.</summary>
    protected override void Submit()
    {
        var radiance=ShaderPreparedSubmission.Resolve(this,GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"vge_atmosphereAerialRadiance"));
        var attenuation=ShaderPreparedSubmission.Resolve(this,GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"vge_atmosphereAerialAttenuation"));
        var occlusion=ShaderPreparedSubmission.Resolve(this,GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"vge_lightShaftOcclusion"));
        var block=ShaderPreparedSubmission.Resolve(this,GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,"AerialLookupInputs"));
        ShaderPreparedSubmission.ValidateSampler(radiance,Radiance);ShaderPreparedSubmission.ValidateSampler(attenuation,Attenuation);
        ShaderPreparedSubmission.ValidateSampler(occlusion,Occlusion);ShaderPreparedSubmission.ValidateUniformBlock(block,inputs);
        ShaderPreparedSubmission.Sampler(radiance,Radiance);ShaderPreparedSubmission.Sampler(attenuation,Attenuation);
        ShaderPreparedSubmission.Sampler(occlusion,Occlusion);ShaderPreparedSubmission.UniformBlock(block,inputs);
    }
    #endregion
}
