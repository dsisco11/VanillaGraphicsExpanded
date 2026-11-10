using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Lights;

/// <summary>Assigns non-recycled identities to native owner instances without keeping them alive.</summary>
internal sealed class ShadowSourceLifetimes
{
    private readonly ConditionalWeakTable<object, Identity> identities = new();
    private long next;

    #region Public API
    /// <summary>Retains the same identity while an owner moves; replacements never inherit it.</summary>
    internal long Get(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return identities.GetValue(owner, _ => new Identity(Allocate())).Value;
    }

    /// <summary>Ends an explicitly removed registration even if its native object is reused.</summary>
    internal void Remove(object owner) => identities.Remove(owner);
    #endregion

    #region Private
    /// <summary>Fails closed before a lifetime counter can wrap into a previous identity.</summary>
    private long Allocate()
    {
        long value = Interlocked.Increment(ref next);
        if (value <= 0) throw new InvalidOperationException("Shadow source lifetime space exhausted.");
        return value;
    }

    /// <summary>Stores a weak-key table's immutable identity value.</summary>
    private sealed record Identity(long Value);
    #endregion
}
