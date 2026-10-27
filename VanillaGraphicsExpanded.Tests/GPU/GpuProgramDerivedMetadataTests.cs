using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks program-owned derived interfaces against canonical metadata updates.</summary>
public sealed partial class GpuProgramMetadataInterceptionTests
{
    #region Public API
    /// <summary>Legacy caches reflect actual native sampler and block assignments after manager publication.</summary>
    [Fact]
    public void LegacyBindingCacheUsesPublishedAssignments()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            OpenGLProgramAssignments.SetUnit(program.ProgramId,location,5);
            Assert.Equal(5,program.BindingCache.SamplerBindings["inputs"]);
            int index=GL.GetUniformBlockIndex(program.ProgramId,"Parameters");
            OpenGLProgramAssignments.UniformBlockBinding(program.ProgramId,index,4);
            Assert.True(program.BindingCache.TryGetUniformBlockBinding("Parameters",out int binding)); Assert.Equal(4,binding);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Valid canonical refresh replaces prepared table identity without performing independent reflection.</summary>
    [Fact]
    public void CanonicalRefreshRebuildsPreparedArrayWithoutReflection()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var layout=PrepareArrayLayout(program); var before=layout.BinaryInterface!.PreparedBindings;
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            using var acquisition = new Fixtures.ProgramAcquisitionCapture();
            var nativeRefresh=Patch(nameof(SetUnits));
            try { SetUnits(3,program.ProgramId,location,0); } finally { nativeRefresh.UnpatchAll(nativeRefresh.Id); }
            var after=layout.BinaryInterface!.PreparedBindings;
            Assert.NotSame(before,after);
Assert.Equal(0,acquisition.Acquisitions);
            Assert.False(program.NeedsNativePreparation);
            var binding=after.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"inputs")); Assert.True(binding.Active); Assert.Equal(2,binding.Contract.Binding.ArrayLength);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>A contract-breaking native assignment withdraws prepared access while all aliases receive actual facts.</summary>
    [Fact]
    public void InvalidContractWithdrawsPreparedTableAndPublishesAliases()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var layout=PrepareArrayLayout(program);
            using var alias=new ProgramAlias(program.ProgramId); GpuResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            Assert.ThrowsAny<Exception>(()=>OpenGLProgramAssignments.SetUnit(program.ProgramId,location,5));
            Assert.True(program.NeedsNativePreparation); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
            Assert.Contains(alias.Snapshot.TextureUnits,item=>item.Location==location && item.Unit==5);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Relinking requires owner preparation even when later unit updates have a valid resource contract.</summary>
    [Fact]
    public void ExecutableWithdrawalSurvivesResourceRefresh()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var program=CreateProgram(out int shader,true);
            try
            {
                var layout=PrepareArrayLayout(program);
                OpenGLProgramCalls.ExecuteCoreLinkProgram(program.ProgramId);
                Assert.True(program.NeedsNativePreparation); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
                int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
                OpenGLProgramAssignments.SetUnit(program.ProgramId,location,0);
                Assert.True(program.NeedsNativePreparation); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
                Assert.Equal(ErrorCode.NoError,GL.GetError());
            }
            finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); }
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Failed executable metadata acquisition retains the requirement to reprepare after recovery.</summary>
    [Fact]
    public void ExecutableReaderFailureCannotRevivePreparedTable()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var program=CreateProgram(out int shader,true);
            try
            {
                var layout=PrepareArrayLayout(program); var manager=GpuResourceManagerSystem.CurrentManager!;
                var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
                var reader=entry.Reader; entry.Reader=new UnavailableProgramReader();
                Assert.Throws<InvalidOperationException>(()=>OpenGLProgramCalls.ExecuteCoreLinkProgram(program.ProgramId));
                Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
                entry.Reader=reader; Assert.True(manager.RefreshMetadata(entry,GpuMetadataChange.None));
                Assert.True(program.NeedsNativePreparation); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
                Assert.Equal(ErrorCode.NoError,GL.GetError());
            }
            finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); }
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Image arrays rebuild from canonical unit assignments without additional native reflection.</summary>
    [Fact]
    public void CanonicalImageArrayRefreshUsesPublishedUnits()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); int shader=GL.CreateShader(ShaderType.ComputeShader),native=0;
        try
        {
            GL.ShaderSource(shader,"#version 430\nlayout(local_size_x=1) in; layout(rgba32f,binding=3) readonly uniform image2D images[2]; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){ result=imageLoad(images[0],ivec2(0))+imageLoad(images[1],ivec2(0)); }");
            GL.CompileShader(shader); native=GL.CreateProgram(); GL.AttachShader(native,shader); GL.LinkProgram(native);
            GL.GetProgram(native,GetProgramParameterName.LinkStatus,out int linked); Assert.Equal(1,linked);
            using var program=GpuProgramObject.Adopt(native); native=0;
            var manager=GpuResourceManagerSystem.CurrentManager!; var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
            var contract=new GpuBindingContract(); contract.Images.Add("images",new GpuBindingContract.Binding(3,true,2)); contract.RegisterShaderStorageBlockBinding("Output",0);
            var layout=new GpuProgramLayout { BinaryInterface=new GpuProgramInterface(program.ProgramId,new[]{contract},metadata:entry.Metadata) }; layout.RegisterContract(contract); program.SetPreparedLayout(layout);
            var before=layout.BinaryInterface!.PreparedBindings; int location=GL.GetUniformLocation(program.ProgramId,"images[0]");
            using var acquisition = new Fixtures.ProgramAcquisitionCapture();
            var nativeRefresh=Patch(nameof(SetUnits));
            try { SetUnits(3,program.ProgramId,location+1,4); } finally { nativeRefresh.UnpatchAll(nativeRefresh.Id); }
            Assert.NotSame(before,layout.BinaryInterface!.PreparedBindings);
Assert.Equal(0,acquisition.Acquisitions);
            Assert.True(layout.BinaryInterface.PreparedBindings.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Image,"images")).Active); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); if(native!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, native)); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Typed links maintain context-local snapshots and recover from failure without a global manager.</summary>
    [Fact]
    public void ContextLocalTypedLinkRecoversMetadata()
    {
        fixture.EnsureContextValid(); GpuResourceManagerSystem.Shutdown(); int shader=0;
        try
        {
            using var program=CreateProgram(out shader,true);
            Assert.True(NativeProgramFixture.Link(program, out _)); Assert.True(program.BindingCache.TryGetSamplerUnit("inputs",out _));
            GL.ShaderSource(shader,"invalid shader"); GL.CompileShader(shader); Assert.False(NativeProgramFixture.Link(program, out _)); Assert.True(program.NeedsNativePreparation);
            GL.ShaderSource(shader,Source.Replace("uniform int numericValue;","").Replace("+vec4(numericValue)","")); GL.CompileShader(shader);
            Assert.True(NativeProgramFixture.Link(program, out _)); Assert.False(program.NeedsNativePreparation); Assert.True(program.BindingCache.TryGetSamplerUnit("inputs",out _)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { if(shader!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); StateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Models unsuccessful native metadata acquisition without changing driver error state.</summary>
    private sealed class UnavailableProgramReader:IGpuResourceMetadataReader<GpuProgramMetadata>
    {
        /// <inheritdoc />
        public bool IsResource(nint id)=>true;
        /// <inheritdoc />
        public bool TryRead(nint id,out GpuProgramMetadata value) { value=default; return false; }
    }
    /// <summary>Prepares an authored sampler-array contract from the manager's actual canonical snapshot.</summary>
    private static GpuProgramLayout PrepareArrayLayout(GpuProgramObject program)
    {
        int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]"); GL.ProgramUniform1(program.ProgramId,location,2,new[]{0,1});
        var manager=GpuResourceManagerSystem.CurrentManager!; var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
        Assert.True(manager.RefreshMetadata(entry,GpuMetadataChange.Metadata));
        var contract=new GpuBindingContract(); contract.Samplers.Add("inputs",new GpuBindingContract.Binding(0,true,2)); contract.RegisterUniformBlockBinding("Parameters",0); contract.RegisterShaderStorageBlockBinding("Output",0);
        var layout=new GpuProgramLayout { BinaryInterface=new GpuProgramInterface(program.ProgramId,new[]{contract},metadata:entry.Metadata) };
        layout.RegisterContract(contract); program.SetPreparedLayout(layout); return layout;
    }
    #endregion
}





