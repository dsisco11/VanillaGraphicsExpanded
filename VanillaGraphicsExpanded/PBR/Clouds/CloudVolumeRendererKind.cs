namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Selects an independent volumetric consumer without changing native cloud mode.</summary>
public enum CloudVolumeRendererKind { Existing=0, LightingVolumeCandidate=1, StoredVolumeCandidate=2 }
