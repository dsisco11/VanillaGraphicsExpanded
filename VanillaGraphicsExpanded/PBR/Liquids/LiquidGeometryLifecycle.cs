using System;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Delivers engine allocation boundaries to borrowed liquid geometry owners without storing membership.</summary>
internal static class LiquidGeometryLifecycle
{
    internal static event Action? Changed;
    internal static event Action<MeshDataPool>? Retiring;

    #region Public API
    /// <summary>Schedules preparation after engine manager selection or pool allocation changes.</summary>
    internal static void ChangedCollections() => Changed?.Invoke();

    /// <summary>Withdraws adapters before their engine allocation is deleted.</summary>
    internal static void Retire(MeshDataPool pool) => Retiring?.Invoke(pool);
    #endregion
}
