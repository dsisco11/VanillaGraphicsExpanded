using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Supplies the additive equation baseline of native engine particle draws without leaking fixture state.</summary>
internal sealed class NativeParticleBlendEquations : IDisposable
{
    private readonly (BlendEquationMode Rgb, BlendEquationMode Alpha)[] saved = new (BlendEquationMode, BlendEquationMode)[2];

    #region Public API
    /// <summary>Preserves the two particle outputs and establishes the equations assumed by their blend equations.</summary>
    internal NativeParticleBlendEquations()
    {
        // Native integration tests supply engine inputs explicitly; the production capture scope
        // owns only blend enables/factors and intentionally preserves caller-owned equations.
        for (int slot = 0; slot < saved.Length; slot++)
        {
            GL.GetInteger((GetIndexedPName)GetPName.BlendEquationRgb, slot, out int rgb);
            GL.GetInteger((GetIndexedPName)GetPName.BlendEquationAlpha, slot, out int alpha);
            saved[slot] = ((BlendEquationMode)rgb, (BlendEquationMode)alpha);
            GL.BlendEquationSeparate(slot, BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
        }
    }

    /// <summary>Returns the caller's equations through observed native setters after fixture draws finish.</summary>
    public void Dispose()
    {
        for (int slot = 0; slot < saved.Length; slot++)
            GL.BlendEquationSeparate(slot, saved[slot].Rgb, saved[slot].Alpha);
    }
    #endregion
}
