using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Authors isotropic luminous flux and linear Rec.709 chromaticity at a view-space metre position.</summary>
internal readonly record struct PunctualLight(Vector3 Position, Vector3 LinearColor, float Lumens, float InfluenceRadiusMetres)
{
    #region Public API
    /// <summary>Validates source authoring and converts flux to the shared evaluated-candela payload.</summary>
    internal Vector4 IntensityPayload()
    {
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z)
            || !float.IsFinite(InfluenceRadiusMetres) || InfluenceRadiusMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(PunctualLight));
        Vector3 intensity = LightingCalibration.SourceRgb(LinearColor, LightingCalibration.IsotropicCandela(Lumens));
        return new(intensity, InfluenceRadiusMetres);
    }
    #endregion
}
