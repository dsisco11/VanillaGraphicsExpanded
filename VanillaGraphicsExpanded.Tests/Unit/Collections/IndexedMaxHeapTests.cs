using VanillaGraphicsExpanded.Collections;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Collections;

public sealed class IndexedMaxHeapTests
{
    [Fact]
    public void InsertPop_OrdersByPriorityDescending()
    {
        var heap = new IndexedMaxHeap<string>();

        heap.Upsert("low", 1);
        heap.Upsert("mid", 5);
        heap.Upsert("high", 10);

        Assert.True(heap.TryPopMax(out string key));
        Assert.Equal("high", key);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("mid", key);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("low", key);

        Assert.False(heap.TryPopMax(out _));
    }

    [Fact]
    public void UpdatePriority_Increase_ReordersUp()
    {
        var heap = new IndexedMaxHeap<string>();

        heap.Upsert("a", 1);
        heap.Upsert("b", 2);

        Assert.True(heap.UpdatePriority("a", 5));

        Assert.True(heap.TryPopMax(out string key));
        Assert.Equal("a", key);
    }

    [Fact]
    public void UpdatePriority_Decrease_ReordersDown()
    {
        var heap = new IndexedMaxHeap<string>();

        heap.Upsert("a", 5);
        heap.Upsert("b", 2);
        heap.Upsert("c", 1);

        Assert.True(heap.UpdatePriority("a", 0));

        Assert.True(heap.TryPopMax(out string key));
        Assert.Equal("b", key);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("c", key);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("a", key);
    }

    [Fact]
    public void Remove_ArbitraryKey_Works()
    {
        var heap = new IndexedMaxHeap<string>();

        heap.Upsert("a", 10);
        heap.Upsert("b", 5);
        heap.Upsert("c", 1);

        Assert.True(heap.Remove("b"));
        Assert.False(heap.ContainsKey("b"));
        Assert.Equal(2, heap.Count);

        Assert.True(heap.TryPopMax(out string key));
        Assert.Equal("a", key);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("c", key);
    }

    [Fact]
    public void Upsert_DoesNotDuplicate_AndUpdatesPriority()
    {
        var heap = new IndexedMaxHeap<string>();

        Assert.True(heap.Upsert("a", 1));
        Assert.False(heap.Upsert("a", 9));

        Assert.Equal(1, heap.Count);

        Assert.True(heap.TryPeekMax(out string key, out float priority));
        Assert.Equal("a", key);
        Assert.Equal(9f, priority);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("a", key);
        Assert.Equal(0, heap.Count);
    }

    [Fact]
    public void CopyTopKeys_ReturnsTopK_WithoutMutating()
    {
        var heap = new IndexedMaxHeap<string>();

        heap.Upsert("a", 1);
        heap.Upsert("b", 10);
        heap.Upsert("c", 5);
        heap.Upsert("d", 7);

        var top = new string[3];
        int n = heap.CopyTopKeys(top);

        Assert.Equal(3, n);
        Assert.Equal("b", top[0]);
        Assert.Equal("d", top[1]);
        Assert.Equal("c", top[2]);

        // Heap should be unchanged.
        Assert.Equal(4, heap.Count);
        Assert.True(heap.TryPopMax(out string first));
        Assert.Equal("b", first);
    }
}
