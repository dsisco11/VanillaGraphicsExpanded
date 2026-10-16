using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Combines current physical transport for rejected pixels with explicit reconstruction inputs.</summary>
[ShaderBindingSet(typeof(ICloudTraceBindings),Defaults=true)]
[ShaderBindingSet(typeof(ICloudHistoryBindings),Defaults=true)]
internal interface ICloudResolveBindings : ICloudTraceBindings,ICloudHistoryBindings { }
