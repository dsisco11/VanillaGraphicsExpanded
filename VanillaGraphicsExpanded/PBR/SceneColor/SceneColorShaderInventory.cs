using System.Collections.Generic;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Checks registered executables before the scene owner chooses a linear-color frame.</summary>
internal static class SceneColorShaderInventory
{
    // File and memory registrations share this engine-owned registry. Do not maintain
    // a second list that could miss a third-party memory shader or a replacement.
    private static readonly System.Reflection.FieldInfo? Programs =
        AccessTools.Field(typeof(ShaderRegistry), "shaderPrograms");

    #region Public API
    /// <summary>Rejects an unavailable registry or any unclassified installed program before scene submission.</summary>
    internal static bool IsCompatible(out string? unsupported)
    {
        if (Programs?.GetValue(null) is not ShaderProgram[] programs)
        {
            unsupported = "shader registry unavailable";
            return false;
        }
        return IsCompatible(programs, out unsupported);
    }

    /// <summary>Validates linked identities, explicit VGE capabilities and known engine-only non-color routes.</summary>
    internal static bool IsCompatible(IEnumerable<ShaderProgramBase?> programs, out string? unsupported)
    {
        foreach (var program in programs)
        {
            if (program is null) continue;
            // VGE consumers prepare their declared programs separately. Registry entries
            // alone cannot establish whether their resources and selected variants are ready.
            if (program is GpuProgram && program.GetType().Assembly == typeof(GpuProgram).Assembly) continue;
            if (program.ProgramId != 0 && (
                ShaderCapabilities.Has(program, ShaderCapability.SceneColorConvention)
                || ShaderCapabilities.Has(program, ShaderCapability.SceneMaterialCapture)
                || IsEngineAuxiliary(program))) continue;
            unsupported = program.PassName;
            return false;
        }
        unsupported = null;
        return true;
    }
    #endregion

    #region Private
    /// <summary>Recognizes installed engine data, UI and color-preserving passes without trusting third-party subclasses.</summary>
    private static bool IsEngineAuxiliary(ShaderProgramBase program)
        => program is ShaderProgram { LoadFromFile: true }
            && program.GetType().Assembly == typeof(ShaderProgram).Assembly
            && program.AssetDomain is null or "" or "game"
            && program.PassName is
            "chunkliquiddepth" or "chunkshadowmap" or "shadowmapentityanimated"
            or "cloudmap" or "ssao" or "bilateralblur"
            or "gui" or "guigear" or "guitopsoil" or "debugdepthbuffer"
            or "blit" or "texture2texture" or "blur" or "findbright" or "transparentcompose";
    #endregion
}
