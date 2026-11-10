using System;
using System.Collections.Immutable;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.World;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Assigns batch-local command intervals from chunk ranges and the already selected native groups.</summary>
internal sealed record NativeShadowDrawRanges(ImmutableArray<ShadowDrawRange> Ranges,
    ImmutableArray<ShadowDrawInterval> Intervals, int CommandCount)
{
    #region Public API
    /// <summary>Sorts range descriptions by native pool ID, then consumes each authoritative group without a reverse category map.</summary>
    internal static NativeShadowDrawRanges Create(NativeShadowTerrainBatch batch, ShadowWorldPublication world,
        ReadOnlySpan<uint> eligibility, int arrays, int tessellationAmplification, ShadowDrawSettings settings)
    {
        var policy = settings.Freeze();
        if (!batch.IsCurrent || !world.Complete || eligibility.Length != world.Ranges.Length
            || arrays is < 1 or > 4 || tessellationAmplification < 1 || world.Ranges.Length > policy.RangeVisits)
            throw new InvalidOperationException("Draw input publication or aggregate range allowance is unavailable.");
        // Sort references, not meshes or category membership. Native table order remains
        // the submission authority; the sorted range list exists only for this batch.
        int[] order = Enumerable.Range(0, world.Ranges.Length).OrderBy(i => world.Ranges[i].PoolId).ToArray();
        var resolved = new bool[order.Length];
        var output = new ShadowDrawRange[order.Length];
        var intervals = ImmutableArray.CreateBuilder<ShadowDrawInterval>();
        int commands = 0;
        for (int groupIndex = 0; groupIndex < batch.Groups.Length; groupIndex++)
        {
            var group = batch.Groups[groupIndex];
            int first = LowerBound(world, order, group.PoolId), end = first;
            while (end < order.Length && world.Ranges[order[end]].PoolId == group.PoolId) end++;
            int count = end - first;
            if (group.Casts && count > (policy.CommandSlots - commands) / arrays)
                throw new InvalidOperationException("Aggregate terrain command allowance exhausted.");
            for (int cursor = first; cursor < end; cursor++)
            {
                int index = order[cursor];
                if (resolved[index]) throw new InvalidOperationException("Native pool identity occurs in multiple table groups.");
                resolved[index] = true;
                var range = world.Ranges[index];
                if (range.IndexStart < 0 || range.IndexCount < 0 || range.IndexCount % 3 != 0
                    || range.VertexStart < 0 || range.VertexCount < 0
                    || range.IndexStart > group.IndexCapacity - range.IndexCount
                    || range.VertexStart > group.VertexCapacity - range.VertexCount)
                    throw new InvalidOperationException("Invalid native indexed range.");
                uint cost = checked((uint)(range.IndexCount / 3) * (uint)tessellationAmplification);
                output[index] = new((uint)groupIndex, (uint)(commands + cursor - first),
                    group.Casts ? eligibility[index] : 0, cost, (uint)range.IndexStart, (uint)range.IndexCount,
                    0, range.RevisionLow, range.RevisionHigh, (uint)range.VertexStart, (uint)range.VertexCount, (uint)count);
            }
            if (!group.Casts) continue;
            for (int array = 0; array < arrays; array++)
                intervals.Add(new(groupIndex, array, checked(commands + array * count), count));
            commands = checked(commands + count * arrays);
        }
        if (resolved.Any(value => !value)) throw new InvalidOperationException("A chunk range has no current native table group.");
        return new(ImmutableArray.Create(output), intervals.ToImmutable(), commands);
    }
    #endregion

    #region Private
    /// <summary>Finds a group's range interval without examining native pool allocations.</summary>
    private static int LowerBound(ShadowWorldPublication world, int[] order, int pool)
    {
        int lo = 0, hi = order.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (world.Ranges[order[mid]].PoolId < pool) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
    #endregion
}
