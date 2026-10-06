using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Independent knowledge for supplemental sampling parameters.</summary>
[Flags]
internal enum CompleteSamplingKnowledge
{
    /// <summary>Declared or observed None value.</summary>
    None = 0,
    /// <summary>Declared or observed SampleCoverage value.</summary>
    SampleCoverage = 1 << 0,
    /// <summary>Declared or observed MinimumSampleShading value.</summary>
    MinimumSampleShading = 1 << 1,
}
