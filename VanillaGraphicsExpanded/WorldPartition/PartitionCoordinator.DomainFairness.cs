using System.Linq;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Reserves minimum service for domain queues which cannot be invoked by the automatic provider pump.</summary>
internal sealed partial class PartitionCoordinator
{
    private long nextDomainWait;
    private long admissionSequence, lastAutomaticAdmission, lastDomainAdmission;
    #region Admission reservation
    /// <summary>Counts eligible waiting peers; cancelled, retired and locally blocked demand reserves no shared credit.</summary>
    private int WaitingDomainPeers(PartitionRegistration current) => registrations.Values.Count(r =>
        r != current && r.WaitingDomainCapture is { } coordinate &&
        (current.WaitingDomainCapture == null || r.DomainWaitOrder < current.DomainWaitOrder) &&
        r.Cells.TryGetValue(coordinate, out PartitionCellState? cell) && cell.Request == null &&
        cell.Desired != PartitionResidency.Unloaded && cell.RetryAt <= tick &&
        r.Captures < r.Limits.Captures && r.Dispatches < r.Limits.Dispatches &&
        Outstanding(r) < r.Limits.InFlight);

    /// <summary>Leaves one capture/dispatch credit per waiting peer so fixed render order cannot consume every update.</summary>
    private bool HasDomainServiceRoom(PartitionRegistration current)
    {
        // A reserved domain turn must not suppress automatic providers forever under continuous domain demand.
        // Once either group receives service, the other group gets its next opportunity first.
        if (current.Provider is IPartitionProvider && lastAutomaticAdmission < lastDomainAdmission) return true;
        int waiting = WaitingDomainPeers(current);
        if (waiting == 0) return true;
        return shared.Captures - captures > waiting && shared.Dispatches - dispatches > waiting &&
            shared.InFlight - Outstanding(null) > waiting;
    }
    #endregion
}
