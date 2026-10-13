using System;
using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Owns one atlas texture lifetime and its admitted current or pending material source envelope.</summary>
internal sealed class MaterialAtlasPageTextures : IDisposable
{
    private readonly object publicationGate = new();
    private float maximumEmissionNits;
    private bool retired;

    #region Public API
    /// <summary>Creates a complete page, optionally retaining compatible emission from a replaced image.</summary>
    public MaterialAtlasPageTextures(Texture2D materialParamsTexture, Texture2D? normalDepthTexture, float emissionMaximumNits = 0)
    {
        MaterialParamsTexture = materialParamsTexture ?? throw new ArgumentNullException(nameof(materialParamsTexture));
        NormalDepthTexture = normalDepthTexture;
        maximumEmissionNits = emissionMaximumNits;
    }

    /// <summary>Supplies the owned material image.</summary>
    public Texture2D MaterialParamsTexture { get; }

    /// <summary>Supplies the optional independently owned normal/depth image.</summary>
    public Texture2D? NormalDepthTexture { get; }

    /// <summary>Includes admitted pending writes before any frame can read the source envelope.</summary>
    internal float MaximumEmissionNits { get { lock (publicationGate) return maximumEmissionNits; } }

    /// <summary>Admits and stages one write only for this exact live material image.</summary>
    internal bool TryPublishMaterial(int textureId, AtlasRect rect, float[] rgba, bool immediate)
    {
        float emission = MaterialAtlasTextureStore.AdmittedEmissionMaximum(rgba);
        if (rgba.Length != checked(rect.Width * rect.Height * 4))
            throw new ArgumentException("Material payload must match its complete tile rectangle.", nameof(rgba));
        lock (publicationGate)
        {
            if (retired || !MaterialParamsTexture.IsValid || MaterialParamsTexture.TextureId != textureId) return false;
            if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0
                || (long)rect.X + rect.Width > MaterialParamsTexture.Width
                || (long)rect.Y + rect.Height > MaterialParamsTexture.Height)
                throw new ArgumentOutOfRangeException(nameof(rect));
            if (immediate)
                MaterialParamsTexture.UploadDataImmediate(rgba, rect.X, rect.Y, rect.Width, rect.Height);
            else
            {
                var result = TextureStreamingSystem.StageCopy(textureId, TextureUploadTarget.For2D(),
                    TextureUploadRegion.For2D(rect.X, rect.Y, rect.Width, rect.Height),
                    PixelFormat.Rgba, PixelType.Float, rgba, TextureUploadPriority.Normal,
                    unpackAlignment: 4, textureOwner: MaterialParamsTexture);
                if (result.Outcome == TextureStageOutcome.Rejected)
                    throw new InvalidOperationException($"Material upload was rejected: {result.RejectReason}.");
            }
            // The same lock covers staging and metadata. A scale reader includes the
            // accepted queued source even if the render thread has not uploaded it yet.
            maximumEmissionNits = Math.Max(maximumEmissionNits, emission);
            return true;
        }
    }

    /// <summary>Retires material publication while allowing the owner to transfer its normal/depth image.</summary>
    internal float RetireMaterial()
    {
        lock (publicationGate)
        {
            retired = true;
            MaterialParamsTexture.Dispose();
            return maximumEmissionNits;
        }
    }

    /// <summary>Withdraws publication before retiring both owned images.</summary>
    public void Dispose()
    {
        RetireMaterial();
        NormalDepthTexture?.Dispose();
    }
    #endregion
}
