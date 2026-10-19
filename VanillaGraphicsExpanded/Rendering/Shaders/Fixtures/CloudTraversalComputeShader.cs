using System;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.PBR.Clouds;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Runs cloud diagnostic helpers through production compute preparation and generated input publication.</summary>
[ShaderProgram("Contract", "tests/cloud-traversal", 2)]
[ShaderStage("Contract", ShaderStageKind.Compute, "tests/cloud-traversal.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(SelfShadowing))]
internal sealed partial class CloudTraversalComputeShader : GpuComputeProgram, ICloudTraversalComputeBindings
{
    private readonly PackedUniformBuffer ray = new(32);
    private readonly PackedUniformBuffer traversal = new(16);
    private CpuUniformBuffer? volume;

    #region Public API
    /// <summary>Selects the same compile-time transport policy as production cloud programs.</summary>
    [ShaderOptionReference(typeof(CloudShaderOptions),nameof(CloudShaderOptions.SelfShadowing))]
    internal static partial ShaderOption<bool> SelfShadowing { get; }
    /// <summary>Loads the registered offline fixture through the same executable owner used by runtime compute shaders.</summary>
    internal static CloudTraversalComputeShader Create(ICoreAPI api,bool selfShadowing=true)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!GpuComputePipeline.TryCreateFromAssets(api, new ShaderSettings(Contract).With(SelfShadowing,selfShadowing), out var pipeline,
                out string log, "Tests.Cloud.Traversal", layout: layout))
            throw new InvalidOperationException("Cloud traversal fixture unavailable: " + log);
        return new(pipeline!);
    }

    /// <summary>Borrows the caller's complete cloud parameters until they are replaced or the owner is retired.</summary>
    internal void SetVolume(CpuUniformBuffer inputs)
    {
        RequireInputMutation();
        volume = inputs ?? throw new ArgumentNullException(nameof(inputs));
    }

    /// <summary>Packs one independent ray and mode before the generated publication boundary.</summary>
    internal void SetRay(Vector3 origin, Vector3 direction, float limit, int mode)
    {
        RequireInputMutation();
        ReadOnlySpan<float> values = [origin.X, origin.Y, origin.Z, limit, direction.X, direction.Y, direction.Z, mode];
        ray.SetBytes(MemoryMarshal.AsBytes(values));
    }

    /// <summary>Explicitly selects the direct path or an authored hierarchy root.</summary>
    internal void SetTraversal(int? root)
    {
        RequireInputMutation();
        Span<byte> state = stackalloc byte[16];
        UboPacking.WriteIVec4(state, 0, root.HasValue ? 1 : 0, root ?? 0, 0, 0);
        traversal.SetBytes(state);
    }
    #endregion

    #region Private
    /// <summary>Owns the diagnostic input blocks while leaving native maps and result storage with the fixture caller.</summary>
    private CloudTraversalComputeShader(GpuComputePipeline pipeline) : base(pipeline)
    {
        OwnUniformBuffer(ray);
        OwnUniformBuffer(traversal);
    }

    /// <summary>Requires a staged cloud publication before activation.</summary>
    CpuUniformBuffer ICloudTraversalComputeBindings.VolumeInputs =>
        volume ?? throw new InvalidOperationException("Cloud traversal volume inputs were not staged.");
    /// <summary>Supplies the owned hierarchy selector to generated submission.</summary>
    CpuUniformBuffer ICloudTraversalComputeBindings.TraversalInputs => traversal;
    /// <summary>Supplies the owned ray block to generated submission.</summary>
    CpuUniformBuffer ICloudTraversalComputeBindings.RayInputs => ray;
    #endregion
}
