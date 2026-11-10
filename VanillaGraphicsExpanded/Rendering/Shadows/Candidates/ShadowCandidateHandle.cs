namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Scopes one allocator slot to its nonrecycled generation and pool lifetime.</summary>
internal readonly record struct ShadowCandidateHandle(long Owner, int Slot, long Generation);
