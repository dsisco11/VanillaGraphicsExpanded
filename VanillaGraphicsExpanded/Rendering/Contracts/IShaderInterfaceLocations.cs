using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Declares shared stage varying and fragment attachment locations; resources use fixed binding units.</summary>
internal interface IShaderInterfaceLocations
{
    #region Public API

    /// <summary>Declares the uv VaryingLocation slot.</summary>
    [ShaderBinding("uv", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding UvVarying { get; }
    /// <summary>Declares the v_uv VaryingLocation slot.</summary>
    [ShaderBinding("v_uv", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VUvVarying { get; }
    /// <summary>Declares the vTexCoord VaryingLocation slot.</summary>
    [ShaderBinding("vTexCoord", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VTexCoordVarying { get; }
    /// <summary>Declares the vColor VaryingLocation slot.</summary>
    [ShaderBinding("vColor", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VColorVarying { get; }
    /// <summary>Declares the vAtlasCoord VaryingLocation slot.</summary>
    [ShaderBinding("vAtlasCoord", ShaderBindingKind.VaryingLocation, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VAtlasCoordVarying { get; }
    /// <summary>Declares the vRadiance VaryingLocation slot.</summary>
    [ShaderBinding("vRadiance", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VRadianceVarying { get; }
    /// <summary>Declares the vAoDirWorld VaryingLocation slot.</summary>
    [ShaderBinding("vAoDirWorld", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VAoDirWorldVarying { get; }
    /// <summary>Declares the vAoConfidence VaryingLocation slot.</summary>
    [ShaderBinding("vAoConfidence", ShaderBindingKind.VaryingLocation, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VAoConfidenceVarying { get; }
    /// <summary>Declares the vConfidence VaryingLocation slot.</summary>
    [ShaderBinding("vConfidence", ShaderBindingKind.VaryingLocation, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VConfidenceVarying { get; }
    /// <summary>Declares the vMeanLogHitDistance VaryingLocation slot.</summary>
    [ShaderBinding("vMeanLogHitDistance", ShaderBindingKind.VaryingLocation, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VMeanLogHitDistanceVarying { get; }
    /// <summary>Declares the vSkyIntensity VaryingLocation slot.</summary>
    [ShaderBinding("vSkyIntensity", ShaderBindingKind.VaryingLocation, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VSkyIntensityVarying { get; }
    /// <summary>Declares the vFlags VaryingLocation slot.</summary>
    [ShaderBinding("vFlags", ShaderBindingKind.VaryingLocation, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderVaryingLocationBinding VFlagsVarying { get; }
    /// <summary>Declares the outColor FragmentOutputLocation slot.</summary>
    [ShaderBinding("outColor", ShaderBindingKind.FragmentOutputLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderFragmentOutputLocationBinding OutColorOutput { get; }
    /// <summary>Declares the fragColor FragmentOutputLocation slot.</summary>
    [ShaderBinding("fragColor", ShaderBindingKind.FragmentOutputLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderFragmentOutputLocationBinding FragColorOutput { get; }
    /// <summary>Declares the color FragmentOutputLocation slot.</summary>
    [ShaderBinding("color", ShaderBindingKind.FragmentOutputLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    ShaderFragmentOutputLocationBinding ColorOutput { get; }
    #endregion
}
