using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns the dedicated compute executable and retained parameters for cloud height reduction.</summary>
[ShaderProgram("Contract", "cloud_hierarchy", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "cloud_hierarchy.csh")]
internal sealed partial class CloudHierarchyComputeShader : GpuComputeProgram, ICloudHierarchyBindings
{
    private readonly PackedUniformBuffer parameters = new(16);
    private readonly byte[] bytes = new byte[16];

    #region Public API
    /// <summary>Loads the packaged binary using the existing compute preparation and reload lifetime.</summary>
    internal static CloudHierarchyComputeShader Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!GpuComputePipeline.TryCreateFromAssets(api, new ShaderSettings(Contract), out var pipeline,
                out string log, "Cloud.Hierarchy", layout: layout))
            throw new InvalidOperationException("Cloud hierarchy compute unavailable: " + log);
        return new(pipeline!);
    }

    /// <summary>Stages one reduction level before dispatch, without mutating inputs during publication.</summary>
    internal void SetLevel(int nativeWidth, int targetWidth, bool nativeLeaves)
    {
        RequireInputMutation();
        UboPacking.WriteIVec4(bytes, 0, nativeWidth, targetWidth, nativeLeaves ? 1 : 0, 0);
        parameters.SetBytes(bytes);
    }
    #endregion

    #region Private
    /// <summary>Adopts the linked executable and owns only its retained CPU parameter block.</summary>
    private CloudHierarchyComputeShader(GpuComputePipeline pipeline) : base(pipeline)
    {
        OwnUniformBuffer(parameters);
    }

    /// <summary>Publishes the retained reduction parameters through the generated binding contract.</summary>
    CpuUniformBuffer ICloudHierarchyBindings.Parameters => parameters;
    #endregion
}
