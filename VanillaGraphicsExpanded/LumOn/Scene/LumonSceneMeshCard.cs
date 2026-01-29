using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// A planar mesh card parameterization in model space.
/// </summary>
/// <remarks>
/// Card space is defined by:
/// <code>
/// p(u,v,depth) = origin + u*axisU + v*axisV + normal*depth
/// </code>
/// where <c>u,v</c> are in [0,1] and <c>depth</c> is a signed displacement along <c>normal</c>.
/// </remarks>
internal readonly struct LumonSceneMeshCard : IEquatable<LumonSceneMeshCard>
{
    public readonly Vector3 OriginMS;
    public readonly Vector3 AxisUMS;  // scaled to card width
    public readonly Vector3 AxisVMS;  // scaled to card height
    public readonly Vector3 NormalMS; // unit-ish; derived from axes

    public LumonSceneMeshCard(Vector3 originMS, Vector3 axisUMS, Vector3 axisVMS)
    {
        OriginMS = originMS;
        AxisUMS = axisUMS;
        AxisVMS = axisVMS;

        Vector3 n = Vector3.Cross(axisUMS, axisVMS);
        float len = n.Length();
        NormalMS = len > 1e-20f ? (n / len) : Vector3.UnitY;
    }

    public bool Equals(LumonSceneMeshCard other)
        => OriginMS.Equals(other.OriginMS)
           && AxisUMS.Equals(other.AxisUMS)
           && AxisVMS.Equals(other.AxisVMS);

    public override bool Equals(object? obj)
        => obj is LumonSceneMeshCard other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(OriginMS, AxisUMS, AxisVMS);

    public static bool operator ==(LumonSceneMeshCard left, LumonSceneMeshCard right) => left.Equals(right);
    public static bool operator !=(LumonSceneMeshCard left, LumonSceneMeshCard right) => !left.Equals(right);

    public void TransformToWorld(in Matrix4x4 modelToWorld, out Vector3 originWS, out Vector3 axisUWS, out Vector3 axisVWS, out Vector3 normalWS)
    {
        originWS = Vector3.Transform(OriginMS, modelToWorld);
        axisUWS = Vector3.TransformNormal(AxisUMS, modelToWorld);
        axisVWS = Vector3.TransformNormal(AxisVMS, modelToWorld);

        Vector3 n = Vector3.Cross(axisUWS, axisVWS);
        float len = n.Length();
        normalWS = len > 1e-20f ? (n / len) : Vector3.UnitY;
    }
}

