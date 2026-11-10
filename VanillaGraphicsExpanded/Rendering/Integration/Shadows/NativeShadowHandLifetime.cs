using System;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Separates hand replacement from motion, including a collectible replaced in the same stack object.</summary>
internal sealed class NativeShadowHandLifetime
{
    private readonly ItemStack?[] items = new ItemStack?[2];
    private readonly CollectibleObject?[] collectibles = new CollectibleObject?[2];
    private readonly long[] lifetimes = new long[2];

    #region Public API
    /// <summary>Advances a hand's emission lifetime only when its item identity changes.</summary>
    internal long Capture(int slot, ItemStack item, ShadowSourceLifetimes owners)
    {
        if (slot is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(slot));
        int index = slot - 1;
        if (!ReferenceEquals(items[index], item) || !ReferenceEquals(collectibles[index], item.Collectible))
        {
            items[index] = item; collectibles[index] = item.Collectible;
            lifetimes[index] = owners.Get(new object());
        }
        return lifetimes[index];
    }
    #endregion
}
