using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Retains a native upload's exact owner and content revision across its exception boundary.</summary>
internal sealed record NativeShadowUpload(NativeShadowWorldAdapter Owner, long Index, long CenterRevision, long EdgeRevision,
    ClientChunk? Chunk = null, int CenterCount = -1, int EdgeCount = -1);
