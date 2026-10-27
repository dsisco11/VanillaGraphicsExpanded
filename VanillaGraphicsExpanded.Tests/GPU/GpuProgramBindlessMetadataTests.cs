using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
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
            PatchOpaqueBoundary(harmony,variant);
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var layout=PrepareArrayLayout(program); OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            using var alias=new ProgramAlias(program.ProgramId); GpuResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
            var manager=GpuResourceManagerSystem.CurrentManager!; var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
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
            PatchOpaqueBoundary(harmony,2); using var program=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            var manager=GpuResourceManagerSystem.CurrentManager!; var entry=manager.GetResourceRegistration(program)!; ulong revision=entry.MetadataRevision;
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
                var manager=GpuResourceManagerSystem.CurrentManager!;
                GpuProgramLayout layout;
                if(image)
                {
                    var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
                    var contract=new VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingContract(); contract.Images.Add("images",new(3,true,2)); contract.RegisterShaderStorageBlockBinding("Output",0);
                    layout=new GpuProgramLayout { BinaryInterface=new VanillaGraphicsExpanded.Rendering.Spirv.GpuProgramInterface(program.ProgramId,new[]{contract},metadata:entry.Metadata) }; layout.RegisterContract(contract); program.SetPreparedLayout(layout);
                }
                else layout=PrepareArrayLayout(program);
                using var alias=new ProgramAlias(program.ProgramId); GpuResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
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
    /// <summary>Installs a typed boundary capture so unsupported hardware entry points are never executed.</summary>
    private static unsafe void PatchOpaqueBoundary(Harmony harmony,int variant)
    {
        Type[] signature=variant switch { 0=>new[]{typeof(int),typeof(long)},1=>new[]{typeof(uint),typeof(int),typeof(ulong)},2=>new[]{typeof(int),typeof(int),typeof(long[])},_=>new[]{typeof(uint),typeof(int),typeof(int),typeof(ulong*)} };
        string name=variant is 0 or 2?"UniformHandle":"ProgramUniformHandle";
        string capture=variant switch {0=>nameof(CaptureSigned),1=>nameof(CaptureUnsigned),2=>nameof(CaptureArray),_=>nameof(CapturePointer)};
        harmony.Patch(AccessTools.Method(typeof(GL.Arb),name,signature),prefix:new HarmonyMethod(typeof(GpuProgramMetadataInterceptionTests),capture));
    }
    /// <summary>Leaves exact native signed, unsigned, array and pointer overloads for production transpilation.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void SetOpaqueHandle(int variant,int program,int location,int count,ulong bits)
    {
        if(variant==0) GL.Arb.UniformHandle(location,unchecked((long)bits));
        else if(variant==1) GL.Arb.ProgramUniformHandle((uint)program,location,bits);
        else if(variant==2) GL.Arb.UniformHandle(location,count,new[]{unchecked((long)bits)});
        else GL.Arb.ProgramUniformHandle((uint)program,location,count,&bits);
    }
    /// <summary>Captures scalar signed bits without executing a native handle command.</summary>
    private static bool CaptureSigned(long __1) { capturedHandle=unchecked((ulong)__1); capturedCount=1; return false; }
    /// <summary>Captures scalar unsigned bits without executing a native handle command.</summary>
    private static bool CaptureUnsigned(ulong __2) { capturedHandle=__2; capturedCount=1; return false; }
    /// <summary>Captures the count and managed vector payload without native dispatch.</summary>
    private static bool CaptureArray(int __1,long[] __2) { capturedCount=__1; capturedHandle=unchecked((ulong)__2[0]); return false; }
    /// <summary>Captures the pointer payload at its native width without native dispatch.</summary>
    private static unsafe bool CapturePointer(int __2,ulong* __3) { capturedCount=__2; capturedHandle=*__3; return false; }
    #endregion
}


