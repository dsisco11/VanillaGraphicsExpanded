namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines owned-staging readback submission independently of CPU completion.</summary>
internal partial interface IRHIBackend
{
    /// <summary>Enqueues an explicit copy and retains its staging until completion or retirement.</summary>
    RHIReadbackTicket EnqueueReadback(in RHIReadbackDescription description);
}
