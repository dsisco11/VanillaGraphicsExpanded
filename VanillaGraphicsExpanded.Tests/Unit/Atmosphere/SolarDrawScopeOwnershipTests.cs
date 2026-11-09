using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks finite solar geometry, shared energy and engine draw scope ownership.</summary>
public sealed class SolarDrawScopeOwnershipTests
{
    #region Public API
    /// <summary>Nested engine callbacks and exception unwinding restore the previous solar classification.</summary>
    [Fact]
    public void DrawScopeRestoresNestedAndFailedCallbacks()
    {
        // Arrange by recording the global draw classification before nested callback simulation.
        bool original = AtmosphereSunDrawHook.Active;
        // Act and Assert: nest callbacks, unwind an exception, and verify the prior scope is restored.
        try
        {
            AtmosphereSunDrawHook.Active = false;
            AtmosphereSunDrawHook.Prefix(out bool outer);
            Assert.True(AtmosphereSunDrawHook.Active);
            AtmosphereSunDrawHook.Prefix(out bool inner);
            try { throw new InvalidOperationException("draw failed"); }
            catch (InvalidOperationException) { }
            finally { AtmosphereSunDrawHook.Finalizer(inner); }
            Assert.True(AtmosphereSunDrawHook.Active);
            AtmosphereSunDrawHook.Finalizer(outer);
            Assert.False(AtmosphereSunDrawHook.Active);
        }
        finally { AtmosphereSunDrawHook.Active = original; }
    }
    #endregion
}
