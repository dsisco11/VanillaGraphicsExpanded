using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Clears selected physical probe slots while preserving overlapping directional history.</summary>
internal sealed partial class LumOnWorldProbeClipmapGpuResources
{
    #region History invalidation

    /// <summary>Clears a local inclusive box while preserving other ring-buffer slots and GL state.</summary>
    public void ClearLocalBox(int level, VectorInt3 ring, VectorInt3 min, VectorInt3 max)
    {
        int[] scissor = new int[4];
        bool[] mask = new bool[4];
        GL.GetInteger(GetPName.ScissorBox, scissor);
        GL.GetBoolean(GetPName.ColorWritemask, mask);
        bool enabled = GL.IsEnabled(EnableCap.ScissorTest);
        GL.Enable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        try
        {
            // Each local X interval becomes at most two contiguous storage intervals at ring wrap.
            int firstX = (min.X + ring.X) % resolution;
            int width = max.X - min.X + 1;
            int firstWidth = Math.Min(width, resolution - firstX);
            int firstY = (min.Y + ring.Y) % resolution;
            int height = max.Y - min.Y + 1;
            int firstHeight = Math.Min(height, resolution - firstY);
            float[] zero = new float[4];
            for (int target = 0; target < 2; target++)
            {
                int scale = target == 0 ? worldProbeTileSize : 1;
                using var binding = GlStateCache.Current.BindFramebufferScope(
                    FramebufferTarget.DrawFramebuffer, target == 0 ? radianceFbo.FboId : fbo.FboId);
                for (int z = min.Z; z <= max.Z; z++)
                for (int ySegment = 0; ySegment < 2; ySegment++)
                {
                    int rows = ySegment == 0 ? firstHeight : height - firstHeight;
                    if (rows == 0) continue;
                    int storageZ = (z + ring.Z) % resolution;
                    int storageY = (ySegment == 0 ? firstY : 0) + level * resolution;
                    for (int segment = 0; segment < 2; segment++)
                    {
                        int count = segment == 0 ? firstWidth : width - firstWidth;
                        if (count == 0) continue;
                        int x = storageZ * resolution + (segment == 0 ? firstX : 0);
                        GL.Scissor(x * scale, storageY * scale, count * scale, rows * scale);
                        for (int attachment = 0; attachment < (target == 0 ? 1 : 3); attachment++)
                            GL.ClearBuffer(ClearBuffer.Color, attachment, zero);
                    }
                }
            }
        }
        finally
        {
            GL.Scissor(scissor[0], scissor[1], scissor[2], scissor[3]);
            GL.ColorMask(mask[0], mask[1], mask[2], mask[3]);
            if (!enabled) GL.Disable(EnableCap.ScissorTest);
        }
    }

    #endregion
}
