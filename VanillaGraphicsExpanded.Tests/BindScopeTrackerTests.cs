using System.Collections.Generic;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public class BindScopeTrackerTests
{
    [Fact]
    public void BeginEnd_NestedScopes_RestoresBindingsInOrder()
    {
        TestTracker.Reset(current: 0);

        int prev0 = TestTracker.Begin(1);
        int prev1 = TestTracker.Begin(2);

        TestTracker.End(prev1);
        TestTracker.End(prev0);

        Assert.Equal(new List<int> { 1, 2, 1, 0 }, TestTracker.Binds);
        Assert.Equal(0, TestTracker.Current);
    }

    [Fact]
    public void BeginEnd_SameId_DoesNotRebind()
    {
        TestTracker.Reset(current: 5);

        int prev = TestTracker.Begin(5);
        TestTracker.End(prev);

        Assert.Empty(TestTracker.Binds);
        Assert.Equal(5, TestTracker.Current);
    }

    [Fact]
    public void End_OutOfOrder_DisposalResetsStack_BestEffortRestores()
    {
        TestTracker.Reset(current: 0);

        int prev0 = TestTracker.Begin(1);
        int prev1 = TestTracker.Begin(2);

        // Dispose out-of-order (end outer before inner).
        TestTracker.End(prev0);
        TestTracker.End(prev1);

        // Best-effort: we should have bound to 1 then 2, then attempted restores.
        Assert.Equal(new List<int> { 1, 2, 0, 1 }, TestTracker.Binds);
    }

    private sealed class TestTracker : BindScopeTracker<TestTracker>
    {
        public static int Current { get; private set; }

        public static List<int> Binds { get; } = new();

        public static void Reset(int current)
        {
            Current = current;
            Binds.Clear();
            ClearThreadStack();
        }

        protected override int GetCurrent() => Current;

        protected override void Bind(int id)
        {
            Current = id;
            Binds.Add(id);
        }
    }
}

