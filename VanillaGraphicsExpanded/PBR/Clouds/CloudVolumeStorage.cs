using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns one immutable payload allocation and disjoint cloud payload views.</summary>
internal sealed class CloudVolumeStorage : IDisposable
{
    internal readonly DynamicTexture3D Array;
    internal readonly CloudVolumeSurface[] Blocks;

    #region Public API
    /// <summary>Bounds the complete three-block allocation before GPU creation and rounds odd view extents upward.</summary>
    internal static (int Width, int Height) Dimensions(int width, int height)
    {
        if (width is < 1 or > 16384 || height is < 1 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(width));
        int reducedWidth = (width + 1) / 2, reducedHeight = (height + 1) / 2;
        ValidateDimensions(reducedWidth, reducedHeight);
        return (reducedWidth, reducedHeight);
    }

    /// <summary>Creates all views and framebuffer configurations transactionally without borrowing engine storage.</summary>
    internal CloudVolumeStorage(int width, int height, string name, int blocks = 3)
    {
        ValidateDimensions(width, height, blocks);
        Blocks=new CloudVolumeSurface[blocks];
        Array = DynamicTexture3D.Create(width, height, blocks*3, PixelInternalFormat.Rgba32f,
            TextureFilterMode.Nearest, TextureTarget.Texture2DArray, immutableStorage: true);
        try
        {
            Array.SetDebugName(name);
            // Each block exposes only its own layers, so shader reads cannot reach another block's writes.
            for (int block = 0; block < Blocks.Length; block++)
                Blocks[block] = new CloudVolumeSurface(Array, block, $"{name}.Block{block}");
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Retires every pipeline, framebuffer and view before releasing their shared array allocation.</summary>
    public void Dispose()
    {
        var actions = new List<Action>();
        foreach (var block in Blocks)
            if (block is not null) actions.Add(block.Dispose);
        actions.Add(Array.Dispose);
        RenderingCleanup.Run(actions.ToArray());
    }
    #endregion

    #region Private
    /// <summary>Includes all selected RGBA32F layers in the one-GiB storage ceiling.</summary>
    private static void ValidateDimensions(int width, int height, int blocks = 3)
    {
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || blocks is <1 or >3 || (long)width * height * 48 * blocks > 1024L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(width), "Cloud targets exceed the allocation limits.");
    }
    #endregion
}
