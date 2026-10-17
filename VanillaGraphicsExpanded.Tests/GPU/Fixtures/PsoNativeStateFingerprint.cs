using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Queries native fixed-function and binding state independently of production coverage, snapshots and cache knowledge.</summary>
internal static class PsoNativeStateFingerprint
{
    #region Public API
    /// <summary>Records supported scalar/vector groups, every indexed blend slot, texture unit and buffer-range binding.</summary>
    internal static string Read()
    {
        var values = new SortedDictionary<string, string>();
        string[] integers = ["DepthFunc", "DepthWritemask", "StencilFunc", "StencilRef", "StencilValueMask", "StencilWritemask",
            "StencilFail", "StencilPassDepthFail", "StencilPassDepthPass", "StencilBackFunc", "StencilBackRef", "StencilBackValueMask",
            "StencilBackWritemask", "StencilBackFail", "StencilBackPassDepthFail", "StencilBackPassDepthPass", "CullFaceMode", "FrontFace",
            "PolygonMode", "ProvokingVertex", "PointSpriteCoordOrigin", "PrimitiveRestartIndex", "PatchVertices", "LogicOpMode",
            "ScissorBox", "Viewport", "CurrentProgram", "VertexArrayBinding", "ArrayBufferBinding", "ElementArrayBufferBinding",
            "DrawFramebufferBinding", "ReadFramebufferBinding", "ReadBuffer", "RenderbufferBinding", "StencilClearValue", "ActiveTexture", "UniformBufferBinding", "ShaderStorageBufferBinding"];
        foreach (string name in integers) ReadInteger(values, name);
        if (GpuSupport.Graphics.ClipControl) { ReadInteger(values, "ClipOrigin"); ReadInteger(values, "ClipDepthMode"); }
        foreach (string name in new[] { "LineWidth", "PointSize", "PolygonOffsetFactor", "PolygonOffsetUnits",
            "BlendColor", "ColorClearValue", "SampleCoverageValue", "SampleCoverageInvert" }) ReadFloat(values, name);
        double[] depthRange = new double[2]; GL.GetDouble(GetPName.DepthRange, depthRange);
        values["DepthRange"] = string.Join(",", depthRange.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        values["DepthClearValue"] = GL.GetDouble(GetPName.DepthClearValue).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (GpuSupport.Graphics.SampleShading) ReadFloat(values, "MinSampleShadingValue");
        if (!GpuSupport.Graphics.CoreProfile)
        {
            foreach (string name in new[] { "AlphaTestFunc", "LineStippleRepeat", "LineStipplePattern" }) ReadInteger(values, name);
            ReadFloat(values, "AlphaTestRef");
            foreach (var cap in new[] { EnableCap.AlphaTest, EnableCap.PointSmooth, EnableCap.LineStipple, EnableCap.PolygonStipple })
                values["enabled:" + cap] = GL.IsEnabled(cap).ToString();
            byte[] pattern = new byte[128]; GL.GetPolygonStipple(pattern); values["polygonStipplePattern"] = Convert.ToHexString(pattern);
        }
        EnableCap[] caps = [EnableCap.DepthTest, EnableCap.StencilTest, EnableCap.CullFace, EnableCap.ScissorTest,
            EnableCap.LineSmooth, EnableCap.PolygonSmooth, EnableCap.PolygonOffsetFill, EnableCap.PolygonOffsetLine,
            EnableCap.PolygonOffsetPoint, EnableCap.RasterizerDiscard, EnableCap.ProgramPointSize, EnableCap.PrimitiveRestart,
            EnableCap.Multisample, EnableCap.SampleCoverage, EnableCap.SampleMask, EnableCap.SampleAlphaToCoverage,
            EnableCap.SampleAlphaToOne, EnableCap.FramebufferSrgb, EnableCap.Dither, EnableCap.ColorLogicOp];
        foreach (var cap in caps) values["enabled:" + cap] = GL.IsEnabled(cap).ToString();
        if (GpuSupport.Graphics.DepthClamp) values["enabled:DepthClamp"] = GL.IsEnabled(EnableCap.DepthClamp).ToString();
        if (GpuSupport.Graphics.FixedIndexRestart) values["enabled:PrimitiveRestartFixedIndex"] = GL.IsEnabled(EnableCap.PrimitiveRestartFixedIndex).ToString();
        if (GpuSupport.Graphics.SampleShading) values["enabled:SampleShading"] = GL.IsEnabled(EnableCap.SampleShading).ToString();
        for (int slot = 0; slot < GpuSupport.Graphics.MaxClipDistances; slot++)
            values["clip:" + slot] = GL.IsEnabled(EnableCap.ClipDistance0 + slot).ToString();
        for (int slot = 0; slot < GpuSupport.Graphics.MaxDrawBuffers; slot++)
        {
            values["blend:" + slot] = GL.IsEnabled(IndexedEnableCap.Blend, slot).ToString();
            foreach (var parameter in new[] { GetPName.BlendSrcRgb, GetPName.BlendDstRgb, GetPName.BlendSrcAlpha,
                GetPName.BlendDstAlpha, GetPName.BlendEquationRgb, GetPName.BlendEquationAlpha })
            {
                GL.GetInteger((GetIndexedPName)parameter, slot, out int value); values[parameter + ":" + slot] = value.ToString();
            }
            bool[] mask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, slot, mask);
            values["mask:" + slot] = string.Join(",", mask);
            values["route:" + slot] = GL.GetInteger(GetPName.DrawBuffer0 + slot).ToString();
        }
        for (int word = 0; word < GpuSupport.Graphics.MaxSampleMaskWords; word++)
        {
            GL.GetInteger(GetIndexedPName.SampleMaskValue, word, out int value); values["samplemask:" + word] = value.ToString();
        }
        int active = GL.GetInteger(GetPName.ActiveTexture);
        for (int unit = 0; unit < GpuSupport.Graphics.MaxCombinedTextureImageUnits; unit++)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            foreach (var parameter in new[] { GetPName.TextureBinding2D, GetPName.TextureBinding3D, GetPName.TextureBinding2DArray,
                GetPName.TextureBindingCubeMap, GetPName.SamplerBinding }) values[parameter + ":" + unit] = GL.GetInteger(parameter).ToString();
        }
        GL.ActiveTexture((TextureUnit)active);
        ReadRanges(values, "UniformBuffer", GpuSupport.Graphics.MaxUniformBufferBindings);
        ReadRanges(values, "ShaderStorageBuffer", GpuSupport.Graphics.MaxShaderStorageBufferBindings);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        return JsonSerializer.Serialize(values);
    }

    /// <summary>Authors nondefault disabled payloads, including trailing outputs that no workload routes.</summary>
    internal static void MakeDormantHostile()
    {
        GL.LineWidth(4); GL.PointSize(4); GL.Enable(EnableCap.LineSmooth);
        GL.Scissor(2, 1, 3, 2); GL.Enable(EnableCap.ScissorTest);
        GL.SampleCoverage(.375f, true); GL.Disable(EnableCap.SampleCoverage);
        GL.BlendColor(.125f, .25f, .5f, .75f);
        for (int slot = 0; slot < GpuSupport.Graphics.MaxDrawBuffers; slot++)
        {
            GL.BlendFuncSeparate(slot, BlendingFactorSrc.Zero, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.Zero);
            GL.BlendEquationSeparate(slot, BlendEquationMode.FuncReverseSubtract, BlendEquationMode.Max);
            GL.Enable(IndexedEnableCap.Blend, slot);
        }
        StateCache.Current.InvalidateAll();
    }
    #endregion

    #region Private
    /// <summary>Reads integer vectors into a bounded buffer, preserving exact mask bits and native object identities.</summary>
    private static void ReadInteger(IDictionary<string, string> values, string name)
    {
        int[] data = new int[4]; GL.GetInteger(Parameter(name), data); values[name] = string.Join(",", data);
    }

    /// <summary>Reads floating vectors without converting them through integer queries.</summary>
    private static void ReadFloat(IDictionary<string, string> values, string name)
    {
        float[] data = new float[4]; GL.GetFloat(Parameter(name), data); values[name] = string.Join(",", data.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Checks all range tuples, including dormant offsets and sizes, through direct 64-bit queries.</summary>
    private static void ReadRanges(IDictionary<string, string> values, string family, int count)
    {
        foreach (string suffix in new[] { "Binding", "Start", "Size" })
        for (int slot = 0; slot < count; slot++)
        {
            GL.GetInteger64((GetIndexedPName)Parameter(family + suffix), slot, out long value);
            values[family + suffix + ":" + slot] = value.ToString();
        }
    }

    /// <summary>Uses named extension tokens when the binding's narrower query enum omits an otherwise supported parameter.</summary>
    private static GetPName Parameter(string name) => Enum.TryParse<GetPName>(name, out var value) ? value : (GetPName)Enum.Parse<All>(name);
    #endregion
}
