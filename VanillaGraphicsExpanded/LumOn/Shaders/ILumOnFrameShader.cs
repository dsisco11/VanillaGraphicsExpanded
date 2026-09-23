using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Accepts externally owned frame data through the shader's declared binding contract.</summary>
internal interface ILumOnFrameShader
{
    /// <summary>Binds frame data without taking ownership; inactive or absent blocks are left untouched.</summary>
    GpuUniformBuffer FrameUniformBuffer { set; }
}
