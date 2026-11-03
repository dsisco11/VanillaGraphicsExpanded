using VanillaGraphicsExpanded.Rendering.RHI;
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
    private readonly byte[] bytes=new byte[96];
    private GpuTexture? radiance,attenuation,occlusion;
    #region Public API
    /// <summary>Registers the shared fixture contract and retained parameter block.</summary>
    public AerialLookupProgram()
    {
        inputs=OwnUniformBuffer(new PackedUniformBuffer(96));
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
        RequireInputMutation();Array.Clear(bytes);UboPacking.WriteVec3(bytes,0,displacement.X,displacement.Y,displacement.Z);
        UboPacking.WriteFloat(bytes,12,visibility);inputs.SetBytes(bytes);
    }
    /// <summary>Captures the direct three-band solar/lunar decode observation without another fixture executable.</summary>
    internal void CaptureCelestialDecoder(Vector3 ray)
    {
        RequireInputMutation();
        Array.Clear(bytes);
        UboPacking.WriteVec3(bytes, 0, ray.X * 100, ray.Y * 100, ray.Z * 100);
        UboPacking.WriteFloat(bytes, 12, 1);
        UboPacking.WriteVec4(bytes, 32, 1, 0, 0, 0);
        UboPacking.WriteVec4(bytes, 48, 0, 1, 0, 0);
        UboPacking.WriteVec4(bytes, 64, 0, 0, 16, 0);
        UboPacking.WriteInt32(bytes, 80, 1);
        inputs.SetBytes(bytes);
    }
    #endregion
    #region Protected API
    /// <summary>Validates the complete typed input set before publishing prepared descriptors.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        return new(Shader,samplers:[
            new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"vge_atmosphereAerialRadiance"),Radiance),
            new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"vge_atmosphereAerialAttenuation"),Attenuation),
            new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"vge_lightShaftOcclusion"),Occlusion)],
            uniforms:[new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,"AerialLookupInputs"),inputs.Capture())]);
    }
    #endregion
}
