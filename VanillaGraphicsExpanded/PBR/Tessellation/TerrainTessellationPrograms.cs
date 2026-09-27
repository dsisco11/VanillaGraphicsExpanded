using System;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Publishes optional identity tessellation on engine terrain objects before uniform locations are collected.</summary>
internal static class TerrainTessellationPrograms
{
    internal static bool DrawHookAvailable { get; set; }
    internal static Action<string>? Log { get; set; }
    private static readonly ConditionalWeakTable<ShaderProgramBase, Installed> programs = new();
    /// <summary>Ties draw topology to the exact linked executable, not merely a recycled GL identifier.</summary>
    private sealed record Installed(int ProgramId);

    #region Compilation and draw policy
    /// <summary>Restricts the initial undisplaced path to engine opaque/topsoil families.</summary>
    internal static bool Eligible(string? name) => name is "chunkopaque" or "chunktopsoil";

    /// <summary>Compiles a candidate before publishing; unsupported or failed candidates retain the engine's ordinary program.</summary>
    internal static void Prepare(ShaderProgram program)
    {
        programs.Remove(program);
        int level = ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel;
        if (level == 0 || !Eligible(program.PassName) || program.AssetDomain == Constants.ModId) return;
        if (!TerrainTessellationPatches.TryGet(program.VertexShader, out var sources)) return;
        try
        {
            if (!DrawHookAvailable) throw new NotSupportedException("Grouped terrain topology interception is unavailable.");
            GpuSupport.Initialize();
            // The engine independently owns any higher requirements of its vertex/fragment variants.
            if (GpuSupport.ApiVersion is not { } version || version < new Version(4, 0))
                throw new NotSupportedException("Terrain tessellation requires OpenGL 4.0.");
            if (GpuSupport.MaxPatchVertices < 3 || GpuSupport.MaxTessGenLevel < level
                || GlStateCache.Current.ProvokingVertex != ProvokingVertexMode.LastVertexConvention)
                throw new NotSupportedException("Unsupported patch limits or provoking-vertex convention.");
            if (program.GeometryShader is not null)
                throw new NotSupportedException("Terrain geometry-stage modifications are not supported by the identity path.");
            if (!TerrainTessellationLinker.TryCreate(program.VertexShader.ShaderId, program.FragmentShader.ShaderId,
                sources, program.VertexShader.PrefixCode, level, out int candidate, out string error)) throw new InvalidOperationException(error);
            int ordinary = program.ProgramId;
            program.ProgramId = candidate;
            programs.Add(program, new(candidate));
            GL.DeleteProgram(ordinary);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[VGE] {program.PassName}: ordinary terrain retained: {ex.Message}");
        }
    }

    /// <summary>Clears metadata before disposal; failed in-place compiles retain the old executable and its topology.</summary>
    internal static void Forget(ShaderProgramBase program) => programs.Remove(program);

    /// <summary>Determines topology from the active managed program and its currently installed executable.</summary>
    internal static bool Active => ShaderProgramBase.CurrentShaderProgram is { } program
        && programs.TryGetValue(program, out var installed) && installed.ProgramId == program.ProgramId;

    /// <summary>Changes only triangle submissions belonging to the published terrain executable.</summary>
    internal static PrimitiveType Topology(PrimitiveType original) =>
        original == PrimitiveType.Triangles && Active ? PrimitiveType.Patches : original;
    #endregion
}
