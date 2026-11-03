using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;
using ShaderBuildTool.Spirv;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns typed inputs for the direct forward-surface helper observation.</summary>
internal sealed class PbrForwardSurfaceProbeProgram : GpuProgram
{
    private static readonly IReadOnlyDictionary<string, GpuShaderContract> contracts = TestShaderPrograms.Create().Programs;
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[80];
    private VgeFrameUniformBuffer? frame;
    private VgeLightsUniformBuffer? lights;
    private GpuShaderStorageBuffer? frozenLighting;
    private GpuTexture? shadow;
    private GpuTexture? aerialRadiance;
    private GpuTexture? aerialAttenuation;
    private GpuTexture? lightShaftOcclusion;

    #region Public API
    /// <summary>Registers the declared input layout and retains its typed input block.</summary>
    public PbrForwardSurfaceProbeProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
        foreach (var stage in contracts["tests/pbr-forward-surface"].Stages)
            ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Resolves the immutable variant selected by the owner before settings are first captured.</summary>
    internal override GpuShaderContract ProgramContract =>
        contracts.TryGetValue(PassName, out var contract) ? contract : contracts["tests/pbr-forward-surface"];

    /// <summary>Stages the exact authored scenario, view matrix and production resources for one draw.</summary>
    internal void Capture(int scenario, float[] view, VgeFrameUniformBuffer frameInputs,
        VgeLightsUniformBuffer lightInputs, GpuShaderStorageBuffer lighting, GpuTexture shadowMap,
        GpuTexture radiance, GpuTexture attenuation, GpuTexture shaftOcclusion)
    {
        RequireInputMutation();
        frame = frameInputs;
        lights = lightInputs;
        frozenLighting = lighting;
        shadow = shadowMap;
        aerialRadiance = radiance;
        aerialAttenuation = attenuation;
        lightShaftOcclusion = shaftOcclusion;
        System.Buffer.BlockCopy(view, 0, bytes, 0, 64);
        UboPacking.WriteInt32(bytes, 64, 2);
        UboPacking.WriteInt32(bytes, 68, scenario);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes frame/light blocks, shadow/aerial/energy textures and frozen storage by declared identity.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        var frameInputs = frame ?? throw new InvalidOperationException("Forward-surface frame inputs have not been captured.");
        var lightInputs = lights ?? throw new InvalidOperationException("Forward-surface light inputs have not been captured.");
        var lighting = frozenLighting ?? throw new InvalidOperationException("Forward-surface storage has not been captured.");
        var shadowMap = shadow ?? throw new InvalidOperationException("Forward-surface shadow map has not been captured.");
        var radiance = aerialRadiance ?? throw new InvalidOperationException("Forward-surface aerial radiance has not been captured.");
        var attenuation = aerialAttenuation ?? throw new InvalidOperationException("Forward-surface aerial attenuation has not been captured.");
        var shaftOcclusion = lightShaftOcclusion ?? throw new InvalidOperationException("Forward-surface shaft occlusion has not been captured.");
        return new(Shader,
            samplers:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "shadowMapFar"), shadowMap,
                    Sampler: GpuSamplers.ShadowCompareLinearClamp),
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_atmosphereAerialRadiance"), radiance),
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_atmosphereAerialAttenuation"), attenuation),
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_lightShaftOcclusion"), shaftOcclusion,
                    Sampler: GpuSamplers.LinearClamp),
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_microfacetEnergy"),
                    MicrofacetEnergyLookup.Prepare(), Sampler: GpuSamplers.LinearClamp)
            ],
            buffers:
            [new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "FrozenFrameLighting"),
                BufferRole.ShaderStorage, new GpuBufferRef(lighting))],
            uniforms:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeFrameUBO"), frameInputs.Capture()),
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeLightsUBO"), lightInputs.Capture()),
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "TestInputs"), inputs.Capture())
            ]);
    }
    #endregion
}
