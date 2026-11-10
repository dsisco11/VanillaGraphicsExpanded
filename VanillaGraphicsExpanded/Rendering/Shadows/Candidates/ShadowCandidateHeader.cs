using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Mirrors the 128-byte GPU continuation and publication header; no CPU readback is needed for submission.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct ShadowCandidateHeader
{
    // Status: 0 clearing, 1 traversal, 2 validation, 3 complete, 4 capacity exhausted, 5 stale.
    internal uint Status, Count, Subset;
    internal int Slab, CursorA, CursorB, MinA, MaxA, MinB, MaxB;
    internal uint Stage, ProbeCursor, CellVisits, SlabVisits, ProbeVisits, Duplicates;
    internal uint Unresolved, ValidationCursor, ChangeCursor, SequenceLow, SequenceHigh;
    internal uint BatchLow, BatchHigh, ClearCursor, GenerationLow, GenerationHigh, RefreshRequested;
    internal uint WorldProbeCursor, WorldLow, WorldHigh, ChangeVisits, Reserved;
}
