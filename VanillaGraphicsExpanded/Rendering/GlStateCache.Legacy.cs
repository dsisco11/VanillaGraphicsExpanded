using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal sealed partial class GlStateCache
{
    public LegacyFixedFunctionScope CaptureLegacyFixedFunctionState()
    {
        var snapshot = LegacyFixedFunctionSnapshot.CaptureBestEffort();
        return new LegacyFixedFunctionScope(this, snapshot);
    }

    public readonly struct LegacyFixedFunctionScope : System.IDisposable
    {
        private readonly GlStateCache cache;
        private readonly LegacyFixedFunctionSnapshot snapshot;

        internal LegacyFixedFunctionScope(GlStateCache cache, LegacyFixedFunctionSnapshot snapshot)
        {
            this.cache = cache;
            this.snapshot = snapshot;
        }

        public void Dispose()
        {
            snapshot.Restore(cache);
        }
    }

    internal readonly struct LegacyFixedFunctionSnapshot
    {
        public readonly bool DepthTest;
        public readonly DepthFunction DepthFunc;
        public readonly bool DepthMask;

        public readonly bool Blend;
        public readonly GlBlendFunc BlendFunc;

        public readonly bool Cull;
        public readonly bool Scissor;
        public readonly GlColorMask ColorMask;
        public readonly float LineWidth;
        public readonly float PointSize;

        private LegacyFixedFunctionSnapshot(
            bool depthTest,
            DepthFunction depthFunc,
            bool depthMask,
            bool blend,
            GlBlendFunc blendFunc,
            bool cull,
            bool scissor,
            GlColorMask colorMask,
            float lineWidth,
            float pointSize)
        {
            DepthTest = depthTest;
            DepthFunc = depthFunc;
            DepthMask = depthMask;
            Blend = blend;
            BlendFunc = blendFunc;
            Cull = cull;
            Scissor = scissor;
            ColorMask = colorMask;
            LineWidth = lineWidth;
            PointSize = pointSize;
        }

        public static LegacyFixedFunctionSnapshot CaptureBestEffort()
        {
            bool depthTest = false;
            bool blend = false;
            bool cull = false;
            bool scissor = false;
            bool depthMask = true;
            int depthFunc = (int)DepthFunction.Less;

            int srcRgb = (int)BlendingFactorSrc.One;
            int dstRgb = (int)BlendingFactorDest.Zero;
            int srcA = (int)BlendingFactorSrc.One;
            int dstA = (int)BlendingFactorDest.Zero;

            bool[] mask = [true, true, true, true];
            float lw = 1f;
            float ps = 1f;

            try { depthTest = GL.IsEnabled(EnableCap.DepthTest); } catch { }
            try { blend = GL.IsEnabled(EnableCap.Blend); } catch { }
            try { cull = GL.IsEnabled(EnableCap.CullFace); } catch { }
            try { scissor = GL.IsEnabled(EnableCap.ScissorTest); } catch { }
            try { depthMask = GL.GetBoolean(GetPName.DepthWritemask); } catch { }
            try { depthFunc = GL.GetInteger(GetPName.DepthFunc); } catch { }

            try { srcRgb = GL.GetInteger(GetPName.BlendSrcRgb); } catch { }
            try { dstRgb = GL.GetInteger(GetPName.BlendDstRgb); } catch { }
            try { srcA = GL.GetInteger(GetPName.BlendSrcAlpha); } catch { }
            try { dstA = GL.GetInteger(GetPName.BlendDstAlpha); } catch { }

            try { GL.GetBoolean(GetPName.ColorWritemask, mask); } catch { }
            try { lw = GL.GetFloat(GetPName.LineWidth); } catch { }
            try { ps = GL.GetFloat(GetPName.PointSize); } catch { }

            return new LegacyFixedFunctionSnapshot(
                depthTest,
                (DepthFunction)depthFunc,
                depthMask,
                blend,
                new GlBlendFunc((BlendingFactorSrc)srcRgb, (BlendingFactorDest)dstRgb, (BlendingFactorSrc)srcA, (BlendingFactorDest)dstA),
                cull,
                scissor,
                GlColorMask.FromRgba(mask[0], mask[1], mask[2], mask[3]),
                lw,
                ps);
        }

        public void Restore(GlStateCache cache)
        {
            // Force the cache to re-emit on restore.
            cache.depthTestEnabled = null;
            cache.blendEnabled = null;
            cache.cullFaceEnabled = null;
            cache.scissorTestEnabled = null;
            cache.depthFunc = null;
            cache.depthWriteMask = null;
            cache.blendFunc = null;
            cache.colorMask = null;
            cache.lineWidth = null;
            cache.pointSize = null;

            SetEnable(EnableCap.DepthTest, DepthTest, ref cache.depthTestEnabled);
            cache.SetDepthFunc(DepthFunc);
            cache.SetDepthWriteMask(DepthMask);

            SetEnable(EnableCap.Blend, Blend, ref cache.blendEnabled);
            cache.SetBlendFunc(BlendFunc);

            SetEnable(EnableCap.CullFace, Cull, ref cache.cullFaceEnabled);
            SetEnable(EnableCap.ScissorTest, Scissor, ref cache.scissorTestEnabled);

            cache.SetColorMask(ColorMask);
            cache.SetLineWidth(LineWidth);
            cache.SetPointSize(PointSize);
        }
    }
}
