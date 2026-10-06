using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Retains linked vertex/fragment locations and compiled clipping information for one executable revision.</summary>
internal sealed class GraphicsExecutableInterface
{
    /// <summary>One linked interface resource, with arrays and matrices still represented by their native type.</summary>
    internal readonly record struct Variable(int Location, ActiveAttribType Type, int ArraySize, int LocationIndex);
    internal IReadOnlyList<Variable> Inputs { get; }
    internal IReadOnlyList<Variable> Outputs { get; }
    internal int? ClipDistanceExtent { get; }
    internal ShaderStageKind FinalVertexStage { get; }
    internal PrimitiveType? GeometryInput { get; }
    internal PrimitiveType? TessellationOutput { get; }
    internal int ReflectionQueries { get; private set; }

    #region Public API
    /// <summary>Inspects the linked interface once and the exact captured binary used for specialization or binary-cache loading.</summary>
    internal GraphicsExecutableInterface(int program, ShaderLoadPlan plan, ShaderAssetReader read)
    {
        using var errors = new GlDebug.ErrorScope("Graphics executable interface preparation");
        Inputs = ReadVariables(program, ProgramInterface.ProgramInput);
        Outputs = ReadVariables(program, ProgramInterface.ProgramOutput);
        var final = plan.Stages.FirstOrDefault(s => s.Stage.Kind == ShaderStageKind.Geometry)
            ?? plan.Stages.FirstOrDefault(s => s.Stage.Kind == ShaderStageKind.TessellationEvaluation)
            ?? plan.Stages.Single(s => s.Stage.Kind == ShaderStageKind.Vertex);
        FinalVertexStage = final.Stage.Kind;
        ClipDistanceExtent = CompiledClipDistance.Read(final, read(final.BinaryPath));
        if (plan.Stages.Any(s => s.Stage.Kind == ShaderStageKind.Geometry))
        {
            GL.GetProgram(program, GetProgramParameterName.GeometryInputType, out int input);
            ReflectionQueries++;
            GeometryInput = (PrimitiveType)input;
        }
        if (plan.Stages.Any(s => s.Stage.Kind == ShaderStageKind.TessellationEvaluation))
        {
            GL.GetProgram(program, GetProgramParameterName.TessGenMode, out int mode);
            GL.GetProgram(program, GetProgramParameterName.TessGenPointMode, out int points);
            ReflectionQueries += 2;
            TessellationOutput = points != 0 ? PrimitiveType.Points
                : (All)mode == All.Isolines ? PrimitiveType.Lines : PrimitiveType.Triangles;
        }
    }
    #endregion

    #region Private
    /// <summary>Retains numeric locations without requiring optional SPIR-V debug names.</summary>
    private IReadOnlyList<Variable> ReadVariables(int program, ProgramInterface kind)
    {
        GL.GetProgramInterface(program, kind, ProgramInterfaceParameter.ActiveResources, out int count);
        ReflectionQueries++;
        ProgramProperty[] properties = kind == ProgramInterface.ProgramOutput
            ? [ProgramProperty.Location, ProgramProperty.Type, ProgramProperty.ArraySize, ProgramProperty.LocationIndex]
            : [ProgramProperty.Location, ProgramProperty.Type, ProgramProperty.ArraySize];
        int[] values = new int[properties.Length];
        var result = new List<Variable>();
        for (int index = 0; index < count; index++)
        {
            GL.GetProgramResource(program, kind, index, properties.Length, properties, values.Length, out _, values);
            ReflectionQueries++;
            // Built-ins have no user location; ClipDistance is separately inspected in its producing stage.
            if (values[0] >= 0)
            {
                if (values[2] < 1) throw new InvalidOperationException("Invalid linked interface array extent.");
                result.Add(new(values[0], (ActiveAttribType)values[1], values[2], values.Length == 4 ? values[3] : 0));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }
    #endregion
}
