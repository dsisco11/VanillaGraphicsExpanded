using OpenTK.Graphics.OpenGL;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Names engine-owned program objects and stages for graphics captures.</summary>
internal static class EngineShaderDebugLabels
{
    #region Compilation lifecycle
    /// <summary>Labels the current executable after compilation, including replacement and reload objects.</summary>
    internal static void Apply(ShaderProgram program)
    {
#if DEBUG
        // VGE-owned programs already label their resources through the GPU abstractions.
        if (program.AssetDomain == Constants.ModId) return;
        string domain = string.IsNullOrWhiteSpace(program.AssetDomain) ? "game" : program.AssetDomain;
        string pass = string.IsNullOrWhiteSpace(program.PassName) ? program.GetType().Name : program.PassName;
        string name = $"{domain}:{pass}";
        GlDebug.TryLabel(ObjectLabelIdentifier.Program, program.ProgramId, name);
        // Shader handles change on reload; label the current objects rather than caching numeric IDs.
        GlDebug.TryLabel(ObjectLabelIdentifier.Shader, program.VertexShader?.ShaderId ?? 0, $"{name}.vertex");
        GlDebug.TryLabel(ObjectLabelIdentifier.Shader, program.FragmentShader?.ShaderId ?? 0, $"{name}.fragment");
        GlDebug.TryLabel(ObjectLabelIdentifier.Shader, program.GeometryShader?.ShaderId ?? 0, $"{name}.geometry");
#endif
    }
    #endregion
}
