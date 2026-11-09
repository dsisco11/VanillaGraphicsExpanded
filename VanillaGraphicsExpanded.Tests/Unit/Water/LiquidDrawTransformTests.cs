using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Unit.Water;

/// <summary>Checks camera removal from liquid-pool draw transforms.</summary>
public sealed class LiquidDrawTransformTests
{
    #region Public API
    /// <summary>Combined camera and object transforms leave only object motion in the draw buffer.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.73f)]
    public void CombinedCameraAndObjectTransformsExtractOnlyObject(float angle)
    {
        // Arrange the engine's row-vector matrices and capture a camera snapshot that must remain immutable.
        var view = Matrix4x4.CreateRotationY(angle) * Matrix4x4.CreateTranslation(-3, 2, -7);
        var model = Matrix4x4.CreateScale(1.2f, .8f, 1.1f)
            * Matrix4x4.CreateRotationX(.31f) * Matrix4x4.CreateTranslation(5, -2, 4);
        float[] viewColumns = Pack(view);
        using var camera = TestFrameCamera.Create(Pack(Matrix4x4.Identity), viewColumns);
        byte[] originalCamera = camera.Bytes.ToArray();
        using var draw = new LiquidDrawParamsUbo();
        draw.SetOrigin(new(9, 8, 7));
        draw.SetTransparency(.25f);

        // Act by storing model*view; Numerics row-vector composition is equivalent to GLSL view*model.
        draw.SetModelView(Pack(model * view), camera);

        // Assert the stored transform acts like the object transform, then verify unrelated draw fields survived.
        var extracted = MemoryMarshal.Read<Matrix4x4>(draw.Bytes);
        foreach (var point in new[] { Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, new Vector4(2, 3, 4, 1) })
        {
            Vector4 expected = Vector4.Transform(point, model);
            Vector4 actual = Vector4.Transform(point, extracted);
            Assert.True(Vector4.Distance(expected, actual) < 1e-4f, $"Expected {expected}; received {actual}.");
        }
        Assert.Equal(originalCamera, camera.Bytes.ToArray());
        Assert.Equal(new float[] { 9, 8, 7, .25f }, MemoryMarshal.Cast<byte, float>(draw.Bytes[64..]).ToArray());

        // Act by setting a camera-only model-view transform.
        draw.SetModelView(viewColumns, camera);

        // Assert a camera-only draw is identity.
        Assert.Equal(Matrix4x4.Identity, MemoryMarshal.Read<Matrix4x4>(draw.Bytes));

        // Act by applying an object transform and explicitly resetting it.
        draw.SetModelView(Pack(model * view), camera);
        draw.ResetModelTransform();

        // Assert explicit reset also erases the retained transform exactly.
        Assert.Equal(Matrix4x4.Identity, MemoryMarshal.Read<Matrix4x4>(draw.Bytes));
    }
    #endregion

    #region Private
    /// <summary>Packs a row-vector matrix as its GLSL column-vector equivalent without changing its action.</summary>
    private static float[] Pack(Matrix4x4 matrix)
        => MemoryMarshal.Cast<Matrix4x4, float>(new[] { matrix }).ToArray();
    #endregion
}
