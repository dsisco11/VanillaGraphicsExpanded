using VanillaGraphicsExpanded.PBR;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies deferred executable publication retains one manager-owned allocation and supplied interface snapshot.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuProgramCandidateMetadataTests(HeadlessGLFixture fixture)
{
    private static int statusQueries, validityQueries, interfaceQueries;
    #region Public API
    /// <summary>Pending creation defers native interface reads and ready preparation reuses the canonical executable snapshot.</summary>
    [Fact]
    public void SubmissionDefersQueriesAndPreparationReusesSnapshot()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int shader=GL.CreateShader(ShaderType.ComputeShader); var harmony=new Harmony("VGE.Tests.ProgramCandidateQueries");
        try
        {
            GL.ShaderSource(shader,"#version 430\nlayout(local_size_x=1) in; void main(){}"); GL.CompileShader(shader);
            harmony.Patch(AccessTools.Method(typeof(GL),nameof(GL.GetProgram),new[]{typeof(int),typeof(GetProgramParameterName),typeof(int).MakeByRefType()}),prefix:new HarmonyMethod(typeof(GpuProgramCandidateMetadataTests),nameof(CountStatus)));
            harmony.Patch(AccessTools.Method(typeof(GL),nameof(GL.IsProgram),new[]{typeof(int)}),prefix:new HarmonyMethod(typeof(GpuProgramCandidateMetadataTests),nameof(CountValidity)));
            harmony.Patch(AccessTools.Method(typeof(GL),nameof(GL.GetProgramInterface),new[]{typeof(int),typeof(ProgramInterface),typeof(ProgramInterfaceParameter),typeof(int).MakeByRefType()}),prefix:new HarmonyMethod(typeof(GpuProgramCandidateMetadataTests),nameof(CountInterface)));
            statusQueries=validityQueries=interfaceQueries=0;
            using var candidate=ShaderProgramLink.Submit(new[]{shader},false
#if DEBUG
                , out _
#endif
                );
            Assert.Equal(0,statusQueries); Assert.Equal(0,validityQueries); Assert.Equal(0,interfaceQueries);
            var manager=GpuResourceManagerSystem.CurrentManager!; var entry=manager.GetResourceRegistration(candidate)!;
            Assert.False(manager.IsResourceMetadataAvailable(candidate));
            Assert.True(ShaderProgramLink.Validate(candidate,out _)); Assert.True(statusQueries>0); Assert.True(interfaceQueries>0);
            Assert.Same(entry,manager.GetResourceRegistration(candidate)); Assert.True(manager.IsResourceMetadataAvailable(candidate));
            statusQueries=validityQueries=interfaceQueries=0;
            var prepared=new GpuProgramInterface(candidate.ProgramId,new[]{new GpuBindingContract()});
#if DEBUG
            Assert.Empty(prepared.PreparedBindings.Entries);
#endif
            Assert.Equal(0,statusQueries);
            Assert.Equal(0,validityQueries);
            Assert.Equal(0,interfaceQueries);
            GL.IsProgram(candidate.ProgramId); Assert.Equal(1,validityQueries); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); StateCache.Current.DeleteShader(shader); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Ready transfer retains its original program registration and releases all temporary stages.</summary>
    [Fact]
    public void PendingTransferPreservesRegistration()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var store=DriverProgramCache.UseStoreForTesting(null); using var assets=new BinaryShaderApiFixture();
        try
        {
            var settings=new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
            using var pending=new PendingShaderProgram(new ShaderLoadPlan(settings),Read,()=>ShaderDigestIndexCache.ForAssets(assets.Api.Assets,ShaderImportsSystem.DefaultDomain),GetProgramParameterName.LinkStatus);
            int id=pending.Program; var manager=GpuResourceManagerSystem.CurrentManager!; var entry=manager.FindResource(GpuResourceKind.Program,id); Assert.NotNull(entry);
            int[] stages=pending.Stages.Select(stage=>stage.Shader).ToArray(); Assert.NotEmpty(stages);
            pending.Complete(TestContext.Current.CancellationToken); using var owner=pending.TakeProgram();
            Assert.Equal(id,owner.ProgramId); Assert.Same(entry,manager.GetResourceRegistration(owner)); Assert.Equal(0,pending.Program);
            pending.Dispose(); pending.Dispose(); Assert.True(GL.IsProgram(id)); Assert.All(stages,stage=>Assert.False(GL.IsShader(stage))); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Worker cancellation queues cleanup against the originating manager rather than issuing GL without its context.</summary>
    [Fact]
    public void PendingDisposalFromWorkerDefersNativeRetirement()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var store=DriverProgramCache.UseStoreForTesting(null); using var assets=new BinaryShaderApiFixture();
        try
        {
            var settings=new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
            using var pending=new PendingShaderProgram(new ShaderLoadPlan(settings),Read,()=>ShaderDigestIndexCache.ForAssets(assets.Api.Assets,ShaderImportsSystem.DefaultDomain),GetProgramParameterName.LinkStatus);
            int id=pending.Program; int[] stages=pending.Stages.Select(stage=>stage.Shader).ToArray();
            Exception? disposalFailure=null;
            var worker=new Thread(()=> { try { pending.Dispose(); pending.Dispose(); } catch(Exception error) { disposalFailure=error; } });
            worker.Start(); worker.Join(); Assert.Null(disposalFailure);
            Assert.Equal(0,pending.Program); Assert.True(GL.IsProgram(id)); Assert.All(stages,stage=>Assert.True(GL.IsShader(stage)));
            GpuResourceManagerSystem.CurrentManager!.OnRenderFrame(0,GpuResourceManager.Stage);
            Assert.False(GL.IsProgram(id)); Assert.All(stages,stage=>Assert.False(GL.IsShader(stage))); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Reads the already-built fixture shader artifact.</summary>
    private static ReadOnlySpan<byte> Read(string path)=>File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"assets","shaders",path));
    /// <summary>Counts status and descriptor queries at the exact native boundary.</summary>
    private static void CountStatus()=>statusQueries++;
    /// <summary>Counts native object validity checks.</summary>
    private static void CountValidity()=>validityQueries++;
    /// <summary>Counts native interface enumerations.</summary>
    private static void CountInterface()=>interfaceQueries++;
    #endregion
}
