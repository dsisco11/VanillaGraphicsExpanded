using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene.Fallback;

/// <summary>Mirrors the cache lookup's integer face address for delayed hit-page lifetime validation.</summary>
internal static class SurfaceFallbackHitAddress
{
    /// <summary>Maps an axial hit to its containing chunk and four-block patch without floating world coordinates.</summary>
    public static bool TryResolve(in SurfaceLightingQuery hit, out VectorInt3 chunk, out uint patch)
    {
        chunk = new(hit.X >> 5,hit.Y >> 5,hit.Z >> 5); patch = 0;
        if (System.Math.Abs((long)hit.NormalX)+System.Math.Abs((long)hit.NormalY)+System.Math.Abs((long)hit.NormalZ)!=1) return false;
        int axis = hit.NormalX!=0 ? hit.NormalX>0?0:1 : hit.NormalY!=0 ? hit.NormalY>0?2:3 : hit.NormalZ>0?4:5;
        int x=hit.X & 31,y=hit.Y & 31,z=hit.Z & 31;
        int plane=axis<2?x:axis<4?y:z;
        int u=axis<2?z:x, v=axis<2?y:axis<4?z:y;
        patch = (uint)(1 + 6 * ((plane << 6) + ((v >> 2) << 3) + (u >> 2)) + axis);
        return true;
    }
}
