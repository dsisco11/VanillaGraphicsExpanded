using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.Unit;

/// <summary>Checks bounded page admission, visible priority and eventual residency fairness.</summary>
public sealed class SurfaceLightingAdmissionScheduleTests
{
    #region Admission
    /// <summary>Even one dispatch credit preserves visible preference and eventual turns for distant pages.</summary>
    [Fact]
    public void OneSlotAlternatesPriorityAndFairness()
    {
        var schedule = new SurfaceLightingAdmissionSchedule(); var selected = new List<uint>();
        for (int frame = 0; frame < 6; frame++)
            schedule.Select([1, 2, 3], [3, 2, 1], 1, page => { selected.Add(page); return true; });
        Assert.Equal(new uint[] { 1, 3, 2, 3, 3, 3 }, selected);
    }

    /// <summary>Deferred candidates consume no dispatch credits and admitted identities cannot repeat in one batch.</summary>
    [Fact]
    public void DeferredPagesYieldWithoutChargingBudget()
    {
        var schedule = new SurfaceLightingAdmissionSchedule(); var admitted = new List<uint>();
        schedule.Select([1, 2, 3, 4, 5], [1, 5, 4, 3, 2], 3, page =>
        {
            if (page <= 2) return false;
            admitted.Add(page); return true;
        });
        Assert.Equal(new uint[] { 3, 4, 5 }, admitted);
    }

    /// <summary>Removing the cursor identity preserves ordering and clearing resets fair ownership.</summary>
    [Fact]
    public void RetiredIdentityAndClearPreserveDefinedOrder()
    {
        var schedule = new SurfaceLightingAdmissionSchedule(); var admitted = new List<uint>();
        bool Admit(uint page) { admitted.Add(page); return true; }
        schedule.Select([1, 2, 3], [3, 2, 1], 2, Admit);
        schedule.Select([2, 3], [3, 2], 2, Admit);
        Assert.Equal(new uint[] { 1, 3, 2, 3 }, admitted);
        schedule.Clear(); admitted.Clear();
        schedule.Select([1, 2, 3], [3, 2, 1], 1, Admit);
        Assert.Equal(new uint[] { 1 }, admitted);
    }

    /// <summary>Disabled and empty allocations never consult page eligibility.</summary>
    [Fact]
    public void DisabledAndEmptyAllocationsDoNoWork()
    {
        var schedule = new SurfaceLightingAdmissionSchedule();
        schedule.Select([1], [1], 0, _ => throw new InvalidOperationException());
        schedule.Select([], [], 4, _ => throw new InvalidOperationException());
    }
    #endregion
}
