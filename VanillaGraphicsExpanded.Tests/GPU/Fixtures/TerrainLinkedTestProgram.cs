using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Publishes the linked engine uniform table for runtime terrain draw tests.</summary>
internal sealed class TerrainLinkedTestProgram : ShaderProgram
{
    #region Linked interface
    /// <summary>Mirrors engine compilation's uniform lookup after optional tessellation linking.</summary>
    internal void PopulateDisplacementUniforms()
    {
        var layout = GpuProgramLayout.TryBuild(ProgramId);
        foreach (string name in new[] { "vge_displacementEnabled", "vge_displacementReactive",
            "vge_tessellationDistance", "vge_tessellationPixels", "vge_tessellationFocalPixels",
            "vge_displacementTex", "vge_displacementRecords", "vge_normalDepthTex" })
            uniformLocations[name] = layout.GetUniformLocation(ProgramId, name);
    }
    #endregion
}
