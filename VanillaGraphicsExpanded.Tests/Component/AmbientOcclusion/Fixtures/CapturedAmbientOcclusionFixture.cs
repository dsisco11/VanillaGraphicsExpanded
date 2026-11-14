using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;


using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.AmbientOcclusion.Fixtures;

/// <summary>Restores exported AO inputs at their native precision, validates uploads, and prepares captured-scene comparisons.</summary>
internal sealed class CapturedAmbientOcclusionFixture : IDisposable
{
    private readonly VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservations imageAssertions;
    private readonly GpuResourceCollection resources = new();
    private readonly JsonDocument manifest;
    private readonly TestAssetSnapshot inputs;
    private readonly DynamicTexture2D depth, hierarchy;
    private readonly Texture3D surface;
    private readonly VgeFrameUniformBuffer camera;
    private readonly PostSsaoShaderProgram horizon;
    private readonly MinimumChildMaskShaderProgram maskShader;
    private readonly MinimumChildMaskPass comparisonMasks=new();

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
    internal CapturedAmbientOcclusionFixture(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservations observations, ComponentShaderPrograms programs, TestAssetSnapshot inputs)
    {
        imageAssertions = observations;
        observations.Programs.Prepare("compare", 0, 0);
        this.inputs = inputs;
        manifest = JsonDocument.Parse(inputs.Copy("manifest.json"));
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
            // This immutable capture predates source calibration; its camera prefix remains exact.
            byte[] frameBytes = CapturedPayload.Read(inputs, blocks.Single(block => block.GetProperty("binding").GetString() == "vgeFrame"),
                VgeFrameUniformBuffer.LightingCalibrationOffset);
            camera = TestFrameCamera.CreateFromPacked(frameBytes);
            // AO reads these two matrices directly. Other frame fields are recaptured by their typed owner.
            Assert.True(camera.Bytes.Slice(VgeFrameUniformBuffer.ViewOffset, VgeFrameUniformBuffer.MatrixSizeBytes * 2)
                .SequenceEqual(frameBytes.AsSpan(VgeFrameUniformBuffer.ViewOffset, VgeFrameUniformBuffer.MatrixSizeBytes * 2)));
            Assert.Equal((float)Width, UboPacking.ReadFloat(frameBytes, VgeFrameUniformBuffer.ScreenSizeOffset));
            Assert.Equal((float)Height, UboPacking.ReadFloat(frameBytes, VgeFrameUniformBuffer.ScreenSizeOffset + sizeof(float)));
            byte[] parameterBytes = CapturedPayload.Read(inputs, blocks.Single(block => block.GetProperty("binding").GetString() == "AmbientOcclusionInputs_var"), 48);
            float[] parameters = DecodeFloat(parameterBytes);
            Assert.Equal(new float[] { 0, 0, 2, 0, 1.25f, .25f, 6, 6, 64, 96, 0, 0 }, parameters);
            Quality = 2;

            var output = root.GetProperty("output");
            Assert.Equal("R16G16B16A16_FLOAT", output.GetProperty("format").GetString());
            Assert.Equal((Width + 1) / 2, output.GetProperty("width").GetInt32());
            Assert.Equal((Height + 1) / 2, output.GetProperty("height").GetInt32());
            CapturedOutput = DecodeHalf(CapturedPayload.Read(inputs, output, checked((Width + 1) / 2 * ((Height + 1) / 2) * 8)));
            horizon = programs.Create<PostSsaoShaderProgram>();
            maskShader = programs.Create<MinimumChildMaskShaderProgram>();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Prepares both shaders with identical captured resources and unchanged high-quality parameters.</summary>
    internal (Action Reference, Action Current) PrepareComparison(AoHorizonReferenceProgram reference,
        PostprocessDraw draw, GpuFramebuffer referenceTarget, GpuFramebuffer currentTarget)
    {
        var maskPipeline=comparisonMasks.Prepare(draw,Width,Height,maskShader);
        {
    var commands=RHICommandContext.Current;
    using var scope36=commands.EnterBoundary(new("Tests.CapturedAoMasks",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([maskPipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope36.Run(()=>comparisonMasks.Render(commands,draw,hierarchy));
}
        reference.Capture(depth, surface, hierarchy, camera, Quality);
        horizon.DepthImage = depth; horizon.SurfaceImage = surface; horizon.DepthHierarchy = hierarchy;
        horizon.MinimumChildren=comparisonMasks.Texture;
        horizon.Capture(camera, 2, 6, 6, radius: 1.25f, relaxation: .25f, fadeStart: 64, fadeEnd: 96);
        var referencePipeline = draw.Prepare(reference,referenceTarget.PostprocessPass());
        var currentPipeline = draw.Prepare(horizon,currentTarget.PostprocessPass());
        // Native uploads, verification, hierarchy masks, preparation and descriptor mutation precede horizon-only timing.
        return (
            () => {
    var commands=RHICommandContext.Current;
    using var scope37=commands.EnterBoundary(new("Tests.CapturedAoReference",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([referencePipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope37.Run(()=>draw.Submit(commands, referencePipeline,referenceTarget.PostprocessPass(), reference.CaptureBindings()));
},
            () => {
    var commands=RHICommandContext.Current;
    using var scope38=commands.EnterBoundary(new("Tests.CapturedAoCurrent",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([currentPipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope38.Run(()=>draw.Submit(commands, currentPipeline,currentTarget.PostprocessPass(), horizon.CaptureBindings()));
});
    }

    /// <summary>Retires all restored GPU inputs and parsed metadata together.</summary>
    public void Dispose() { comparisonMasks.Dispose();camera?.Dispose(); resources.Dispose(); manifest.Dispose(); }
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
        var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, Width), Height = System.Math.Max(1, Height), MipLevels = levels, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (levels) - 1, Label = null }).RequireResource();
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
                float[] pixels = DecodeFloat(CapturedPayload.Read(inputs, subresource, checked(width * height * 4)));
                using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture.TextureId))
                using (OpenGLPixelLayouts.UnpackScope(OpenGlStateCache.Current, new OpenGlStateCache.PixelUnpackState(4)))
                    GL.TexSubImage2D(TextureTarget.Texture2D, mip, 0, 0, width, height, PixelFormat.Red, PixelType.Float, pixels);
                imageAssertions.Comparisons.Compare(texture,pixels,selection:GpuImageSnapshot.Mip(texture,mip)).AssertEqual();
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
        var texture = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, Width), Height = System.Math.Max(1, Height), Depth = System.Math.Max(1, layers), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        try
        {
            var subresources = record.GetProperty("subresources").EnumerateArray().ToArray();
            Assert.Equal(layers, subresources.Length);
            foreach (var subresource in subresources)
            {
                Assert.Equal(0, subresource.GetProperty("mip").GetInt32());
                int layer = subresource.GetProperty("layer").GetInt32();
                float[] pixels = DecodeHalf(CapturedPayload.Read(inputs, subresource, checked(Width * Height * 8)));
                // Half values are exactly representable as float; the upload returns to the original RGBA16F storage.
                texture.UploadDataImmediate(pixels, 0, 0, layer, Width, Height, 1);
                imageAssertions.Comparisons.Compare(texture,pixels,selection:new(new(0,layer),0,0,0,Width,Height)).AssertEqual();
            }
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }
    #endregion

    #region Payload verification and decoding
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
