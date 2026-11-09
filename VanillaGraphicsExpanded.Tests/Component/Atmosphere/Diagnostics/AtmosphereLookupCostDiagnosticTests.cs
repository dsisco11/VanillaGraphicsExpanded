using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere.Diagnostics;

/// <summary>Measures warm CPU cost for bounded atmosphere lookup updates without a timing threshold.</summary>
public sealed class AtmosphereLookupCostDiagnosticTests
{
    private const int DefaultUpdates = (AtmosphereLookup.DefaultWidth * AtmosphereLookup.DefaultHeight + AtmosphereLookup.SamplesPerUpdate - 1) / AtmosphereLookup.SamplesPerUpdate;

    #region Public API
    /// <summary>Records warm CPU cost for the bounded update and complete refresh without imposing machine-specific thresholds.</summary>
    [Fact]
    [Trait("Purpose", "Diagnostic")]
    public void RecordBoundedUpdateCost()
    {
        // Arrange two warm-up lookups using the production default dimensions before collecting timed runs.
        for (int warm = 0; warm < 2; warm++)
        {
            var warmLookup = new AtmosphereLookup();
            for (int update = 0; update < DefaultUpdates; update++) warmLookup.Update(Vector3.UnitY, 0, .5f, complete: false);
        }
        var elapsed = new double[5];
        // Act by timing five full bounded refreshes; assert only that each completes as one publication revision.
        for (int run = 0; run < elapsed.Length; run++)
        {
            var lookup = new AtmosphereLookup();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int update = 0; update < DefaultUpdates; update++) lookup.Update(Vector3.UnitY, 0, .5f, complete: false);
            timer.Stop();
            elapsed[run] = timer.Elapsed.TotalMilliseconds;
            Assert.Equal(1, lookup.Revision);
        }
        Array.Sort(elapsed);
        Xunit.TestContext.Current.TestOutputHelper!.WriteLine($"Warm median complete refresh: {elapsed[2]:F3} ms; amortized bounded update: {elapsed[2] / DefaultUpdates:F3} ms; five runs range: {elapsed[0]:F3}..{elapsed[^1]:F3} ms.");
    }
    #endregion
}
