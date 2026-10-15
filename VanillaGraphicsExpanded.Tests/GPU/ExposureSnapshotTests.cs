using System.Globalization;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Postprocessing.Diagnostics;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the requested exposure snapshot against independent display-equation expectations.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class ExposureSnapshotTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>The probe distinguishes capped global gain from independently adapted local shadow correction.</summary>
    [Theory]
    [InlineData(-12f,0f,1f)]
    [InlineData(-6f,3f,1f)]
    [InlineData(-6f,3f,64f)]
    [InlineData(-6f,3f,.0009765625f)]
    public void SnapshotSeparatesAppliedGlobalAndLocalGain(float adapted,float expectedLocal,float scale)
    {
        EnsureShaderTestAvailable();
        float[] history=[2,adapted,-12,1,8,1,0,0];
        float[] data=Capture(history,Policy(),Camera(),false,scale:scale);
        Assert.Equal(history.Take(6),data.Take(6));
        Close(2,data[6]);Close(expectedLocal,data[7]);
        Close(-12,data[8]);Close(-12,data[9]);Close(1,data[10]);
        Close(2+expectedLocal,data[11]);
    }

    /// <summary>Manual mode uses the captured manual gain and compensation while preserving borrowed history metadata.</summary>
    [Fact]
    public void ManualGainOverridesAutomaticHistory()
    {
        EnsureShaderTestAvailable();
        float[] data=Capture([2,-6,-12,1,8,1,0,0],Policy() with {Enabled=false},
            Camera() with {Enabled=false,ManualEV=-1,Compensation=.25f},false);
        Close(2,data[0]);Close(-.75f,data[6]);Close(0,data[7]);Close(0,data[10]);Close(-.75f,data[11]);
    }

    /// <summary>Disabled local correction and missing or corrupt physical validity produce neutral local gain.</summary>
    [Theory]
    [InlineData(false,1f)]
    [InlineData(true,0f)]
    [InlineData(true,float.NaN)]
    [InlineData(true,float.PositiveInfinity)]
    public void MissingLocalHistoryIsNeutral(bool enabled,float validity)
    {
        EnsureShaderTestAvailable();
        float[] data=Capture([2,-6,-12,validity,8,1,0,0],Policy() with {Enabled=enabled},Camera(),false);
        Close(0,data[7]);Close(0,data[10]);Close(2,data[11]);
    }

    /// <summary>The reported probe luminance includes the same optional edge filter as final presentation.</summary>
    [Fact]
    public void SnapshotReportsAntialiasedProbeLuminance()
    {
        EnsureShaderTestAvailable();
        float[] pixels=[.25f,.25f,.25f,0,1,1,1,0,16,16,16,1];
        float[] plain=Capture([2,-6,-12,1,8,1,0,0],Policy() with {Enabled=false},Camera(),false,pixels,3,1);
        float[] filtered=Capture([2,-6,-12,1,8,1,0,0],Policy() with {Enabled=false},Camera(),true,pixels,3,1);
        // A horizontal full-contrast edge blends the centre equally with two half-texel bilinear probes.
        double expected=.75*1+.125*(.25+16);
        Close(0,plain[8]);Close((float)Math.Log2(expected),filtered[8]);
        Close(plain[11],filtered[11]);
    }

    /// <summary>Logs use invariant numeric fields and explicitly identify absent history without inventing validity.</summary>
    [Fact]
    public void SnapshotFormattingIsInvariantAndIdentifiesMissingHistory()
    {
        var previous=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
            float[] data=[2,-6,-12,0,8,1,2,3,-12,-12,0,5];
            string text=ExposureSnapshotLog.Format(data,Camera(),Policy(),false,17,false,true);
            Assert.Contains("frame=17",text);Assert.Contains("history=False",text);
            Assert.Contains("globalStops=2.00000",text);Assert.Contains("localStops=3.00000",text);
            Assert.Contains("combinedStops=5.00000",text);Assert.Contains("combinedGain=32",text);
            Assert.Contains("adaptationValid=0",text);Assert.Contains("localProbeValid=0",text);
            Assert.Contains("FXAA=True",text);Assert.Contains("LumOn=False",text);
            Assert.DoesNotContain("2,00000",text);
            Assert.Throws<ArgumentException>(()=>ExposureSnapshotLog.Format(new float[11],Camera(),Policy(),false,0,false,false));
        }
        finally {CultureInfo.CurrentCulture=previous;}
    }
    #endregion

    #region Private
    /// <summary>Declares the currently selected local compression without depending on mutable configuration.</summary>
    private static LocalExposureParameters Policy()=>new(true,1,1,1,.5f,.5f,4,4,false);
    /// <summary>Declares an automatic camera with the current two-stop brightening cap.</summary>
    private static CameraExposureParameters Camera()=>new(true,0,0,-20,2,.18f,0,1,4,8,false);
    /// <summary>Compares a single independent expectation within a tight shader arithmetic tolerance.</summary>
    private static void Close(float expected,float actual)=>Assert.InRange(actual,expected-.00005f,expected+.00005f);
    /// <summary>Dispatches the real diagnostic binary with isolated frozen frame and history publications.</summary>
    private float[] Capture(float[] state,LocalExposureParameters local,CameraExposureParameters camera,bool antialias,
        float[]? pixels=null,int width=1,int height=1,float scale=1)
    {
        using var scene=TestFramework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,
            pixels ?? [MathF.Pow(2,-12)*scale,MathF.Pow(2,-12)*scale,MathF.Pow(2,-12)*scale,1]);
        using var baseImage=TestFramework.CreateTexture(1,1,PixelInternalFormat.Rg32f,[-12f,1f]);
        using var frame=TestFrameCamera.CreateIdentity(width,height);
        using var lighting=new TestFrameLightingPublication();
        lighting.Capture(scale);
        using var history=GpuShaderStorageBuffer.Create(debugName:"Tests.ExposureSnapshot.History");history.UploadData(state);
        using var output=GpuShaderStorageBuffer.Create(debugName:"Tests.ExposureSnapshot.Output");output.Allocate(48);
        var program=Programs.CreateCompute(ExposureSnapshotProgram.Create);
        program.FrameOverride=frame;program.LightingOverride=lighting.Buffer;
        program.Scene=scene;program.LocalBase=baseImage;program.History=history;program.Output=output;
        program.Capture(local,camera,antialias);program.Dispatch(1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        var result=ExposureTestStorage.Read<float>(output);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        return result;
    }
    #endregion
}
