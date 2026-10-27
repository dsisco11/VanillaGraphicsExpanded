using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;
/// <summary>Preserves the former raster exposure executable for matched numerical and timing comparisons.</summary>
internal class RasterCameraExposureShader : GpuProgram
{
    private readonly CameraExposureInputs inputs;
    #region Public API
    /// <summary>Selects the original metering executable.</summary>
    public RasterCameraExposureShader() : this(false) { }
    /// <summary>Registers the original raster resources without altering production contracts.</summary>
    protected RasterCameraExposureShader(bool adaptation)
    {
        var bindings=new GpuBindingContract();
        bindings.RegisterUniformBlockBinding("CameraExposureInputs", GpuBindingRegistry.Ubo.ShaderInputs);
        if(adaptation) { bindings.RegisterUniformBlockBinding("VgeFrameUBO", GpuBindingRegistry.Ubo.Frame); bindings.RegisterSamplerUnit("histogram",0); bindings.RegisterSamplerUnit("previousExposure",1); bindings.FragmentOutputLocations.Add("exposureEV",0); }
        else { bindings.RegisterSamplerUnit("sceneRadiance",0); bindings.FragmentOutputLocations.Add("histogram",0); }
        string name=adaptation?"raster-camera-adapt":"raster-camera-histogram";
        ProgramContract=new("tests/"+name,[new("tests/raster-camera.vsh","tests/raster-camera.vsh",ShaderStageKind.Vertex,new()),new("tests/"+name+".fsh","tests/"+name+".fsh",ShaderStageKind.Fragment,bindings)],1);
        ProgramLayout.RegisterContract(bindings);
        inputs=OwnUniformBuffer(new CameraExposureInputs());
    }
    /// <summary>Provides the immutable retained raster declaration.</summary>
    internal override GpuShaderContract ProgramContract { get; }
    /// <summary>Supplies scene or histogram input.</summary>
    internal GpuTexture? Source { get; set; }
    /// <summary>Supplies prior exposure for adaptation.</summary>
    internal GpuTexture? Previous { get; set; }
    /// <summary>Supplies the same shared camera duration as the compute path.</summary>
    internal VgeFrameUniformBuffer? Frame { get; set; }
    /// <summary>Captures one complete reference exposure policy.</summary>
    internal void Capture(CameraExposureParameters settings,bool reset)=>inputs.Capture(settings,reset);
    #endregion
    #region Protected API
    /// <summary>Publishes the original parameter and nearest-clamped texture inputs.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        bool adaptation=Previous is not null;
        return new(Shader,samplers:adaptation
            ? [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"histogram"),Source),new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"previousExposure"),Previous)]
            : [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"sceneRadiance"),Source)],
            uniforms:adaptation
            ? [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,"CameraExposureInputs"),inputs.Capture()),new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,"VgeFrameUBO"),Frame?.Capture())]
            : [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,"CameraExposureInputs"),inputs.Capture())]);
    }
    #endregion
}
/// <summary>Selects the retained original adaptation contract.</summary>
internal sealed class RasterCameraAdaptShader : RasterCameraExposureShader
{
    /// <summary>Creates the original adaptation reference owner.</summary>
    public RasterCameraAdaptShader() : base(true) { }
}
