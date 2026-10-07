using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Owns the atlas baker TileResources implementation.</summary>
internal static partial class MaterialAtlasNormalDepthGpuBuilder
{
    #region Private
    /// <summary>Retains the reusable scalar and gradient images for one bake tile.</summary>
    private sealed class TileResources
    {
        public DynamicTexture2D L { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_L");
        public DynamicTexture2D Base { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_Base");
        public DynamicTexture2D D0 { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_D0");
        public DynamicTexture2D G1 { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_G1");
        public DynamicTexture2D G2 { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_G2");
        public DynamicTexture2D G3 { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_G3");
        public DynamicTexture2D G4 { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_G4");
        public DynamicTexture2D D { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_D");
        public DynamicTexture2D G { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rg32f, TextureFilterMode.Nearest, "vge_bake_Gxy");
        public DynamicTexture2D Div { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_Div");
        public DynamicTexture2D H { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_H");
        public DynamicTexture2D Hn { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_Hn");
        public DynamicTexture2D Tmp { get; private set; } = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, TextureFilterMode.Nearest, "vge_bake_Tmp");

        /// <summary>Resizes reusable solver storage when the working rectangle changes.</summary>
        public void EnsureSize(int w, int h)
        {
            ResizeIfNeeded(L, w, h);
            ResizeIfNeeded(Base, w, h);
            ResizeIfNeeded(D0, w, h);
            ResizeIfNeeded(G1, w, h);
            ResizeIfNeeded(G2, w, h);
            ResizeIfNeeded(G3, w, h);
            ResizeIfNeeded(G4, w, h);
            ResizeIfNeeded(D, w, h);
            ResizeIfNeeded(G, w, h);
            ResizeIfNeeded(Div, w, h);
            ResizeIfNeeded(H, w, h);
            ResizeIfNeeded(Hn, w, h);
            ResizeIfNeeded(Tmp, w, h);
        }

        /// <summary>Preserves existing texture storage when the requested dimensions are unchanged.</summary>
        private static void ResizeIfNeeded(DynamicTexture2D tex, int w, int h)
        {
            if (tex.Width == w && tex.Height == h) return;
            tex.Resize(w, h);
        }
    }

    #endregion
}
