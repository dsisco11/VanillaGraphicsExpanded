using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Exercises the production temporal debug reprojection helper with a perspective history matrix.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnTemporalDebugShaderTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared graphics context for the imported GLSL helper.</summary>
    public LumOnTemporalDebugShaderTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>The debug helper maps current-relative points into the previous frame with the rebased homogeneous W.</summary>
    [Fact]
    public void ImportedDebugHelperUsesRebasedPerspectiveHistory()
    {
        // Arrange a translated camera history and capture the exact frame inputs consumed by the helper.
        EnsureContextValid();
        float[] matrix = [2,0,0,0, 0,3,0,0, 0,0,-1.02f,-1, 0,0,-.202f,0];
        var history = new FrameCameraHistory();
        history.Capture(matrix, 16777216.25, 32, -16777216.25); history.Commit();
        history.Capture(matrix, 16777216.375, 32.0625, -16777216.125);
        using var inputs = new VgeFrameUniformBuffer();
        float[] identity = LumOnTestInputFactory.CreateIdentityMatrix();
        inputs.Capture(identity, identity, identity, identity, history.PreviousViewProjection, identity,
            new System.Numerics.Vector2(1, 1), 0, 1, System.Numerics.Vector3.Zero,
            System.Numerics.Vector3.Zero, 0);

        var commands = RHICommandContext.Current;
        using var output = commands.CreateShaderStorageBuffer(new RHIBufferDescription
        {
            Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 8,
            Usage = BufferUsage.DynamicRead, Label = "Tests.LumOnTemporalDebug.Result"
        }).RequireResource();
        using var programs = new ComponentShaderPrograms();
        var program = programs.CreateCompute(api => TemporalDebugComputeProgram.Create(api, output));
        program.FrameInputs = inputs;

        using var boundary = commands.EnterBoundary(new("Tests.LumOnTemporalDebug", true, compute: [program.Pipeline]));
        // Act through the production imported compute helper and make its storage writes visible to readback.
        boundary.Run(() => program.Dispatch(1, 1, 1));
        commands.Barrier(new(ResourceAccess.ShaderStorage | ResourceAccess.BufferUpdate));
        float[] result = new float[2];
        using (OpenGlStateCache.Current.BindBufferScope(BufferTarget.ShaderStorageBuffer, output.BufferId))
            GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero, 8, result);
        // Assert the rebased perspective projection; previous-relative point=(.375,-.0625,-1.875), W=1.875.
        Assert.InRange(result[0], .69999f, .70001f);
        Assert.InRange(result[1], .44999f, .45001f);
    }

    #endregion

}
