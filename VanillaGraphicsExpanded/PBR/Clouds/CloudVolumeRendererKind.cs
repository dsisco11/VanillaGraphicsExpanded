namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Selects an independent volumetric consumer without changing native cloud mode.</summary>
public enum CloudVolumeRendererKind { StoredVolume=0, LightingVolumeCandidate=1, Previous=2 }
