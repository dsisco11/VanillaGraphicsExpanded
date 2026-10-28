using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks scalar classification against native errors, resulting state and managed suppression.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class OpenTKScalarEffectTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Forward-compatible rejection preserves the established width without consuming its native error.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void LineAndPatchRejectionsPreserveKnownValues(bool core)
    {
        fixture.EnsureContextValid();
        Assert.True(GpuSupport.IsForwardCompatibleContext);
        var cache = OpenGlStateCache.Current;
        cache.SetLineWidth(1);
        cache.SetPatchVertices(3);
        int lines = 0, patches = 0;
        using var native = new NativeDriverCapture();
        nint line = native.Original("glLineWidth"), patch = native.Original("glPatchParameteri");
        native.Add("glLineWidth", new LineCall(value =>
        {
            lines++;
            ((delegate* unmanaged[Stdcall]<float, void>)line)(value);
        }));
        native.Add("glPatchParameteri", new PatchCall((parameter, value) =>
        {
            patches++;
            ((delegate* unmanaged[Stdcall]<int, int, void>)patch)(parameter, value);
        }));
        native.Install();
        Action<float> setLine = core ? OpenTK.Graphics.OpenGL4.GL.LineWidth : GL.LineWidth;
        setLine(2);
        Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
        cache.SetLineWidth(1);
        Assert.Equal(1, lines);
        Assert.Equal(1f, GL.GetFloat(GetPName.LineWidth));
        setLine(.5f);
        cache.SetLineWidth(.5f);
        Assert.Equal(2, lines);
        Assert.Equal(.5f, GL.GetFloat(GetPName.LineWidth));
        if (core) OpenTK.Graphics.OpenGL4.GL.PatchParameter(OpenTK.Graphics.OpenGL4.PatchParameterInt.PatchVertices, 0);
        else GL.PatchParameter(PatchParameterInt.PatchVertices, 0);
        Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
        cache.SetPatchVertices(3);
        Assert.Equal(1, patches);
        if (core) OpenTK.Graphics.OpenGL4.GL.PatchParameter(OpenTK.Graphics.OpenGL4.PatchParameterInt.PatchVertices, 4);
        else GL.PatchParameter(PatchParameterInt.PatchVertices, 4);
        cache.SetPatchVertices(4);
        Assert.Equal(2, patches);
        Assert.Equal(4, GL.GetInteger(GetPName.PatchVertices));
        Assert.Equal(OpenGLObservedEffect.Uncertain, OpenGlStateCache.ClassifyLineWidth(float.NaN));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        cache.SetLineWidth(1);
        cache.SetPatchVertices(3);
    }
    #endregion

    #region Private
    /// <summary>Matches the scalar line-width native ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void LineCall(float value);
    /// <summary>Matches the integer patch-parameter native ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void PatchCall(int parameter, int value);
    #endregion
}
