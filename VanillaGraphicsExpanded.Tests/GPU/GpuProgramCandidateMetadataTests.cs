using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Linq;
using System.Threading;
using HarmonyLib;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
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
        Start();
        var harmony = new Harmony("VGE.Tests.RHIProgramCandidateQueries");
        try
        {
            var description = RHIShaderInputs.ComputeDescription();
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.GetProgram), [typeof(int), typeof(GetProgramParameterName), typeof(int).MakeByRefType()]),
                prefix: new HarmonyMethod(typeof(GpuProgramCandidateMetadataTests), nameof(CountStatus)));
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.IsProgram), [typeof(int)]),
                prefix: new HarmonyMethod(typeof(GpuProgramCandidateMetadataTests), nameof(CountValidity)));
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.GetProgramInterface), [typeof(int), typeof(ProgramInterface), typeof(ProgramInterfaceParameter), typeof(int).MakeByRefType()]),
                prefix: new HarmonyMethod(typeof(GpuProgramCandidateMetadataTests), nameof(CountInterface)));
            statusQueries = validityQueries = interfaceQueries = 0;
            using var request = RHICommandContext.Current.BeginCreateShader(description);
            if (GpuSupport.Graphics.SupportsArbParallelShaderCompile || GpuSupport.Graphics.SupportsKhrParallelShaderCompile)
            {
                Assert.Equal(0, statusQueries);
                Assert.Equal(0, interfaceQueries);
            }
            Complete(request);
            using var shader = request.Result.RequireResource();
            Assert.True(statusQueries > 0);
            Assert.True(interfaceQueries > 0);
            statusQueries = validityQueries = interfaceQueries = 0;
            shader.RequireUsable();
            var prepared = new GpuProgramInterface(shader.Program.ProgramId, [description.Bindings], metadata: shader.Program.MetadataSnapshot);
            Assert.Equal(shader.Interface.PreparedBindings.Entries.Count, prepared.PreparedBindings.Entries.Count);
            Assert.Equal(0, statusQueries);
            Assert.Equal(0, validityQueries);
            Assert.Equal(0, interfaceQueries);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); Finish(); }
    }

    /// <summary>Ready transfer retains its original program registration and releases all temporary stages.</summary>
    [Fact]
    public void PendingTransferPreservesRegistration()
    {
        Start();
        try
        {
            using var capture = new NativeShaderAllocationCapture();
            using var request = RHICommandContext.Current.BeginCreateShader(RHIShaderInputs.ComputeDescription());
            int id = capture.Program;
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = capture.Registration;
            Assert.NotNull(entry);
            int[] stages = capture.Stages;
            Complete(request);
            using var shader = request.Result.RequireResource();
            Assert.Equal(id, shader.Program.ProgramId);
            Assert.Same(entry, manager.GetResourceRegistration(shader.Program));
            request.Dispose(); request.Dispose();
            Assert.True(GL.IsProgram(id));
            Assert.All(stages, stage => Assert.False(GL.IsShader(stage)));
            GL.GetProgram(id, GetProgramParameterName.AttachedShaders, out int attached);
            Assert.Equal(0, attached);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Worker cancellation queues cleanup against the originating manager rather than issuing GL without its context.</summary>
    [Fact]
    public void PendingDisposalFromWorkerDefersNativeRetirement()
    {
        Start();
        try
        {
            using var capture = new NativeShaderAllocationCapture();
            using var request = RHICommandContext.Current.BeginCreateShader(RHIShaderInputs.ComputeDescription());
            var ready = request.Status == CompletionStatus.Ready ? request.Result.RequireResource() : null;
            int id = capture.Program;
            int[] stages = capture.Stages;
            Exception? failure = null;
            var worker = new Thread(() =>
            {
                try { if (ready is null) request.Dispose(); else ready.Dispose(); }
                catch (Exception error) { failure = error; }
            });
            worker.Start(); worker.Join();
            Assert.Null(failure);
            GpuResourceManagerSystem.CurrentManager!.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.False(GL.IsProgram(id));
            Assert.All(stages, stage => Assert.False(GL.IsShader(stage)));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    #endregion

    #region Private
    /// <summary>Waits on the specific pending candidate only until its ready or failed terminal outcome.</summary>
    private static void Complete(RHIPendingShaderCreation request)
    {
        while (request.TryComplete() == CompletionStatus.Pending)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            Thread.Yield();
        }
        Assert.Equal(CompletionStatus.Ready, request.Status);
    }
    /// <summary>Starts a clean manager interval for candidate ownership assertions.</summary>
    private void Start() { fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); OpenGlStateCache.Current.InvalidateAll(); }
    /// <summary>Retires all original fixture resources and clears selector knowledge.</summary>
    private void Finish() { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    /// <summary>Counts exact native program status reads.</summary>
    private static void CountStatus() => statusQueries++;
    /// <summary>Counts exact native validity checks.</summary>
    private static void CountValidity() => validityQueries++;
    /// <summary>Counts exact native interface enumerations.</summary>
    private static void CountInterface() => interfaceQueries++;
    #endregion
}
