using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;


namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Owns one coherently uploaded sky and finite-path texture generation.</summary>
internal sealed class AtmosphereTextureSet : IDisposable
{
    private float[] skyUpload = Array.Empty<float>(), aerialUpload = Array.Empty<float>();
    /// <summary>Contains smooth and Mie sky lighting from one completed generation.</summary>
    internal DynamicTexture2D Sky { get; }
    /// <summary>Contains finite-path smooth and Mie lighting in the published encoding.</summary>
    internal DynamicTexture3D Radiance { get; }
    /// <summary>Contains dimensionless finite-path extinction independently of lighting scale.</summary>
    internal DynamicTexture3D Attenuation { get; }
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
        int depth = lighting.AerialRadiance.IsEmpty ? 1 : AtmosphereAerialPerspective.Depth;
        int width = depth == 1 ? 1 : lighting.Width, height = depth == 1 ? 1 : lighting.Height;
        Sky = DynamicTexture2D.Create(lighting.Width, lighting.Height << 1, PixelInternalFormat.Rgba16f, debugName: "Atmosphere.Sky");
        try
        {
            Radiance = DynamicTexture3D.Create(width, height << 1, depth, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture3D);
            Attenuation = DynamicTexture3D.Create(width, height, depth, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture3D);
            foreach (GpuTexture texture in new GpuTexture[] { Sky, Radiance, Attenuation })
            {
                texture.DisableMipmaps();
                texture.SetTexFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
                texture.SetTexWrap(TextureWrapMode.Repeat, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            }
        }
        catch { Sky.Dispose(); Radiance?.Dispose(); Attenuation?.Dispose(); throw; }
    }

    /// <summary>Checks storage compatibility without changing the currently published set.</summary>
    internal bool Matches(AtmosphereLighting lighting) => Sky.Width == lighting.Width && Sky.Height == (lighting.Height << 1)
        && Radiance.Depth == (lighting.AerialRadiance.IsEmpty ? 1 : AtmosphereAerialPerspective.Depth);

    /// <summary>Uploads immutable completed data synchronously; callers publish only after all uploads succeed.</summary>
    internal void Upload(AtmosphereLighting lighting)
    {
        if (!LightingCalibration.FiniteNonnegative(lighting.Solar) || !LightingCalibration.FiniteNonnegative(lighting.Environment))
            throw new ArgumentException("Atmosphere source publication requires finite nonnegative quantities.", nameof(lighting));
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
        // The GPU upload only reads these arrays; no mutable reference escapes this owner.
        if (skyUpload.Length != (lighting.Sky.Length << 1)) skyUpload = new float[lighting.Sky.Length << 1];
        AtmosphereMieTransport.Pack(lighting.Sky.AsSpan(), lighting.SkyMie.AsSpan(), skyUpload,
            lighting.Width, lighting.Height, 1, lighting.Sun, lighting.HorizonElevation, lighting.LookupStorageScale);
        float[] neutral = [0, 0, 0, 1];
        int aerialLength = (lighting.AerialRadiance.IsEmpty ? 4 : lighting.AerialRadiance.Length) << 1;
        if (aerialUpload.Length != aerialLength) aerialUpload = new float[aerialLength];
        AtmosphereMieTransport.Pack(lighting.AerialRadiance.IsEmpty ? neutral : lighting.AerialRadiance.AsSpan(),
            lighting.AerialMie.AsSpan(), aerialUpload, Radiance.Width, Radiance.Height >> 1, Radiance.Depth, lighting.Sun, lighting.HorizonElevation,
            lighting.LookupStorageScale);
        float reconstructionMaximum = Math.Max(ReconstructionBound(skyUpload, lighting.Width * lighting.Height * 4,
            lighting.LookupStorageScale), ReconstructionBound(aerialUpload, Radiance.Width * (Radiance.Height >> 1) * 4,
            lighting.LookupStorageScale));
        // Validate both light-bearing arrays before submitting uploads into the unpublished set.
        // Attenuation and coverage retain their original dimensionless representation.
        Sky.UploadData(skyUpload);
        Radiance.UploadData(aerialUpload,
            0, 0, 0, Radiance.Width, Radiance.Height, Radiance.Depth);
        Attenuation.UploadData(lighting.AerialRadiance.IsEmpty ? neutral : ImmutableCollectionsMarshal.AsArray(lighting.AerialAttenuation)!,
            0, 0, 0, Attenuation.Width, Attenuation.Height, Attenuation.Depth);
        StorageScale = lighting.LookupStorageScale;
        CalibrationGeneration = lighting.CalibrationGeneration;
        ReconstructionMaximumNits = reconstructionMaximum;
    }
    #endregion

    #region Lifetime
    /// <summary>Retires all resources in the set through their GPU owners.</summary>
    public void Dispose() { Sky.Dispose(); Radiance.Dispose(); Attenuation.Dispose(); }
    #endregion
    #endregion

    #region Private
    /// <summary>Bounds filtered bands and the maximum phase before exposing matching-generation metadata.</summary>
    private static float ReconstructionBound(ReadOnlySpan<float> packed, int sliceLength, float scale)
    {
        double smooth = 0, mie = 0;
        for (int offset = 0; offset < packed.Length; offset += sliceLength * 2)
        for (int index = 0; index < sliceLength; index += 4)
        for (int channel = 0; channel < 3; channel++)
        {
            smooth = Math.Max(smooth, packed[offset + index + channel]);
            mie = Math.Max(mie, packed[offset + sliceLength + index + channel]);
        }
        // Binary16 rounding, filtered reconstruction and the FP32 phase expression get separate headroom.
        double phase = AtmosphereMieTransport.Factor(1) * (1 + 32.0 / (1 << 23));
        return LightingStorage.UpperBound((smooth + phase * mie) / scale * (1 + 1.0 / 1024));
    }
    #endregion
}
