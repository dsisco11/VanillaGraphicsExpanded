namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Upload bytes for one declared vertex binding; upload copies data into owned GPU storage.</summary>
internal readonly record struct GraphicsVertexData(int Binding, byte[] Data);
