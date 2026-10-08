using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Lends exposure history only to scene display consumers and restores its dedicated sampler unit.</summary>
internal static class CameraExposureDisplayBindings
{
    // Installed final composition uses units 0..4; unit 15 is available on the GLSL 330 baseline.
    private const int ExposureUnit = 15;
    private static StateCache.TextureScope? textureScope;
    private static StateCache.SamplerScope? samplerScope;
    #region Public API
    /// <summary>Clears reused display executables so UI and offscreen draws cannot inherit scene exposure.</summary>
    internal static void Reset(ShaderProgramBase program)
    {
        if (!program.HasUniform("vge_cameraExposureEnabled")) return;
        program.Uniform("vge_cameraExposureEnabled", 0);
        program.Uniform("vge_cameraManualEV", 0f);
    }
    /// <summary>Uses the published GPU exposure for display and the matching perceptual FXAA metric.</summary>
    internal static void Bind(ShaderProgramBase program, bool sceneInput)
    {
        EndBinding();
        if (!sceneInput || program.PassName is not ("final" or "luma")) return;
        if (!program.HasUniform("vge_cameraExposureEnabled"))
            throw new System.InvalidOperationException($"Camera exposure contract is missing from {program.PassName}.");
        var exposure = CameraExposureRenderer.DisplayExposure();
        program.Uniform("vge_cameraManualEV", exposure.ManualEV);
        program.Uniform("vge_cameraExposureEnabled", exposure.Texture is not null ? 1 : 0);
        if (exposure.Texture is null) return;
        // Retain the scope until the engine submission completes, including exceptional exits.
        textureScope = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, ExposureUnit, exposure.Texture.TextureId);
        samplerScope = StateCache.Current.BindSamplerScope(ExposureUnit, GpuSamplers.NearestClamp.SamplerId);
        program.Uniform("vge_cameraExposure", ExposureUnit);
    }
    /// <summary>Restores only the borrowed texture/sampler bindings after the owning engine invocation.</summary>
    internal static void EndBinding()
    {
        try { samplerScope?.Dispose(); }
        finally
        {
            samplerScope = null;
            try { textureScope?.Dispose(); }
            finally { textureScope = null; }
        }
    }
    #endregion
}
