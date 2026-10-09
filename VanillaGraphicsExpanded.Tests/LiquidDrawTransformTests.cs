using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks that liquid pool matrices retain only object motion under independent camera snapshots.</summary>
public sealed class LiquidDrawTransformTests
{
    #region Public API
    /// <summary>Camera removal preserves rotated and translated pool geometry and leaves the shared snapshot unchanged.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.73f)]
    public void CombinedCameraAndObjectTransformsExtractOnlyObject(float angle)
    {
        var view = Matrix4x4.CreateRotationY(angle) * Matrix4x4.CreateTranslation(-3, 2, -7);
        var model = Matrix4x4.CreateScale(1.2f, .8f, 1.1f)
            * Matrix4x4.CreateRotationX(.31f) * Matrix4x4.CreateTranslation(5, -2, 4);
        float[] viewColumns = Pack(view);
        using var camera = TestFrameCamera.Create(Pack(Matrix4x4.Identity), viewColumns);
        byte[] originalCamera = camera.Bytes.ToArray();
        using var draw = new LiquidDrawParamsUbo();
        draw.SetOrigin(new(9, 8, 7));
        draw.SetTransparency(.25f);
        // Numerics row-vector composition is model * view, equivalent to GLSL view * model.
        draw.SetModelView(Pack(model * view), camera);
        var extracted = MemoryMarshal.Read<Matrix4x4>(draw.Bytes);
        foreach (var point in new[] { Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, new Vector4(2, 3, 4, 1) })
        {
            Vector4 expected = Vector4.Transform(point, model);
            Vector4 actual = Vector4.Transform(point, extracted);
            Assert.True(Vector4.Distance(expected, actual) < 1e-4f, $"Expected {expected}; received {actual}.");
        }
        Assert.Equal(originalCamera, camera.Bytes.ToArray());
        Assert.Equal(new float[] { 9, 8, 7, .25f }, MemoryMarshal.Cast<byte, float>(draw.Bytes[64..]).ToArray());
        // Both the engine restoration callback and a new ordinary sequence must erase retained object motion exactly.
        draw.SetModelView(viewColumns, camera);
        Assert.Equal(Matrix4x4.Identity, MemoryMarshal.Read<Matrix4x4>(draw.Bytes));
        draw.SetModelView(Pack(model * view), camera);
        draw.ResetModelTransform();
        Assert.Equal(Matrix4x4.Identity, MemoryMarshal.Read<Matrix4x4>(draw.Bytes));
    }
    #endregion

    #region Private
    /// <summary>Stores row-vector matrices as GLSL column-vector equivalents without changing their action.</summary>
    private static float[] Pack(Matrix4x4 matrix)
        => MemoryMarshal.Cast<Matrix4x4, float>(new[] { matrix }).ToArray();
    #endregion
}
