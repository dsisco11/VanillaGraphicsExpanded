using System.Collections.Generic;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies nested binding restoration and redundant-bind suppression.</summary>
public class BindScopeTrackerTests
{
    #region Public API

    /// <summary>Nested scopes restore each binding in reverse order.</summary>
    [Fact]
    public void BeginEnd_NestedScopes_RestoresBindingsInOrder()
    {
        // Arrange: start with a known binding and an empty thread-local scope stack.
        TestTracker.Reset(current: 0);

        // Act: bind twice and unwind the inner scope before the outer scope.
        int prev0 = TestTracker.Begin(1);
        int prev1 = TestTracker.Begin(2);

        TestTracker.End(prev1);
        TestTracker.End(prev0);

        // Assert: both restorations occur in order and leave the initial binding active.
        Assert.Equal(new List<int> { 1, 2, 1, 0 }, TestTracker.Binds);
        Assert.Equal(0, TestTracker.Current);
    }

    /// <summary>Entering and exiting an already active identifier does not bind again.</summary>
    [Fact]
    public void BeginEnd_SameId_DoesNotRebind()
    {
        // Arrange
        TestTracker.Reset(current: 5);

        // Act
        int prev = TestTracker.Begin(5);
        TestTracker.End(prev);

        // Assert
        Assert.Empty(TestTracker.Binds);
        Assert.Equal(5, TestTracker.Current);
    }

    /// <summary>Out-of-order disposal clears stale stack state and attempts best-effort restoration.</summary>
    [Fact]
    public void End_OutOfOrder_DisposalResetsStack_BestEffortRestores()
    {
        // Arrange
        TestTracker.Reset(current: 0);

        int prev0 = TestTracker.Begin(1);
        int prev1 = TestTracker.Begin(2);

        // Act: end the outer scope while its inner scope is still active.
        TestTracker.End(prev0);
        TestTracker.End(prev1);

        // Assert: recovery attempts restore the values captured by the stale scopes.
        Assert.Equal(new List<int> { 1, 2, 0, 1 }, TestTracker.Binds);
    }

    #endregion

    #region Private

    /// <summary>Captures bind operations while exercising the generic scope tracker.</summary>
    private sealed class TestTracker : BindScopeTracker<TestTracker>
    {
        #region Public API

        /// <summary>Gets the simulated active binding identifier.</summary>
        public static int Current { get; private set; }

        /// <summary>Gets the identifiers applied by the simulated bind operation.</summary>
        public static List<int> Binds { get; } = new();

        /// <summary>Sets the initial binding and clears recorded operations and thread-local scopes.</summary>
        public static void Reset(int current)
        {
            Current = current;
            Binds.Clear();
            ClearThreadStack();
        }

        #endregion

        #region Private

        /// <summary>Returns the currently simulated binding.</summary>
        protected override int GetCurrent() => Current;

        /// <summary>Updates the simulated binding and records the operation.</summary>
        protected override void Bind(int id)
        {
            Current = id;
            Binds.Add(id);
        }

        #endregion
    }

    #endregion
}

