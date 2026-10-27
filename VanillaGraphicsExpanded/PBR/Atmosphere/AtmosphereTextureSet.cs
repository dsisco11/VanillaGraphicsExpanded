using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;


namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Owns one coherently uploaded sky and finite-path texture generation.</summary>
internal sealed class AtmosphereTextureSet : IDisposable
{
    private readonly int bands;
    private float[] skyUpload = Array.Empty<float>(), aerialUpload = Array.Empty<float>();
    /// <summary>Contains smooth and Mie sky lighting from one completed generation.</summary>
    internal DynamicTexture2D Sky { get; }
    /// <summary>Contains finite-path smooth and Mie lighting in the published encoding.</summary>
    internal DynamicTexture3D Radiance { get; }
    /// <summary>Contains dimensionless finite-path extinction independently of lighting scale.</summary>
    internal DynamicTexture3D Attenuation { get; }
    /// <summary>Contains dimensionless observer-to-space extinction from the same completed generation.</summary>
    internal DynamicTexture2D ExternalTransmission { get; }
    /// <summary>Matches encoded texture magnitudes to the last successfully uploaded CPU generation.</summary>
    internal float StorageScale { get; private set; } = 1;
    /// <summary>Matches source calibration to the same completed sky/aerial upload.</summary>
    internal uint CalibrationGeneration { get; private set; }
    /// <summary>Conservative angular reconstruction bound committed with this complete texture generation.</summary>
    internal float ReconstructionMaximumNits { get; private set; }

