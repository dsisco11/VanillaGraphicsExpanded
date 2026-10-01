using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Owns liquid SPIR-V and publishes mesh-pool parameters through the engine shader interface.</summary>
[ShaderProgram("Contract", "pbr_liquid", 3)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_liquid.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_liquid.fsh")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(CaptureMode))]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
internal sealed partial class LiquidShaderProgram : GpuProgram, IShaderProgram
{

    #region Private: GPU binding declarations
    /// <summary>Declares the VgeLiquidFrameParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidFrameParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuUniformBuffer FrameParameters { set; }
    /// <summary>Declares the VgeLiquidDrawParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidDrawParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuUniformBuffer DrawParameters { set; }
    /// <summary>Declares the VgeLiquidWaveParams UniformBlock slot.</summary>
    [ShaderBinding("VgeLiquidWaveParams", ShaderBindingKind.UniformBlock, 15, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuUniformBuffer WaveParameters { set; }
    /// <summary>Declares the terrainTex Sampler slot.</summary>
    [ShaderBinding("terrainTex", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture TerrainTex { set; }
    /// <summary>Declares the depthTex Sampler slot.</summary>
    [ShaderBinding("depthTex", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture DepthTex { set; }
    /// <summary>Declares the vge_materialParamsTex Sampler slot.</summary>
    [ShaderBinding("vge_materialParamsTex", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture MaterialParamsTex { set; }
    /// <summary>Declares the shadowMapNear Sampler slot.</summary>
    [ShaderBinding("shadowMapNear", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture ShadowMapNearTexture { set; }
    /// <summary>Declares the shadowMapFar Sampler slot.</summary>
    [ShaderBinding("shadowMapFar", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture ShadowMapFarTexture { set; }
    /// <summary>Declares the vge_atmosphereAerialRadiance Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture AtmosphereAerialRadiance { set; }
    /// <summary>Declares the vge_atmosphereAerialAttenuation Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture AtmosphereAerialAttenuation { set; }
    /// <summary>Declares the terrainTex UniformLocation slot.</summary>
    [ShaderBinding("terrainTex", ShaderBindingKind.UniformLocation, 100, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private static partial ShaderUniformLocationBinding TerrainTexLocation { get; }
    /// <summary>Declares the depthTex UniformLocation slot.</summary>
    [ShaderBinding("depthTex", ShaderBindingKind.UniformLocation, 101, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private static partial ShaderUniformLocationBinding DepthTexLocation { get; }
    /// <summary>Declares the vge_materialParamsTex UniformLocation slot.</summary>
    [ShaderBinding("vge_materialParamsTex", ShaderBindingKind.UniformLocation, 102, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private static partial ShaderUniformLocationBinding MaterialParamsTexLocation { get; }
    #endregion

    /// <summary>Selects normal OIT output or a precompiled diagnostic output for GPU tests.</summary>
    [ShaderOption("VGE_LIQUID_CAPTURE_MODE", 0, Domain = new object[] { 0, 1, 2 })]
    internal partial int CaptureMode { get; set; }
    private readonly LiquidDrawParamsUbo draw = new();
    private readonly LiquidFrameParamsUbo frame = new();
    private readonly LiquidWaveParamsUbo wave = new();
    /// <summary>Returns the generated offline contract.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    #region Binding
    /// <summary>Installs the same resource contract used by the offline compiler.</summary>
    protected override GpuProgramLayout CreateLayout()
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(GpuShaderContracts.Create("pbr_liquid"));
        return layout;
    }

    /// <summary>Publishes an immutable draw snapshot before the engine's following mesh submission.</summary>
    private void PublishDraw()
    {
        if (!GpuUniformRingSystem.TryBind(this, LiquidDrawParamsUbo.BlockName, draw.Bytes, "VGE.Liquid.Draw", true))
            throw new InvalidOperationException("Liquid draw parameters require an active uniform ring and linked block.");
    }

    /// <summary>Publishes frame inputs once after all CPU-side fields have been captured.</summary>
    private void PublishFrame()
    {
        if (!GpuUniformRingSystem.TryBind(this, LiquidFrameParamsUbo.BlockName, frame.Bytes, "VGE.Liquid.Frame", true))
            throw new InvalidOperationException("Liquid frame parameters require an active uniform ring and linked block.");
    }

    /// <summary>Binds an externally owned image using the program's declared resource slot.</summary>
    private void BindImage(string name, int texture, TextureTarget target, int sampler)
        => ProgramLayout.TryBindSamplerTextureActive(ProgramId, name, target, texture, sampler, LayoutWarn);
    #endregion
}
