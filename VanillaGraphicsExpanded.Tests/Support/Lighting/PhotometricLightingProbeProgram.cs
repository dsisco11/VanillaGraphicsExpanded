using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;
using ShaderBuildTool.Spirv;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Owns typed inputs for direct observations of the shipped photometric and transport helpers.</summary>
internal sealed class PhotometricLightingProbeProgram : GpuProgram
{
    private static readonly GpuShaderContract contract = TestShaderPrograms.Create().Programs["tests/photometric-helpers"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[48];
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? atmosphereParameters;
    private GpuShaderStorageBuffer? frozenLighting;

    #region Public API
    /// <summary>Registers the one shared photometric helper observation and its typed input blocks.</summary>
    public PhotometricLightingProbeProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Identifies the packaged direct-helper observation program.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Stages the real frame calibration and two fixed storage blocks for the next draw.</summary>
    internal void Capture(PhotometricProbeOperation operation, int selector, Vector4 limits0, Vector4 limits1,
        VgeFrameUniformBuffer frameInputs, GpuShaderStorageBuffer atmosphere, GpuShaderStorageBuffer lighting)
    {
        RequireInputMutation();
        frame = frameInputs;
        atmosphereParameters = atmosphere;
        frozenLighting = lighting;
        UboPacking.WriteInt32(bytes, 0, (int)operation);
        UboPacking.WriteInt32(bytes, 4, selector);
        UboPacking.WriteVec4(bytes, 16, limits0.X, limits0.Y, limits0.Z, limits0.W);
        UboPacking.WriteVec4(bytes, 32, limits1.X, limits1.Y, limits1.Z, limits1.W);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes declared frame/input blocks and their borrowed storage through the prepared binding table.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        var frameInputs = frame ?? throw new InvalidOperationException("Photometric frame inputs have not been captured.");
        var atmosphere = atmosphereParameters ?? throw new InvalidOperationException("Atmosphere parameters have not been captured.");
        var lighting = frozenLighting ?? throw new InvalidOperationException("Frozen lighting has not been captured.");
        return new(Shader,
            buffers:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "AtmosphereParameters"),
                    BufferRole.ShaderStorage, new GpuBufferRef(atmosphere)),
                new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "FrozenFrameLighting"),
                    BufferRole.ShaderStorage, new GpuBufferRef(lighting))
            ],
            uniforms:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeFrameUBO"), frameInputs.Capture()),
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "PhotometricProbeInputs"), inputs.Capture())
            ]);
    }
    #endregion
}

/// <summary>Selects one direct production-helper observation in the shared photometric probe.</summary>
internal enum PhotometricProbeOperation
{
    /// <summary>Observes zero-distance ground blocking for inward surface sunlight.</summary>
    SurfaceInwardSunlight,
    /// <summary>Observes metre-scale altitude from the declared Float32 position.</summary>
    GroundGeometry,
    /// <summary>Observes calibrated outputs for both installed byte decoders.</summary>
    ActiveBlockDecoder,
    /// <summary>Observes the original smooth block falloff for either decoder.</summary>
    BlockFalloff,
    /// <summary>Observes the intermediate-field block response and high-value limit.</summary>
    BlockFade,
    /// <summary>Observes terrain and stored-sky decoder endpoint normalization.</summary>
    SkyAvailability,
    /// <summary>Observes point illuminance and emission helper outputs directly.</summary>
    PointAndEmission,
    /// <summary>Observes storage containment and its independent validity/clipping flags.</summary>
    StorageProtection,
    /// <summary>Observes block conversion, diffuse incidence, and persistent-light encoding.</summary>
    BlockAndPersistent,
    /// <summary>Observes bounded water source transport and invalid winding rejection.</summary>
    BoundaryTransport,
    /// <summary>Observes frozen physical storage conversion and frame-history admission.</summary>
    FrozenStorage,
    /// <summary>Observes visible-disk storage and directional-light admission boundaries.</summary>
    VisibleDisk
}
