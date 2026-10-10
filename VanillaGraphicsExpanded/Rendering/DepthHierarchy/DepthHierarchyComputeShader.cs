using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns the typed executable that generates a shared depth chain in one dispatch.</summary>
[ShaderProgram("Contract", "vge_depth_hierarchy", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "vge_depth_hierarchy.csh")]
internal sealed partial class DepthHierarchyComputeShader : GpuComputeProgram, IDepthHierarchyComputeBindings
{
    private readonly PackedUniformBuffer parameters = new(16);
    private readonly byte[] parameterBytes = new byte[16];
    #region Public API
    /// <summary>Loads the declared compute binary before adopting its executable lifetime.</summary>
    internal static DepthHierarchyComputeShader Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!GpuComputePipeline.TryCreateFromAssets(api, new ShaderSettings(Contract), out var pipeline,
            out string log, "DepthHierarchy.Compute", layout: layout))
            throw new InvalidOperationException("Depth hierarchy compute shader unavailable: " + log);
        return new DepthHierarchyComputeShader(pipeline!);
    }
    /// <summary>Stages one coherent extent and mip count for the next dispatch.</summary>
    internal void SetExtent(int width, int height, int levels)
    {
        RequireInputMutation();
        UboPacking.WriteIVec4(parameterBytes, 0, width, height, levels, 0);
        parameters.SetBytes(parameterBytes);
    }
    #endregion
    #region Private
    /// <summary>Adopts the linked executable and guards retained parameter mutation.</summary>
    private DepthHierarchyComputeShader(GpuComputePipeline pipeline) : base(pipeline)
    {
        OwnUniformBuffer(parameters);
    }
    /// <summary>Supplies the retained dispatch block.</summary>
    CpuUniformBuffer IDepthHierarchyComputeBindings.Parameters => parameters;
    #endregion
}
