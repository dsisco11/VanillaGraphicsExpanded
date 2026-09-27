using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Owns one coherent atlas index map and compact rectangle/amplitude table.</summary>
internal sealed class MaterialDisplacementPage(Texture2D indices, Texture2D records) : IDisposable
{
    internal Texture2D Indices { get; } = indices;
    internal Texture2D Records { get; } = records;

    /// <summary>Releases both resources together when an atlas generation is retired.</summary>
    public void Dispose()
    {
        Indices.Dispose();
        Records.Dispose();
    }
}
