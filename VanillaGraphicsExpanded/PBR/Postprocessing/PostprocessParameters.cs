namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Captures a coherent frame's bounded bloom and solar-shaft policy.</summary>
internal readonly record struct PostprocessParameters(float BloomStrength,float BloomThreshold,float BloomKnee,int BloomLevels,
    float GodRayStrength,float GodRayLimit,int GodRaySamples);
