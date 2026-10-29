using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies exact opaque-handle forwarding and unavailable unit metadata without unsupported native dispatch.</summary>
public sealed partial class GpuProgramMetadataInterceptionTests
{
    private static ulong capturedHandle;
    private static int capturedCount;
    #region Public API
    /// <summary>Controlled native boundaries receive all 64 bits while the original unit snapshot remains withdrawn.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public unsafe void OpaqueHandleDispatchWithdrawsOriginalProgram(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SetOpaqueHandle));
        try
        {
            bool supported=Enumerable.Range(0,GL.GetInteger(GetPName.NumExtensions)).Any(index=>GL.GetString(StringNameIndexed.Extensions,index)=="GL_ARB_bindless_texture");
            output.WriteLine("Native GL_ARB_bindless_texture="+supported+"; this case uses a controlled native boundary, not native handle residency.");
            using var boundary = CaptureOpaqueBoundary(variant);
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var layout=PrepareArrayLayout(program); OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            using var alias=new ProgramAlias(program.ProgramId); RHIResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
            var manager=RHIResourceManagerSystem.CurrentManager!; var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
            var snapshot=entry.Metadata; int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            const ulong bits=0xFEDCBA9876543210UL; capturedHandle=0; capturedCount=-1;
            SetOpaqueHandle(variant,program.ProgramId,location,1,bits);
            Assert.Equal(bits,capturedHandle); Assert.Equal(1,capturedCount);
            Assert.False(manager.IsResourceMetadataAvailable(program)); Assert.False(manager.IsResourceMetadataAvailable(alias)); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
            Assert.False(manager.RefreshMetadata(entry,GpuMetadataChange.None)); Assert.False(manager.RefreshMetadata(entry,GpuMetadataChange.Metadata));
            Assert.Throws<InvalidOperationException>(()=>manager.PublishMetadata(entry,snapshot,GpuMetadataChange.Metadata));
            using var replacement=CreateProgram(out int replacementShader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, replacementShader));
            Assert.True(manager.IsResourceMetadataAvailable(replacement)); Assert.NotSame(entry,manager.GetResourceRegistration(replacement));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Ignored locations, empty vectors and ordinary numeric destinations do not withdraw unit metadata.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OpaqueHandleNonResourceDestinationsDoNotInvalidate(int mode)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SetOpaqueHandle));
        try
        {
            using var boundary = CaptureOpaqueBoundary(2); using var program=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            var manager=RHIResourceManagerSystem.CurrentManager!; var entry=manager.GetResourceRegistration(program)!; ulong revision=entry.MetadataRevision;
            int location=mode==0?-1:GL.GetUniformLocation(program.ProgramId,mode==1?"numericValue":"inputs[0]");
            SetOpaqueHandle(2,program.ProgramId,location,mode==2?0:1,ulong.MaxValue);
            Assert.True(manager.IsResourceMetadataAvailable(program)); Assert.Equal(revision,entry.MetadataRevision); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Real opaque sampler and image handles pass through native setters while withdrawing unit-based preparation.</summary>
    [Theory]
    [InlineData(false,0)] [InlineData(false,1)] [InlineData(false,2)] [InlineData(false,3)] [InlineData(true,0)] [InlineData(true,1)]
    public void NativeOpaqueHandleAssignmentWithdrawsUnits(bool image,int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SetOpaqueHandle)); int shader=0,native=0;
        try
        {
            bool supported=Enumerable.Range(0,GL.GetInteger(GetPName.NumExtensions)).Any(index=>GL.GetString(StringNameIndexed.Extensions,index)=="GL_ARB_bindless_texture");
            Assert.SkipUnless(supported,"GL_ARB_bindless_texture is unavailable.");
            GpuProgramObject program;
            if(image)
            {
                shader=GL.CreateShader(ShaderType.ComputeShader);
                GL.ShaderSource(shader,"#version 430\nlayout(local_size_x=1) in; layout(rgba32f,binding=3) readonly uniform image2D images[2]; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){ result=imageLoad(images[0],ivec2(0))+imageLoad(images[1],ivec2(0)); }");
                GL.CompileShader(shader); native=GL.CreateProgram(); GL.AttachShader(native,shader); GL.LinkProgram(native);
                GL.GetProgram(native,GetProgramParameterName.LinkStatus,out int linked); Assert.Equal(1,linked); program=GpuProgramObject.Adopt(native); native=0;
            }
            else program=CreateProgram(out shader,true);
            using(program)
            using(var texture=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource())
            {
                var manager=RHIResourceManagerSystem.CurrentManager!;
                GpuProgramLayout layout;
                if(image)
                {
                    var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
                    var contract=new VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingContract(); contract.Images.Add("images",new(3,true,2)); contract.RegisterShaderStorageBlockBinding("Output",0);
                    layout=new GpuProgramLayout { BinaryInterface=new VanillaGraphicsExpanded.Rendering.Spirv.GpuProgramInterface(program.ProgramId,new[]{contract},metadata:entry.Metadata) }; layout.RegisterContract(contract); program.SetPreparedLayout(layout);
                }
                else layout=PrepareArrayLayout(program);
                using var alias=new ProgramAlias(program.ProgramId); RHIResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
                ulong handle=unchecked((ulong)(image?GL.Arb.GetImageHandle((uint)texture.TextureId,0,false,0,(PixelFormat)SizedInternalFormat.Rgba32f):GL.Arb.GetTextureHandle((uint)texture.TextureId)));
                Assert.NotEqual(0UL,handle); Assert.Equal(ErrorCode.NoError,GL.GetError());
                output.WriteLine($"Native {(image?"image":"sampler")} handle=0x{handle:X16}; no residency needed for assignment/readback (no shader execution).");
                OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId); int location=GL.GetUniformLocation(program.ProgramId,image?"images[0]":"inputs[0]");
                SetOpaqueHandle(variant,program.ProgramId,location,1,handle); Assert.Equal(ErrorCode.NoError,GL.GetError());
                GL.Arb.GetUniform((uint)program.ProgramId,location,out ulong actual); Assert.Equal(handle,actual); Assert.Equal(ErrorCode.NoError,GL.GetError());
                Assert.False(manager.IsResourceMetadataAvailable(program)); Assert.False(manager.IsResourceMetadataAvailable(alias)); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
                using var replacement=CreateProgram(out int replacementShader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, replacementShader)); Assert.True(manager.IsResourceMetadataAvailable(replacement));
            }
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); if(shader!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); if(native!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, native)); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Substitutes a typed native oracle without requiring valid resident driver handles.</summary>
    private static unsafe NativeDriverCapture CaptureOpaqueBoundary(int variant)
    {
        var native = new NativeDriverCapture();
        switch (variant)
        {
            case 0:
                native.Add("glUniformHandleui64ARB", new BoundHandle((location, bits) =>
                    native.Observe(() => { capturedHandle = bits; capturedCount = 1; })));
                break;
            case 1:
                native.Add("glProgramUniformHandleui64ARB", new NamedHandle((program, location, bits) =>
                    native.Observe(() => { capturedHandle = bits; capturedCount = 1; })));
                break;
            case 2:
                native.Add("glUniformHandleui64vARB", new BoundHandles((location, count, bits) =>
                    native.Observe(() => { capturedCount = count; if (count > 0) capturedHandle = *bits; })));
                break;
            default:
                native.Add("glProgramUniformHandleui64vARB", new NamedHandles((program, location, count, bits) =>
                    native.Observe(() => { capturedCount = count; if (count > 0) capturedHandle = *bits; })));
                break;
        }
        native.Install();
        return native;
    }
    /// <summary>Matches scalar bound opaque assignment.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate void BoundHandle(int location, ulong bits);
    /// <summary>Matches scalar named opaque assignment.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate void NamedHandle(uint program, int location, ulong bits);
    /// <summary>Matches vector bound opaque assignment.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private unsafe delegate void BoundHandles(int location, int count, ulong* bits);
    /// <summary>Matches vector named opaque assignment.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private unsafe delegate void NamedHandles(uint program, int location, int count, ulong* bits);
    /// <summary>Leaves exact native signed, unsigned, array and pointer overloads through the production native-slot interception.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void SetOpaqueHandle(int variant,int program,int location,int count,ulong bits)
    {
        if(variant==0) GL.Arb.UniformHandle(location,unchecked((long)bits));
        else if(variant==1) GL.Arb.ProgramUniformHandle((uint)program,location,bits);
        else if(variant==2) GL.Arb.UniformHandle(location,count,new[]{unchecked((long)bits)});
        else GL.Arb.ProgramUniformHandle((uint)program,location,count,&bits);
    }
    #endregion
}


