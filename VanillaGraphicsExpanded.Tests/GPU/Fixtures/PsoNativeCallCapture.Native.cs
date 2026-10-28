using System.Globalization;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Captures scalar native state effects through ABI-specific delegates and true driver pointers.</summary>
internal sealed unsafe partial class PsoNativeCallCapture
{
    private readonly NativeDriverCapture native = new();

    #region Private
    #region Observer installation
    /// <summary>Decorates core scalar and indexed entrypoints used by the workload and its restoration.</summary>
    private void InstallNativeObservers()
    {
        CaptureInteger("glEnable", "Enable");
        CaptureInteger("glDisable", "Disable");
        CaptureIndexedInteger("glEnablei", "Enable");
        CaptureIndexedInteger("glDisablei", "Disable");
        CaptureFloat("glLineWidth", "LineWidth");
        CaptureFloat("glPointSize", "PointSize");
        CaptureIntegerQuad("glScissor", "Scissor");
        CaptureIndexedIntegerQuad("glScissorIndexed", "Scissor");
        CaptureIntegerQuad("glBlendFuncSeparate", "BlendFuncSeparate");
        CaptureIndexedIntegerQuad("glBlendFuncSeparatei", "BlendFuncSeparate");
        CaptureIntegerPair("glBlendEquationSeparate", "BlendEquationSeparate");
        CaptureIndexedIntegerPair("glBlendEquationSeparatei", "BlendEquationSeparate");
        CaptureFloatQuad("glBlendColor", "BlendColor");
        CaptureByteQuad("glColorMask", "ColorMask");
        CaptureIndexedByteQuad("glColorMaski", "ColorMask");
        CaptureInteger("glDepthFunc", "DepthFunc");
        CaptureByte("glDepthMask", "DepthMask");
        CaptureStencilFunction("glStencilFuncSeparate", "StencilFuncSeparate");
        CaptureIntegerQuad("glStencilOpSeparate", "StencilOpSeparate");
        CaptureIntegerUnsigned("glStencilMaskSeparate", "StencilMaskSeparate");
        CaptureIntegerPair("glPolygonMode", "PolygonMode");
        CaptureFloatPair("glPolygonOffset", "PolygonOffset");
        CaptureInteger("glCullFace", "CullFace");
        CaptureInteger("glFrontFace", "FrontFace");
        CaptureIntegerQuad("glViewport", "Viewport");
        CaptureIndexedFloatQuad("glViewportIndexedf", "Viewport");
        CaptureSampleCoverage("glSampleCoverage", "SampleCoverage");
        CaptureUnsignedPair("glSampleMaski", "SampleMask");
        CaptureFloat("glMinSampleShading", "MinSampleShading");
        CaptureUnsigned("glPrimitiveRestartIndex", "PrimitiveRestartIndex");
        CaptureIntegerPair("glPatchParameteri", "PatchParameter");
        CaptureIntegerPair("glPointParameteri", "PointParameter");
        CaptureIntegerFloat("glPointParameterf", "PointParameter");
        CaptureInteger("glProvokingVertex", "ProvokingVertex");
        CaptureDoublePair("glDepthRange", "DepthRange");
        CaptureIndexedDoublePair("glDepthRangeIndexed", "DepthRange");
        native.Install();
    }

