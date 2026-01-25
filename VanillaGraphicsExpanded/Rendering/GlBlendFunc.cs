using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal readonly record struct GlBlendFunc(
    BlendingFactorSrc SrcRgb,
    BlendingFactorDest DstRgb,
    BlendingFactorSrc SrcAlpha,
    BlendingFactorDest DstAlpha)
{
    public static GlBlendFunc Default => new(
        BlendingFactorSrc.One,
        BlendingFactorDest.Zero,
        BlendingFactorSrc.One,
        BlendingFactorDest.Zero);
}
