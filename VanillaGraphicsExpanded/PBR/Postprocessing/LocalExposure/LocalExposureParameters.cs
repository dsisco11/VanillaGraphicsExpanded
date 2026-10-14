namespace VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
/// <summary>Captures one coherent optional spatial display policy.</summary>
internal readonly record struct LocalExposureParameters(bool Enabled,int Width,int Height,float RangeWidth,
    float ShadowStrength,float HighlightStrength,float ShadowLimit,float HighlightLimit,bool Visualize);
