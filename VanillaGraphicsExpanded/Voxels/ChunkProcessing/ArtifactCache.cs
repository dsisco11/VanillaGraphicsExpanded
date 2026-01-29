using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

internal sealed class ArtifactCache
{
    private readonly object gate = new();

    private readonly long budgetBytes;

    private long bytesInUse;

    private readonly Dictionary<ArtifactKey, LinkedListNode<CacheNode>> map = new();

    private readonly LinkedList<CacheNode> lru = new();

    internal ArtifactCache(long budgetBytes)
    {
        this.budgetBytes = Math.Max(0, budgetBytes);
    }

    public long BudgetBytes => budgetBytes;

    public long BytesInUse
    {
        get
        {
            lock (gate)
            {
                return bytesInUse;
            }
        }
    }

    public bool TryGet(ArtifactKey key, out object? artifact)
    {
        lock (gate)
        {
            if (!map.TryGetValue(key, out LinkedListNode<CacheNode>? node))
            {
                artifact = null;
                return false;
            }

            lru.Remove(node);
            lru.AddFirst(node);
            artifact = node.Value.Artifact;
            return true;
        }
    }

    public void Put(ArtifactKey key, object artifact)
    {
        if (artifact is null)
        {
            return;
        }

        long estimatedBytes = artifact is IArtifactSizeInfo sizeInfo ? Math.Max(0, sizeInfo.EstimatedBytes) : 0;

        lock (gate)
        {
            if (map.TryGetValue(key, out LinkedListNode<CacheNode>? existing))
            {
                lru.Remove(existing);
                bytesInUse -= existing.Value.EstimatedBytes;
                map.Remove(key);
            }

            if (budgetBytes > 0 && estimatedBytes > budgetBytes)
            {
                return;
            }

            var node = new CacheNode(key, artifact, estimatedBytes);
            var listNode = lru.AddFirst(node);
            map.Add(key, listNode);
            bytesInUse += estimatedBytes;

            if (budgetBytes > 0)
            {
                TrimToBudgetLocked();
            }
        }
    }

    private void TrimToBudgetLocked()
    {
        while (bytesInUse > budgetBytes && lru.Last is not null)
        {
            LinkedListNode<CacheNode> tail = lru.Last;
            lru.RemoveLast();

            map.Remove(tail.Value.Key);
            bytesInUse -= tail.Value.EstimatedBytes;
        }
    }

    private readonly record struct CacheNode(ArtifactKey Key, object Artifact, long EstimatedBytes);
}
