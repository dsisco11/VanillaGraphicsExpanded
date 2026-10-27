namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Expresses update frequency and intended producer/consumer access.</summary>
public enum BufferUsage { StaticDraw, StaticRead, StaticCopy, DynamicDraw, DynamicRead, DynamicCopy, StreamDraw, StreamRead, StreamCopy }
