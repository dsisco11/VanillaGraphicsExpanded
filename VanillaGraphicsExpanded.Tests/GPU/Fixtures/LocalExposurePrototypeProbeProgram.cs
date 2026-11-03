using System.Collections.Generic;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Publishes typed inputs for the shared direct-observation local-exposure helper probe.</summary>
internal sealed class LocalExposurePrototypeProbeProgram : GpuProgram
{
    private static readonly GpuShaderContract contract =
        ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/local-exposure-prototype"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[48];
    private GpuTexture? source;
    private GpuTexture? baseImage;
    private GpuTexture? gradingLookup;

    #region Public API
    /// <summary>Uses the single declared probe whose output is unavailable from the production display shader.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Registers the declared helper probe interface before executable preparation.</summary>
    public LocalExposurePrototypeProbeProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Captures one helper operation and its borrowed source textures for the next draw.</summary>
    internal void Capture(LocalExposureProbeOperation operation, Vector4 parameters, Vector4 strengthsAndBounds,
        GpuTexture sourceImage, GpuTexture? localBase = null, GpuTexture? lookup = null)
    {
        RequireInputMutation();
        source = sourceImage;
        baseImage = localBase ?? sourceImage;
        gradingLookup = lookup;
        UboPacking.WriteVec4(bytes, 0, parameters.X, parameters.Y, parameters.Z, parameters.W);
        UboPacking.WriteVec4(bytes, 16, strengthsAndBounds.X, strengthsAndBounds.Y,
            strengthsAndBounds.Z, strengthsAndBounds.W);
        UboPacking.WriteInt32(bytes, 32, (int)operation);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes captured numeric inputs and borrowed textures through the prepared binding table.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        if (source is null) throw new InvalidOperationException("Capture the local-exposure probe inputs before submission.");
        var samplers = new List<RHISamplerBinding>
        {
            new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "sourceImage"), source),
            new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "baseImage"), baseImage!)
        };
        if (gradingLookup is not null)
            samplers.Add(new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "gradingLookup"), gradingLookup));
        return new(Shader, samplers: samplers,
            uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,
                "LocalExposureProbeInputs"), inputs.Capture())]);
    }
    #endregion
}

/// <summary>Selects one shared observation expression over the production local-exposure helper functions.</summary>
internal enum LocalExposureProbeOperation
{
    /// <summary>Returns guided base reconstruction and its validity bit.</summary>
    GuidedSample = 1,
    /// <summary>Tests candidate coordinates read from the source texture.</summary>
    GuidedCoordinate,
    /// <summary>Returns the bounded correction for source red values.</summary>
    Correction,
    /// <summary>Checks base, camera, and display metadata admission.</summary>
    InvalidCorrectionMetadata,
    /// <summary>Checks nonfinite display-pivot rejection.</summary>
    InvalidDisplayPivot,
    /// <summary>Exposes correction and residual detail channels for independent assertions.</summary>
    BoundsAndDetail,
    /// <summary>Checks zero-strength hard limits.</summary>
    HardLimit,
    /// <summary>Exposes the helper's invalid-pixel boundary result.</summary>
    FilterLocalBase,
    /// <summary>Applies guided correction directly to physical RGB for independent numerical comparison.</summary>
    ApplyCorrection = 9,
    /// <summary>Returns the selected undithered LUT lookup for the authored pre-display oracle.</summary>
    GradingLookup = 10
}
