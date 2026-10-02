using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable declaration for tests/GpuProgramLayoutBindingTests_1.</summary>
[ShaderProgram("Contract", "tests/GpuProgramLayoutBindingTests_1", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "tests/GpuProgramLayoutBindingTests_1.csh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
[ShaderBindingSet(typeof(ILayoutBindingComputeBindings))]
internal static partial class LayoutBindingComputeShader { }
