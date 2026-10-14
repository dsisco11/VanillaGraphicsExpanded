using System;
using System.Collections.Generic;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Owns one persistent immutable instance stream and its procedural six-vertex billboard geometry.</summary>
internal sealed class CelestialStarGeometry : IDisposable
{
    internal static readonly VertexLayoutDesc Layout = new([
        new(0, 4, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 32, 1),
        new(1, 4, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 16, 32, 1)]);
    private readonly GpuResourceCollection owned = new();
    /// <summary>Contains the VAO borrowing this owner's instance stream.</summary>
    internal ArrayGraphicsGeometry Geometry { get; }
    /// <summary>Contains exactly the nested quality prefix, without a per-frame upload.</summary>
    internal int Count { get; }
    #region Public API
    /// <summary>Uploads calibrated direction, color and flux exactly once for the completed source subset.</summary>
    internal CelestialStarGeometry(CelestialSource source)
    {
        Count = source.Quality.StarCount;
        try
        {
            var data = new Vector4[Count * 2];
            for (int i = 0; i < Count; i++)
            {
                var star = source.Stars[i];
                data[2*i] = new(star.Direction, star.FluxLux);
                data[2*i+1] = new(star.Color, 0);
            }
            var stream = owned.Own(GpuVbo.Create(debugName: "Celestial.Stars"));
            stream.UploadData(data);
            Geometry = new(Layout, PrimitiveType.Triangles, new Dictionary<int,GpuVbo> { [0] = stream });
        }
        catch { owned.Dispose(); throw; }
    }
    /// <summary>Retires the borrower before its deferred GPU stream storage.</summary>
    public void Dispose() => Rendering.Integration.RenderingCleanup.Run(Geometry.Dispose, owned.Dispose);
    #endregion
}
