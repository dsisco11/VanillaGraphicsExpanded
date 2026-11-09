using VanillaGraphicsExpanded.Collections;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Collections;

/// <summary>Verifies indexed max-heap insertion, priority updates, removal, and non-mutating reads.</summary>
public sealed class IndexedMaxHeapTests
{
    #region Public API

    #region Insertion and priority ordering

    /// <summary>Popping repeatedly returns entries from highest to lowest priority.</summary>
    [Fact]
    public void InsertPop_OrdersByPriorityDescending()
    {
        // Arrange
        var heap = new IndexedMaxHeap<string>();

        heap.Upsert("low", 1);
        heap.Upsert("mid", 5);
        heap.Upsert("high", 10);

        // Act and assert the heap's descending order and empty result.
        Assert.True(heap.TryPopMax(out string key));
        Assert.Equal("high", key);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("mid", key);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("low", key);

        Assert.False(heap.TryPopMax(out _));
    }

    /// <summary>Increasing an entry's priority moves it ahead of lower-priority entries.</summary>
    [Fact]
    public void UpdatePriority_Increase_ReordersUp()
    {
        // Arrange
        var heap = new IndexedMaxHeap<string>();

        heap.Upsert("a", 1);
        heap.Upsert("b", 2);

        // Act
        Assert.True(heap.UpdatePriority("a", 5));

        // Assert
        Assert.True(heap.TryPopMax(out string key));
        Assert.Equal("a", key);
    }

    /// <summary>Decreasing the maximum entry's priority moves it behind the remaining entries.</summary>
    [Fact]
    public void UpdatePriority_Decrease_ReordersDown()
    {
        // Arrange
        var heap = new IndexedMaxHeap<string>();

        heap.Upsert("a", 5);
        heap.Upsert("b", 2);
        heap.Upsert("c", 1);

        // Act
        Assert.True(heap.UpdatePriority("a", 0));

        // Assert
        Assert.True(heap.TryPopMax(out string key));
        Assert.Equal("b", key);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("c", key);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("a", key);
    }

    #endregion

    #region Removal and lookup

    /// <summary>Removing an arbitrary key updates membership, count, and subsequent pop order.</summary>
    [Fact]
    public void Remove_ArbitraryKey_Works()
    {
        // Arrange
        var heap = new IndexedMaxHeap<string>();

        heap.Upsert("a", 10);
        heap.Upsert("b", 5);
        heap.Upsert("c", 1);

        // Act
        Assert.True(heap.Remove("b"));
        // Assert
        Assert.False(heap.ContainsKey("b"));
        Assert.Equal(2, heap.Count);

        Assert.True(heap.TryPopMax(out string key));
        Assert.Equal("a", key);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("c", key);
    }

    /// <summary>Upserting an existing key updates its priority without adding another entry.</summary>
    [Fact]
    public void Upsert_DoesNotDuplicate_AndUpdatesPriority()
    {
        // Arrange
        var heap = new IndexedMaxHeap<string>();

        // Act
        Assert.True(heap.Upsert("a", 1));
        Assert.False(heap.Upsert("a", 9));

        // Assert
        Assert.Equal(1, heap.Count);

        Assert.True(heap.TryPeekMax(out string key, out float priority));
        Assert.Equal("a", key);
        Assert.Equal(9f, priority);

        Assert.True(heap.TryPopMax(out key));
        Assert.Equal("a", key);
        Assert.Equal(0, heap.Count);
    }

    /// <summary>Copying the top keys returns the requested prefix without mutating the heap.</summary>
    [Fact]
    public void CopyTopKeys_ReturnsTopK_WithoutMutating()
    {
        // Arrange
        var heap = new IndexedMaxHeap<string>();

        heap.Upsert("a", 1);
        heap.Upsert("b", 10);
        heap.Upsert("c", 5);
        heap.Upsert("d", 7);

        var top = new string[3];
        // Act
        int n = heap.CopyTopKeys(top);

        // Assert
        Assert.Equal(3, n);
        Assert.Equal("b", top[0]);
        Assert.Equal("d", top[1]);
        Assert.Equal("c", top[2]);

        // Heap should be unchanged.
        Assert.Equal(4, heap.Count);
        Assert.True(heap.TryPopMax(out string first));
        Assert.Equal("b", first);
    }

    #endregion

    #endregion
}
