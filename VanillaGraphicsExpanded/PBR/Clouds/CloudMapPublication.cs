namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Certifies observed uploads and completed generations without reading worker tiles or copying map pixels.</summary>
internal sealed class CloudMapPublication(object owner, long world)
{
    private CloudMapStorage storage;
    private long allocation, upload, shader, generation;
    private bool storageReady, shaderReady, retired;
    private CloudMapInput? completed;

    #region Public API
    #region Storage and program certificates
    /// <summary>Withdraws all content certificates before storage replacement, even if names and dimensions are reused.</summary>
    internal void BeginAllocation()
    {
        allocation++; upload = 0; storageReady = false; completed = null;
    }

    /// <summary>Records validated storage only; allocation is not evidence of initialized texels.</summary>
    internal void CompleteAllocation(CloudMapStorage current, bool valid)
    {
        storage = current; storageReady = !retired && valid && current.IsSupported;
    }

    /// <summary>Withdraws the upload before an in-place update whose failure may destroy previous content.</summary>
    internal void BeginUpload() { upload = 0; completed = null; }

    /// <summary>Certifies both native raw uploads only on a successful error-free boundary.</summary>
    internal void CompleteUpload(bool success)
    {
        if (!retired && storageReady && success) upload = ++generation;
    }

    /// <summary>Withdraws shader readiness while leaving same-allocation raw content intact.</summary>
    internal void BeginShader() { shader++; shaderReady = false; completed = null; }

    /// <summary>Accepts a linked current program, including validated late acquisition.</summary>
    internal void CompleteShader(bool success) { shaderReady = !retired && success; }
    #endregion

    #region Generation and borrowed lifetime
    /// <summary>Previous publication expires at every producer entry, including mode-gated and failed callbacks.</summary>
    internal void BeginGeneration() => completed = null;

    /// <summary>Publishes a coherent descriptor only after the matching native generation succeeded.</summary>
    internal void CompleteGeneration(uint frame, CloudMapStorage current, CloudPlacement placement, bool success)
    {
        if (current != storage)
        {
            // Unobserved replacement cannot inherit a previous upload certificate.
            BeginAllocation(); storage = current;
        }
        if (!retired && success && storageReady && shaderReady && upload != 0 && placement.IsFinite)
            completed = new(owner, world, allocation, upload, shader, ++generation, frame, storage, placement);
    }

    /// <summary>Rejects stale frames, worlds, owners and retired allocations without sampling GPU memory.</summary>
    internal bool TryGet(object expectedOwner, long expectedWorld, uint frame, out CloudMapInput input)
    {
        input = default;
        if (retired || !ReferenceEquals(owner, expectedOwner) || expectedWorld != world || completed is not { } value || value.Frame != frame)
            return false;
        input = value; return true;
    }

    /// <summary>Revalidates a previously borrowed descriptor after any intervening native activity.</summary>
    internal bool IsCurrent(CloudMapInput input, uint frame) =>
        TryGet(input.Owner, input.World, frame, out var current) && current == input;

    /// <summary>Retires certificates only; the native owner disposes all borrowed storage.</summary>
    internal void Retire() { retired = true; completed = null; upload = 0; storageReady = shaderReady = false; }
    #endregion
    #endregion
}
