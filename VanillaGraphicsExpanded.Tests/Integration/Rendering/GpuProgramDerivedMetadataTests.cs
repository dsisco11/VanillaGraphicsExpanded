using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks program-owned derived interfaces against canonical metadata updates.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed partial class GpuProgramDerivedMetadataTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Assignment caches
    /// <summary>Legacy caches reflect actual native sampler and block assignments after manager publication.</summary>
    [Fact]
    public void LegacyBindingCacheUsesPublishedAssignments()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            // Act
            OpenGLProgramAssignments.SetUnit(program.ProgramId,location,5);
            // Assert
            Assert.Equal(5,program.BindingCache.SamplerBindings["inputs"]);
            int index=GL.GetUniformBlockIndex(program.ProgramId,"Parameters");
            // Act: update the block binding after publishing the sampler unit.
            OpenGLProgramAssignments.UniformBlockBinding(program.ProgramId,index,4);
            // Assert: both assignments are reflected by the legacy view.
            Assert.True(program.BindingCache.TryGetUniformBlockBinding("Parameters",out int binding)); Assert.Equal(4,binding);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { DrainGlErrors(); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>An equal canonical sampler assignment preserves prepared identity and revisions without reflection.</summary>
    [Fact]
    public void EqualSamplerAssignmentPreservesPreparedArrayWithoutReflection()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var layout=PrepareArrayLayout(program); var before=layout.BinaryInterface!.PreparedBindings;
            var entry=RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(program)!;
            ulong revision=entry.MetadataRevision;
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            using var acquisition = new ProgramAcquisitionCapture();
            var nativeRefresh=Patch(nameof(SetUnits));
            // Act
            try { SetUnits(3,program.ProgramId,location,0); } finally { nativeRefresh.UnpatchAll(nativeRefresh.Id); }
            var after=layout.BinaryInterface!.PreparedBindings;
            // Assert
            Assert.Same(before,after);
            Assert.Equal(revision,entry.MetadataRevision);
            GL.GetUniform(program.ProgramId,location,out int unit); Assert.Equal(0,unit);
            Assert.Equal(0,acquisition.Acquisitions);
            Assert.False(program.NeedsNativePreparation);
            var binding=after.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"inputs")); Assert.True(binding.Active); Assert.Equal(2,binding.Contract.Binding.ArrayLength);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { DrainGlErrors(); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>A contract-breaking native assignment withdraws prepared access while all aliases receive actual facts.</summary>
    [Fact]
    public void InvalidContractWithdrawsPreparedTableAndPublishesAliases()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var layout=PrepareArrayLayout(program);
            layout.RebuildCache(program.ProgramId);
            Assert.NotEmpty(layout.SamplerBindings);
            Assert.NotEmpty(layout.UniformBlockBindings);
            using var alias=new ProgramAlias(program.ProgramId); RHIResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            // Act
            Assert.ThrowsAny<Exception>(()=>OpenGLProgramAssignments.SetUnit(program.ProgramId,location,5));
            // Assert
            Assert.True(program.NeedsNativePreparation); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
            Assert.Empty(layout.SamplerBindings);
            Assert.Empty(layout.UniformBlockBindings);
            Assert.Contains(alias.Snapshot.TextureUnits,item=>item.Location==location && item.Unit==5);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { DrainGlErrors(); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Executable withdrawal and recovery
    /// <summary>Relinking requires owner preparation even when later unit updates have a valid resource contract.</summary>
    [Fact]
    public void ExecutableWithdrawalSurvivesResourceRefresh()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var program=CreateProgram(out int shader,true);
            try
            {
                var layout=PrepareArrayLayout(program);
                // Act
                GL.LinkProgram(program.ProgramId);
                // Assert
                Assert.True(program.NeedsNativePreparation); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
                int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
                // Act: assign a unit while the executable remains withdrawn.
                OpenGLProgramAssignments.SetUnit(program.ProgramId,location,0);
                Assert.True(program.NeedsNativePreparation); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
                Assert.Equal(ErrorCode.NoError,GL.GetError());
            }
            finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); }
        }
        finally { DrainGlErrors(); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Failed executable metadata acquisition retains the requirement to reprepare after recovery.</summary>
    [Fact]
    public void ExecutableReaderFailureCannotRevivePreparedTable()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var program = CreateProgram(out int shader, true);
            try
            {
                var layout=PrepareArrayLayout(program); var manager=RHIResourceManagerSystem.CurrentManager!;
                var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
                var reader=entry.Reader; entry.Reader=new UnavailableProgramReader();
                // Act
                GL.LinkProgram(program.ProgramId);
                // Assert
                Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
                // Act: restore the reader and refresh metadata for the linked executable.
                entry.Reader=reader; Assert.True(manager.RefreshMetadata(entry,GpuMetadataChange.None));
                // Assert: metadata recovery still cannot revive the prepared table.
                Assert.True(program.NeedsNativePreparation); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
                Assert.Equal(ErrorCode.NoError,GL.GetError());
            }
            finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); }
        }
        finally { DrainGlErrors(); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>An equal canonical image assignment retains prepared identity and revisions without reflection.</summary>
    [Fact]
    public void EqualImageAssignmentPreservesPublishedUnits()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); int shader=GL.CreateShader(ShaderType.ComputeShader),native=0;
        try
        {
            GL.ShaderSource(shader,"#version 430\nlayout(local_size_x=1) in; layout(rgba32f,binding=3) readonly uniform image2D images[2]; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){ result=imageLoad(images[0],ivec2(0))+imageLoad(images[1],ivec2(0)); }");
            GL.CompileShader(shader); native=GL.CreateProgram(); GL.AttachShader(native,shader); GL.LinkProgram(native);
            GL.GetProgram(native,GetProgramParameterName.LinkStatus,out int linked); Assert.Equal(1,linked);
            using var program=GpuProgramObject.Adopt(native); native=0;
            var manager=RHIResourceManagerSystem.CurrentManager!; var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
            var contract=new GpuBindingContract(); contract.Images.Add("images",new GpuBindingContract.Binding(3,true,2)); contract.RegisterShaderStorageBlockBinding("Output",0);
            var layout=new GpuProgramLayout { BinaryInterface=new GpuProgramInterface(program.ProgramId,new[]{contract},metadata:entry.Metadata) }; layout.RegisterContract(contract); program.SetPreparedLayout(layout);
            var before=layout.BinaryInterface!.PreparedBindings; int location=GL.GetUniformLocation(program.ProgramId,"images[0]");
            ulong revision=entry.MetadataRevision;
            using var acquisition = new ProgramAcquisitionCapture();
            var nativeRefresh=Patch(nameof(SetUnits));
            // Act
            try { SetUnits(3,program.ProgramId,location+1,4); } finally { nativeRefresh.UnpatchAll(nativeRefresh.Id); }
            // Assert
            Assert.Same(before,layout.BinaryInterface!.PreparedBindings);
            Assert.Equal(revision,entry.MetadataRevision);
            GL.GetUniform(program.ProgramId,location+1,out int unit); Assert.Equal(4,unit);
            Assert.Equal(0,acquisition.Acquisitions);
            Assert.True(layout.BinaryInterface.PreparedBindings.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Image,"images")).Active); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { DrainGlErrors(); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); if(native!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, native)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Typed links maintain context-local snapshots and recover from failure without a global manager.</summary>
    [Fact]
    public void ContextLocalTypedLinkRecoversMetadata()
    {
        // Arrange
        fixture.EnsureContextValid(); RHIResourceManagerSystem.Shutdown(); int shader=0;
        try
        {
            using var program=CreateProgram(out shader,true);
            // Act: establish the initial executable and its canonical binding view.
            Assert.True(NativeProgramFixture.Link(program, out _));
            // Assert: successful linking publishes the sampler binding.
            Assert.True(program.BindingCache.TryGetSamplerUnit("inputs",out _));
            // Act: fail a relink, then restore valid source and recover the same program.
            GL.ShaderSource(shader,"invalid shader"); GL.CompileShader(shader); Assert.False(NativeProgramFixture.Link(program, out _)); Assert.True(program.NeedsNativePreparation);
            // Act: recompile valid source and relink the original object.
            GL.ShaderSource(shader,Source.Replace("uniform int numericValue;","").Replace("+vec4(numericValue)","")); GL.CompileShader(shader);
            Assert.True(NativeProgramFixture.Link(program, out _)); Assert.False(program.NeedsNativePreparation); Assert.True(program.BindingCache.TryGetSamplerUnit("inputs",out _)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { DrainGlErrors(); if(shader!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Clears pending native errors during failure cleanup without replacing the test's original assertion.</summary>
    private static void DrainGlErrors()
    {
        while (GL.GetError() != ErrorCode.NoError) { }
    }

    /// <summary>Receives the program's canonical metadata without taking native deletion ownership.</summary>
    private sealed class ProgramAlias(int id) : GpuResource<GpuProgramMetadata>
    {
        #region Public API
        /// <summary>Returns the current manager-published metadata snapshot.</summary>
        public GpuProgramMetadata Snapshot => metadata;

        /// <inheritdoc />
        public override nint ResourceId { get; protected set; } = id;

        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Program;

        /// <inheritdoc />
        protected override bool OwnsResource => false;

        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
        #endregion
    }

    /// <summary>Models unsuccessful native metadata acquisition without changing driver error state.</summary>
    private sealed class UnavailableProgramReader:IGpuResourceMetadataReader<GpuProgramMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public bool IsResource(nint id)=>true;
        /// <inheritdoc />
        public bool TryRead(nint id,out GpuProgramMetadata value) { value=default; return false; }
        #endregion
    }
    /// <summary>Prepares an authored sampler-array contract from the manager's actual canonical snapshot.</summary>
    private static GpuProgramLayout PrepareArrayLayout(GpuProgramObject program)
    {
        int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]"); GL.ProgramUniform1(program.ProgramId,location,2,new[]{0,1});
        var manager=RHIResourceManagerSystem.CurrentManager!; var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
        Assert.True(manager.RefreshMetadata(entry,GpuMetadataChange.Metadata));
        var contract=new GpuBindingContract(); contract.Samplers.Add("inputs",new GpuBindingContract.Binding(0,true,2)); contract.RegisterUniformBlockBinding("Parameters",0); contract.RegisterShaderStorageBlockBinding("Output",0);
        var layout=new GpuProgramLayout { BinaryInterface=new GpuProgramInterface(program.ProgramId,new[]{contract},metadata:entry.Metadata) };
        layout.RegisterContract(contract); program.SetPreparedLayout(layout); return layout;
    }
    /// <summary>Compiles and owns the small sampler-array program used by this metadata suite.</summary>
    private static GpuProgramObject CreateProgram(out int shader,bool prepared=false)
    {
        const string source="#version 430\nlayout(local_size_x=1) in; uniform sampler2D inputs[2]; uniform int numericValue; layout(std140,binding=0) uniform Parameters { vec4 parameter; }; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){ result=texelFetch(inputs[0],ivec2(0),0)+texelFetch(inputs[1],ivec2(0),0)+vec4(numericValue)+parameter; }";
        shader=0;
        int program=0;
        int candidateShader=0;
        try
        {
            candidateShader=GL.CreateShader(ShaderType.ComputeShader);
            GL.ShaderSource(candidateShader,prepared?source.Replace("uniform int numericValue;","").Replace("+vec4(numericValue)",""):source);
            GL.CompileShader(candidateShader);
            GL.GetShader(candidateShader,ShaderParameter.CompileStatus,out int compiled);
            Assert.Equal(1,compiled);
            program=GL.CreateProgram();
            GL.AttachShader(program,candidateShader);
            GL.LinkProgram(program);
            GL.GetProgram(program,GetProgramParameterName.LinkStatus,out int linked);
            Assert.Equal(1,linked);
            var owner=GpuProgramObject.Adopt(program);
            shader=candidateShader;
            program=0;
            candidateShader=0;
            return owner;
        }
        catch
        {
            if(program!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program,program));
            if(candidateShader!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader,candidateShader));
            throw;
        }
    }
    /// <summary>Creates the unique interception owner used by the existing program metadata call sites.</summary>
    private static Harmony Patch(string method) => new("VGE.Tests.ProgramMetadata."+method);
    private const string Source="#version 430\nlayout(local_size_x=1) in; uniform sampler2D inputs[2]; uniform int numericValue; layout(std140,binding=0) uniform Parameters { vec4 parameter; }; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){ result=texelFetch(inputs[0],ivec2(0),0)+texelFetch(inputs[1],ivec2(0),0)+vec4(numericValue)+parameter; }";
    /// <summary>Preserves each native integer setter overload as an interception call site.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void SetUnits(int variant,int program,int location,int value)
    {
        switch(variant)
        {
            case 0: GL.Uniform1(location,value); break;
            case 1: GL.Uniform1(location,1,new[]{value}); break;
            case 2: GL.Uniform1(location,1,ref value); break;
            case 3: GL.ProgramUniform1(program,location,value); break;
            case 4: GL.ProgramUniform1((uint)program,location,1,new[]{value}); break;
            case 5: GL.ProgramUniform1(program,location,1,&value); break;
            case 6: GL.Ext.ProgramUniform1(program,location,value); break;
            case 7: GL.Ext.ProgramUniform1(program,location,1,ref value); break;
        }
    }
    #endregion
}





