using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes Sampler creation without acquiring unrepresented metadata.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Requests Sampler creation; configuration and use remain separate operations.</summary>
    internal GpuSamplerMetadata? CreateSampler(out nint id)
    {
        id = GL.GenSampler();
        if (id == 0) return null;
        // Retain existing realization policy and restore unit zero's selection.
        using var binding = BindSamplerScope(0, (int)id);
        return new GpuSamplerMetadata();
    }
    #endregion
}
