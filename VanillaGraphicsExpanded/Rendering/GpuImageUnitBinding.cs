using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII binding scope for <c>glBindImageTexture</c>.
/// Captures the previous image-unit state on construction and restores it on <see cref="Dispose"/>.
/// </summary>
public readonly struct GpuImageUnitBinding : IDisposable
{
    private readonly int unit;
    private readonly int prevName;
    private readonly int prevLevel;
    private readonly int prevLayered;
    private readonly int prevLayer;
    private readonly int prevAccess;
    private readonly int prevFormat;

    private readonly bool shouldRestore;

    private GpuImageUnitBinding(
        int unit,
        int prevName,
        int prevLevel,
        int prevLayered,
        int prevLayer,
        int prevAccess,
        int prevFormat,
        bool shouldRestore)
    {
        this.unit = unit;
        this.prevName = prevName;
        this.prevLevel = prevLevel;
        this.prevLayered = prevLayered;
        this.prevLayer = prevLayer;
        this.prevAccess = prevAccess;
        this.prevFormat = prevFormat;
        this.shouldRestore = shouldRestore;
    }

    /// <summary>
    /// Binds an image texture to the specified image unit and returns a scope that restores previous state.
    /// </summary>
    public static GpuImageUnitBinding Bind(
        int unit,
        int textureId,
        int level,
        bool layered,
        int layer,
        TextureAccess access,
        SizedInternalFormat format)
    {
        if (unit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Image unit must be >= 0.");
        }

        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be >= 0.");
        }

        if (layer < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "Layer must be >= 0.");
        }

        int prevName = 0;
        int prevLevel = 0;
        int prevLayered = 0;
        int prevLayer = 0;
        int prevAccess = 0;
        int prevFormat = 0;
        bool shouldRestore = false;

        try
        {
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, unit, out prevName);
            GL.GetInteger((GetIndexedPName)All.ImageBindingLevel, unit, out prevLevel);
            GL.GetInteger((GetIndexedPName)All.ImageBindingLayered, unit, out prevLayered);
            GL.GetInteger((GetIndexedPName)All.ImageBindingLayer, unit, out prevLayer);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, unit, out prevAccess);
            GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, unit, out prevFormat);
            shouldRestore = true;
        }
        catch
        {
            // Best-effort: some drivers/contexts may not expose indexed image binding queries.
            shouldRestore = false;
        }

        try
        {
            GL.BindImageTexture(unit, textureId, level, layered, layer, access, format);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[GpuImageUnitBinding] Failed to bind image unit {unit}: {e.Message}");
            // If we couldn't bind, don't claim we can restore.
            shouldRestore = false;
        }

        return new GpuImageUnitBinding(unit, prevName, prevLevel, prevLayered, prevLayer, prevAccess, prevFormat, shouldRestore);
    }

    /// <summary>
    /// Binds a <see cref="GpuTexture"/> to the specified image unit and returns a scope that restores previous state.
    /// </summary>
    public static GpuImageUnitBinding Bind(
        int unit,
        GpuTexture texture,
        TextureAccess access = TextureAccess.ReadOnly,
        int level = 0,
        bool layered = false,
        int layer = 0,
        SizedInternalFormat? formatOverride = null)
    {
        ArgumentNullException.ThrowIfNull(texture);

        if (!texture.IsValid)
        {
            return default;
        }

        var format = formatOverride ?? (SizedInternalFormat)texture.InternalFormat;
        return Bind(unit, texture.TextureId, level, layered, layer, access, format);
    }

    /// <summary>
    /// Binds a <see cref="GpuBufferView"/> to the specified image unit and returns a scope that restores previous state.
    /// </summary>
    public static GpuImageUnitBinding Bind(
        int unit,
        GpuBufferView bufferTexture,
        TextureAccess access = TextureAccess.ReadOnly)
    {
        ArgumentNullException.ThrowIfNull(bufferTexture);

        if (!bufferTexture.IsValid)
        {
            return default;
        }

        return Bind(
            unit,
            bufferTexture.TextureId,
            level: 0,
            layered: false,
            layer: 0,
            access: access,
            format: bufferTexture.Format);
    }

    /// <summary>
    /// Restores the previously captured image binding for this unit.
    /// </summary>
    public void Dispose()
    {
        if (!shouldRestore)
        {
            return;
        }

        try
        {
            GL.BindImageTexture(
                unit,
                prevName,
                prevLevel,
                prevLayered != 0,
                prevLayer,
                (TextureAccess)prevAccess,
                (SizedInternalFormat)prevFormat);
        }
        catch
        {
            // Best-effort: context may be gone during shutdown.
        }
    }
}

