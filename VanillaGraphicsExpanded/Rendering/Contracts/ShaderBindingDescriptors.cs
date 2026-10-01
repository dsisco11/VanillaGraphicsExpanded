namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>A sampler declaration shared without requiring a live program or texture.</summary>
public readonly record struct ShaderSamplerBinding(string Name, int Index, bool Required);
/// <summary>An image declaration shared without requiring a live program or image view.</summary>
public readonly record struct ShaderImageBinding(string Name, int Index, bool Required);
/// <summary>A uniform-buffer declaration shared without requiring a live buffer.</summary>
public readonly record struct ShaderUniformBlockBinding(string Name, int Index, bool Required);
/// <summary>A storage-buffer declaration shared without requiring a live buffer.</summary>
public readonly record struct ShaderStorageBlockBinding(string Name, int Index, bool Required);
/// <summary>A standalone uniform's explicitly declared interface location.</summary>
public readonly record struct ShaderUniformLocationBinding(string Name, int Index, bool Required);
/// <summary>An explicitly declared inter-stage interface location.</summary>
public readonly record struct ShaderVaryingLocationBinding(string Name, int Index, bool Required);
/// <summary>An explicitly declared fragment output attachment location.</summary>
public readonly record struct ShaderFragmentOutputLocationBinding(string Name, int Index, bool Required);