    /// <summary>Records after dispatch, containing instrumentation failures at the managed fixture boundary.</summary>
    private void Record(string name, params object[] arguments)
        => native.Observe(() => Events.Add(segment + ":" + name + "("
            + string.Join(",", arguments.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))) + ")"));
    #endregion

    #region ABI callback installation
    /// <summary>Matches the Integer native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IntegerCall(int a0);

    /// <summary>Forwards the Integer native signature once before recording its arguments.</summary>
    private void CaptureInteger(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IntegerCall((a0) =>
        {
            ((delegate* unmanaged[Stdcall]<int, void>)original)(a0);
            Record(display, a0);
        }));
    }

    /// <summary>Matches the Unsigned native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnsignedCall(uint a0);

    /// <summary>Forwards the Unsigned native signature once before recording its arguments.</summary>
    private void CaptureUnsigned(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new UnsignedCall((a0) =>
        {
            ((delegate* unmanaged[Stdcall]<uint, void>)original)(a0);
            Record(display, a0);
        }));
    }

    /// <summary>Matches the IntegerPair native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IntegerPairCall(int a0, int a1);

    /// <summary>Forwards the IntegerPair native signature once before recording its arguments.</summary>
    private void CaptureIntegerPair(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IntegerPairCall((a0, a1) =>
        {
            ((delegate* unmanaged[Stdcall]<int, int, void>)original)(a0, a1);
            Record(display, a0, a1);
        }));
    }

    /// <summary>Matches the IntegerQuad native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IntegerQuadCall(int a0, int a1, int a2, int a3);

    /// <summary>Forwards the IntegerQuad native signature once before recording its arguments.</summary>
    private void CaptureIntegerQuad(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IntegerQuadCall((a0, a1, a2, a3) =>
        {
            ((delegate* unmanaged[Stdcall]<int, int, int, int, void>)original)(a0, a1, a2, a3);
            Record(display, a0, a1, a2, a3);
        }));
    }

    /// <summary>Matches the IndexedInteger native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IndexedIntegerCall(int a0, uint a1);

    /// <summary>Forwards the IndexedInteger native signature once before recording its arguments.</summary>
    private void CaptureIndexedInteger(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IndexedIntegerCall((a0, a1) =>
        {
            ((delegate* unmanaged[Stdcall]<int, uint, void>)original)(a0, a1);
            Record(display, a0, a1);
        }));
    }

    /// <summary>Matches the IndexedIntegerPair native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IndexedIntegerPairCall(uint a0, int a1, int a2);

    /// <summary>Forwards the IndexedIntegerPair native signature once before recording its arguments.</summary>
    private void CaptureIndexedIntegerPair(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IndexedIntegerPairCall((a0, a1, a2) =>
        {
            ((delegate* unmanaged[Stdcall]<uint, int, int, void>)original)(a0, a1, a2);
            Record(display, a0, a1, a2);
        }));
    }

    /// <summary>Matches the IndexedIntegerQuad native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IndexedIntegerQuadCall(uint a0, int a1, int a2, int a3, int a4);

    /// <summary>Forwards the IndexedIntegerQuad native signature once before recording its arguments.</summary>
    private void CaptureIndexedIntegerQuad(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IndexedIntegerQuadCall((a0, a1, a2, a3, a4) =>
        {
            ((delegate* unmanaged[Stdcall]<uint, int, int, int, int, void>)original)(a0, a1, a2, a3, a4);
            Record(display, a0, a1, a2, a3, a4);
        }));
    }

    /// <summary>Matches the Float native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FloatCall(float a0);

    /// <summary>Forwards the Float native signature once before recording its arguments.</summary>
    private void CaptureFloat(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new FloatCall((a0) =>
        {
            ((delegate* unmanaged[Stdcall]<float, void>)original)(a0);
            Record(display, a0);
        }));
    }

    /// <summary>Matches the FloatPair native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FloatPairCall(float a0, float a1);

    /// <summary>Forwards the FloatPair native signature once before recording its arguments.</summary>
    private void CaptureFloatPair(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new FloatPairCall((a0, a1) =>
        {
            ((delegate* unmanaged[Stdcall]<float, float, void>)original)(a0, a1);
            Record(display, a0, a1);
        }));
    }

    /// <summary>Matches the FloatQuad native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FloatQuadCall(float a0, float a1, float a2, float a3);

    /// <summary>Forwards the FloatQuad native signature once before recording its arguments.</summary>
    private void CaptureFloatQuad(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new FloatQuadCall((a0, a1, a2, a3) =>
        {
            ((delegate* unmanaged[Stdcall]<float, float, float, float, void>)original)(a0, a1, a2, a3);
            Record(display, a0, a1, a2, a3);
        }));
    }

    /// <summary>Matches the IndexedFloatQuad native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IndexedFloatQuadCall(uint a0, float a1, float a2, float a3, float a4);

    /// <summary>Forwards the IndexedFloatQuad native signature once before recording its arguments.</summary>
    private void CaptureIndexedFloatQuad(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IndexedFloatQuadCall((a0, a1, a2, a3, a4) =>
        {
            ((delegate* unmanaged[Stdcall]<uint, float, float, float, float, void>)original)(a0, a1, a2, a3, a4);
            Record(display, a0, a1, a2, a3, a4);
        }));
    }

    /// <summary>Matches the Byte native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ByteCall(byte a0);

    /// <summary>Forwards the Byte native signature once before recording its arguments.</summary>
    private void CaptureByte(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new ByteCall((a0) =>
        {
            ((delegate* unmanaged[Stdcall]<byte, void>)original)(a0);
            Record(display, a0);
        }));
    }

    /// <summary>Matches the ByteQuad native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ByteQuadCall(byte a0, byte a1, byte a2, byte a3);

    /// <summary>Forwards the ByteQuad native signature once before recording its arguments.</summary>
    private void CaptureByteQuad(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new ByteQuadCall((a0, a1, a2, a3) =>
        {
            ((delegate* unmanaged[Stdcall]<byte, byte, byte, byte, void>)original)(a0, a1, a2, a3);
            Record(display, a0, a1, a2, a3);
        }));
    }

    /// <summary>Matches the IndexedByteQuad native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IndexedByteQuadCall(uint a0, byte a1, byte a2, byte a3, byte a4);

    /// <summary>Forwards the IndexedByteQuad native signature once before recording its arguments.</summary>
    private void CaptureIndexedByteQuad(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IndexedByteQuadCall((a0, a1, a2, a3, a4) =>
        {
            ((delegate* unmanaged[Stdcall]<uint, byte, byte, byte, byte, void>)original)(a0, a1, a2, a3, a4);
            Record(display, a0, a1, a2, a3, a4);
        }));
    }

    /// <summary>Matches the StencilFunction native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void StencilFunctionCall(int a0, int a1, int a2, uint a3);

    /// <summary>Forwards the StencilFunction native signature once before recording its arguments.</summary>
    private void CaptureStencilFunction(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new StencilFunctionCall((a0, a1, a2, a3) =>
        {
            ((delegate* unmanaged[Stdcall]<int, int, int, uint, void>)original)(a0, a1, a2, a3);
            Record(display, a0, a1, a2, a3);
        }));
    }

    /// <summary>Matches the IntegerUnsigned native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IntegerUnsignedCall(int a0, uint a1);

    /// <summary>Forwards the IntegerUnsigned native signature once before recording its arguments.</summary>
    private void CaptureIntegerUnsigned(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IntegerUnsignedCall((a0, a1) =>
        {
            ((delegate* unmanaged[Stdcall]<int, uint, void>)original)(a0, a1);
            Record(display, a0, a1);
        }));
    }

    /// <summary>Matches the UnsignedPair native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnsignedPairCall(uint a0, uint a1);

    /// <summary>Forwards the UnsignedPair native signature once before recording its arguments.</summary>
    private void CaptureUnsignedPair(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new UnsignedPairCall((a0, a1) =>
        {
            ((delegate* unmanaged[Stdcall]<uint, uint, void>)original)(a0, a1);
            Record(display, a0, a1);
        }));
    }

    /// <summary>Matches the SampleCoverage native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SampleCoverageCall(float a0, byte a1);

    /// <summary>Forwards the SampleCoverage native signature once before recording its arguments.</summary>
    private void CaptureSampleCoverage(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new SampleCoverageCall((a0, a1) =>
        {
            ((delegate* unmanaged[Stdcall]<float, byte, void>)original)(a0, a1);
            Record(display, a0, a1);
        }));
    }

    /// <summary>Matches the IntegerFloat native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IntegerFloatCall(int a0, float a1);

    /// <summary>Forwards the IntegerFloat native signature once before recording its arguments.</summary>
    private void CaptureIntegerFloat(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IntegerFloatCall((a0, a1) =>
        {
            ((delegate* unmanaged[Stdcall]<int, float, void>)original)(a0, a1);
            Record(display, a0, a1);
        }));
    }

    /// <summary>Matches the DoublePair native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DoublePairCall(double a0, double a1);

    /// <summary>Forwards the DoublePair native signature once before recording its arguments.</summary>
    private void CaptureDoublePair(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new DoublePairCall((a0, a1) =>
        {
            ((delegate* unmanaged[Stdcall]<double, double, void>)original)(a0, a1);
            Record(display, a0, a1);
        }));
    }

    /// <summary>Matches the IndexedDoublePair native state-call ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IndexedDoublePairCall(uint a0, double a1, double a2);

    /// <summary>Forwards the IndexedDoublePair native signature once before recording its arguments.</summary>
    private void CaptureIndexedDoublePair(string entrypoint, string display)
    {
        nint original = native.Original(entrypoint);
        native.Add(entrypoint, new IndexedDoublePairCall((a0, a1, a2) =>
        {
            ((delegate* unmanaged[Stdcall]<uint, double, double, void>)original)(a0, a1, a2);
            Record(display, a0, a1, a2);
        }));
    }

    #endregion
    #endregion
}
