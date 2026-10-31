using ErrorCode = OpenTK.Graphics.OpenGL4.ErrorCode;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies the compact GPU oracle against authored data, deliberate failures and hostile native state.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuImageAssertionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Numeric predicates
    /// <summary>Exact comparison preserves numeric zero, NaN/infinity and float subnormals without an arithmetic flush.</summary>
    [Fact]
    public void ExactAndTolerantFloatContractsRemainIndependent()
    {
        EnsureContextValid();
        float[] values = [0, -0f, float.Epsilon, -float.Epsilon, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity];
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 8, 1);
        Upload(image, values, PixelComponentType.Float);
        using var assertions = new GpuImageAssertions();
        var equal = assertions.Compare(image, values); equal.AssertEqual(); Assert.Equal(3u, equal.NonFinite);
        float[] wrong = values.ToArray(); wrong[2] = 0;
        var mismatch = assertions.Compare(image, wrong); Assert.Equal(1u, mismatch.Mismatches);
        Assert.Contains("(2,0,0), channel=0", mismatch.ToString());
        assertions.Compare(image, wrong, tolerance: (double)float.Epsilon).AssertEqual();
        wrong[6] = float.NegativeInfinity;
        Assert.Equal(2u, assertions.Compare(image, wrong).Mismatches);
        using var finite = Create(TextureDimension.Two, TextureFormat.R32f, 4, 1);
        Upload(finite, new float[] { -2, 0, 1, 3 }, PixelComponentType.Float);
        var range = assertions.Inspect(finite, -1, 2);
        Assert.Equal(2u, range.OutsideRange); Assert.Equal(2u, range.Positive);
        Assert.Equal(-2d, range.Minimum); Assert.Equal(3d, range.Maximum);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(range.AssertFiniteRange);
    }

    /// <summary>Integer predicates retain the full 32-bit domain, rather than round-tripping through float.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IntegerValuesPreserveAllBits(bool signed)
    {
        EnsureContextValid();
        uint[] bits = signed ? [0, 0x80000000, 0x7fffffff, uint.MaxValue] : [0, uint.MaxValue, 16777217, 0x80000000];
        using var image = Create(TextureDimension.Two, signed ? TextureFormat.R32i : TextureFormat.R32ui, 4, 1);
        RHICommandContext.Current.UploadTexture(image, new(new(0,0),0,0,0,4,1),
            new(PixelChannelLayout.RedInteger, signed ? PixelComponentType.Int : PixelComponentType.UnsignedInt), MemoryMarshal.AsBytes(bits.AsSpan()));
        using var assertions = new GpuImageAssertions();
        assertions.CompareBits(image, bits).AssertEqual();
        bits[2] ^= 1;
        Assert.Equal(1u, assertions.CompareBits(image, bits).Mismatches);
    }

    /// <summary>Rank validation rejects duplicates, malformed values and an unchanged temporal slice.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void RankAndSliceNegativesAreObservable(int mutation)
    {
        EnsureContextValid();
        float[] data = [.125f,.375f,.625f,.875f,.375f,.625f,.875f,.125f];
        if (mutation == 1) data[7] = data[6];
        if (mutation == 2) data[5] = float.NaN;
        if (mutation == 3) Array.Copy(data, 0, data, 4, 4);
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 2, 4);
        Upload(image, data, PixelComponentType.Float);
        using var assertions = new GpuImageAssertions();
        var result = assertions.CheckRanks(image, 2, 2);
        if (mutation == 0) result.AssertRanks(2);
        else Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => result.AssertRanks(2));
    }
    #endregion

    #region Domains and history
    /// <summary>Every texel in each selected mip, region, volume layer or cube face reaches the comparison.</summary>
    [Theory]
    [InlineData(TextureDimension.Two)] [InlineData(TextureDimension.TwoArray)]
    [InlineData(TextureDimension.Three)] [InlineData(TextureDimension.Cube)]
    public void SelectionsAndHistoryRetainTheCompleteDomain(TextureDimension dimension)
    {
        EnsureContextValid();
        int layers = dimension == TextureDimension.Cube ? 6 : dimension == TextureDimension.Two ? 1 : 3;
        using var image = Create(dimension, TextureFormat.R32f, 4, 4, layers, 2);
        for (int layer = 0; layer < layers; layer++)
            Upload(image, Enumerable.Range(0, 16).Select(index => (float)(index + layer * 16)).ToArray(), PixelComponentType.Float, layer);
        var selection = new RHITextureRegion(new(0, layers - 1), 1, 1, 0, 2, 2);
        float[] expected = [5+(layers-1)*16,6+(layers-1)*16,9+(layers-1)*16,10+(layers-1)*16];
        using var assertions = new GpuImageAssertions();
        assertions.Compare(image, Enumerable.Range(0,16*layers).Select(value=>(float)value).ToArray()).AssertEqual();
        assertions.Compare(image, expected, selection: selection).AssertEqual();
        using var previous = new GpuImageSnapshot(image, selection);
        assertions.CompareHistory(image, previous, selection).AssertEqual();
        RHICommandContext.Current.UploadTexture(image, new(new(0,layers-1),2,2,0,1,1),
            new(PixelChannelLayout.Red,PixelComponentType.Float),MemoryMarshal.AsBytes(new float[] {99}.AsSpan()));
        Assert.Equal(1u, assertions.CompareHistory(image, previous, selection).Mismatches);
        // A selected nonbase mip has its own extent and contents; no base-image resampling is permitted.
        Upload(image, new float[] {.1f,.2f,.3f,.4f}, PixelComponentType.Float, 0, 1, 2, 2);
        assertions.Compare(image, new float[] {.1f,.2f,.3f,.4f}, selection:new(new(1,0),0,0,0,2,2)).AssertEqual();
    }

    /// <summary>Depth is sampled as depth, while channel masks exclude only explicitly unasserted color components.</summary>
    [Fact]
    public void DepthAndChannelSelectionsPreserveTheirAspects()
    {
        EnsureContextValid();
        using var depth = Create(TextureDimension.Two, TextureFormat.DepthComponent32f, 3, 1);
        Upload(depth, new float[] {.1f,.5f,.9f}, PixelComponentType.Float);
        using var assertions = new GpuImageAssertions();
        assertions.Compare(depth, new float[] {.1f,.5f,.9f}).AssertEqual();
        using var rgba = Create(TextureDimension.Two, TextureFormat.Rgba32f, 1, 1);
        Upload(rgba, new float[] {1,2,3,4}, PixelComponentType.Float);
        assertions.Compare(rgba, new float[] {1,99,3,99}, channelMask:5).AssertEqual();
        Assert.Equal(2u, assertions.Compare(rgba, new float[] {1,99,3,99}).Mismatches);
        using var encoded=Create(TextureDimension.Two,TextureFormat.Srgb8Alpha8,1,1);
        Upload(encoded,new float[] {.25f,.5f,.75f,1},PixelComponentType.Float);
        assertions.Compare(encoded,new float[] {64f/255,128f/255,191f/255,1},tolerance:1e-7).AssertEqual();
        using var packed=Create(TextureDimension.Two,TextureFormat.Depth24Stencil8,2,1);
        uint[] depthStencil=[0x000000ff,0xffffff12];
        RHICommandContext.Current.UploadTexture(packed,new(new(0,0),0,0,0,2,1),
            new(PixelChannelLayout.DepthStencil,PixelComponentType.UnsignedInt248),MemoryMarshal.AsBytes(depthStencil.AsSpan()));
        assertions.Compare(packed,new float[] {0,1}).AssertEqual();
    }

    /// <summary>Layer construction retains authored nearest resampling, exact copied values and zero expansion.</summary>
    [Fact]
    public void LayerCopiesAndConversionsPreserveOriginalContracts()
    {
        EnsureContextValid();
        using var color = Create(TextureDimension.Two, TextureFormat.Rgba32f, 3, 2);
        float[] authored = Enumerable.Range(0,24).Select(index => index*.125f).ToArray();
        Upload(color, authored, PixelComponentType.Float);
        using var scalar = Create(TextureDimension.Two, TextureFormat.R32f, 2, 1);
        Upload(scalar, new float[] {.25f,.75f}, PixelComponentType.Float);
        using var layered = LayeredTestTexture.Create(color, scalar, null);
        float[] expected = new float[3*2*3*4]; Array.Copy(authored, expected, authored.Length);
        for (int y=0;y<2;y++)for(int x=0;x<3;x++) expected[(6+y*3+x)*4] = x*2/3==0 ? .25f : .75f;
        using var assertions = new GpuImageAssertions();
        assertions.Compare(layered, expected).AssertEqual();
    }
    #endregion

    #region Execution failures
    /// <summary>Incomplete native dispatch cannot turn zeroed counters into a passing assertion.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SuppressedNativeDispatchFailsTheCompletionSentinel(bool partial)
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 129, 1);
        Upload(image, Enumerable.Range(0,129).Select(value=>(float)value).ToArray(), PixelComponentType.Float);
        using var assertions = new GpuImageAssertions();
        using var native = new NativeDriverCapture();
        int calls=0;
        var original=Marshal.GetDelegateForFunctionPointer<Dispatch>(native.Original("glDispatchCompute"));
        native.Add("glDispatchCompute",new Dispatch((x,y,z)=>{calls++;if(partial)original(x-1,y,z);})); native.Install();
        var error=Assert.Throws<InvalidOperationException>(() => assertions.Inspect(image));
        if(partial)Assert.Contains("texels=128/129",error.Message);
        Assert.Equal(1,calls); native.RequireSuccessfulObservation();
    }

    /// <summary>The independent histogram-validation dispatch must complete even when the first rank pass succeeds.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RankValidationRequiresItsOwnCompleteDispatch(bool partial)
    {
        EnsureContextValid();
        const int side=16,ranks=side*side;
        float[] values=Enumerable.Range(0,ranks*2).Select(index=>((index+index/ranks)%ranks+.5f)/ranks).ToArray();
        using var image=Create(TextureDimension.Two,TextureFormat.R32f,side,side*2);
        Upload(image,values,PixelComponentType.Float);
        using var assertions=new GpuImageAssertions();using var native=new NativeDriverCapture();
        var original=Marshal.GetDelegateForFunctionPointer<Dispatch>(native.Original("glDispatchCompute"));
        int calls=0;
        native.Add("glDispatchCompute",new Dispatch((x,y,z)=>{calls++;if(calls==1)original(x,y,z);else if(partial)original(x-1,y,z);}));
        native.Install();
        var failure=Assert.Throws<InvalidOperationException>(()=>assertions.CheckRanks(image,side,2));
        Assert.Contains("GPU rank validation did not complete",failure.Message);
        if(partial)Assert.Contains("ranks=448/512",failure.Message);
        Assert.Equal(2,calls);native.RequireSuccessfulObservation();
    }

    /// <summary>Partial compilation failure releases the original allocation and never publishes an executable.</summary>
    [Fact]
    public void MalformedKernelRetiresPartialConstruction()
    {
        EnsureContextValid();
        bool createdService=RHIResourceManagerSystem.CurrentManager is null;
        var service=RHIResourceManagerSystem.EnsureCurrentContext();
        try
        {
        int partial=0;
        Assert.Throws<IOException>(()=>new GpuFixtureProgram("unused",shader=>{partial=shader;throw new IOException("injected construction fault");}));
        Assert.NotEqual(0,partial);Assert.False(GL.IsShader(partial));
        Assert.Throws<InvalidOperationException>(() => new GpuFixtureProgram("#version 450\nnot a shader"));
        // A fresh valid owner remains independently usable after the failed construction.
        using var valid = new GpuFixtureProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        Assert.True(GL.IsProgram(valid.Id));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally {if(createdService){RHIResourceManagerSystem.Shutdown();service.Dispose();}}
    }

    #endregion

    #region Selector restoration
    /// <summary>Unbound, base and ranged SSBO slots, generic binding, program, images and pack layout survive checks.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HostileSelectorsRemainUnchanged(bool failedDispatch)
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 2, 1);
        Upload(image,new float[] {1,2},PixelComponentType.Float);
        using var arraySentinel=Create(TextureDimension.TwoArray,TextureFormat.R32f,1,1);
        using var sentinel = new GpuFixtureProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        int buffer=GL.GenBuffer();
        try
        {
            GL.GetInteger(GetPName.ShaderStorageBufferOffsetAlignment,out int alignment);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer,buffer); GL.BufferData(BufferTarget.ShaderStorageBuffer,alignment*4,IntPtr.Zero,BufferUsageHint.DynamicDraw);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer,0,buffer);
            GL.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer,1,buffer,(IntPtr)alignment,alignment);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer,2,0); GL.BindBuffer(BufferTarget.ShaderStorageBuffer,buffer);
            GL.BindBuffer(BufferTarget.PixelPackBuffer,buffer); GL.PixelStore(PixelStoreParameter.PackRowLength,7); GL.PixelStore(PixelStoreParameter.PackSkipPixels,1);
            GL.UseProgram(sentinel.Id); GL.ActiveTexture(TextureUnit.Texture5);
            GL.ActiveTexture(TextureUnit.Texture0);GL.BindTexture(TextureTarget.Texture2DArray,arraySentinel.TextureId);GL.ActiveTexture(TextureUnit.Texture5);
            GL.BindImageTexture(0,image.TextureId,0,false,0,TextureAccess.ReadOnly,SizedInternalFormat.R32f);
            int sampler=GL.GenSampler();GL.BindSampler(0,sampler);
            try
            {
            using var native=failedDispatch?new NativeDriverCapture():null;
            if(native is not null){native.Add("glDispatchCompute",new Dispatch((_,_,_)=>{}));native.Install();}
            using var assertions=new GpuImageAssertions();
            if(failedDispatch)Assert.Throws<InvalidOperationException>(()=>assertions.Compare(image,new float[] {1,2}));
            else assertions.Compare(image,new float[] {1,2}).AssertEqual();
            Assert.Equal(sentinel.Id,GL.GetInteger(GetPName.CurrentProgram)); Assert.Equal((int)TextureUnit.Texture5,GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(buffer,GL.GetInteger(GetPName.ShaderStorageBufferBinding)); Assert.Equal(buffer,GL.GetInteger(GetPName.PixelPackBufferBinding));
            Assert.Equal(7,GL.GetInteger(GetPName.PackRowLength)); Assert.Equal(1,GL.GetInteger(GetPName.PackSkipPixels));
            GL.GetInteger(GetIndexedPName.ShaderStorageBufferBinding,0,out int first); Assert.Equal(buffer,first);
            GL.GetInteger64(GetIndexedPName.ShaderStorageBufferStart,1,out long offset); Assert.Equal(alignment,offset);
            GL.GetInteger64(GetIndexedPName.ShaderStorageBufferSize,1,out long size); Assert.Equal(alignment,size);
            GL.GetInteger(GetIndexedPName.ShaderStorageBufferBinding,2,out int empty); Assert.Equal(0,empty);
            GL.GetInteger((GetIndexedPName)All.ImageBindingName,0,out int imageBinding);Assert.Equal(image.TextureId,imageBinding);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess,0,out int access);Assert.Equal((int)TextureAccess.ReadOnly,access);
            GL.GetInteger((GetIndexedPName)All.SamplerBinding,0,out int samplerBinding);Assert.Equal(sampler,samplerBinding);
            GL.ActiveTexture(TextureUnit.Texture0);Assert.Equal(arraySentinel.TextureId,GL.GetInteger(GetPName.TextureBinding2DArray));GL.ActiveTexture(TextureUnit.Texture5);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
            }
            finally {GL.BindSampler(0,0);GL.DeleteSampler(sampler);GL.BindImageTexture(0,0,0,false,0,TextureAccess.ReadOnly,SizedInternalFormat.R32f);}
        }
        finally
        {
            GL.UseProgram(0);GL.ActiveTexture(TextureUnit.Texture0);GL.BindTexture(TextureTarget.Texture2DArray,0);GL.BindBuffer(BufferTarget.PixelPackBuffer,0);
            GL.PixelStore(PixelStoreParameter.PackRowLength,0); GL.PixelStore(PixelStoreParameter.PackSkipPixels,0);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer,0,0);GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer,1,0);GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer,2,0);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer,0);GL.DeleteBuffer(buffer);
        }
    }

    #endregion

    #region Context ownership
    /// <summary>A foreign physical namespace cannot use or retire a live checker, and returning restores its use.</summary>
    [Fact]
    public unsafe void ForeignContextCannotAccessOrRetireAnOwner()
    {
        EnsureContextValid();
        using var image=Create(TextureDimension.Two,TextureFormat.R32f,1,1);Upload(image,new float[] {1},PixelComponentType.Float);
        using var assertions=new GpuImageAssertions();assertions.Inspect(image).AssertFiniteRange();
        GLFW.DefaultWindowHints();GLFW.WindowHint(WindowHintInt.ContextVersionMajor,4);GLFW.WindowHint(WindowHintInt.ContextVersionMinor,5);
        GLFW.WindowHint(WindowHintBool.Visible,false);
        Window* window=GLFW.CreateWindow(16,16,"Assertion context qualification",null,null);
        Assert.True(window!=null);object owner=new();
        try
        {
            GLFW.MakeContextCurrent(window);RenderContextRegistry.RegisterCurrent(owner,static _=>true);
            int foreign=GL.CreateProgram();
            try
            {
                Assert.Throws<InvalidOperationException>(()=>assertions.Inspect(image));
                Assert.Throws<InvalidOperationException>(assertions.Dispose);
                Assert.True(GL.IsProgram(foreign));
            }
            finally {GL.DeleteProgram(foreign);}
        }
        finally {RenderContextRegistry.Retire(owner);GLFW.DestroyWindow(window);fixture.MakeCurrent();}
        assertions.Inspect(image).AssertFiniteRange();
    }

    /// <summary>A constructor interruption defers its partial shader to the original service, never deleting foreign names.</summary>
    [Fact]
    public unsafe void InterruptedConstructionRetiresOnReturnToOrigin()
    {
        EnsureContextValid();
        bool createdService=RHIResourceManagerSystem.CurrentManager is null;
        var service=RHIResourceManagerSystem.EnsureCurrentContext();
        GLFW.DefaultWindowHints();GLFW.WindowHint(WindowHintInt.ContextVersionMajor,4);GLFW.WindowHint(WindowHintInt.ContextVersionMinor,5);
        GLFW.WindowHint(WindowHintBool.Visible,false);
        Window* window=GLFW.CreateWindow(16,16,"Partial construction qualification",null,null);
        Assert.True(window!=null);object foreignOwner=new();int foreignShader=0,partial=0;
        try
        {
            GLFW.MakeContextCurrent(window);RenderContextRegistry.RegisterCurrent(foreignOwner,static _=>true);
            foreignShader=GL.CreateShader(ShaderType.ComputeShader);fixture.MakeCurrent();
            var expected=new IOException("construction interrupted");
            Assert.Same(expected,Assert.Throws<IOException>(()=>new GpuFixtureProgram("unused",shader=>
            {
                partial=shader;GLFW.MakeContextCurrent(window);throw expected;
            })));
            Assert.True(GL.IsShader(foreignShader));
            fixture.MakeCurrent();Assert.True(GL.IsShader(partial));
            service.OnRenderFrame(0,RHIResourceManager.Stage);Assert.False(GL.IsShader(partial));
            GLFW.MakeContextCurrent(window);Assert.True(GL.IsShader(foreignShader));
        }
        finally
        {
            GLFW.MakeContextCurrent(window);if(foreignShader!=0)GL.DeleteShader(foreignShader);
            RenderContextRegistry.Retire(foreignOwner);GLFW.DestroyWindow(window);fixture.MakeCurrent();
            if(createdService){RHIResourceManagerSystem.Shutdown();service.Dispose();}
        }
    }

    /// <summary>Retiring a real native namespace cannot delete recycled program names in the surviving context.</summary>
    [Fact]
    public unsafe void RetiredContextDropsNativeNamesWithoutForeignDeletion()
    {
        EnsureContextValid();
        GLFW.DefaultWindowHints();GLFW.WindowHint(WindowHintInt.ContextVersionMajor,4);GLFW.WindowHint(WindowHintInt.ContextVersionMinor,5);
        GLFW.WindowHint(WindowHintBool.Visible,false);
        Window* window=GLFW.CreateWindow(16,16,"Assertion retirement qualification",null,null);
        Assert.True(window!=null);object owner=new();GpuFixtureProgram? retired=null;
        try
        {
            GLFW.MakeContextCurrent(window);RenderContextRegistry.RegisterCurrent(owner,static _=>true);
            retired=new GpuFixtureProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        }
        finally {RenderContextRegistry.Retire(owner);GLFW.DestroyWindow(window);fixture.MakeCurrent();}
        using var surviving=new GpuFixtureProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        retired!.Dispose();Assert.True(GL.IsProgram(surviving.Id));retired.Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates a fresh native subject for each numeric/domain contract.</summary>
    private static GpuTexture Create(TextureDimension dimension,TextureFormat format,int width,int height,int depth=1,int levels=1)
    {
        return RHICommandContext.Current.CreateTexture(new RHITextureDescription {Dimension=dimension,Format=format,
            Width=width,Height=height,Depth=dimension==TextureDimension.Cube?1:depth,MipLevels=levels,Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest}).RequireResource();
    }

    /// <summary>Uploads authored values through the production transfer boundary, with no readback-derived expectation.</summary>
    private static void Upload(GpuTexture image,float[] values,PixelComponentType type,int layer=0,int mip=0,int? width=null,int? height=null)
    {
        var encoding=RHICommandContext.Current.GetTextureEncoding(image,type);
        RHICommandContext.Current.UploadTexture(image,new(new(mip,layer),0,0,0,width??image.Width,height??image.Height),encoding,MemoryMarshal.AsBytes(values.AsSpan()));
    }

    /// <summary>Matches the native dispatch ABI for deliberate no-execution fault injection.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Dispatch(uint x,uint y,uint z);
    #endregion
}
