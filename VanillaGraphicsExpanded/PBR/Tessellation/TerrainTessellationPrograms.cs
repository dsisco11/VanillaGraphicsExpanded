using VanillaGraphicsExpanded.Rendering.Shaders;
using System;
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
    private static readonly WeakReference<ShaderProgram>[] family =
        [new(null!), new(null!), new(null!)];

    /// <summary>True only for the explicitly selected geometric detail mode.</summary>
    internal static bool Requested => ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode == (int)TerrainSurfaceDetailMode.Tessellation;

    /// <summary>Requires all three live executables before any draw may displace.</summary>
    internal static bool Complete
    {
        get
        {
            foreach (var reference in family)
                if (!reference.TryGetTarget(out var program) || !Adaptive(program)) return false;
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
        ShaderCapabilities.Remove(program, ShaderCapability.TerrainDisplacement);
        if (!Requested || !Eligible(program.PassName) || program.AssetDomain == Constants.ModId) return;
        if (!TerrainTessellationPatches.TryGet(program.VertexShader, out var sources)) return;
        try
        {
            if (!DrawHookAvailable || !MeshDrawHookAvailable) throw new NotSupportedException("Terrain or shared-shadow topology interception is unavailable.");
            // The engine independently owns any higher requirements of its vertex/fragment variants.
            if (GpuSupport.ApiVersion is not { } version || version < new Version(4, 0))
                throw new NotSupportedException("Terrain tessellation requires OpenGL 4.0.");
            if (GpuSupport.MaxPatchVertices < 3 || GpuSupport.MaxTessGenLevel < 1
                || StateCache.Current.ProvokingVertex != ProvokingVertexMode.LastVertexConvention)
                throw new NotSupportedException("Unsupported patch limits or provoking-vertex convention.");
            if (Requested && (GpuSupport.MaxCombinedTextureImageUnits <= TerrainReliefBindings.HeightUnit
                || GpuSupport.MaxTessControlTextureImageUnits < 3 || GpuSupport.MaxTessEvaluationTextureImageUnits < 1
                || GpuSupport.MaxTessGenLevel < (int)ConfigModSystem.Config.MaterialAtlas.TerrainSubdivision.MaximumLevel))
                throw new NotSupportedException("Insufficient adaptive tessellation texture or subdivision limits.");
            if (program.GeometryShader is not null)
                throw new NotSupportedException("Terrain geometry-stage modifications are not supported by the identity path.");
            if (!TerrainTessellationLinker.TryCreate(program.VertexShader.ShaderId, program.FragmentShader.ShaderId,
                sources, program.VertexShader.PrefixCode,
                out int candidate, out string error, $"{(string.IsNullOrWhiteSpace(program.AssetDomain) ? "game" : program.AssetDomain)}:{program.PassName}")) throw new InvalidOperationException(error);
            int ordinary = program.ProgramId;
            program.ProgramId = candidate;
            ShaderCapabilities.Publish(program, ShaderCapability.TerrainDisplacement);
            if (Requested) family[program.PassName switch { "chunkopaque" => 0, "chunktopsoil" => 1, _ => 2 }].SetTarget(program);
            GL.DeleteProgram(ordinary);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[VGE] {program.PassName}: ordinary terrain retained: {ex.Message}");
        }
    }

    /// <summary>Withdraws displacement metadata without removing other features owned by the executable.</summary>
    internal static void Forget(ShaderProgramBase program)
        => ShaderCapabilities.Remove(program, ShaderCapability.TerrainDisplacement);

    /// <summary>Determines topology from the active managed program and its currently installed executable.</summary>
    internal static bool Active => ShaderProgramBase.CurrentShaderProgram is { } program
        && Adaptive(program);

    /// <summary>Identifies adaptive executables independently of current configuration during reload.</summary>
    internal static bool Adaptive(ShaderProgramBase program)
        => ShaderCapabilities.Has(program, ShaderCapability.TerrainDisplacement);

    /// <summary>Changes only triangle submissions belonging to the published terrain executable.</summary>
    internal static PrimitiveType Topology(PrimitiveType original) =>
        original == PrimitiveType.Triangles && Active ? PrimitiveType.Patches : original;
    #endregion
}
