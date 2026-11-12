using System;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Freezes existing native displacement eligibility, camera policy and atlas ownership for one terrain group.</summary>
internal sealed class NativeShadowTerrainSubdivision
{
    private readonly NativeShadowTerrainGroup group;
    private readonly MaterialAtlasSystem owner;
    private readonly TerrainSubdivisionInputs inputs;
    private readonly MaterialDisplacementSnapshot material;
    private readonly bool eligible;

    #region Public API
    /// <summary>Selects displacement only where the visible native terrain family also permits it.</summary>
    internal bool Enabled => eligible && material.HasDisplacement;
    /// <summary>Bounds triangle expansion by the existing maximum subdivision policy, including odd-level rounding.</summary>
    internal int TriangleAmplification => Enabled ? checked((int)Math.Ceiling(inputs.MaximumLevel) * (int)Math.Ceiling(inputs.MaximumLevel)) : 1;
    /// <summary>Supplies the existing conservative authored displacement envelope.</summary>
    internal float BoundsPadding => Enabled ? MaterialDisplacement.MaximumAmplitudeMetres : 0;
    /// <summary>Retains the material epoch even when this atlas currently has no authored displacement.</summary>
    internal long MaterialRevision => material.Revision;
    /// <summary>Rejects live policy, family, storage or publication changes before native draws.</summary>
    internal bool IsCurrent => group.IsCurrent && inputs == TerrainDisplacementRuntime.CaptureInputs() &&
        (!eligible || owner.IsDisplacementCurrent(material));

    /// <summary>Reads existing native policy and the owning atlas publication without adding material or pool classification state.</summary>
    internal NativeShadowTerrainSubdivision(NativeShadowTerrainGroup group, MaterialAtlasSystem owner)
    {
        ArgumentNullException.ThrowIfNull(group); ArgumentNullException.ThrowIfNull(owner);
        if (!group.IsCurrent) throw new InvalidOperationException("Native terrain storage is unavailable.");
        this.group = group; this.owner = owner;
        inputs = TerrainDisplacementRuntime.CaptureInputs();
        eligible = inputs.Enabled && group.Dimension != 1 && TerrainDisplacementRuntime.IsEligiblePass(group.Pass);
        if (eligible && !owner.TryCaptureDisplacement(group.TextureId, out material))
            throw new InvalidOperationException("Native displacement material publication is incomplete.");
    }

    /// <summary>Associates this snapshot with its exact native group rather than a matching pool identifier alone.</summary>
    internal bool Matches(NativeShadowTerrainGroup current) => ReferenceEquals(group, current) && IsCurrent;

    /// <summary>Publishes the exact frozen native parameters to the packaged displacement executable.</summary>
    internal void Apply(ShadowDisplacedTerrainShader shader)
    {
        if (!Enabled || !IsCurrent) throw new InvalidOperationException("Native displacement snapshot is unavailable.");
        shader.CaptureSubdivision(inputs.EdgePixels, inputs.MaximumLevel, inputs.FadeStart, inputs.FadeEnd,
            inputs.FocalPixels, true, material.Indices, material.Records, material.Height);
    }
    #endregion
}
