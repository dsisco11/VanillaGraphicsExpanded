using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Preserves established scissor state without querying or changing unrelated driver state.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Captures authoritative cached scissor state; the caller must establish it before entering this boundary.</summary>
    /// <exception cref="InvalidOperationException">Scissor state was never established or has been invalidated.</exception>
    public ScissorStateScope PreserveScissorState()
    {
        if (scissorTestEnabled is not { } enabled)
            throw new InvalidOperationException("Establish scissor state through the pipeline state cache before preserving it.");
        return new ScissorStateScope(this, enabled);
    }

    /// <summary>Restores only scissor enablement through a pipeline description, leaving indexed state untouched.</summary>
    public readonly struct ScissorStateScope : IDisposable
    {
        private readonly StateCache cache;
        private readonly GlPipelineDesc restore;

        /// <summary>Builds the restoration contract from known cached state without issuing driver calls.</summary>
        internal ScissorStateScope(StateCache cache, bool enabled)
        {
            this.cache = cache;
            var mask = GlPipelineStateMask.From(GlPipelineStateId.ScissorTestEnable);
            restore = new GlPipelineDesc(enabled ? default : mask, enabled ? mask : default,
                name: "Restore.Scissor");
        }

        /// <summary>Reestablishes the captured state even when the enclosed operation throws.</summary>
        public void Dispose() => cache.Apply(restore);
    }
    #endregion
}
