using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks the atmospheric sky input contract with controlled engine state.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AtmosphereOwnedSkyTests
{
    #region Public API
    /// <summary>Captures altitude and fivefold wind animation while shared camera fields remain outside the sky effect block.</summary>
    [Fact]
    public void CaptureBuildsCameraRelativeInputs()
    {
        // Arrange
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var worldData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        var entity = new EntityPlayer();
        entity.Pos.SetPos(0, 123, 0);
        typeof(ClientWorldPlayerData).GetField("entityplayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(worldData, entity);
        typeof(ClientPlayer).GetField("worlddata", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(player, worldData);
        api.SetupGet(value => value.World.Player).Returns(player);
        api.SetupGet(value => value.World.SeaLevel).Returns(100);
        float[] projection = Elements(Perspective(1.2f, 1.7f));
        Matrix4x4 camera = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateRotationX(-.3f);
        camera.Translation = new Vector3(12, -7, 99);
        float[] view = Elements(camera);
        float[] originalView = (float[])view.Clone();
        api.SetupGet(value => value.Render.CurrentProjectionMatrix).Returns(projection);
        api.SetupGet(value => value.Render.CurrentModelviewMatrix).Returns(view);
        api.SetupGet(value => value.Render.FrameWidth).Returns(1280);
        api.SetupGet(value => value.Render.FrameHeight).Returns(720);
        var uniforms = new DefaultShaderUniforms
        {
            FogWaveCounter = 7, ZNear = .1f, ZFar = 1000,
            WaterMurkColor = new Vec4f(.2f, .3f, .4f, .9f), CameraUnderwater = .8f,
            WindWaveCounter = 2, PsychedelicStrength = .25f, NightVisionStrength = .5f,
            FlagFogDensity = -.01f, FlatFogStartYPos = 4, PlayerPos = new Vec3f(0, 3, 0)
        };
        typeof(DefaultShaderUniforms).GetField("SkyDaylight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(uniforms, .4f);
        api.SetupGet(value => value.Render.ShaderUniforms).Returns(uniforms);
        var lighting = new AtmosphereLighting(Vector3.UnitY, Vector3.Zero, Vector3.Zero,
            Vector3.Zero, Vector3.Zero, ImmutableArray<float>.Empty) { HorizonElevation = -.2f };
        using var inputs = new AtmosphereSkyInputs();
        // Act
        inputs.Capture(api.Object, lighting, true);
        float[] actual = MemoryMarshal.Cast<byte, float>(inputs.Bytes).ToArray();
        // Assert
        Assert.Equal(112, inputs.SizeBytes);
        Assert.Equal(new float[] { 1, 2, 0, 0 }, actual[20..24]);
        Assert.Equal(originalView, view);
        view[12] = -345; view[13] = 900; view[14] = 12;
        inputs.Capture(api.Object, lighting, true);
        Assert.Equal(actual, MemoryMarshal.Cast<byte, float>(inputs.Bytes).ToArray());
        Assert.Equal(new float[] { 0, 1, 0, -.2f }, actual[..4]);
        Assert.Equal(23, actual[10]);
        Assert.Equal(7, actual[11]);
        Assert.Equal(new float[] { .2f, .3f, .4f, .8f }, actual[12..16]);
        Assert.Equal(new float[] { .5f, .25f, 10, 1 }, actual[16..20]);
        byte[] admittedBytes = inputs.Bytes.ToArray();
        float admittedMaximum = inputs.CompatibilityMaximumNits;
        Assert.True(admittedMaximum > 0);
        // Act
        foreach (float invalidWind in new[] { float.NaN, float.PositiveInfinity, float.MaxValue })
        {
            uniforms.WindWaveCounter = invalidWind;
            Assert.Throws<ArgumentException>(() => inputs.Capture(api.Object, lighting, true));
            // Assert
            Assert.Equal(admittedBytes, inputs.Bytes.ToArray());
            Assert.Equal(admittedMaximum, inputs.CompatibilityMaximumNits);
        }
    }
    #endregion

    #region Private
    /// <summary>Builds an OpenGL perspective matrix independently of production matrix helpers.</summary>
    private static Matrix4x4 Perspective(float fov, float aspect)
                            {
                                float scale = 1 / MathF.Tan(fov / 2);
                                return new(scale / aspect, 0, 0, 0, 0, scale, 0, 0, 0, 0, -100.1f / 99.9f, -1, 0, 0, -20f / 99.9f, 0);
                            }
    /// <summary>Serializes row-vector matrices as the equivalent OpenGL column-major transforms.</summary>
    private static float[] Elements(Matrix4x4 m) =>
                                [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
                                 m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
    #endregion
}
