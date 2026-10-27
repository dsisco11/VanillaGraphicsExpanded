using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Stages explicit solar geometry inputs for the existing independently authored raster fixture.</summary>
internal sealed class SolarRasterProgram : GpuProgram
{
    private static readonly GpuShaderContract contract=ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/sun-raster-linear"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes=new byte[64];
    private VgeFrameUniformBuffer? frame;
    private readonly TestFrameLightingPublication lighting = new();
    #region Public API
    /// <summary>Registers the raster executable and its shared numeric input block.</summary>
    public SolarRasterProgram()
    {
        inputs=OwnUniformBuffer(new PackedUniformBuffer(64));
        foreach(var stage in contract.Stages)ProgramLayout.RegisterContract(stage.Bindings);
    }
    /// <summary>Borrows the controlled shared camera snapshot for this solar receiver.</summary>
    internal VgeFrameUniformBuffer FrameInputs
    {
        set { RequireInputMutation(); frame = value; }
    }
    /// <summary>Identifies the packaged solar vertex and fragment binary pair.</summary>
    internal override GpuShaderContract ProgramContract=>contract;
    /// <summary>Captures already extincted disk radiance, horizon clipping and scene routing.</summary>
    internal void Capture(Vector3 radiance,float horizon)
    {
        RequireInputMutation();
        UboPacking.WriteInt32(bytes,4,1);
        UboPacking.WriteVec4(bytes,16,0,0,-1,horizon);
        UboPacking.WriteVec4(bytes,32,radiance.X,radiance.Y,radiance.Z,.00465f);
        UboPacking.WriteFloat(bytes,48,AtmosphereSolarDiskStorage.MaximumScale(radiance,1,1));
        inputs.SetBytes(bytes);
    }
    #endregion
    #region Protected API
    /// <summary>Publishes the guarded numeric block through the prepared shader submission contract.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        return new(Shader,uniforms:[new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,"VgeFrameUBO"),frame?.Capture()),
            new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,"SunInputs"),inputs.Capture())],
            buffers:[new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock,"FrameLighting"),BufferRole.ShaderStorage,lighting.Buffer)]);
    }
    /// <summary>Retires the borrowed storage publication with its independent raster fixture.</summary>
    protected override void ReleaseResources()
    {
        lighting.Dispose();
        base.ReleaseResources();
    }
    #endregion
}
