using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Projects linked vertex/fragment locations and stage facts from one canonical executable snapshot.</summary>
internal sealed class GraphicsExecutableInterface
{
    /// <summary>One linked interface resource, with arrays and matrices still represented by their native type.</summary>
    internal readonly record struct Variable(int Location, ShaderValueType Type, int ArraySize, int LocationIndex);
    internal IReadOnlyList<Variable> Inputs { get; }
    internal IReadOnlyList<Variable> Outputs { get; }
    internal PrimitiveTopology? GeometryInput { get; }
    internal PrimitiveTopology? TessellationOutput { get; }

    #region Public API
    /// <summary>Projects linked numeric locations from the manager snapshot without repeating native reflection.</summary>
    internal GraphicsExecutableInterface(GpuProgramMetadata metadata)
    {
        if (!metadata.ExecutableAvailable || metadata.Resources.IsDefault)
            throw new InvalidOperationException("A complete linked interface snapshot is required.");
        Inputs = ReadPublishedVariables(metadata, ProgramResourceKind.ProgramInput);
        Outputs = ReadPublishedVariables(metadata, ProgramResourceKind.ProgramOutput);
        GeometryInput = metadata.GeometryInput;
        TessellationOutput = metadata.TessellationOutput;
    }
    #endregion

    #region Private
    /// <summary>Requires represented native properties rather than substituting missing interface data with zeros.</summary>
    private static IReadOnlyList<Variable> ReadPublishedVariables(GpuProgramMetadata metadata, ProgramResourceKind kind)
    {
        var result = new List<Variable>();
        foreach (var resource in metadata.Resources.Where(resource => resource.Interface == kind))
        {
            int location = resource.Location ?? throw new InvalidOperationException("Interface location is unavailable.");
            if (location < 0) continue;
            int count = resource.ArraySize ?? throw new InvalidOperationException("Interface extent is unavailable.");
            if (count < 1) throw new InvalidOperationException("Invalid linked interface array extent.");
            result.Add(new(location, resource.Type ?? throw new InvalidOperationException("Interface type is unavailable."), count,
                kind == ProgramResourceKind.ProgramOutput ? resource.LocationIndex ?? throw new InvalidOperationException("Output location index is unavailable.") : 0));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    #endregion
}
