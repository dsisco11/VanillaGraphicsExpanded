using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using Xunit;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks numerical behavior of the LumOn velocity shader.</summary>
/// <remarks>Renders the production velocity shader and compares its output against independently calculated camera reprojection cases.</remarks>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class LumOnVelocityFunctionalTests : LumOnShaderFunctionalTestBase
{
    #region Public API
    /// <summary>Uses the shared GPU context to verify velocity reprojection behavior.</summary>
    public LumOnVelocityFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>The production shader projects fixed world points through paired frame origins including rotation and view bob.</summary>
    [Theory]
    [InlineData(16777216.25, true)]
    [InlineData(-16777216.25, true)]
    [InlineData(16777216.25, false)]
    public void Velocity_RenderOriginMovementPreservesWorldReprojection(double origin, bool historyValid)
    {
        // Arrange paired large-world origins, camera history, and fixed non-sky depth for the production shader.
        EnsureShaderTestAvailable();
        var program = CompileVelocityShader();
        using var depth = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f,
            CreateUniformDepthData(ScreenWidth, ScreenHeight, .5f));
        using var target = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, TextureFormat.Rgba32f);
        float[] previous = CreateRotationZ(.1f);
        previous[12] = .015625f;
        previous[13] = -.03125f;
        float[] current = LumOnTestInputFactory.CreateIdentityMatrix();
        var temporal = new FrameCameraHistory();
        temporal.Capture(previous, origin, 32, -origin);
        temporal.Commit();
        temporal.Capture(current, origin + .0625, 32.125, -origin - .03125);
        SetupVelocityUniforms(program, current, temporal.PreviousViewProjection, historyValid ? 1 : 0);
        program.PrimaryDepth = depth;
        // Act by drawing the velocity field once with the current/history camera pair.
        TestFramework.RenderQuadTo(program, target);
        // Assert against the independently calculated interval for every retained interior pixel.
        // Compute independent CPU bounds for every original interior pixel.
        int interiorWidth=ScreenWidth-2,interiorHeight=ScreenHeight-2;
        var lower=new double[interiorWidth*interiorHeight*2];
        var upper=new double[lower.Length];
        for (int y = 1; y < ScreenHeight - 1; y++)
        for (int x = 1; x < ScreenWidth - 1; x++)
        {
            Vector2 uv = new((x + .5f) / ScreenWidth, (y + .5f) / ScreenHeight);
            // Convert current-relative coordinates to the old frame before applying its raw matrix.
            Vector4 oldRelative = new(uv.X * 2 - 1 + .0625f, uv.Y * 2 - 1 + .125f, -.03125f, 1);
            Vector4 clip = MulMat4Vec4(previous, oldRelative);
            Vector2 expected = historyValid ? uv - new Vector2(clip.X / clip.W, clip.Y / clip.W) * .5f - new Vector2(.5f) : Vector2.Zero;
            int index = ((y-1)*interiorWidth+x-1)*2;
            lower[index]=expected.X-TestEpsilon;upper[index]=expected.X+TestEpsilon;
            lower[index+1]=expected.Y-TestEpsilon;upper[index+1]=expected.Y+TestEpsilon;
        }
        var region=new RHITextureRegion(new(0,0),1,1,0,interiorWidth,interiorHeight,1);
        ImageAssertions.Comparisons.CompareComponentIntervals(target[0],lower,upper,3,region).AssertEqual();
        using var interior=new GpuImageSnapshot(target[0],region);
        ImageAssertions.Predicates.Predicate(interior.Texture, SurfacePredicates.LumOnVelocityFunctionalTests_Velocity_RenderOriginMovementPreservesWorldReprojection,
            parameters:new(historyValid?1:2,0,0,0)).AssertEqual();
    }

    /// <summary>Verifies static camera inputs produce a zero velocity field.</summary>

    [Fact]
    public void Velocity_StaticCamera_IsZero()
    {
        // Arrange identical current and previous camera transforms over non-sky depth.
        EnsureShaderTestAvailable();

        var programId = CompileVelocityShader();


        // Depth that is not sky and not zero.
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, depth: 0.5f);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);

        using var outRt = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, TextureFormat.Rgba32f);

        float[] invCurr = LumOnTestInputFactory.CreateIdentityMatrix();
        float[] prev = LumOnTestInputFactory.CreateIdentityMatrix();

        SetupVelocityUniforms(programId, invCurr, prev, historyValid: 1);

        programId.PrimaryDepth = depthTex;
        // Act by rendering the static-camera velocity field.
        TestFramework.RenderQuadTo(programId, outRt);

        // Assert the output is zero within the shader tolerance.
        ImageAssertions.Predicates.Predicate(outRt[0], SurfacePredicates.LumOnVelocityFunctionalTests_Velocity_StaticCamera_IsZero,
            parameters:new(TestEpsilon,0,0,0)).AssertEqual();
    }

    /// <summary>Verifies pure translation produces velocity in the expected direction.</summary>

    [Fact]
    public void Velocity_PureTranslation_ProducesExpectedDirection()
    {
        // Arrange an identity current transform and a translated previous transform.
        EnsureShaderTestAvailable();

        var programId = CompileVelocityShader();


        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, depth: 0.5f);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var outRt = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, TextureFormat.Rgba32f);


        // With invCurr = I and prevViewProj = translate(+tx,0,0) in clip space,
        // prevUv.x = currUv.x + tx*0.5 => velocityUv.x = -tx*0.5.
        const float tx = 0.2f;
        float expectedVx = -tx * 0.5f;

        float[] invCurr = LumOnTestInputFactory.CreateIdentityMatrix();
        float[] prev = LumOnTestInputFactory.CreateTranslationMatrix(tx, 0f, 0f);

        SetupVelocityUniforms(programId, invCurr, prev, historyValid: 1);

        programId.PrimaryDepth = depthTex;
        // Act by rendering the translated-camera velocity field.
        TestFramework.RenderQuadTo(programId, outRt);

        // Assert the independently derived horizontal direction and tolerance interval.
        // Preserve the CPU-rounded interval endpoints of the independent translation oracle.
        ImageAssertions.Predicates.Predicate(outRt[0], SurfacePredicates.LumOnVelocityFunctionalTests_Velocity_PureTranslation_ProducesExpectedDirection,
            parameters:new(expectedVx-TestEpsilon,expectedVx+TestEpsilon,TestEpsilon,0)).AssertEqual();
    }

    /// <summary>Verifies pure rotation produces the expected velocity pattern.</summary>

    [Fact]
    public void Velocity_PureRotation_ProducesExpectedPattern()
    {
        // Arrange one rotation with fixed depth and identity current-frame reprojection.
        EnsureShaderTestAvailable();

        var programId = CompileVelocityShader();


        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, depth: 0.5f);


        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var outRt = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, TextureFormat.Rgba32f);
        float[] invCurr = LumOnTestInputFactory.CreateIdentityMatrix();
        float[] prev = CreateRotationZ(radians: 10f * MathF.PI / 180f);

        SetupVelocityUniforms(programId, invCurr, prev, historyValid: 1);

        programId.PrimaryDepth = depthTex;
        // Act by rendering the rotated-camera velocity field.
        TestFramework.RenderQuadTo(programId, outRt);



        // Assert the independently calculated center and corner magnitudes and their validity flags.
        // Compare center-ish pixel vs corner pixel magnitudes.
        // gl_FragCoord.xy is (x+0.5, y+0.5); currUv is divided by screenSize.
        Vector2 centerUv = new((1.5f) / ScreenWidth, (1.5f) / ScreenHeight); // pixel (1,1)
        Vector2 cornerUv = new((3.5f) / ScreenWidth, (3.5f) / ScreenHeight); // pixel (3,3)

        Vector2 expectedCenter = ComputeExpectedVelocityUv(centerUv, depthRaw: 0.5f, invCurr, prev);
        Vector2 expectedCorner = ComputeExpectedVelocityUv(cornerUv, depthRaw: 0.5f, invCurr, prev);

        float expectedCenterMag = expectedCenter.Length();
        float expectedCornerMag = expectedCorner.Length();

        Assert.True(expectedCornerMag > expectedCenterMag + 1e-4f);

        // Only these two local tuples are the subject of the rotation assertions.
        var center=outRt[0].ReadPixelsRegion(1,1,1,1);
        var corner=outRt[0].ReadPixelsRegion(3,3,1,1);
        var centerPx=(vx:center[0],vy:center[1],flags:BitConverter.SingleToUInt32Bits(center[3]));
        var cornerPx=(vx:corner[0],vy:corner[1],flags:BitConverter.SingleToUInt32Bits(corner[3]));
        Assert.True((centerPx.flags & FlagValid) != 0u);
        Assert.True((cornerPx.flags & FlagValid) != 0u);

        Assert.InRange(centerPx.vx, expectedCenter.X - TestEpsilon, expectedCenter.X + TestEpsilon);
        Assert.InRange(centerPx.vy, expectedCenter.Y - TestEpsilon, expectedCenter.Y + TestEpsilon);

        Assert.InRange(cornerPx.vx, expectedCorner.X - TestEpsilon, expectedCorner.X + TestEpsilon);
        Assert.InRange(cornerPx.vy, expectedCorner.Y - TestEpsilon, expectedCorner.Y + TestEpsilon);
    }

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }

    #endregion

    #region Private
    #region Shader setup and packed inputs
    /// <summary>Identifies output pixels whose reprojection history is valid.</summary>
    private const uint FlagValid = 1u << 0;

    /// <summary>Compiles or retrieves the velocity shader used by this test.</summary>
    private LumOnVelocityShaderProgram CompileVelocityShader() => Programs.Create<LumOnVelocityShaderProgram>();

    /// <summary>Reinterprets packed velocity flags as a floating-point shader input.</summary>
    private static float PackFlags(uint flags) => BitConverter.UInt32BitsToSingle(flags);

    /// <summary>Binds the velocity inputs and configures uniforms for one draw.</summary>
    private void SetupVelocityUniforms(
        LumOnVelocityShaderProgram programId,
        float[] invCurrViewProj,
        float[] prevViewProj,
        int historyValid,
        int depthUnit = 0)
    {
        UpdateAndBindLumOnFrameUbo(
            programId,
            invCurrViewProjMatrix: invCurrViewProj,
            prevViewProjMatrix: prevViewProj,
            historyValid: historyValid);
        var identity=TestFramework.CreateTexture(ScreenWidth,ScreenHeight,TextureFormat.Rgba32ui);
        identity.UploadDataImmediate(new uint[ScreenWidth*ScreenHeight*4]);
        programId.PatchIdentity=identity;

    }
    #endregion

    #region Reprojection reference math
    /// <summary>Multiplies a column-major 4x4 matrix by a vector.</summary>
    private static Vector4 MulMat4Vec4(float[] m, Vector4 v)
    {
        // Column-major 4x4: m[col*4 + row]
        float x = m[0] * v.X + m[4] * v.Y + m[8] * v.Z + m[12] * v.W;
        float y = m[1] * v.X + m[5] * v.Y + m[9] * v.Z + m[13] * v.W;
        float z = m[2] * v.X + m[6] * v.Y + m[10] * v.Z + m[14] * v.W;
        float w = m[3] * v.X + m[7] * v.Y + m[11] * v.Z + m[15] * v.W;
        return new Vector4(x, y, z, w);
    }

    /// <summary>Computes the reference previous-frame UV from current UV and clip transforms.</summary>
    private static Vector2 ComputeExpectedVelocityUv(Vector2 currUv, float depthRaw, float[] invCurrViewProj, float[] prevViewProj)
    {
        var currClip = new Vector4(
            currUv.X * 2f - 1f,
            currUv.Y * 2f - 1f,
            depthRaw * 2f - 1f,
            1f);

        var worldPosH = MulMat4Vec4(invCurrViewProj, currClip);
        var worldPos = new Vector3(worldPosH.X, worldPosH.Y, worldPosH.Z) / worldPosH.W;

        var prevClip = MulMat4Vec4(prevViewProj, new Vector4(worldPos, 1f));
        var prevNdc = new Vector2(prevClip.X / prevClip.W, prevClip.Y / prevClip.W);
        var prevUv = prevNdc * 0.5f + new Vector2(0.5f, 0.5f);

        return currUv - prevUv;
    }

    /// <summary>Creates a column-major rotation matrix around the Z axis.</summary>
    private static float[] CreateRotationZ(float radians)
    {
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);

        // Column-major
        return
        [
            c, s, 0f, 0f,   // col0
            -s, c, 0f, 0f,  // col1
            0f, 0f, 1f, 0f, // col2
            0f, 0f, 0f, 1f  // col3
        ];
    }
    #endregion

    #endregion

}