    #region Public API
    #region Construction and upload
    /// <summary>Allocates the full generation, or neutral one-voxel transport for the startup snapshot.</summary>
    internal AtmosphereTextureSet(AtmosphereLighting lighting)
    {
        bands = lighting.HasNightSources ? 3 : 2;
        int depth = lighting.AerialRadiance.IsEmpty ? 1 : AtmosphereAerialPerspective.Depth;
        int width = depth == 1 ? 1 : lighting.Width, height = depth == 1 ? 1 : lighting.Height;
        Sky = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, lighting.Width), Height = System.Math.Max(1, lighting.Height * bands), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Atmosphere.Sky" }).RequireResource();
        try
        {
            Radiance = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height * bands), Depth = System.Math.Max(1, depth), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            Attenuation = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), Depth = System.Math.Max(1, depth), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            ExternalTransmission = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, lighting.ExternalTransmission.IsEmpty ? 1 : AtmosphereExternalTransmission.SampleCount), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Atmosphere.ExternalTransmission" }).RequireResource();
            foreach (GpuTexture texture in new GpuTexture[] { Sky, Radiance, Attenuation })
            {
                texture.DisableMipmaps();
                texture.SetTexFilter(SamplerFilter.Linear, SamplerFilter.Linear);
                texture.SetTexWrap(SamplerAddressMode.Repeat, SamplerAddressMode.ClampToEdge, SamplerAddressMode.ClampToEdge);
            }
        }
        catch { Sky.Dispose(); Radiance?.Dispose(); Attenuation?.Dispose(); ExternalTransmission?.Dispose(); throw; }
    }

    /// <summary>Checks storage compatibility without changing the currently published set.</summary>
    internal bool Matches(AtmosphereLighting lighting) => bands == (lighting.HasNightSources ? 3 : 2)
        && Sky.Width == lighting.Width && Sky.Height == lighting.Height * bands
        && Radiance.Depth == (lighting.AerialRadiance.IsEmpty ? 1 : AtmosphereAerialPerspective.Depth)
        && ExternalTransmission.Width == (lighting.ExternalTransmission.IsEmpty ? 1 : AtmosphereExternalTransmission.SampleCount);

    /// <summary>Uploads immutable completed data synchronously; callers publish only after all uploads succeed.</summary>
    internal void Upload(AtmosphereLighting lighting)
    {
        if (!LightingCalibration.FiniteNonnegative(lighting.Solar) || !LightingCalibration.FiniteNonnegative(lighting.Lunar)
            || !LightingCalibration.FiniteNonnegative(lighting.Environment))
            throw new ArgumentException("Atmosphere source publication requires finite nonnegative quantities.", nameof(lighting));
        // The retained solar response bound also covers the larger installed lunar disk.
        // Reject an incompatible smaller emitter before it can bypass that storage contract.
        if (lighting.HasNightSources && (!float.IsFinite(lighting.MoonAngularRadius)
            || lighting.MoonAngularRadius < AtmosphereSolarDisk.AngularRadius || lighting.MoonAngularRadius > .04f))
            throw new ArgumentException("Lunar radius must match the admitted luminous footprint.", nameof(lighting));
        if ((lighting.LookupStorageScale != 1 && lighting.LookupStorageScale != LightingStorage.PersistentScale)
            || (lighting.LookupStorageScale == LightingStorage.PersistentScale && lighting.CalibrationGeneration == 0))
            throw new ArgumentException("Atmosphere encoding requires matching calibration metadata.", nameof(lighting));
        int skyLength = checked(lighting.Width * lighting.Height * 4);
        if (lighting.Width <= 0 || lighting.Height <= 0 || lighting.Sky.Length != skyLength)
            throw new ArgumentException("Atmosphere sky dimensions do not match the snapshot.", nameof(lighting));
        if (!lighting.AerialRadiance.IsEmpty && (lighting.AerialRadiance.Length != lighting.Width * lighting.Height * AtmosphereAerialPerspective.Depth * 4
            || lighting.AerialAttenuation.Length != lighting.AerialRadiance.Length))
            throw new ArgumentException("Atmospheric volume dimensions do not match the snapshot.", nameof(lighting));
        // Extinction metadata is dimensionless and must not gain validity from RGB storage containment.
        for (int index = 0; index < lighting.AerialAttenuation.Length; index++)
        {
            float value = lighting.AerialAttenuation[index];
            if (!float.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentException("Atmospheric attenuation must be finite and within its unit interval.", nameof(lighting));
        }
        if ((!lighting.ExternalTransmission.IsEmpty && lighting.ExternalTransmission.Length != AtmosphereExternalTransmission.SampleCount*4)
            || ExternalTransmission.Width != (lighting.ExternalTransmission.IsEmpty ? 1 : AtmosphereExternalTransmission.SampleCount))
            throw new ArgumentException("External atmospheric transmission dimensions do not match the snapshot.",nameof(lighting));
        foreach (float value in lighting.ExternalTransmission)
            if (!float.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentException("External atmospheric transmission must be finite and within its unit interval.",nameof(lighting));
        // The GPU upload only reads these arrays; no mutable reference escapes this owner.
        if (bands != (lighting.HasNightSources ? 3 : 2)) throw new ArgumentException("Celestial texture layout changed.");
        if (skyUpload.Length != lighting.Sky.Length * bands) skyUpload = new float[lighting.Sky.Length * bands];
        AtmosphereMieTransport.Pack(lighting.Sky.AsSpan(), lighting.SkyMie.AsSpan(), skyUpload,
            lighting.Width, lighting.Height, 1, lighting.Sun, lighting.HorizonElevation, lighting.LookupStorageScale,
            lighting.SkyMoonMie.AsSpan(), lighting.Moon);
        float[] neutral = [0, 0, 0, 1];
        int aerialLength = (lighting.AerialRadiance.IsEmpty ? 4 : lighting.AerialRadiance.Length) * bands;
        if (aerialUpload.Length != aerialLength) aerialUpload = new float[aerialLength];
        AtmosphereMieTransport.Pack(lighting.AerialRadiance.IsEmpty ? neutral : lighting.AerialRadiance.AsSpan(),
            lighting.AerialMie.AsSpan(), aerialUpload, Radiance.Width, Radiance.Height / bands, Radiance.Depth, lighting.Sun, lighting.HorizonElevation,
            lighting.LookupStorageScale, lighting.AerialMoonMie.AsSpan(), lighting.Moon);
        float reconstructionMaximum = Math.Max(ReconstructionBound(skyUpload, lighting.Width * lighting.Height * 4,
            lighting.LookupStorageScale), ReconstructionBound(aerialUpload, Radiance.Width * (Radiance.Height / bands) * 4,
            lighting.LookupStorageScale));
        // Validate both light-bearing arrays before submitting uploads into the unpublished set.
        // Attenuation and coverage retain their original dimensionless representation.
        Sky.UploadData(skyUpload);
        Radiance.UploadData(aerialUpload,
            0, 0, 0, Radiance.Width, Radiance.Height, Radiance.Depth);
        Attenuation.UploadData(lighting.AerialRadiance.IsEmpty ? neutral : ImmutableCollectionsMarshal.AsArray(lighting.AerialAttenuation)!,
            0, 0, 0, Attenuation.Width, Attenuation.Height, Attenuation.Depth);
        ExternalTransmission.UploadDataImmediate(lighting.ExternalTransmission.IsEmpty ? neutral : ImmutableCollectionsMarshal.AsArray(lighting.ExternalTransmission)!);
        StorageScale = lighting.LookupStorageScale;
        CalibrationGeneration = lighting.CalibrationGeneration;
        ReconstructionMaximumNits = reconstructionMaximum;
    }
    #endregion

    #region Lifetime
    /// <summary>Retires all resources in the set through their GPU owners.</summary>
    public void Dispose() => Rendering.Integration.RenderingCleanup.Run(Sky.Dispose,Radiance.Dispose,Attenuation.Dispose,ExternalTransmission.Dispose);
    #endregion
    #endregion

    #region Private
    /// <summary>Bounds filtered bands and the maximum phase before exposing matching-generation metadata.</summary>
    private float ReconstructionBound(ReadOnlySpan<float> packed, int sliceLength, float scale)
    {
        double smooth = 0, mie = 0, lunar = 0;
        for (int offset = 0; offset < packed.Length; offset += sliceLength * bands)
        for (int index = 0; index < sliceLength; index += 4)
        for (int channel = 0; channel < 3; channel++)
        {
            smooth = Math.Max(smooth, packed[offset + index + channel]);
            mie = Math.Max(mie, packed[offset + sliceLength + index + channel]);
            if (bands == 3) lunar = Math.Max(lunar, packed[offset + 2 * sliceLength + index + channel]);
        }
        // Binary16 rounding, filtered reconstruction and the FP32 phase expression get separate headroom.
        double phase = AtmosphereMieTransport.Factor(1) * (1 + 32.0 / (1 << 23));
        return LightingStorage.UpperBound((smooth + phase * (mie + lunar)) / scale * (1 + 1.0 / 1024));
    }
    #endregion
}
