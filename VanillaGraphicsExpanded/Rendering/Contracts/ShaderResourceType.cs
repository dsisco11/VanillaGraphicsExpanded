namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Engine-independent sampler and image types used by authored binding contracts.</summary>
/// <remarks>Values describe authored shader resources independently of native API tokens.</remarks>
public enum ShaderResourceType
{
    /// <summary>Leaves the exact resource type unconstrained.</summary>
    Unspecified = 0,
    /// <summary>Identifies the Sampler1D resource type.</summary>
    Sampler1D,
    /// <summary>Identifies the Sampler2D resource type.</summary>
    Sampler2D,
    /// <summary>Identifies the Sampler3D resource type.</summary>
    Sampler3D,
    /// <summary>Identifies the SamplerCube resource type.</summary>
    SamplerCube,
    /// <summary>Identifies the Sampler1DShadow resource type.</summary>
    Sampler1DShadow,
    /// <summary>Identifies the Sampler2DShadow resource type.</summary>
    Sampler2DShadow,
    /// <summary>Identifies the Sampler2DRect resource type.</summary>
    Sampler2DRect,
    /// <summary>Identifies the Sampler2DRectShadow resource type.</summary>
    Sampler2DRectShadow,
    /// <summary>Identifies the Sampler1DArray resource type.</summary>
    Sampler1DArray,
    /// <summary>Identifies the Sampler2DArray resource type.</summary>
    Sampler2DArray,
    /// <summary>Identifies the SamplerBuffer resource type.</summary>
    SamplerBuffer,
    /// <summary>Identifies the Sampler1DArrayShadow resource type.</summary>
    Sampler1DArrayShadow,
    /// <summary>Identifies the Sampler2DArrayShadow resource type.</summary>
    Sampler2DArrayShadow,
    /// <summary>Identifies the SamplerCubeShadow resource type.</summary>
    SamplerCubeShadow,
    /// <summary>Identifies the IntSampler1D resource type.</summary>
    IntSampler1D,
    /// <summary>Identifies the IntSampler2D resource type.</summary>
    IntSampler2D,
    /// <summary>Identifies the IntSampler3D resource type.</summary>
    IntSampler3D,
    /// <summary>Identifies the IntSamplerCube resource type.</summary>
    IntSamplerCube,
    /// <summary>Identifies the IntSampler2DRect resource type.</summary>
    IntSampler2DRect,
    /// <summary>Identifies the IntSampler1DArray resource type.</summary>
    IntSampler1DArray,
    /// <summary>Identifies the IntSampler2DArray resource type.</summary>
    IntSampler2DArray,
    /// <summary>Identifies the IntSamplerBuffer resource type.</summary>
    IntSamplerBuffer,
    /// <summary>Identifies the UnsignedIntSampler1D resource type.</summary>
    UnsignedIntSampler1D,
    /// <summary>Identifies the UnsignedIntSampler2D resource type.</summary>
    UnsignedIntSampler2D,
    /// <summary>Identifies the UnsignedIntSampler3D resource type.</summary>
    UnsignedIntSampler3D,
    /// <summary>Identifies the UnsignedIntSamplerCube resource type.</summary>
    UnsignedIntSamplerCube,
    /// <summary>Identifies the UnsignedIntSampler2DRect resource type.</summary>
    UnsignedIntSampler2DRect,
    /// <summary>Identifies the UnsignedIntSampler1DArray resource type.</summary>
    UnsignedIntSampler1DArray,
    /// <summary>Identifies the UnsignedIntSampler2DArray resource type.</summary>
    UnsignedIntSampler2DArray,
    /// <summary>Identifies the UnsignedIntSamplerBuffer resource type.</summary>
    UnsignedIntSamplerBuffer,
    /// <summary>Identifies the SamplerCubeMapArray resource type.</summary>
    SamplerCubeMapArray,
    /// <summary>Identifies the SamplerCubeMapArrayShadow resource type.</summary>
    SamplerCubeMapArrayShadow,
    /// <summary>Identifies the IntSamplerCubeMapArray resource type.</summary>
    IntSamplerCubeMapArray,
    /// <summary>Identifies the UnsignedIntSamplerCubeMapArray resource type.</summary>
    UnsignedIntSamplerCubeMapArray,
    /// <summary>Identifies the Image1D resource type.</summary>
    Image1D,
    /// <summary>Identifies the Image2D resource type.</summary>
    Image2D,
    /// <summary>Identifies the Image3D resource type.</summary>
    Image3D,
    /// <summary>Identifies the Image2DRect resource type.</summary>
    Image2DRect,
    /// <summary>Identifies the ImageCube resource type.</summary>
    ImageCube,
    /// <summary>Identifies the ImageBuffer resource type.</summary>
    ImageBuffer,
    /// <summary>Identifies the Image1DArray resource type.</summary>
    Image1DArray,
    /// <summary>Identifies the Image2DArray resource type.</summary>
    Image2DArray,
    /// <summary>Identifies the ImageCubeMapArray resource type.</summary>
    ImageCubeMapArray,
    /// <summary>Identifies the Image2DMultisample resource type.</summary>
    Image2DMultisample,
    /// <summary>Identifies the Image2DMultisampleArray resource type.</summary>
    Image2DMultisampleArray,
    /// <summary>Identifies the IntImage1D resource type.</summary>
    IntImage1D,
    /// <summary>Identifies the IntImage2D resource type.</summary>
    IntImage2D,
    /// <summary>Identifies the IntImage3D resource type.</summary>
    IntImage3D,
    /// <summary>Identifies the IntImage2DRect resource type.</summary>
    IntImage2DRect,
    /// <summary>Identifies the IntImageCube resource type.</summary>
    IntImageCube,
    /// <summary>Identifies the IntImageBuffer resource type.</summary>
    IntImageBuffer,
    /// <summary>Identifies the IntImage1DArray resource type.</summary>
    IntImage1DArray,
    /// <summary>Identifies the IntImage2DArray resource type.</summary>
    IntImage2DArray,
    /// <summary>Identifies the IntImageCubeMapArray resource type.</summary>
    IntImageCubeMapArray,
    /// <summary>Identifies the IntImage2DMultisample resource type.</summary>
    IntImage2DMultisample,
    /// <summary>Identifies the IntImage2DMultisampleArray resource type.</summary>
    IntImage2DMultisampleArray,
    /// <summary>Identifies the UnsignedIntImage1D resource type.</summary>
    UnsignedIntImage1D,
    /// <summary>Identifies the UnsignedIntImage2D resource type.</summary>
    UnsignedIntImage2D,
    /// <summary>Identifies the UnsignedIntImage3D resource type.</summary>
    UnsignedIntImage3D,
    /// <summary>Identifies the UnsignedIntImage2DRect resource type.</summary>
    UnsignedIntImage2DRect,
    /// <summary>Identifies the UnsignedIntImageCube resource type.</summary>
    UnsignedIntImageCube,
    /// <summary>Identifies the UnsignedIntImageBuffer resource type.</summary>
    UnsignedIntImageBuffer,
    /// <summary>Identifies the UnsignedIntImage1DArray resource type.</summary>
    UnsignedIntImage1DArray,
    /// <summary>Identifies the UnsignedIntImage2DArray resource type.</summary>
    UnsignedIntImage2DArray,
    /// <summary>Identifies the UnsignedIntImageCubeMapArray resource type.</summary>
    UnsignedIntImageCubeMapArray,
    /// <summary>Identifies the UnsignedIntImage2DMultisample resource type.</summary>
    UnsignedIntImage2DMultisample,
    /// <summary>Identifies the UnsignedIntImage2DMultisampleArray resource type.</summary>
    UnsignedIntImage2DMultisampleArray,
    /// <summary>Identifies the Sampler2DMultisample resource type.</summary>
    Sampler2DMultisample,
    /// <summary>Identifies the IntSampler2DMultisample resource type.</summary>
    IntSampler2DMultisample,
    /// <summary>Identifies the UnsignedIntSampler2DMultisample resource type.</summary>
    UnsignedIntSampler2DMultisample,
    /// <summary>Identifies the Sampler2DMultisampleArray resource type.</summary>
    Sampler2DMultisampleArray,
    /// <summary>Identifies the IntSampler2DMultisampleArray resource type.</summary>
    IntSampler2DMultisampleArray,
    /// <summary>Identifies the UnsignedIntSampler2DMultisampleArray resource type.</summary>
    UnsignedIntSampler2DMultisampleArray,
}

