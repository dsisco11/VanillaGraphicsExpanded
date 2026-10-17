using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares actual GLSL skipping with unchanged GPU DDA and independent direct column-box enumeration.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CloudHierarchyTraversalTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Adversarial and fixed-seed rays preserve every occupied interval exactly while avoiding proven-missed leaves.</summary>
    [Theory]
    [InlineData(21)]
    [InlineData(81)]
    [InlineData(200)]
    public void OrderedIntervalsMatchOriginalDdaAndIndependentGeometry(int width)
    {
        EnsureShaderTestAvailable();
        using var owner = new CloudHierarchy(() => CloudHierarchyComputeShader.Create(Programs.Api));
        using var native = TestFramework.CreateTexture(width, width, PixelInternalFormat.Rgba32f);
        using var volume = new CloudVolumeParameters();
        volume.CaptureMap(width, default, 1, CloudVolumeSettings.Default);
        int shader = BuiltShaderFixture.LoadFixture("tests/cloud-traversal.csh", ShaderType.ComputeShader);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, shader); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            var input = new CloudMapInput(new object(), 1, 1, 1, 1, 1, 1,
                new(native.TextureId, 2, 3, 4, width, 50, 5), default);
            int probesSaved = 0;
            bool skippedBesideCloud = false;
            foreach (int pattern in new[] { 0, 1, 2 })
            {
                Vector4[] columns = Columns(width, pattern);
                native.UploadDataImmediate(MemoryMarshal.Cast<Vector4, float>(columns.AsSpan()).ToArray());
                input = input with { Generation = input.Generation + 1 };
                Assert.True(owner.TryBuild(input, () => true), owner.Failure);
                Assert.True(owner.TryGetCurrent(input, out var bounds));
                foreach (var (origin, direction) in Rays(width))
                {
                    const float limit = 100000;
                    var original = Trace(program, volume, native, bounds!, origin, direction, limit, false);
                    var accelerated = Trace(program, volume, native, bounds!, origin, direction, limit, true);
                    Assert.Equal(original.Intervals, accelerated.Intervals);
                    Assert.True(accelerated.Probes <= original.Probes);
                    probesSaved += original.Probes - accelerated.Probes;
                    skippedBesideCloud |= original.Intervals.Length > 0 && accelerated.Probes < original.Probes;
                    var oracle = CloudTraversalOracle.Intersections(columns, width, origin, direction, limit);
                    CheckGeometry(columns, width, origin, direction, limit, accelerated.Intervals, oracle);
                }
            }
            Assert.True(probesSaved > width, "Empty/coarse-missed groups must avoid native leaf queries.");
            Assert.True(skippedBesideCloud, "Skipping must also reduce probes on rays that integrate occupied clouds.");
        }
        finally
        {
            GL.UseProgram(0); GL.DeleteProgram(program); GL.DeleteShader(shader);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Qualifies native float corner slivers individually while requiring every substantive independent intersection.</summary>
    private static void CheckGeometry(Vector4[] columns, int width, Vector3 origin, Vector3 direction, float limit,
        Vector4[] intervals, List<(double Near, double Far, int X, int Z)> oracle)
    {
        var visited = new HashSet<(int X, int Z)>();
        double previousFar = 0;
        foreach (var actual in intervals)
        {
            int x = (int)actual.Z, z = (int)actual.W;
            Assert.InRange(x, 0, width - 1); Assert.InRange(z, 0, width - 1);
            Assert.True(visited.Add((x, z)), "Native cells must not be integrated twice.");
            Assert.True(actual.X >= previousFar && actual.Y > actual.X);
            previousFar = actual.Y;
            Vector4 column = columns[z * width + x];
            Assert.True(column.X > 0 && column.W > column.Z);
            var direct = CloudTraversalOracle.CellBounds(column, x, z, origin, direction, limit);
            Assert.True(double.IsFinite(direct.Near) && double.IsFinite(direct.Far));
            double error = CloudTraversalOracle.ClockError(width, Math.Max(Math.Abs(direct.Near), Math.Abs(direct.Far)));
            Assert.InRange(Math.Abs(actual.X - direct.Near), 0, error);
            Assert.InRange(Math.Abs(actual.Y - direct.Far), 0, error);
            if (direct.Far <= direct.Near)
                Assert.InRange(actual.Y - actual.X, 0, 2 * error);
        }
        int last = -1;
        foreach (var expected in oracle)
        {
            int index = Array.FindIndex(intervals, value => (int)value.Z == expected.X && (int)value.W == expected.Z);
            double error = CloudTraversalOracle.ClockError(width, Math.Max(Math.Abs(expected.Near), Math.Abs(expected.Far)));
            // Native repeated additions can create or remove only rounding-sized corner intervals.
            // All longer independently enumerated intervals must appear in the same order.
            if (index < 0) Assert.InRange(expected.Far - expected.Near, 0, 2 * error);
            else { Assert.True(index > last); last = index; }
        }
    }

    /// <summary>Executes the actual imported traversal and reads test-only interval/probe records.</summary>
    private static (Vector4[] Intervals, int Probes) Trace(int program, CloudVolumeParameters volume,
        DynamicTexture2D native, DynamicTexture2D bounds, Vector3 origin, Vector3 direction, float limit, bool accelerated)
    {
        using var ray = new PackedUniformBuffer(32);
        using var traversal = new PackedUniformBuffer(16);
        float[] rayData = [origin.X, origin.Y, origin.Z, limit, direction.X, direction.Y, direction.Z, accelerated ? 1 : 0];
        ray.SetBytes(MemoryMarshal.AsBytes(rayData.AsSpan()));
        byte[] state = new byte[16];
        UboPacking.WriteIVec4(state, 0, 1, bounds.MipLevels - 1, 0, 0); traversal.SetBytes(state);
        using var output = GpuShaderStorageBuffer.Create();
        output.UploadData(new float[403 * 4]);
        GL.UseProgram(program);
        native.Bind(0); bounds.Bind(8);
        Assert.True(volume.TryBindToSlot(28)); Assert.True(traversal.TryBindToSlot(15)); Assert.True(ray.TryBindToSlot(16));
        output.BindBase(0);
        GL.DispatchCompute(1, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
        float[] result = new float[403 * 4];
        using (StateCache.Current.BindBufferScope(BufferTarget.ShaderStorageBuffer, output.BufferId))
            GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero, result.Length * sizeof(float), result);
        GL.UseProgram(0); StateCache.Current.InvalidateAll();
        int count = (int)result[402 * 4];
        Assert.InRange(count, 0, 402);
        return (MemoryMarshal.Cast<float, Vector4>(result.AsSpan(0, count * 4)).ToArray(), (int)result[402 * 4 + 1]);
    }

    /// <summary>Includes empty, sparse and dense stepped heights with a far corner that logical padding cannot duplicate.</summary>
    private static Vector4[] Columns(int width, int pattern)
    {
        var values = new Vector4[width * width];
        if (pattern == 0) return values;
        for (int z = 0; z < width; z++)
        for (int x = 0; x < width; x++)
        {
            if (pattern == 1 && (x * 7 + z * 11) % 19 != 0) continue;
            float lower = ((x + z) % 5 - 2) * .5f;
            values[z * width + x] = new(1, 0, lower, lower + .75f);
        }
        values[^1] = new(1, 0, -3, 4);
        return values;
    }

    /// <summary>Exercises parallel/tied axes, map corners, inside origins, thin tangencies and deterministic general directions.</summary>
    private static IEnumerable<(Vector3 Origin, Vector3 Direction)> Rays(int width)
    {
        float extent = width * 50;
        Vector3[] origins = [new(-20, 0, 25), new(extent + 20, 0, 25), new(50, 0, 50),
            new(0, 0, 0), new(extent, 0, extent), new(25, 200, 25), new(25, -200, 25),
            new(25, 37.499f, 25), new(25, 37.501f, 25)];
        Vector3[] directions = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY,
            -Vector3.UnitY, Vector3.Normalize(new Vector3(1, 0, 1)), Vector3.Normalize(new Vector3(-1, 0, -1)),
            Vector3.Normalize(new Vector3(1, 1e-10f, .25f)), new(1e-10f, 0, 1),
            Vector3.Normalize(new Vector3(-1e-7f, 0, 1)), Vector3.Normalize(new Vector3(1, 0, 1e-7f))];
        foreach (var origin in origins)
        foreach (var direction in directions) yield return (origin, direction);
        var random = new Random(0x434C4F55);
        for (int i = 0; i < 32; i++)
        {
            var origin = new Vector3((float)random.NextDouble() * extent, (float)random.NextDouble() * 200 - 100,
                (float)random.NextDouble() * extent);
            var direction = Vector3.Normalize(new Vector3((float)random.NextDouble() * 2 - 1,
                (float)random.NextDouble() * .2f - .1f, (float)random.NextDouble() * 2 - 1));
            yield return (origin, direction);
        }
    }
    #endregion
}
