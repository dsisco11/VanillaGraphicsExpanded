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

/// <summary>Owns the atlas multigrid storage and iterative solve.</summary>
internal static partial class MaterialAtlasNormalDepthGpuBuilder
{
    #region Private
    /// <summary>Owns the levels and relaxation passes of the periodic Poisson solver.</summary>
    private sealed class MultigridResources
    {
        private int w;
        private int h;
        private int levels;
        private (int w, int h)[] levelSizes = Array.Empty<(int w, int h)>();

        private DynamicTexture2D[] hLevel = Array.Empty<DynamicTexture2D>();
        private DynamicTexture2D[] hTmpLevel = Array.Empty<DynamicTexture2D>();
        private DynamicTexture2D[] bLevel = Array.Empty<DynamicTexture2D>();
        private DynamicTexture2D[] residualLevel = Array.Empty<DynamicTexture2D>();

        /// <summary>Resizes reusable solver storage when the working rectangle changes.</summary>
        public void EnsureSize(int width, int height)
        {
            if (w == width && h == height && levels > 0)
            {
                return;
            }

            w = width;
            h = height;

            // Allow non-power-of-two by halving with ceil.
            var sizes = new List<(int w, int h)>(capacity: 12);
            int lw = w;
            int lh = h;
            sizes.Add((lw, lh));

            while (lw > 32 || lh > 32)
            {
                lw = Math.Max(1, (lw + 1) / 2);
                lh = Math.Max(1, (lh + 1) / 2);
                sizes.Add((lw, lh));
                if (lw == 1 && lh == 1) break;
            }

            levels = sizes.Count;
            levelSizes = sizes.ToArray();

            DisposeAll();

            hLevel = new DynamicTexture2D[levels];
            hTmpLevel = new DynamicTexture2D[levels];
            bLevel = new DynamicTexture2D[levels];
            residualLevel = new DynamicTexture2D[levels];

            for (int l = 0; l < levels; l++)
            {
                (int levelW, int levelH) = levelSizes[l];
                hLevel[l] = DynamicTexture2D.Create(levelW, levelH, PixelInternalFormat.R32f, TextureFilterMode.Nearest, $"vge_mg_h_{levelW}x{levelH}");
                hTmpLevel[l] = DynamicTexture2D.Create(levelW, levelH, PixelInternalFormat.R32f, TextureFilterMode.Nearest, $"vge_mg_htmp_{levelW}x{levelH}");
                bLevel[l] = DynamicTexture2D.Create(levelW, levelH, PixelInternalFormat.R32f, TextureFilterMode.Nearest, $"vge_mg_b_{levelW}x{levelH}");
                residualLevel[l] = DynamicTexture2D.Create(levelW, levelH, PixelInternalFormat.R32f, TextureFilterMode.Nearest, $"vge_mg_r_{levelW}x{levelH}");
            }
        }

        /// <summary>Performs the configured multigrid cycles and publishes the final solution image.</summary>
        public void Solve(BakeDrawContext draw, DynamicTexture2D rhs, DynamicTexture2D outH, VgeConfig.NormalDepthBakeConfig bake)
        {
            // Copy rhs into bLevel[0] (keep a stable reference for residual).
            RunCopy(draw, rhs, bLevel[0]);

            // Clear solution guess.
            ClearR32f(draw, hLevel[0], 0f);
            ClearR32f(draw, hTmpLevel[0], 0f);

            for (int cycle = 0; cycle < bake.MultigridVCycles; cycle++)
            {
                VCycle(draw, 0, bake);
            }

            // Copy final hLevel[0] into outH.
            RunCopy(draw, hLevel[0], outH);
        }

