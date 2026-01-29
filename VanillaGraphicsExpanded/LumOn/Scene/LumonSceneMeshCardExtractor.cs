using System;
using System.Collections.Generic;
using System.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// v1 mesh-card extractor: groups triangles into planar cards in model space.
/// </summary>
/// <remarks>
/// This is intentionally generic (does not depend on Vintage Story mesh types).
/// Callers are expected to supply positions + triangle indices for a mesh asset.
/// </remarks>
internal static class LumonSceneMeshCardExtractor
{
    private readonly struct GroupKey : IEquatable<GroupKey>
    {
        public readonly short Nx;
        public readonly short Ny;
        public readonly short Nz;
        public readonly int Dq;

        public GroupKey(short nx, short ny, short nz, int dq)
        {
            Nx = nx;
            Ny = ny;
            Nz = nz;
            Dq = dq;
        }

        public bool Equals(GroupKey other)
            => Nx == other.Nx && Ny == other.Ny && Nz == other.Nz && Dq == other.Dq;

        public override bool Equals(object? obj)
            => obj is GroupKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Nx, Ny, Nz, Dq);
    }

    private sealed class Group
    {
        public Vector3 Normal;
        public float PlaneD;
        public readonly List<int> TriIndices = new();
    }

    /// <summary>
    /// Extracts planar cards from a triangle mesh.
    /// </summary>
    public static int ExtractPlanarCards(
        ReadOnlySpan<Vector3> positions,
        ReadOnlySpan<int> triangleIndices,
        List<LumonSceneMeshCard> dst,
        float planeDistanceQuant = 1f / 256f,
        float minCardArea = 1e-6f)
    {
        if (triangleIndices.Length % 3 != 0)
        {
            throw new ArgumentException("triangleIndices length must be a multiple of 3.", nameof(triangleIndices));
        }

        if (planeDistanceQuant <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(planeDistanceQuant));
        }

        if (dst is null)
        {
            throw new ArgumentNullException(nameof(dst));
        }

        var groupsByKey = new Dictionary<GroupKey, Group>();

        for (int i = 0; i < triangleIndices.Length; i += 3)
        {
            int i0 = triangleIndices[i + 0];
            int i1 = triangleIndices[i + 1];
            int i2 = triangleIndices[i + 2];

            if ((uint)i0 >= (uint)positions.Length
                || (uint)i1 >= (uint)positions.Length
                || (uint)i2 >= (uint)positions.Length)
            {
                continue;
            }

            Vector3 p0 = positions[i0];
            Vector3 p1 = positions[i1];
            Vector3 p2 = positions[i2];

            Vector3 e0 = p1 - p0;
            Vector3 e1 = p2 - p0;
            Vector3 n = Vector3.Cross(e0, e1);
            float len = n.Length();
            if (len <= 1e-20f)
            {
                continue;
            }

            n /= len;

            float d = Vector3.Dot(n, p0);

            var key = Quantize(n, d, planeDistanceQuant);
            if (!groupsByKey.TryGetValue(key, out Group? g))
            {
                g = new Group { Normal = n, PlaneD = d };
                groupsByKey.Add(key, g);
            }

            g.TriIndices.Add(i0);
            g.TriIndices.Add(i1);
            g.TriIndices.Add(i2);
        }

        int before = dst.Count;

        foreach (Group g in groupsByKey.Values)
        {
            if (g.TriIndices.Count < 3)
            {
                continue;
            }

            Vector3 n = g.Normal;
            float d = g.PlaneD;

            // Construct a point on the plane: n dot p = d.
            Vector3 pPlane = n * d;

            OrthonormalBasis(n, out Vector3 uAxis, out Vector3 vAxis);

            float minU = float.PositiveInfinity;
            float minV = float.PositiveInfinity;
            float maxU = float.NegativeInfinity;
            float maxV = float.NegativeInfinity;

            for (int i = 0; i < g.TriIndices.Count; i++)
            {
                Vector3 p = positions[g.TriIndices[i]];
                Vector3 rel = p - pPlane;
                float u = Vector3.Dot(rel, uAxis);
                float v = Vector3.Dot(rel, vAxis);

                if (u < minU) minU = u;
                if (v < minV) minV = v;
                if (u > maxU) maxU = u;
                if (v > maxV) maxV = v;
            }

            float du = maxU - minU;
            float dv = maxV - minV;
            float area = du * dv;
            if (!(area > minCardArea))
            {
                continue;
            }

            Vector3 origin = pPlane + uAxis * minU + vAxis * minV;
            Vector3 axisU = uAxis * du;
            Vector3 axisV = vAxis * dv;

            dst.Add(new LumonSceneMeshCard(origin, axisU, axisV));
        }

        // Deterministic order for stable cardIndex assignment.
        dst.Sort(before, dst.Count - before, MeshCardComparer.Instance);

        return dst.Count - before;
    }

    private static GroupKey Quantize(Vector3 n, float d, float planeDistanceQuant)
    {
        // Normal quant: [-1,1] -> short.
        short qx = (short)Math.Clamp((int)MathF.Round(n.X * 32767f), short.MinValue, short.MaxValue);
        short qy = (short)Math.Clamp((int)MathF.Round(n.Y * 32767f), short.MinValue, short.MaxValue);
        short qz = (short)Math.Clamp((int)MathF.Round(n.Z * 32767f), short.MinValue, short.MaxValue);

        int dq = (int)MathF.Round(d / planeDistanceQuant);
        return new GroupKey(qx, qy, qz, dq);
    }

    private static void OrthonormalBasis(Vector3 n, out Vector3 u, out Vector3 v)
    {
        // Choose a stable "up" that is not parallel to n.
        Vector3 up = MathF.Abs(n.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
        u = Vector3.Normalize(Vector3.Cross(up, n));
        v = Vector3.Normalize(Vector3.Cross(n, u));
    }

    private sealed class MeshCardComparer : IComparer<LumonSceneMeshCard>
    {
        public static readonly MeshCardComparer Instance = new();

        public int Compare(LumonSceneMeshCard x, LumonSceneMeshCard y)
        {
            int c = CompareVec(x.NormalMS, y.NormalMS);
            if (c != 0) return c;

            c = CompareVec(x.OriginMS, y.OriginMS);
            if (c != 0) return c;

            c = CompareVec(x.AxisUMS, y.AxisUMS);
            if (c != 0) return c;

            return CompareVec(x.AxisVMS, y.AxisVMS);
        }

        private static int CompareVec(Vector3 a, Vector3 b)
        {
            int c = a.X.CompareTo(b.X);
            if (c != 0) return c;
            c = a.Y.CompareTo(b.Y);
            if (c != 0) return c;
            return a.Z.CompareTo(b.Z);
        }
    }
}

