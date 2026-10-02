using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Owns a tile index image and compact homogeneous-water coefficient table.</summary>
internal sealed class MaterialWaterMediumPage(Texture2D indices, Texture2D records) : IDisposable
{
    internal Texture2D Indices { get; } = indices;
    internal Texture2D Records { get; } = records;

    #region Public API
    /// <summary>Retires both images with their material-atlas generation.</summary>
    public void Dispose()
    {
        Indices.Dispose();
        Records.Dispose();
    }
    #endregion
}
