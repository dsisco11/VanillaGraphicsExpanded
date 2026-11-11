using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Validates compiler-owned stage linkage and derives the final producer's clipping extent.</summary>
internal static class GraphicsClipDistances
{
    #region Public API
    /// <summary>Resolves immutable clip enables once during executable preparation, including cached executables.</summary>
    internal static int Resolve(IReadOnlyList<RHIShaderStageDescription> stages, int maximum)
    {
        // Source-only legacy RHI callers retain their explicitly unsupported custom-clipping route.
        // Packaged production stages cannot substitute absent facts with that route.
        if (stages.All(stage => stage.Format == ShaderCodeFormat.Glsl)) return 0;
        if (stages.Any(stage => stage.Format != ShaderCodeFormat.Spirv || stage.Interface == null))
            throw new InvalidOperationException("Graphics SPIR-V requires compiler interface metadata for every stage.");

        PackagedClipDistance? previous = null;
        int enabled = 0;
        foreach (var stage in stages.OrderBy(stage => Order(stage.Stage)))
        {
            var input = stage.Interface!.ClipInput;
            var output = stage.Interface.ClipOutput;
            Validate(input, maximum);
            Validate(output, maximum);
            if (input.Active && (previous is not { Active: true } || previous.DeclaredExtent != input.DeclaredExtent))
                throw new InvalidOperationException("Active clip-distance input does not match its preceding stage output.");
            if (stage.Stage == ShaderStage.Fragment)
            {
                if (output.Active) throw new InvalidOperationException("A fragment stage cannot produce clip distances.");
                continue;
            }
            previous = output;
            if (stage.Stage is ShaderStage.Vertex or ShaderStage.TessellationEvaluation or ShaderStage.Geometry)
                enabled = output.Active ? checked((int)output.DeclaredExtent) : 0;
        }
        return enabled;
    }
    #endregion

    #region Private
    /// <summary>Rejects absent facts and enabled extents beyond the context's supported plane count.</summary>
    private static void Validate(PackagedClipDistance? value, int maximum)
    {
        if (value == null || value.DeclaredExtent > int.MaxValue ||
            value.Active && (value.DeclaredExtent == 0 || value.DeclaredExtent > maximum))
            throw new InvalidOperationException("Clip-distance metadata is missing or exceeds the context limit.");
    }

    /// <summary>Orders programmable stages by data flow rather than descriptor insertion order.</summary>
    private static int Order(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => 0,
        ShaderStage.TessellationControl => 1,
        ShaderStage.TessellationEvaluation => 2,
        ShaderStage.Geometry => 3,
        ShaderStage.Fragment => 4,
        _ => throw new InvalidOperationException("A graphics executable contains a non-graphics stage.")
    };
    #endregion
}
