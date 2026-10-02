namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Engine-independent sampler and image types used by authored binding contracts.</summary>
/// <remarks>Values use the standardized OpenGL type identifiers so linked types can be compared numerically.</remarks>
public enum ShaderResourceType
{
    /// <summary>Leaves the exact resource type unconstrained.</summary>
    Unspecified = 0,
    /// <summary>Identifies the Sampler1D resource type.</summary>
    Sampler1D = 0x8B5D,
    /// <summary>Identifies the Sampler2D resource type.</summary>
    Sampler2D = 0x8B5E,
    /// <summary>Identifies the Sampler3D resource type.</summary>
    Sampler3D = 0x8B5F,
    /// <summary>Identifies the SamplerCube resource type.</summary>
    SamplerCube = 0x8B60,
    /// <summary>Identifies the Sampler1DShadow resource type.</summary>
    Sampler1DShadow = 0x8B61,
    /// <summary>Identifies the Sampler2DShadow resource type.</summary>
    Sampler2DShadow = 0x8B62,
    /// <summary>Identifies the Sampler2DRect resource type.</summary>
    Sampler2DRect = 0x8B63,
    /// <summary>Identifies the Sampler2DRectShadow resource type.</summary>
    Sampler2DRectShadow = 0x8B64,
    /// <summary>Identifies the Sampler1DArray resource type.</summary>
    Sampler1DArray = 0x8DC0,
    /// <summary>Identifies the Sampler2DArray resource type.</summary>
    Sampler2DArray = 0x8DC1,
    /// <summary>Identifies the SamplerBuffer resource type.</summary>
    SamplerBuffer = 0x8DC2,
    /// <summary>Identifies the Sampler1DArrayShadow resource type.</summary>
    Sampler1DArrayShadow = 0x8DC3,
    /// <summary>Identifies the Sampler2DArrayShadow resource type.</summary>
    Sampler2DArrayShadow = 0x8DC4,
    /// <summary>Identifies the SamplerCubeShadow resource type.</summary>
    SamplerCubeShadow = 0x8DC5,
    /// <summary>Identifies the IntSampler1D resource type.</summary>
    IntSampler1D = 0x8DC9,
    /// <summary>Identifies the IntSampler2D resource type.</summary>
    IntSampler2D = 0x8DCA,
    /// <summary>Identifies the IntSampler3D resource type.</summary>
    IntSampler3D = 0x8DCB,
    /// <summary>Identifies the IntSamplerCube resource type.</summary>
    IntSamplerCube = 0x8DCC,
    /// <summary>Identifies the IntSampler2DRect resource type.</summary>
    IntSampler2DRect = 0x8DCD,
    /// <summary>Identifies the IntSampler1DArray resource type.</summary>
    IntSampler1DArray = 0x8DCE,
    /// <summary>Identifies the IntSampler2DArray resource type.</summary>
    IntSampler2DArray = 0x8DCF,
    /// <summary>Identifies the IntSamplerBuffer resource type.</summary>
    IntSamplerBuffer = 0x8DD0,
    /// <summary>Identifies the UnsignedIntSampler1D resource type.</summary>
    UnsignedIntSampler1D = 0x8DD1,
    /// <summary>Identifies the UnsignedIntSampler2D resource type.</summary>
    UnsignedIntSampler2D = 0x8DD2,
    /// <summary>Identifies the UnsignedIntSampler3D resource type.</summary>
    UnsignedIntSampler3D = 0x8DD3,
    /// <summary>Identifies the UnsignedIntSamplerCube resource type.</summary>
    UnsignedIntSamplerCube = 0x8DD4,
    /// <summary>Identifies the UnsignedIntSampler2DRect resource type.</summary>
    UnsignedIntSampler2DRect = 0x8DD5,
    /// <summary>Identifies the UnsignedIntSampler1DArray resource type.</summary>
    UnsignedIntSampler1DArray = 0x8DD6,
    /// <summary>Identifies the UnsignedIntSampler2DArray resource type.</summary>
    UnsignedIntSampler2DArray = 0x8DD7,
    /// <summary>Identifies the UnsignedIntSamplerBuffer resource type.</summary>
    UnsignedIntSamplerBuffer = 0x8DD8,
    /// <summary>Identifies the SamplerCubeMapArray resource type.</summary>
    SamplerCubeMapArray = 0x900C,
    /// <summary>Identifies the SamplerCubeMapArrayShadow resource type.</summary>
    SamplerCubeMapArrayShadow = 0x900D,
    /// <summary>Identifies the IntSamplerCubeMapArray resource type.</summary>
    IntSamplerCubeMapArray = 0x900E,
    /// <summary>Identifies the UnsignedIntSamplerCubeMapArray resource type.</summary>
    UnsignedIntSamplerCubeMapArray = 0x900F,
    /// <summary>Identifies the Image1D resource type.</summary>
    Image1D = 0x904C,
    /// <summary>Identifies the Image2D resource type.</summary>
    Image2D = 0x904D,
    /// <summary>Identifies the Image3D resource type.</summary>
    Image3D = 0x904E,
    /// <summary>Identifies the Image2DRect resource type.</summary>
    Image2DRect = 0x904F,
    /// <summary>Identifies the ImageCube resource type.</summary>
    ImageCube = 0x9050,
    /// <summary>Identifies the ImageBuffer resource type.</summary>
    ImageBuffer = 0x9051,
    /// <summary>Identifies the Image1DArray resource type.</summary>
    Image1DArray = 0x9052,
    /// <summary>Identifies the Image2DArray resource type.</summary>
    Image2DArray = 0x9053,
    /// <summary>Identifies the ImageCubeMapArray resource type.</summary>
    ImageCubeMapArray = 0x9054,
    /// <summary>Identifies the Image2DMultisample resource type.</summary>
    Image2DMultisample = 0x9055,
    /// <summary>Identifies the Image2DMultisampleArray resource type.</summary>
    Image2DMultisampleArray = 0x9056,
    /// <summary>Identifies the IntImage1D resource type.</summary>
    IntImage1D = 0x9057,
    /// <summary>Identifies the IntImage2D resource type.</summary>
    IntImage2D = 0x9058,
    /// <summary>Identifies the IntImage3D resource type.</summary>
    IntImage3D = 0x9059,
    /// <summary>Identifies the IntImage2DRect resource type.</summary>
    IntImage2DRect = 0x905A,
    /// <summary>Identifies the IntImageCube resource type.</summary>
    IntImageCube = 0x905B,
    /// <summary>Identifies the IntImageBuffer resource type.</summary>
    IntImageBuffer = 0x905C,
    /// <summary>Identifies the IntImage1DArray resource type.</summary>
    IntImage1DArray = 0x905D,
    /// <summary>Identifies the IntImage2DArray resource type.</summary>
    IntImage2DArray = 0x905E,
    /// <summary>Identifies the IntImageCubeMapArray resource type.</summary>
    IntImageCubeMapArray = 0x905F,
    /// <summary>Identifies the IntImage2DMultisample resource type.</summary>
    IntImage2DMultisample = 0x9060,
    /// <summary>Identifies the IntImage2DMultisampleArray resource type.</summary>
    IntImage2DMultisampleArray = 0x9061,
    /// <summary>Identifies the UnsignedIntImage1D resource type.</summary>
    UnsignedIntImage1D = 0x9062,
    /// <summary>Identifies the UnsignedIntImage2D resource type.</summary>
    UnsignedIntImage2D = 0x9063,
    /// <summary>Identifies the UnsignedIntImage3D resource type.</summary>
    UnsignedIntImage3D = 0x9064,
    /// <summary>Identifies the UnsignedIntImage2DRect resource type.</summary>
    UnsignedIntImage2DRect = 0x9065,
    /// <summary>Identifies the UnsignedIntImageCube resource type.</summary>
    UnsignedIntImageCube = 0x9066,
    /// <summary>Identifies the UnsignedIntImageBuffer resource type.</summary>
    UnsignedIntImageBuffer = 0x9067,
    /// <summary>Identifies the UnsignedIntImage1DArray resource type.</summary>
    UnsignedIntImage1DArray = 0x9068,
    /// <summary>Identifies the UnsignedIntImage2DArray resource type.</summary>
    UnsignedIntImage2DArray = 0x9069,
    /// <summary>Identifies the UnsignedIntImageCubeMapArray resource type.</summary>
    UnsignedIntImageCubeMapArray = 0x906A,
    /// <summary>Identifies the UnsignedIntImage2DMultisample resource type.</summary>
    UnsignedIntImage2DMultisample = 0x906B,
    /// <summary>Identifies the UnsignedIntImage2DMultisampleArray resource type.</summary>
    UnsignedIntImage2DMultisampleArray = 0x906C,
    /// <summary>Identifies the Sampler2DMultisample resource type.</summary>
    Sampler2DMultisample = 0x9108,
    /// <summary>Identifies the IntSampler2DMultisample resource type.</summary>
    IntSampler2DMultisample = 0x9109,
    /// <summary>Identifies the UnsignedIntSampler2DMultisample resource type.</summary>
    UnsignedIntSampler2DMultisample = 0x910A,
    /// <summary>Identifies the Sampler2DMultisampleArray resource type.</summary>
    Sampler2DMultisampleArray = 0x910B,
    /// <summary>Identifies the IntSampler2DMultisampleArray resource type.</summary>
    IntSampler2DMultisampleArray = 0x910C,
    /// <summary>Identifies the UnsignedIntSampler2DMultisampleArray resource type.</summary>
    UnsignedIntSampler2DMultisampleArray = 0x910D,
}

