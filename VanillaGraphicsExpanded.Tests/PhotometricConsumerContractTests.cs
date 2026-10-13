using System.Numerics;
using System.Runtime.InteropServices;
using System.Reflection;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks independent consumer lifecycle identity and raw-medium quantity packing.</summary>
public sealed class PhotometricConsumerContractTests
{
    #region Public API
    /// <summary>Decoder identity and payload revisions change only when a complete admitted calibration changes.</summary>
    [Fact]
    public void MatchingCalibrationIdentityRejectsInvalidAndRetainsStablePayload()
    {
        using var frame = new VgeFrameUniformBuffer();
        var revision = typeof(CpuUniformBuffer).GetField("contentRevision", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var calibration = LightingCalibration.Default;
        frame.SetLightingCalibration(calibration);
        uint original = frame.CalibrationGeneration;
        Assert.Equal(original, MemoryMarshal.Read<uint>(frame.Bytes[584..]));
        ulong content = (ulong)revision.GetValue(frame)!;
        byte[] payload = frame.Bytes.ToArray();
        frame.SetLightingCalibration(calibration);
        Assert.Equal(original, frame.CalibrationGeneration);
        Assert.Equal(content, (ulong)revision.GetValue(frame)!);
        Assert.Equal(payload, frame.Bytes.ToArray());
        frame.SetLightingCalibration(calibration with { AccessorBlockReferenceLux = 120 });
        Assert.Equal(original + 1, frame.CalibrationGeneration);
        Assert.Equal(original + 1, MemoryMarshal.Read<uint>(frame.Bytes[584..]));
        payload = frame.Bytes.ToArray();
        content = (ulong)revision.GetValue(frame)!;
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.SetLightingCalibration(calibration with { BlockReferenceLux = float.NaN }));
        Assert.Equal(original + 1, frame.CalibrationGeneration);
        Assert.Equal(content, (ulong)revision.GetValue(frame)!);
        Assert.Equal(payload, frame.Bytes.ToArray());
    }

    /// <summary>Sky-only probe history invalidates changed source calibration without requiring an available surface cache.</summary>
    [Fact]
    public void CalibrationChangesInvalidateHistoryWithoutCacheAndStableRepeatsDoNot()
    {
        var dependencies = new ProbeLightingHistoryDependencies();
        LightingCalibration original = LightingCalibration.Default;
        Assert.True(dependencies.Synchronize(null, null, original));
        Assert.False(dependencies.Synchronize(null, null, original));
        foreach (var changed in new[]
        {
            original with { BlockReferenceLux = original.BlockReferenceLux * 2 },
            original with { AccessorSkyFloor = .04f },
            original with { PointCandelaPerUnit = original.PointCandelaPerUnit * 2 }
        })
        {
            Assert.True(dependencies.Synchronize(null, null, changed));
            Assert.False(dependencies.Synchronize(null, null, changed));
        }
        Assert.True(dependencies.Synchronize(null, null, original));
        Assert.False(dependencies.Synchronize(null, null, original));
    }

    /// <summary>Changing underwater selection after raw camera-light publication preserves its independent decode flag and payload.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnderwaterSelectionPreservesRawCameraLightConvention(bool underwater)
    {
        using var parameters = new PbrCompositeParamsUbo();
        Vector4 raw = new(.2f, .3f, .4f, .5f);
        parameters.SetWaterVolume(new WaterVolumeFrame(null!, null, SourceUpper: Vector3.One, RawCameraLight: raw));
        parameters.SetUnderwater(underwater);
        ReadOnlySpan<float> packed = MemoryMarshal.Cast<byte, float>(parameters.Bytes);
        Assert.Equal(underwater ? 1 : 0, packed[0]);
        Assert.Equal(1, packed[2]);
        Assert.Equal(new float[] { raw.X, raw.Y, raw.Z, raw.W }, packed.Slice(28, 4).ToArray());
        parameters.SetWaterVolume(null);
        Assert.Equal(0, MemoryMarshal.Cast<byte, float>(parameters.Bytes)[2]);
    }
    #endregion
}
