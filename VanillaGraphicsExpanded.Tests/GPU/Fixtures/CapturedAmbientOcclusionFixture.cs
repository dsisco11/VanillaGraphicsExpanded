using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Restores exported AO inputs at their native precision, validates uploads, and prepares captured-scene comparisons.</summary>
internal sealed class CapturedAmbientOcclusionFixture : IDisposable
{
    private readonly GpuResourceCollection resources = new();
    private readonly JsonDocument manifest;
    private readonly string directory;
    private readonly DynamicTexture2D depth, hierarchy;
    private readonly Texture3D surface;
    private readonly VgeFrameUniformBuffer camera;
    private readonly PostSsaoShaderProgram horizon;

    #region Public API
    /// <summary>Gets the captured receiver extent.</summary>
    internal int Width { get; }
    /// <summary>Gets the captured receiver extent.</summary>
    internal int Height { get; }
    /// <summary>Gets the native quality proven by the captured parameter block.</summary>
    internal int Quality { get; }
    /// <summary>Gets the original horizon image for a replay-to-headless round-trip check.</summary>
    internal float[] CapturedOutput { get; }

    /// <summary>Loads verified payloads without changing formats, camera transforms, mip contents or array layer order.</summary>
    internal CapturedAmbientOcclusionFixture(ComponentShaderPrograms programs, string directory)
    {
        this.directory = directory;
        manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        try
        {
            var root = manifest.RootElement;
            Assert.Equal(7091, root.GetProperty("event").GetInt32());
            var textures = root.GetProperty("textures").EnumerateArray().ToArray();
            var depthRecord = textures.Single(record => record.GetProperty("binding").GetString() == "depthImage");
            Width = depthRecord.GetProperty("width").GetInt32(); Height = depthRecord.GetProperty("height").GetInt32();
            depth = resources.Own(LoadFloatTexture(depthRecord));
            hierarchy = resources.Own(LoadFloatTexture(textures.Single(record => record.GetProperty("binding").GetString() == "depthHierarchy")));
            surface = resources.Own(LoadSurface(textures.Single(record => record.GetProperty("binding").GetString() == "surfaceImage")));

            var blocks = root.GetProperty("uniformBuffers").EnumerateArray().ToArray();
            byte[] frameBytes = ReadPayload(blocks.Single(block => block.GetProperty("binding").GetString() == "vgeFrame"), VgeFrameUniformBuffer.PackedSize);
            camera = TestFrameCamera.CreateFromPacked(frameBytes);
            // AO reads these two matrices directly. Other frame fields are recaptured by their typed owner.
            Assert.True(camera.Bytes.Slice(64, 128).SequenceEqual(frameBytes.AsSpan(64, 128)));
            Assert.Equal((float)Width, UboPacking.ReadFloat(frameBytes, 384));
            Assert.Equal((float)Height, UboPacking.ReadFloat(frameBytes, 388));
            byte[] parameterBytes = ReadPayload(blocks.Single(block => block.GetProperty("binding").GetString() == "AmbientOcclusionInputs_var"), 48);
            float[] parameters = DecodeFloat(parameterBytes);
            Assert.Equal(new float[] { 0, 0, 2, 0, 1.25f, .25f, 6, 6, 64, 96, 0, 0 }, parameters);
            Quality = 2;

            var output = root.GetProperty("output");
            Assert.Equal("R16G16B16A16_FLOAT", output.GetProperty("format").GetString());
            Assert.Equal((Width + 1) / 2, output.GetProperty("width").GetInt32());
            Assert.Equal((Height + 1) / 2, output.GetProperty("height").GetInt32());
            CapturedOutput = DecodeHalf(ReadPayload(output, checked((Width + 1) / 2 * ((Height + 1) / 2) * 8)));
            horizon = programs.Create<PostSsaoShaderProgram>();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Prepares both shaders with identical captured resources and unchanged high-quality parameters.</summary>
    internal (Action Reference, Action Current) PrepareComparison(AoHorizonReferenceProgram reference,
        PostprocessDraw draw, GpuFramebuffer referenceTarget, GpuFramebuffer currentTarget)
    {
        reference.Capture(depth, surface, hierarchy, camera, Quality);
        horizon.DepthImage = depth; horizon.SurfaceImage = surface; horizon.DepthHierarchy = hierarchy;
        horizon.Capture(camera, new(0, 0, 2, 0), new(1.25f, .25f, 6, 6), new(64, 96, 0, 0));
        var referencePipeline = draw.Prepare(reference, referenceTarget);
        var currentPipeline = draw.Prepare(horizon, currentTarget);
        // Native uploads, verification, preparation and descriptor mutation precede all timing.
        return (
            () => Assert.True(GraphicsCommandContext.TryRun("Tests.CapturedAoReference", [referencePipeline], true,
                commands => draw.Submit(commands, referencePipeline, referenceTarget))),
            () => Assert.True(GraphicsCommandContext.TryRun("Tests.CapturedAoCurrent", [currentPipeline], true,
                commands => draw.Submit(commands, currentPipeline, currentTarget))));
    }

    /// <summary>Retires all restored GPU inputs and parsed metadata together.</summary>
    public void Dispose() { camera?.Dispose(); resources.Dispose(); manifest.Dispose(); }
    #endregion

    #region Private
    #region Native texture restoration
    /// <summary>Uploads every R32F mip and verifies complete readback before the captured input can be used.</summary>
    private DynamicTexture2D LoadFloatTexture(JsonElement record)
    {
        Assert.Equal("R32_FLOAT", record.GetProperty("format").GetString());
        Assert.Equal(1, record.GetProperty("arraySize").GetInt32());
        Assert.Equal(Width, record.GetProperty("width").GetInt32());
        Assert.Equal(Height, record.GetProperty("height").GetInt32());
        int levels = record.GetProperty("mips").GetInt32();
        var texture = DynamicTexture2D.CreateMipmapped(Width, Height, PixelInternalFormat.R32f, levels);
        try
        {
            var subresources = record.GetProperty("subresources").EnumerateArray().ToArray();
            Assert.Equal(levels, subresources.Length);
            foreach (var subresource in subresources)
            {
                int mip = subresource.GetProperty("mip").GetInt32();
                Assert.Equal(0, subresource.GetProperty("layer").GetInt32());
                int width = Math.Max(1, Width >> mip), height = Math.Max(1, Height >> mip);
                Assert.Equal(width, subresource.GetProperty("width").GetInt32());
                Assert.Equal(height, subresource.GetProperty("height").GetInt32());
                float[] pixels = DecodeFloat(ReadPayload(subresource, checked(width * height * 4)));
                using (StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture.TextureId))
                using (StateCache.Current.SetPixelUnpackScope(new StateCache.PixelUnpackState(4)))
                    GL.TexSubImage2D(TextureTarget.Texture2D, mip, 0, 0, width, height, PixelFormat.Red, PixelType.Float, pixels);
                Assert.Equal(pixels, texture.ReadPixels(mip));
            }
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }

    /// <summary>Restores the original half-float surface array, including invalid-receiver markers and transmission.</summary>
    private Texture3D LoadSurface(JsonElement record)
    {
        Assert.Equal("R16G16B16A16_FLOAT", record.GetProperty("format").GetString());
        Assert.Equal(Width, record.GetProperty("width").GetInt32());
        Assert.Equal(Height, record.GetProperty("height").GetInt32());
        Assert.Equal(1, record.GetProperty("mips").GetInt32());
        int layers = record.GetProperty("arraySize").GetInt32();
        Assert.Equal(3, layers);
        var texture = Texture3D.Create(Width, Height, layers, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray);
        try
        {
            var subresources = record.GetProperty("subresources").EnumerateArray().ToArray();
            Assert.Equal(layers, subresources.Length);
            foreach (var subresource in subresources)
            {
                Assert.Equal(0, subresource.GetProperty("mip").GetInt32());
                int layer = subresource.GetProperty("layer").GetInt32();
                float[] pixels = DecodeHalf(ReadPayload(subresource, checked(Width * Height * 8)));
                // Half values are exactly representable as float; the upload returns to the original RGBA16F storage.
                texture.UploadDataImmediate(pixels, 0, 0, layer, Width, Height, 1);
                using var restored = texture.ReadPixelsRegion(0, 0, Width, Height, layer);
                Assert.True(restored.Span.SequenceEqual(pixels));
            }
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }
    #endregion

    #region Payload verification and decoding
    /// <summary>Checks exported length and digest before a payload is interpreted or uploaded.</summary>
    private byte[] ReadPayload(JsonElement record, int expectedBytes)
    {
        Assert.Equal(expectedBytes, record.GetProperty("bytes").GetInt32());
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, record.GetProperty("file").GetString()!));
        Assert.Equal(expectedBytes, bytes.Length);
        Assert.Equal(record.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)));
        return bytes;
    }

    /// <summary>Decodes exported little-endian R32F texels without resampling.</summary>
    private static float[] DecodeFloat(byte[] bytes)
    {
        float[] pixels = new float[bytes.Length / 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
        return pixels;
    }

    /// <summary>Expands half-float channels exactly for the existing texture upload and readback APIs.</summary>
    private static float[] DecodeHalf(byte[] bytes)
    {
        float[] pixels = new float[bytes.Length / 2];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2)));
        return pixels;
    }
    #endregion
    #endregion
}
