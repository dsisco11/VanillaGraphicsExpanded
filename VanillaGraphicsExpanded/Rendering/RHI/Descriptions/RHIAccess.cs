using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Explicit subsequent access categories made visible by an ordered barrier.</summary>
[Flags]
public enum RHIAccess
{
    None = 0, Vertex = 1, Index = 2, Uniform = 4, TextureFetch = 8,
    Image = 16, Indirect = 32, PixelTransfer = 64, TextureUpdate = 128,
    BufferUpdate = 256, Framebuffer = 512, TransformFeedback = 1024,
    AtomicCounter = 2048, ShaderStorage = 4096, ClientMapped = 8192,
    QueryResult = 16384, All = 32767
}
