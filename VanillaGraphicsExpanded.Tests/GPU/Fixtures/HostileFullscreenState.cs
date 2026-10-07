using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Models hostile external raster state around a complete fullscreen consumer.</summary>
internal sealed class HostileFullscreenState : IDisposable
{
    private static readonly EnableCap[] Caps = [EnableCap.StencilTest, EnableCap.RasterizerDiscard,
        EnableCap.ScissorTest, EnableCap.SampleCoverage, EnableCap.ClipDistance0];
    private readonly bool[] enabled = Caps.Select(GL.IsEnabled).ToArray();
    private readonly int[] scissor = new int[4];
    private readonly bool[] color = new bool[4];

    #region Public API
    /// <summary>Captures the external baseline, then blocks drawing through independent native categories.</summary>
    internal HostileFullscreenState()
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        GL.GetInteger(GetPName.ScissorBox, scissor);
        GL.GetBoolean(GetIndexedPName.ColorWritemask, 0, color);
        foreach (var cap in Caps) GL.Enable(cap);
        GL.Scissor(0, 0, 0, 0); GL.ColorMask(0, false, false, false, false);
        StateCache.Current.InvalidateAll();
    }

    /// <summary>Checks the consumer restored the native state rather than just cached knowledge.</summary>
    internal void AssertRestored()
    {
        foreach (var cap in Caps) Assert.True(GL.IsEnabled(cap), cap.ToString());
        int[] actual = new int[4]; GL.GetInteger(GetPName.ScissorBox, actual);
        Assert.Equal(new[] { 0, 0, 0, 0 }, actual);
        bool[] mask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, 0, mask);
        Assert.All(mask, value => Assert.False(value));
    }

    /// <summary>Returns the test's external baseline after success or assertion failure.</summary>
    public void Dispose()
    {
        for (int index = 0; index < Caps.Length; index++)
            if (enabled[index]) GL.Enable(Caps[index]); else GL.Disable(Caps[index]);
        GL.Scissor(scissor[0], scissor[1], scissor[2], scissor[3]);
        GL.ColorMask(0, color[0], color[1], color[2], color[3]);
        StateCache.Current.InvalidateAll();
    }
    #endregion
}
