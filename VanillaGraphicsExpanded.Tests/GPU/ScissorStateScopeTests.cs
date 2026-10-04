using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies cached scissor scopes preserve caller state without adopting unknown driver state.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ScissorStateScopeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Nested scopes and exceptional exits restore their own known entry state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownStateRestoresAfterNestedException(bool initial)
    {
        EnsureContextValid();
        SetScissor(initial);
        try
        {
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var outer = StateCache.Current.PreserveScissorState();
                SetScissor(!initial);
                using (StateCache.Current.PreserveScissorState())
                {
                    SetScissor(initial);
                    Assert.Equal(initial, GL.IsEnabled(EnableCap.ScissorTest));
                }
                Assert.Equal(!initial, GL.IsEnabled(EnableCap.ScissorTest));
                throw new InvalidOperationException("Controlled operation failure.");
            }));
            Assert.Equal(initial, GL.IsEnabled(EnableCap.ScissorTest));
            // Re-entering also proves restoration updated the authoritative cached value.
            using (StateCache.Current.PreserveScissorState()) SetScissor(!initial);
            Assert.Equal(initial, GL.IsEnabled(EnableCap.ScissorTest));
        }
        finally { SetScissor(false); }
    }

    /// <summary>Unknown cache state rejects capture rather than querying or changing an independently set driver state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownStateRejectsWithoutMutatingDriver(bool driverEnabled)
    {
        EnsureContextValid();
        if (driverEnabled) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);
        StateCache.Current.InvalidateAll();
        try
        {
            Assert.Throws<InvalidOperationException>(() => StateCache.Current.PreserveScissorState());
            Assert.Equal(driverEnabled, GL.IsEnabled(EnableCap.ScissorTest));
            // A rejected scope must not silently learn the driver value for the next attempt.
            Assert.Throws<InvalidOperationException>(() => StateCache.Current.PreserveScissorState());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { SetScissor(false); }
    }
    #endregion

    #region Private
    /// <summary>Uses the same narrow pipeline contract as an authoritative caller state boundary.</summary>
    private static void SetScissor(bool enabled)
    {
        var mask = GlPipelineStateMask.From(GlPipelineStateId.ScissorTestEnable);
        StateCache.Current.Apply(new GlPipelineDesc(enabled ? default : mask, enabled ? mask : default));
    }
    #endregion
}
