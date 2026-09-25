using System;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Detached per-frame work limits beneath the shared coordinator's resource ceilings.</summary>
internal sealed record TraceGeometryWorkBudget(int SourceChunksPerFrame, int CellsPerFrame, long UploadBytesPerFrame)
{
    public static TraceGeometryWorkBudget Default { get; } = new(2, 16, 8L << 20);

    #region Configuration
    /// <summary>Normalizes settings without changing live residency or outstanding publication leases.</summary>
    public static TraceGeometryWorkBudget From(VgeConfig.LumOnSettingsConfig.LumonSceneConfig.TraceSceneConfig settings) =>
        new(Math.Clamp(settings.SourceChunksPerFrame, 0, 8), Math.Clamp(settings.CellUploadsPerFrame, 0, 32),
            settings.UploadBytesPerFrame <= 0 ? 0 : Math.Clamp(settings.UploadBytesPerFrame, 1L << 16, 8L << 20));

    /// <summary>Rejects caller-supplied budgets outside the bounded source and coordinator capacity.</summary>
    public void Validate()
    {
        if (SourceChunksPerFrame is < 0 or > 8 || CellsPerFrame is < 0 or > 32 ||
            UploadBytesPerFrame != 0 && (UploadBytesPerFrame < (1L << 16) || UploadBytesPerFrame > (8L << 20)))
            throw new ArgumentOutOfRangeException(nameof(TraceGeometryWorkBudget));
    }
    #endregion
}
