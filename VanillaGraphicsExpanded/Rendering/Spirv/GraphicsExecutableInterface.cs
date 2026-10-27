using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Projects linked vertex/fragment locations and stage facts from one canonical executable snapshot.</summary>
internal sealed class GraphicsExecutableInterface
{
    /// <summary>One linked interface resource, with arrays and matrices still represented by their native type.</summary>
    internal readonly record struct Variable(int Location, ActiveAttribType Type, int ArraySize, int LocationIndex);
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
        Inputs = ReadPublishedVariables(metadata, ProgramInterface.ProgramInput);
        Outputs = ReadPublishedVariables(metadata, ProgramInterface.ProgramOutput);
        GeometryInput = metadata.GeometryInput;
        TessellationOutput = metadata.TessellationOutput;
    }
    #endregion

    #region Private
    /// <summary>Requires represented native properties rather than substituting missing interface data with zeros.</summary>
    private static IReadOnlyList<Variable> ReadPublishedVariables(GpuProgramMetadata metadata, ProgramInterface kind)
    {
        var result = new List<Variable>();
        foreach (var resource in metadata.Resources.Where(resource => resource.Interface == kind))
        {
            int location = Property(resource, ProgramProperty.Location);
            if (location < 0) continue;
            int count = Property(resource, ProgramProperty.ArraySize);
            if (count < 1) throw new InvalidOperationException("Invalid linked interface array extent.");
            result.Add(new(location, (ActiveAttribType)Property(resource, ProgramProperty.Type), count,
                kind == ProgramInterface.ProgramOutput ? Property(resource, ProgramProperty.LocationIndex) : 0));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    /// <summary>Reads one already-published scalar resource property.</summary>
    private static int Property(GpuProgramResourceMetadata resource, ProgramProperty property)
        => resource.Properties.TryGetValue(property, out var values) && values.Length == 1 ? values[0]
            : throw new InvalidOperationException("The linked interface snapshot is incomplete.");
    #endregion
}
