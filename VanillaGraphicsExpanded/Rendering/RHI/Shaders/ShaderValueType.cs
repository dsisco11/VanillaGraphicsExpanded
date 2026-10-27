namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Describes reflected scalar, vector, matrix and opaque shader values independently of native tokens.</summary>
public enum ShaderValueType
{
    /// <summary>Represents an unsupported native type, which cannot certify compatible input.</summary>
    Unknown,
    /// <summary>Identifies the Bool shader value shape.</summary>
    Bool,
    /// <summary>Identifies the BoolVec2 shader value shape.</summary>
    BoolVec2,
    /// <summary>Identifies the BoolVec3 shader value shape.</summary>
    BoolVec3,
    /// <summary>Identifies the BoolVec4 shader value shape.</summary>
    BoolVec4,
    /// <summary>Identifies the Double shader value shape.</summary>
    Double,
    /// <summary>Identifies the DoubleMat2 shader value shape.</summary>
    DoubleMat2,
    /// <summary>Identifies the DoubleMat2x3 shader value shape.</summary>
    DoubleMat2x3,
    /// <summary>Identifies the DoubleMat2x4 shader value shape.</summary>
    DoubleMat2x4,
    /// <summary>Identifies the DoubleMat3 shader value shape.</summary>
    DoubleMat3,
    /// <summary>Identifies the DoubleMat3x2 shader value shape.</summary>
    DoubleMat3x2,
    /// <summary>Identifies the DoubleMat3x4 shader value shape.</summary>
    DoubleMat3x4,
    /// <summary>Identifies the DoubleMat4 shader value shape.</summary>
    DoubleMat4,
    /// <summary>Identifies the DoubleMat4x2 shader value shape.</summary>
    DoubleMat4x2,
    /// <summary>Identifies the DoubleMat4x3 shader value shape.</summary>
    DoubleMat4x3,
    /// <summary>Identifies the DoubleVec2 shader value shape.</summary>
    DoubleVec2,
    /// <summary>Identifies the DoubleVec3 shader value shape.</summary>
    DoubleVec3,
    /// <summary>Identifies the DoubleVec4 shader value shape.</summary>
    DoubleVec4,
    /// <summary>Identifies the Float shader value shape.</summary>
    Float,
    /// <summary>Identifies the FloatMat2 shader value shape.</summary>
    FloatMat2,
    /// <summary>Identifies the FloatMat2x3 shader value shape.</summary>
    FloatMat2x3,
    /// <summary>Identifies the FloatMat2x4 shader value shape.</summary>
    FloatMat2x4,
    /// <summary>Identifies the FloatMat3 shader value shape.</summary>
    FloatMat3,
    /// <summary>Identifies the FloatMat3x2 shader value shape.</summary>
    FloatMat3x2,
    /// <summary>Identifies the FloatMat3x4 shader value shape.</summary>
    FloatMat3x4,
    /// <summary>Identifies the FloatMat4 shader value shape.</summary>
    FloatMat4,
    /// <summary>Identifies the FloatMat4x2 shader value shape.</summary>
    FloatMat4x2,
    /// <summary>Identifies the FloatMat4x3 shader value shape.</summary>
    FloatMat4x3,
    /// <summary>Identifies the FloatVec2 shader value shape.</summary>
    FloatVec2,
    /// <summary>Identifies the FloatVec3 shader value shape.</summary>
    FloatVec3,
    /// <summary>Identifies the FloatVec4 shader value shape.</summary>
    FloatVec4,
    /// <summary>Identifies the Image1D shader value shape.</summary>
    Image1D,
    /// <summary>Identifies the Image1DArray shader value shape.</summary>
    Image1DArray,
    /// <summary>Identifies the Image2D shader value shape.</summary>
    Image2D,
    /// <summary>Identifies the Image2DArray shader value shape.</summary>
    Image2DArray,
    /// <summary>Identifies the Image2DMultisample shader value shape.</summary>
    Image2DMultisample,
    /// <summary>Identifies the Image2DMultisampleArray shader value shape.</summary>
    Image2DMultisampleArray,
    /// <summary>Identifies the Image2DRect shader value shape.</summary>
    Image2DRect,
    /// <summary>Identifies the Image3D shader value shape.</summary>
    Image3D,
    /// <summary>Identifies the ImageBuffer shader value shape.</summary>
    ImageBuffer,
    /// <summary>Identifies the ImageCube shader value shape.</summary>
    ImageCube,
    /// <summary>Identifies the ImageCubeMapArray shader value shape.</summary>
    ImageCubeMapArray,
    /// <summary>Identifies the Int shader value shape.</summary>
    Int,
    /// <summary>Identifies the IntImage1D shader value shape.</summary>
    IntImage1D,
    /// <summary>Identifies the IntImage1DArray shader value shape.</summary>
    IntImage1DArray,
    /// <summary>Identifies the IntImage2D shader value shape.</summary>
    IntImage2D,
    /// <summary>Identifies the IntImage2DArray shader value shape.</summary>
    IntImage2DArray,
    /// <summary>Identifies the IntImage2DMultisample shader value shape.</summary>
    IntImage2DMultisample,
    /// <summary>Identifies the IntImage2DMultisampleArray shader value shape.</summary>
    IntImage2DMultisampleArray,
    /// <summary>Identifies the IntImage2DRect shader value shape.</summary>
    IntImage2DRect,
    /// <summary>Identifies the IntImage3D shader value shape.</summary>
    IntImage3D,
    /// <summary>Identifies the IntImageBuffer shader value shape.</summary>
    IntImageBuffer,
    /// <summary>Identifies the IntImageCube shader value shape.</summary>
    IntImageCube,
    /// <summary>Identifies the IntImageCubeMapArray shader value shape.</summary>
    IntImageCubeMapArray,
    /// <summary>Identifies the IntSampler1D shader value shape.</summary>
    IntSampler1D,
    /// <summary>Identifies the IntSampler1DArray shader value shape.</summary>
    IntSampler1DArray,
    /// <summary>Identifies the IntSampler2D shader value shape.</summary>
    IntSampler2D,
    /// <summary>Identifies the IntSampler2DArray shader value shape.</summary>
    IntSampler2DArray,
    /// <summary>Identifies the IntSampler2DMultisample shader value shape.</summary>
    IntSampler2DMultisample,
    /// <summary>Identifies the IntSampler2DMultisampleArray shader value shape.</summary>
    IntSampler2DMultisampleArray,
    /// <summary>Identifies the IntSampler2DRect shader value shape.</summary>
    IntSampler2DRect,
    /// <summary>Identifies the IntSampler3D shader value shape.</summary>
    IntSampler3D,
    /// <summary>Identifies the IntSamplerBuffer shader value shape.</summary>
    IntSamplerBuffer,
    /// <summary>Identifies the IntSamplerCube shader value shape.</summary>
    IntSamplerCube,
    /// <summary>Identifies the IntSamplerCubeMapArray shader value shape.</summary>
    IntSamplerCubeMapArray,
    /// <summary>Identifies the IntVec2 shader value shape.</summary>
    IntVec2,
    /// <summary>Identifies the IntVec3 shader value shape.</summary>
    IntVec3,
    /// <summary>Identifies the IntVec4 shader value shape.</summary>
    IntVec4,
    /// <summary>Identifies the Sampler1D shader value shape.</summary>
    Sampler1D,
    /// <summary>Identifies the Sampler1DArray shader value shape.</summary>
    Sampler1DArray,
    /// <summary>Identifies the Sampler1DArrayShadow shader value shape.</summary>
    Sampler1DArrayShadow,
    /// <summary>Identifies the Sampler1DShadow shader value shape.</summary>
    Sampler1DShadow,
    /// <summary>Identifies the Sampler2D shader value shape.</summary>
    Sampler2D,
    /// <summary>Identifies the Sampler2DArray shader value shape.</summary>
    Sampler2DArray,
    /// <summary>Identifies the Sampler2DArrayShadow shader value shape.</summary>
    Sampler2DArrayShadow,
    /// <summary>Identifies the Sampler2DMultisample shader value shape.</summary>
    Sampler2DMultisample,
    /// <summary>Identifies the Sampler2DMultisampleArray shader value shape.</summary>
    Sampler2DMultisampleArray,
    /// <summary>Identifies the Sampler2DRect shader value shape.</summary>
    Sampler2DRect,
    /// <summary>Identifies the Sampler2DRectShadow shader value shape.</summary>
    Sampler2DRectShadow,
    /// <summary>Identifies the Sampler2DShadow shader value shape.</summary>
    Sampler2DShadow,
    /// <summary>Identifies the Sampler3D shader value shape.</summary>
    Sampler3D,
    /// <summary>Identifies the SamplerBuffer shader value shape.</summary>
    SamplerBuffer,
    /// <summary>Identifies the SamplerCube shader value shape.</summary>
    SamplerCube,
    /// <summary>Identifies the SamplerCubeMapArray shader value shape.</summary>
    SamplerCubeMapArray,
    /// <summary>Identifies the SamplerCubeMapArrayShadow shader value shape.</summary>
    SamplerCubeMapArrayShadow,
    /// <summary>Identifies the SamplerCubeShadow shader value shape.</summary>
    SamplerCubeShadow,
    /// <summary>Identifies the UnsignedInt shader value shape.</summary>
    UnsignedInt,
    /// <summary>Identifies the UnsignedIntAtomicCounter shader value shape.</summary>
    UnsignedIntAtomicCounter,
    /// <summary>Identifies the UnsignedIntImage1D shader value shape.</summary>
    UnsignedIntImage1D,
    /// <summary>Identifies the UnsignedIntImage1DArray shader value shape.</summary>
    UnsignedIntImage1DArray,
    /// <summary>Identifies the UnsignedIntImage2D shader value shape.</summary>
    UnsignedIntImage2D,
    /// <summary>Identifies the UnsignedIntImage2DArray shader value shape.</summary>
    UnsignedIntImage2DArray,
    /// <summary>Identifies the UnsignedIntImage2DMultisample shader value shape.</summary>
    UnsignedIntImage2DMultisample,
    /// <summary>Identifies the UnsignedIntImage2DMultisampleArray shader value shape.</summary>
    UnsignedIntImage2DMultisampleArray,
    /// <summary>Identifies the UnsignedIntImage2DRect shader value shape.</summary>
    UnsignedIntImage2DRect,
    /// <summary>Identifies the UnsignedIntImage3D shader value shape.</summary>
    UnsignedIntImage3D,
    /// <summary>Identifies the UnsignedIntImageBuffer shader value shape.</summary>
    UnsignedIntImageBuffer,
    /// <summary>Identifies the UnsignedIntImageCube shader value shape.</summary>
    UnsignedIntImageCube,
    /// <summary>Identifies the UnsignedIntImageCubeMapArray shader value shape.</summary>
    UnsignedIntImageCubeMapArray,
    /// <summary>Identifies the UnsignedIntSampler1D shader value shape.</summary>
    UnsignedIntSampler1D,
    /// <summary>Identifies the UnsignedIntSampler1DArray shader value shape.</summary>
    UnsignedIntSampler1DArray,
    /// <summary>Identifies the UnsignedIntSampler2D shader value shape.</summary>
    UnsignedIntSampler2D,
    /// <summary>Identifies the UnsignedIntSampler2DArray shader value shape.</summary>
    UnsignedIntSampler2DArray,
    /// <summary>Identifies the UnsignedIntSampler2DMultisample shader value shape.</summary>
    UnsignedIntSampler2DMultisample,
    /// <summary>Identifies the UnsignedIntSampler2DMultisampleArray shader value shape.</summary>
    UnsignedIntSampler2DMultisampleArray,
    /// <summary>Identifies the UnsignedIntSampler2DRect shader value shape.</summary>
    UnsignedIntSampler2DRect,
    /// <summary>Identifies the UnsignedIntSampler3D shader value shape.</summary>
    UnsignedIntSampler3D,
    /// <summary>Identifies the UnsignedIntSamplerBuffer shader value shape.</summary>
    UnsignedIntSamplerBuffer,
    /// <summary>Identifies the UnsignedIntSamplerCube shader value shape.</summary>
    UnsignedIntSamplerCube,
    /// <summary>Identifies the UnsignedIntSamplerCubeMapArray shader value shape.</summary>
    UnsignedIntSamplerCubeMapArray,
    /// <summary>Identifies the UnsignedIntVec2 shader value shape.</summary>
    UnsignedIntVec2,
    /// <summary>Identifies the UnsignedIntVec3 shader value shape.</summary>
    UnsignedIntVec3,
    /// <summary>Identifies the UnsignedIntVec4 shader value shape.</summary>
    UnsignedIntVec4,
}
