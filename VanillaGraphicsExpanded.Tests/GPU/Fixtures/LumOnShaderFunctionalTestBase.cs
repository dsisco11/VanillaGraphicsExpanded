using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>
/// Base class for all LumOn shader functional tests.
/// 
/// Provides common infrastructure for GPU shader testing:
/// - OpenGL context via HeadlessGLFixture
/// - Shader compilation via ShaderTestHelper
/// - Fullscreen rendering via ShaderTestFramework
/// - Common test constants (screen sizes, Z-planes, epsilon)
/// - Standard texture creation helpers
/// - Proper resource cleanup
/// 
/// Usage:
/// <code>
/// [Collection("GPU")]
/// [Trait("Category", "GPU")]
/// public class MyShaderTests : LumOnShaderFunctionalTestBase
/// {
///     public MyShaderTests(HeadlessGLFixture fixture) : base(fixture) { }
///     
///     [Fact]
///     public void MyTest()
///     {
///         EnsureShaderTestAvailable();
///         var programId = CompileShader("my_shader.vsh", "my_shader.fsh");
///         // ... test logic ...
///         GL.DeleteProgram(programId);
///     }
/// }
/// </code>
/// </summary>
/// <remarks>
/// Test Configuration Standards:
/// - Screen buffer: 4×4 pixels (full resolution)
/// - Half-res buffer: 2×2 pixels
/// - Probe grid: 2×2 probes
/// - Octahedral atlas: 16×16 (8×8 texels per probe)
/// - Z-planes: zNear=0.1, zFar=100
/// - Float comparison epsilon: 1e-2f (for GPU precision variance)
/// 
/// All derived test classes should:
/// 1. Use [Collection("GPU")] and [Trait("Category", "GPU")] attributes
/// 2. Call EnsureShaderTestAvailable() at the start of each test
/// 3. Clean up shader programs with GL.DeleteProgram()
/// 4. Document expected value derivations in XML comments
/// </remarks>
public abstract class LumOnShaderFunctionalTestBase : RenderTestBase, IDisposable
{
    #region Constants

    /// <summary>Full resolution screen width (4 pixels).</summary>
    protected const int ScreenWidth = LumOnTestInputFactory.ScreenWidth;

    /// <summary>Full resolution screen height (4 pixels).</summary>
    protected const int ScreenHeight = LumOnTestInputFactory.ScreenHeight;

    /// <summary>Half resolution width (2 pixels).</summary>
    protected const int HalfResWidth = ScreenWidth / 2;

    /// <summary>Half resolution height (2 pixels).</summary>
    protected const int HalfResHeight = ScreenHeight / 2;

    /// <summary>Probe grid width (2 probes).</summary>
    protected const int ProbeGridWidth = LumOnTestInputFactory.ProbeGridWidth;

    /// <summary>Probe grid height (2 probes).</summary>
    protected const int ProbeGridHeight = LumOnTestInputFactory.ProbeGridHeight;

    /// <summary>Octahedral texels per probe (8×8).</summary>
    protected const int OctahedralSize = 8;

    /// <summary>Octahedral atlas width (16 texels).</summary>
    protected const int AtlasWidth = ProbeGridWidth * OctahedralSize;

    /// <summary>Octahedral atlas height (16 texels).</summary>
    protected const int AtlasHeight = ProbeGridHeight * OctahedralSize;

    /// <summary>Probe spacing in pixels (2).</summary>
    protected const int ProbeSpacing = 2;

    /// <summary>Near Z-plane for depth calculations.</summary>
    protected const float ZNear = LumOnTestInputFactory.DefaultZNear;

    /// <summary>Far Z-plane for depth calculations.</summary>
    protected const float ZFar = LumOnTestInputFactory.DefaultZFar;

    /// <summary>
    /// Default epsilon for float comparisons.
    /// Accounts for GPU precision variance across different hardware.
    /// </summary>
    protected const float TestEpsilon = 1e-2f;

    #endregion

    #region Fields

    private readonly HeadlessGLFixture _fixture;
    private ShaderTestHelper? _shaderHelper;
    private ShaderTestFramework? _testFramework;
    private bool _disposed;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the shader test helper for compiling and linking shaders.
    /// </summary>
    protected ShaderTestHelper ShaderHelper
    {
        get
        {
            if (_shaderHelper == null)
                throw new InvalidOperationException("ShaderTestHelper not available. Call EnsureShaderTestAvailable() first.");
            return _shaderHelper;
        }
    }

    /// <summary>
    /// Gets the test framework for rendering and texture management.
    /// </summary>
    protected ShaderTestFramework TestFramework
    {
        get
        {
            if (_testFramework == null)
                throw new InvalidOperationException("ShaderTestFramework not available. Call EnsureShaderTestAvailable() first.");
            return _testFramework;
        }
    }

    /// <summary>
    /// Gets whether shader testing is available (valid GL context and shader paths).
    /// </summary>
    protected bool IsShaderTestAvailable => _shaderHelper != null && _testFramework != null;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new LumOnShaderFunctionalTestBase instance.
    /// </summary>
    /// <param name="fixture">The HeadlessGLFixture providing the GL context.</param>
    protected LumOnShaderFunctionalTestBase(HeadlessGLFixture fixture) : base(fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        InitializeTestInfrastructure();
    }