        /// <summary>Smooths, restricts residuals and prolongates corrections through the multigrid hierarchy.</summary>
        private void VCycle(BakeDrawContext draw, int level, VgeConfig.NormalDepthBakeConfig bake)
        {
            // Pre-smooth.
            for (int i = 0; i < bake.MultigridPreSmooth; i++)
            {
                RunJacobi(draw, hLevel[level], bLevel[level], hTmpLevel[level]);
                Swap(ref hLevel[level], ref hTmpLevel[level]);
            }

            // Residual: r = b - A*h.
            RunResidual(draw, hLevel[level], bLevel[level], residualLevel[level]);

            bool isCoarsest = level == levels - 1;
            if (!isCoarsest)
            {
                // Restrict residual to coarse RHS.
                RunRestrict(draw, residualLevel[level], bLevel[level + 1]);

                // Clear coarse error guess.
                ClearR32f(draw, hLevel[level + 1], 0f);
                ClearR32f(draw, hTmpLevel[level + 1], 0f);

                VCycle(draw, level + 1, bake);

                // Prolongate coarse error and add to fine.
                RunProlongateAdd(draw, hLevel[level], hLevel[level + 1], hTmpLevel[level]);
                Swap(ref hLevel[level], ref hTmpLevel[level]);

                // Post-smooth.
                for (int i = 0; i < bake.MultigridPostSmooth; i++)
                {
                    RunJacobi(draw, hLevel[level], bLevel[level], hTmpLevel[level]);
                    Swap(ref hLevel[level], ref hTmpLevel[level]);
                }
            }
            else
            {
                // Coarsest: iterate more.
                for (int i = 0; i < bake.MultigridCoarsestIters; i++)
                {
                    RunJacobi(draw, hLevel[level], bLevel[level], hTmpLevel[level]);
                    Swap(ref hLevel[level], ref hTmpLevel[level]);
                }
            }
        }

        /// <summary>Exchanges the current and scratch solution image references.</summary>
        private static void Swap(ref DynamicTexture2D a, ref DynamicTexture2D b)
        {
            (a, b) = (b, a);
        }

        /// <summary>Clears a scalar solver image through explicit pass load operations.</summary>
        private static void ClearR32f(BakeDrawContext draw, DynamicTexture2D tex, float v)
        {
            draw.SetTarget(tex);
            draw.Clear(v, 0, 0, 0);
        }

        // Uses outer RunCopy

        /// <summary>Applies one Jacobi relaxation step to the selected multigrid level.</summary>
        private static void RunJacobi(BakeDrawContext draw, DynamicTexture2D h, DynamicTexture2D b, DynamicTexture2D dst)
        {
            draw.SetTarget(dst);

            progJacobi!.h = h;
            progJacobi!.b = b;
            Params.CommonSize = (dst.Width, dst.Height);
            progJacobi!.Parameters = Params;
            draw.Draw(progJacobi!);

        }

        /// <summary>Computes the residual error for coarse-grid correction.</summary>
        private static void RunResidual(BakeDrawContext draw, DynamicTexture2D h, DynamicTexture2D b, DynamicTexture2D dst)
        {
            draw.SetTarget(dst);

            progResidual!.h = h;
            progResidual!.b = b;
            Params.CommonSize = (dst.Width, dst.Height);
            progResidual!.Parameters = Params;
            draw.Draw(progResidual!);

        }

        /// <summary>Restricts a fine residual image into the next coarser level.</summary>
        private static void RunRestrict(BakeDrawContext draw, DynamicTexture2D fine, DynamicTexture2D coarse)
        {
            draw.SetTarget(coarse);

            progRestrict!.fine = fine;
            // Pack all pass inputs before activation.
            {
                Params.MultigridFineSize = (fine.Width, fine.Height);
                Params.MultigridCoarseSize = (coarse.Width, coarse.Height);
            }
            progRestrict!.Parameters = Params;
            draw.Draw(progRestrict!);

        }

        /// <summary>Interpolates a coarse correction and adds it to the fine solution.</summary>
        private static void RunProlongateAdd(BakeDrawContext draw, DynamicTexture2D fineH, DynamicTexture2D coarseE, DynamicTexture2D dst)
        {
            draw.SetTarget(dst);

            progProlongateAdd!.fineH = fineH;
            progProlongateAdd!.coarseE = coarseE;
            // Pack all pass inputs before activation.
            {
                Params.MultigridFineSize = (fineH.Width, fineH.Height);
                Params.MultigridCoarseSize = (coarseE.Width, coarseE.Height);
            }
            progProlongateAdd!.Parameters = Params;
            draw.Draw(progProlongateAdd!);

        }

        /// <summary>Retires all multigrid level images before replacing the hierarchy.</summary>
        private void DisposeAll()
        {
            foreach (DynamicTexture2D t in hLevel) t.Dispose();
            foreach (DynamicTexture2D t in hTmpLevel) t.Dispose();
            foreach (DynamicTexture2D t in bLevel) t.Dispose();
            foreach (DynamicTexture2D t in residualLevel) t.Dispose();

            hLevel = Array.Empty<DynamicTexture2D>();
            hTmpLevel = Array.Empty<DynamicTexture2D>();
            bLevel = Array.Empty<DynamicTexture2D>();
            residualLevel = Array.Empty<DynamicTexture2D>();
        }
    }
    #endregion
}
