using System;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

/// <summary>Retires directional history when a probe's world-space identity or geometry changes.</summary>
internal sealed partial class LumOnWorldProbeUpdateRenderer
{
    #region Directional history ownership

    /// <summary>Retires introduced ring slots before publishing the new clipmap anchor.</summary>
    private void OnProbeAnchorShifted(LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent evt)
    {
        var resources = clipmapBufferManager.Resources;
        if (resources is not null)
        {
            int n = resources.Resolution;
            var ring = new VectorInt3(evt.NewRingOffset.X, evt.NewRingOffset.Y, evt.NewRingOffset.Z);
            int[] delta = { evt.DeltaProbes.X, evt.DeltaProbes.Y, evt.DeltaProbes.Z };
            if (Math.Abs((long)delta[0]) >= n || Math.Abs((long)delta[1]) >= n || Math.Abs((long)delta[2]) >= n)
                resources.ClearLocalBox(evt.Level, ring, new VectorInt3(), new VectorInt3(n - 1, n - 1, n - 1));
            else
            {
                // Only newly introduced slabs change identity; overlapping world probes retain their directions.
                for (int axis = 0; axis < 3; axis++)
                {
                    if (delta[axis] == 0) continue;
                    int[] min = { 0, 0, 0 };
                    int[] max = { n - 1, n - 1, n - 1 };
                    if (delta[axis] > 0) min[axis] = n - delta[axis];
                    else max[axis] = -delta[axis] - 1;
                    resources.ClearLocalBox(evt.Level, ring,
                        new VectorInt3(min[0], min[1], min[2]), new VectorInt3(max[0], max[1], max[2]));
                }
            }
        }
        clipmapBufferManager.NotifyAnchorShifted(in evt);
    }

    /// <summary>Invalidates the same inclusive local range as the scheduler's geometry dirty notification.</summary>
    private void ClearDirtyProbeHistory(int level, Vector3d min, Vector3d max, double baseSpacing)
    {
        var resources = clipmapBufferManager.Resources;
        if (resources is null || scheduler is null || !scheduler.TryGetLevelParams(level, out var origin, out var ring)) return;
        var lo = new Vector3d(Math.Min(min.X, max.X), Math.Min(min.Y, max.Y), Math.Min(min.Z, max.Z));
        var hi = new Vector3d(Math.Max(min.X, max.X), Math.Max(min.Y, max.Y), Math.Max(min.Z, max.Z));
        double spacing = LumOnClipmapTopology.GetSpacing(baseSpacing, level);
        int last = resources.Resolution - 1;
        resources.ClearLocalBox(level, new VectorInt3(ring.X, ring.Y, ring.Z),
            new VectorInt3(Math.Clamp((int)Math.Floor((lo.X - origin.X) / spacing), 0, last),
                Math.Clamp((int)Math.Floor((lo.Y - origin.Y) / spacing), 0, last),
                Math.Clamp((int)Math.Floor((lo.Z - origin.Z) / spacing), 0, last)),
            new VectorInt3(Math.Clamp((int)Math.Floor((hi.X - origin.X) / spacing), 0, last),
                Math.Clamp((int)Math.Floor((hi.Y - origin.Y) / spacing), 0, last),
                Math.Clamp((int)Math.Floor((hi.Z - origin.Z) / spacing), 0, last)));
    }

    #endregion
}
