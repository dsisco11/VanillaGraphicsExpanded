using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Globalization;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Postprocessing.Diagnostics;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Checks the requested exposure snapshot against independent display-equation expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ExposureSnapshotTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Local correction uses applied global gain and the display pivot across physical storage scales.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(64f)]
    [InlineData(.0009765625f)]
    public void SnapshotUsesDisplayRelativePivot(float scale)
    {
        // Arrange: Scale frozen frame storage while holding physical camera gain, base log, and local policy constant.
        EnsureShaderTestAvailable();
        // Act and assert: Capture a snapshot and compare its physical statistics and display-relative correction fields.
        float[] data=Capture([2,99,-12,1,8,1,0,0],Policy(),Camera(),false,scale:scale);
        float expected=.5f*(MathF.Log2(.18f)-2+12);
        Close(MathF.Log2(.18f),data[1]);Close(2,data[6]);Close(expected,data[7]);
        Close(-12,data[8]);Close(-12,data[9]);Close(1,data[10]);Close(2+expected,data[11]);
    }
    /// <summary>The same dark receiver never becomes dimmer as the applied camera gain reaches its cap.</summary>
    [Fact]
    public void SamePatchRegressionIsMonotonicAndCapMetadataCannotChangeIt()
    {
        // Arrange: Keep the same dark receiver while varying applied gain and injecting stale cap metadata.
        EnsureShaderTestAvailable();
        // Act and assert: Compare the patch across capped states and a sweep of gains; it never becomes dimmer.
        var policy=Camera() with {MaxLogGain=8};
        float[] first=Capture([6.69853f,0,-9,1,6.69853f,0,0,0],Policy(),policy,false,baseLog:-14.42059f);
        float[] dark=Capture([8,0,-18,1,15,1,0,0],Policy(),policy,false,baseLog:-14.42059f);
        float[] sameCap=Capture([8,float.NaN,-80,0,-10,0,0,0],Policy(),policy,false,baseLog:-14.42059f);
        Assert.True(dark[11]>=first[11]);Close(dark[7],sameCap[7]);Close(dark[11],sameCap[11]);
        float previous=float.NegativeInfinity;
        foreach(float gain in new[]{-4f,0,2,4,6,8,12,16})
        {
            float[] data=Capture([gain,0,0,0,0,0,0,0],Policy(),policy,false,baseLog:-14.42059f);
            Assert.True(data[11]>=previous-.00005f);previous=data[11];
        }
    }
    /// <summary>Uncapped automatic metering retains the former centered response algebraically without physical history.</summary>
    [Fact]
    public void UncappedAutomaticResponseMatchesCenteredEquation()
    {
        // Arrange: Use automatic metering with an uncapped gain and explicit compensation.
        EnsureShaderTestAvailable();
        // Act and assert: Capture the requested snapshot and compare its gain and pivot to the centered equation.
        var camera=Camera() with {MaxLogGain=20,Compensation=1.25f};
        float gain=MathF.Log2(camera.MiddleGray)+8+camera.Compensation;
        float[] data=Capture([gain,0,-8,1,gain,0,0,0],Policy(),camera,false);
        Close(2,data[7]);Close(MathF.Log2(.18f)+1.25f,data[1]);
    }
    /// <summary>Manual mode needs no histogram metadata and uses fixed gray while automatic compensation shifts its pivot.</summary>
    [Theory]
    [InlineData(false,-1f,.25f,.18f)]
    [InlineData(false,5f,1f,.09f)]
    [InlineData(false,25f,1f,.09f)]
    [InlineData(true,5f,1.25f,.18f)]
    [InlineData(true,5f,-1f,.36f)]
    public void CameraControlsDefineAppliedGainAndPivot(bool automatic,float gain,float compensation,float gray)
    {
        // Arrange: Select mode, manual gain, compensation, gray pivot, and bounds from the theory row.
        EnsureShaderTestAvailable();
        // Act and assert: Capture fields and verify applied gain, pivot, clamp metadata, and bounded local correction.
        var camera=Camera() with {Enabled=automatic,ManualLogGain=gain,Compensation=compensation,MiddleGray=gray,MinLogGain=-10,MaxLogGain=20};
        float[] data=Capture([gain,0,0,0,0,0,0,0],Policy(),camera,false);
        float applied=automatic?gain:Math.Clamp(gain+compensation,camera.MinLogGain,camera.MaxLogGain);
        float pivot=MathF.Log2(gray)+(automatic?compensation:0);
        Close(applied,data[6]);Close(pivot,data[1]);
        if (!automatic) { Close(gain+compensation,data[4]); Close(gain+compensation<camera.MinLogGain || gain+compensation>camera.MaxLogGain ? 1 : 0,data[5]); }
        Close(Math.Clamp(.5f*(pivot-applied+12),-4,4),data[7]);Close(1,data[10]);
    }
    /// <summary>Meter validity and obsolete reserved history values cannot withdraw local correction.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void MeterMetadataDoesNotControlLocalCorrection(float validity)
    {
        // Arrange: Supply invalid meter metadata while keeping local exposure enabled, then prepare a disabled policy.
        EnsureShaderTestAvailable();
        // Act and assert: Capture both policies and ensure meter validity does not gate enabled local correction.
        float[] data=Capture([2,float.NaN,-12,validity,8,1,0,0],Policy(),Camera(),false);
        Close(.5f*(MathF.Log2(.18f)-2+12),data[7]);Close(1,data[10]);
        float[] disabled=Capture([2,0,0,0,0,0,0,0],Policy() with {Enabled=false},Camera(),false);
        Close(0,disabled[7]);Close(0,disabled[10]);Close(2,disabled[11]);
    }
    /// <summary>The reported probe luminance includes the same optional edge filter as final presentation.</summary>
    [Fact]
    public void SnapshotReportsAntialiasedProbeLuminance()
    {
        // Arrange: Provide a three-texel luminance edge and the authored filtering policy.
        EnsureShaderTestAvailable();
        // Act and assert: Capture probe metadata and verify it includes the same optional edge filtering as presentation.
        float[] pixels=[.25f,.25f,.25f,0,1,1,1,0,16,16,16,1];
        float[] plain=Capture([2,-6,-12,1,8,1,0,0],Policy() with {Enabled=false},Camera(),false,pixels,3,1);
        float[] filtered=Capture([2,-6,-12,1,8,1,0,0],Policy() with {Enabled=false},Camera(),true,pixels,3,1);
        // A horizontal full-contrast edge blends the centre equally with two half-texel bilinear probes.
        double expected=.75*1+.125*(.25+16);
        Close(0,plain[8]);Close((float)Math.Log2(expected),filtered[8]);
        Close(plain[11],filtered[11]);
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
        float[]? pixels=null,int width=1,int height=1,float scale=1,float baseLog=-12)
    {
        using var scene=TestFramework.CreateTexture(width,height,TextureFormat.Rgba32f,
            pixels ?? [MathF.Pow(2,baseLog)*scale,MathF.Pow(2,baseLog)*scale,MathF.Pow(2,baseLog)*scale,1]);
        using var baseImage=TestFramework.CreateTexture(1,1,TextureFormat.Rg32f,[baseLog,1f]);
        using var frame=TestFrameCamera.CreateIdentity(width,height);
        using var lighting=new TestFrameLightingPublication();
        lighting.Capture(scale);
        using var history=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Tests.ExposureSnapshot.History" }).RequireResource();history.UploadData(state);
        using var output=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Tests.ExposureSnapshot.Output" }).RequireResource();output.Allocate(48);
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
