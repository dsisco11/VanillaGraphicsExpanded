namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Identifies the intended consumer of buffer storage independently of native binding targets.</summary>
public enum BufferRole { Vertex, Index, Uniform, ShaderStorage, AtomicCounter, IndirectDraw, IndirectDispatch, PixelPack, PixelUnpack, CopySource, CopyDestination, TransformFeedback }
