using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Accepts externally owned world-probe data without exposing uniform-block names to callers.</summary>
internal interface ILumOnWorldProbeShader
{
    /// <summary>Binds world-probe data without taking ownership; variants without the block are a no-op.</summary>
    GpuUniformBuffer WorldProbeUniformBuffer { set; }
}
