using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains an explicit sampled-slot declaration for engine integration without owning its resources.</summary>
[CollectionBuilder(typeof(RHISampledTextureBindings), nameof(Create))]
public sealed class RHISampledTextureBindings : IEnumerable<RHISampledTextureSlot>
{
    private bool dirty = true;
    private Exception? failure;
    private IRHIContextLifetime? origin;
    internal ulong Revision { get; private set; }
    /// <summary>Lets an integration owner rebuild authored selections at its existing replacement boundary.</summary>
    internal bool RequiresRefresh => dirty;

    #region Public API
    /// <summary>Gets copied selections in stable slot order.</summary>
    public ImmutableArray<RHISampledTextureSlot> Inputs { get; }

    /// <summary>Copies authored slots once; recording and capture perform no native queries or allocation.</summary>
    public RHISampledTextureBindings(ReadOnlySpan<RHISampledTextureSlot> inputs)
    {
        Inputs = inputs.ToArray().OrderBy(value => value.Slot).ToImmutableArray();
        int prior = -1;
        foreach (var input in Inputs)
        {
            if (input.Slot < 0 || input.Slot == prior || !Enum.IsDefined(input.Dimension))
                throw new ArgumentException("Invalid or duplicate sampled slot.", nameof(inputs));
            prior = input.Slot;
            input.Texture.Resource?.ObservePreparation(this, Invalidate);
            if (!input.Sampler.IsEmpty) input.Sampler.RequireResource().ObservePreparation(this, Invalidate);
        }
    }

    /// <summary>Builds a retained declaration from collection-expression storage.</summary>
    public static RHISampledTextureBindings Create(ReadOnlySpan<RHISampledTextureSlot> inputs) => new(inputs);
    /// <summary>Freezes an existing array rather than retaining mutable caller storage.</summary>
    public static implicit operator RHISampledTextureBindings(RHISampledTextureSlot[] inputs) => new(inputs);
    /// <summary>Freezes stack-backed selections before their caller's storage expires.</summary>
    public static implicit operator RHISampledTextureBindings(ReadOnlySpan<RHISampledTextureSlot> inputs) => new(inputs);
    /// <inheritdoc />
    public IEnumerator<RHISampledTextureSlot> GetEnumerator() => ((IEnumerable<RHISampledTextureSlot>)Inputs).GetEnumerator();
    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    #endregion

    #region Internal API
    /// <summary>Establishes declared targets and sampler availability only initially or after a relevant change.</summary>
    internal void Prepare(RHICapabilities capabilities, IRHIContextLifetime context)
    {
        origin ??= context;
        origin.RequireContext();
        if (dirty)
        {
            dirty = false; failure = null;
            try
            {
                foreach (var input in Inputs)
                {
                    if (input.Slot >= capabilities.MaxCombinedTextureImageUnits) throw new ArgumentOutOfRangeException(nameof(Inputs));
                    if (!input.Texture.IsEmpty && input.Texture.RequireMetadata().Target != input.Dimension)
                        throw new ArgumentException("Sampled texture dimension does not match its declared slot.");
                    if (!input.Sampler.IsEmpty) _ = RHIResourceManager.RequireSamplerReferenceMetadata(input.Sampler.RequireResource());
                }
            }
            catch (Exception error) { failure = error; }
        }
        if (failure is not null) throw new InvalidOperationException("Sampled-slot preparation is unavailable.", failure);
    }
    #endregion

    #region Private
    /// <summary>Withdraws selectors until maintained resource facts can establish the changed declaration.</summary>
    private void Invalidate() { dirty = true; Revision++; }
    #endregion
}