    private void InitializeTestInfrastructure()
    {
        if (!_fixture.IsContextValid)
            return;

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaderincludes");

        if (Directory.Exists(shaderPath) && Directory.Exists(includePath))
        {
            _shaderHelper = new ShaderTestHelper(shaderPath, includePath);
            _testFramework = new ShaderTestFramework();
        }
    }

    #endregion

    #region Test Setup Helpers

    /// <summary>
    /// Ensures shader testing is available, skipping the test if not.
    /// Call this at the beginning of each test method.
    /// </summary>
    protected void EnsureShaderTestAvailable()
    {
        EnsureContextValid();
        Assert.SkipWhen(_shaderHelper == null, "ShaderTestHelper not available - shader paths not found");
        Assert.SkipWhen(_testFramework == null, "ShaderTestFramework not available");
    }

    /// <summary>
    /// Compiles and links a shader program.
    /// </summary>
    /// <param name="vertexShader">Vertex shader filename (in shaders directory).</param>
    /// <param name="fragmentShader">Fragment shader filename (in shaders directory).</param>
    /// <returns>The linked program ID.</returns>
    /// <exception cref="Xunit.Sdk.XunitException">If compilation fails.</exception>
    protected int CompileShader(string vertexShader, string fragmentShader)
    {
        var result = ShaderHelper.CompileAndLink(vertexShader, fragmentShader);
        Assert.True(result.IsSuccess, $"Shader compilation failed: {result.ErrorMessage}");
        return result.ProgramId;
    }

    #endregion

    #region Texture Creation Helpers

    /// <summary>
    /// Creates a texture with uniform color (RGBA16F).
    /// </summary>
    protected DynamicTexture CreateUniformColorTexture(int width, int height, float r, float g, float b, float a = 1.0f)
    {
        var data = new float[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = a;
        }
        return TestFramework.CreateTexture(width, height, PixelInternalFormat.Rgba16f, data);
    }

    /// <summary>
    /// Creates a depth texture with uniform depth (R32F).
    /// </summary>
    protected DynamicTexture CreateUniformDepthTexture(int width, int height, float depth)
    {
        var data = new float[width * height];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = depth;
        }
        return TestFramework.CreateTexture(width, height, PixelInternalFormat.R32f, data);
    }

    /// <summary>
    /// Creates a normal texture with uniform normals (RGBA16F, encoded to [0,1]).
    /// </summary>
    protected DynamicTexture CreateUniformNormalTexture(int width, int height, float nx, float ny, float nz)
    {
        var data = new float[width * height * 4];
        float encX = nx * 0.5f + 0.5f;
        float encY = ny * 0.5f + 0.5f;
        float encZ = nz * 0.5f + 0.5f;
        
        for (int i = 0; i < width * height; i++)
        {
            int idx = i * 4;
            data[idx + 0] = encX;
            data[idx + 1] = encY;
            data[idx + 2] = encZ;
            data[idx + 3] = 1.0f;
        }
        return TestFramework.CreateTexture(width, height, PixelInternalFormat.Rgba16f, data);
    }

    /// <summary>
    /// Creates a material texture (RGBA16F).
    /// Layout: R=roughness, G=metallic, B=emissive, A=reflectivity
    /// </summary>
    protected DynamicTexture CreateMaterialTexture(int width, int height, float roughness, float metallic, float emissive = 0f, float reflectivity = 0f)
    {
        var data = new float[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            int idx = i * 4;
            data[idx + 0] = roughness;
            data[idx + 1] = metallic;
            data[idx + 2] = emissive;
            data[idx + 3] = reflectivity;
        }
        return TestFramework.CreateTexture(width, height, PixelInternalFormat.Rgba16f, data);
    }

    /// <summary>
    /// Creates an identity 4×4 matrix.
    /// </summary>
    protected static float[] CreateIdentityMatrix() => LumOnTestInputFactory.CreateIdentityMatrix();

    #endregion

    #region Pixel Reading Helpers

    /// <summary>
    /// Reads a single pixel from a float array (assuming RGBA layout).
    /// </summary>
    protected static (float r, float g, float b, float a) ReadPixel(float[] data, int width, int x, int y)
    {
        int idx = (y * width + x) * 4;
        return (data[idx], data[idx + 1], data[idx + 2], data[idx + 3]);
    }

    /// <summary>
    /// Asserts that two float values are approximately equal.
    /// </summary>
    protected static void AssertApproximatelyEqual(float expected, float actual, float epsilon, string message)
    {
        Assert.True(MathF.Abs(expected - actual) < epsilon, 
            $"{message}: expected {expected:F4}, got {actual:F4} (diff: {MathF.Abs(expected - actual):F6})");
    }

    #endregion

    #region Dispose

    /// <summary>
    /// Disposes resources used by the test class.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _shaderHelper?.Dispose();
                _testFramework?.Dispose();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    #endregion
}
