namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Names the independent GPU resource and interface index namespaces.</summary>
internal enum ShaderBindingKind
{
    /// <summary>An explicit location for a standalone shader uniform.</summary>
    UniformLocation,

    /// <summary>A texture unit supplying a sampled texture and its sampler state.</summary>
    Sampler,

    /// <summary>An image unit supplying a texture for shader image loads and stores.</summary>
    Image,

    /// <summary>An indexed uniform-buffer binding supplying a shader uniform block.</summary>
    UniformBlock,

    /// <summary>An indexed shader-storage-buffer binding supplying a shader storage block.</summary>
    StorageBlock,

    /// <summary>An explicit input or output location connecting shader-stage varyings.</summary>
    VaryingLocation,

    /// <summary>An explicit fragment output location mapped through the framebuffer's draw-buffer routing.</summary>
    FragmentOutputLocation,

    /// <summary>An indexed atomic-counter-buffer binding supplying shader atomic counters.</summary>
    AtomicCounter
}
