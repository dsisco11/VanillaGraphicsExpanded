using System;
using System.Linq;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Completes represented stage execution facts before the executable's single canonical publication.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    /// <summary>Reads only properties belonging to stages actually present in the frozen creation request.</summary>
    private GpuProgramMetadata ReadExecutableStageMetadata(GpuProgramObject program, GpuProgramMetadata data,
        IReadOnlyList<ShaderStage> stages)
    {
        if (stages.Contains(ShaderStage.Compute))
        {
            int[] size = new int[3];
            GL.GetProgram(program.ProgramId, (GetProgramParameterName)ArbComputeShader.ComputeWorkGroupSize, size);
            RequireContext();
            if (size.Any(value => value <= 0)) throw new InvalidOperationException("The linked compute workgroup size is unavailable.");
            data = data with { ComputeLocalSize = (size[0], size[1], size[2]) };
        }
        if (stages.Contains(ShaderStage.Geometry))
        {
            GL.GetProgram(program.ProgramId, GetProgramParameterName.GeometryInputType, out int mode);
            RequireContext();
            var input = (OpenTK.Graphics.OpenGL.PrimitiveType)mode;
            if (input is not (OpenTK.Graphics.OpenGL.PrimitiveType.Points or OpenTK.Graphics.OpenGL.PrimitiveType.Lines
                or OpenTK.Graphics.OpenGL.PrimitiveType.LinesAdjacency or OpenTK.Graphics.OpenGL.PrimitiveType.Triangles
                or OpenTK.Graphics.OpenGL.PrimitiveType.TrianglesAdjacency))
                throw new InvalidOperationException("The linked geometry input primitive is unavailable.");
            data = data with { GeometryInput = OpenGLPrimitiveValues.FromNative(input) };
        }
        if (stages.Contains(ShaderStage.TessellationEvaluation))
        {
            GL.GetProgram(program.ProgramId, GetProgramParameterName.TessGenMode, out int mode);
            RequireContext();
            GL.GetProgram(program.ProgramId, GetProgramParameterName.TessGenPointMode, out int points);
            RequireContext();
            if ((All)mode is not (All.Isolines or All.Triangles or All.Quads))
                throw new InvalidOperationException("The linked tessellation output primitive is unavailable.");
            data = data with { TessellationOutput = points != 0 ? PrimitiveTopology.Points
                : (All)mode == All.Isolines ? PrimitiveTopology.Lines : PrimitiveTopology.Triangles };
        }
        return data;
    }
    #endregion
}
