using System;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Publishes identity or coherent adaptive tessellation before engine uniform locations are collected.</summary>
internal static class TerrainTessellationPrograms
{
    internal static bool DrawHookAvailable { get; set; }
    internal static bool MeshDrawHookAvailable { get; set; }
    internal static Action<string>? Log { get; set; }
    private static readonly ConditionalWeakTable<ShaderProgramBase, Installed> programs = new();
    /// <summary>Ties draw topology to the exact linked executable, not merely a recycled GL identifier.</summary>
    private sealed record Installed(int ProgramId, bool Adaptive);
    private static readonly WeakReference<ShaderProgram>[] family =
        [new(null!), new(null!), new(null!)];

    /// <summary>True only for the explicitly selected geometric detail mode.</summary>
    internal static bool Requested => ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode == TerrainSurfaceDetailMode.Tessellation;

    /// <summary>Requires all three live executables before any draw may displace.</summary>
    internal static bool Complete
    {
        get
        {
            foreach (var reference in family)
                if (!reference.TryGetTarget(out var program) || !programs.TryGetValue(program, out var installed)
                    || !installed.Adaptive || installed.ProgramId != program.ProgramId) return false;
            return Requested;
        }
    }

    #region Compilation and draw policy
    /// <summary>Prevents partially reloaded families from enabling geometry displacement.</summary>
    internal static void BeginReload()
    {
        foreach (var reference in family) reference.SetTarget(null!);
        TerrainDisplacementRuntime.ResetHistory();
    }
    /// <summary>Includes terrain shadows only when geometric displacement is requested.</summary>
    internal static bool Eligible(string? name) => name is "chunkopaque" or "chunktopsoil"
        || Requested && name == "chunkshadowmap";

    /// <summary>Compiles a candidate before publishing; unsupported or failed candidates retain the engine's ordinary program.</summary>
    internal static void Prepare(ShaderProgram program)
    {
        programs.Remove(program);
        if (!Requested || !Eligible(program.PassName) || program.AssetDomain == Constants.ModId) return;
        if (!TerrainTessellationPatches.TryGet(program.VertexShader, out var sources)) return;
        try
        {
            if (!DrawHookAvailable || !MeshDrawHookAvailable) throw new NotSupportedException("Terrain or shared-shadow topology interception is unavailable.");
            // The engine independently owns any higher requirements of its vertex/fragment variants.
            if (GpuSupport.ApiVersion is not { } version || version < new Version(4, 0))
                throw new NotSupportedException("Terrain tessellation requires OpenGL 4.0.");
            if (GpuSupport.MaxPatchVertices < 3 || GpuSupport.MaxTessGenLevel < 1
                || GlStateCache.Current.ProvokingVertex != ProvokingVertexMode.LastVertexConvention)
                throw new NotSupportedException("Unsupported patch limits or provoking-vertex convention.");
            if (Requested && (GpuSupport.MaxCombinedTextureImageUnits <= TerrainReliefBindings.HeightUnit
                || GpuSupport.MaxTessControlTextureImageUnits < 3 || GpuSupport.MaxTessEvaluationTextureImageUnits < 1
                || GpuSupport.MaxTessGenLevel < (int)ConfigModSystem.Config.MaterialAtlas.TerrainSubdivision.MaximumLevel))
                throw new NotSupportedException("Insufficient adaptive tessellation texture or subdivision limits.");
            if (program.GeometryShader is not null)
                throw new NotSupportedException("Terrain geometry-stage modifications are not supported by the identity path.");
            if (!TerrainTessellationLinker.TryCreate(program.VertexShader.ShaderId, program.FragmentShader.ShaderId,
                sources, program.VertexShader.PrefixCode + "\n#define VGE_PRODUCTION_DISPLACEMENT 1\n", 1,
                out int candidate, out string error)) throw new InvalidOperationException(error);
            int ordinary = program.ProgramId;
            program.ProgramId = candidate;
            programs.Add(program, new(candidate, Requested));
            if (Requested) family[program.PassName switch { "chunkopaque" => 0, "chunktopsoil" => 1, _ => 2 }].SetTarget(program);
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

    /// <summary>Identifies adaptive executables independently of current configuration during reload.</summary>
    internal static bool Adaptive(ShaderProgramBase program) => programs.TryGetValue(program, out var installed)
        && installed.Adaptive && installed.ProgramId == program.ProgramId;

    /// <summary>Changes only triangle submissions belonging to the published terrain executable.</summary>
    internal static PrimitiveType Topology(PrimitiveType original) =>
        original == PrimitiveType.Triangles && Active ? PrimitiveType.Patches : original;
    #endregion
}
