using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>A 64-byte GPU hit query retaining integer world coordinates and an optional CPU block identity.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SurfaceLightingQuery
{
    public int X, Y, Z, BlockId;
    public int NormalX, NormalY, NormalZ, Reserved;
    public Vector4 Fraction;
    public Vector4 Result;

    #region Construction
    /// <summary>Creates an unresolved query without converting the world anchor to floats.</summary>
    public SurfaceLightingQuery(in VectorInt3 cell, in VectorInt3 normal, in Vector3 fraction, int blockId = 0)
    {
        X=cell.X; Y=cell.Y; Z=cell.Z; BlockId=blockId;
        NormalX=normal.X; NormalY=normal.Y; NormalZ=normal.Z; Reserved=0;
        Fraction=new(fraction,0); Result=default;
    }
    #endregion
}
