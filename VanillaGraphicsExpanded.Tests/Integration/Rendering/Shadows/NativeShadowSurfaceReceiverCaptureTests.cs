using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using VanillaGraphicsExpanded.Tests.Integration.Support.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Exercises production camera-frustum receiver publication against installed native shader owners.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeShadowSurfaceReceiverCaptureTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Publishes complete main and hand receivers and refreshes both after a receiver moves outside the prior view.</summary>
    [Fact]
    public void NativeCaptureIncludesAllSurfaceFamiliesAndTracksCameraMovement()
    {
        EnsureContextValid();
        using var lightingMode = new NativeLightingModeScope();
        using var platform = new EngineShaderPlatformScope();
        using var owners = new NativeSurfaceShaderOwners();
        var state = new MutableCameraState();
        state.Set(0);
        var cameraWorld = new RuntimeCameraWorld(RuntimeEngineServices.CameraPlayer(() => state.Current));
        SetWindowSize(cameraWorld.World.Platform, 320, 240);
        var events = new RuntimeRenderEvents();
        float[] view = Mat4f.Create();
        float[] projection = Perspective(100f * MathF.PI / 180f, 4f / 3f, .1f, 100f);
        var render = RuntimeEngineServices.Render(1, [new FrameBufferRef { FboId = 1, Width = 1, Height = 1 }],
            () => view, () => projection, () => { });
        var api = RuntimeEngineServices.Client(owners.AssetsApi, events.Api, cameraWorld.World, render,
            owners.AssetsApi.Shader, owners.ModLoader);
        Mock.Get(api.Settings).SetupGet(settings => settings.Int["fpHandsFoV"]).Returns(70);
        using var frameOwner = new VgeFrameRenderer(api);
        var receivers = new NativeShadowSurfaceReceivers(api);

        // Act: capture at the initial native camera, then move the camera far enough that a new receiver is outside the old envelope.
        cameraWorld.RefreshCamera();
        frameOwner.BeginFrame();
        frameOwner.OnRenderFrame(.016f, EnumRenderStage.Before);
        var firstLights = LightsFromCurrentFrame(1, 1);
        var first = receivers.Capture(firstLights, ReceiverBudget());

        state.Set(512);
        cameraWorld.RefreshCamera();
        frameOwner.BeginFrame();
        frameOwner.OnRenderFrame(.016f, EnumRenderStage.Before);
        var movedLights = LightsFromCurrentFrame(2, 1);
        var moved = receivers.Capture(movedLights, ReceiverBudget());

        // Assert: each view covers all families and multiple depth layers; the independent hand FOV is narrower,
        // and the current camera's receiving envelope follows a point outside the previous publication.
        AssertPublication(first, frame: 1);
        AssertPublication(moved, frame: 2);
        Assert.Equal(8, first.Programs.Length);
        Assert.Equal(8, moved.Programs.Length);
        Assert.All(first.Programs, program => Assert.True(ShaderCapabilities.Has(program, ShaderCapability.SharedVertexDeformation)));
        Assert.All(moved.Programs, program => Assert.True(ShaderCapabilities.Has(program, ShaderCapability.SharedVertexDeformation)));
        var firstMain = first.Regions[0].Bounds;
        var firstHands = first.Regions[1].Bounds;
        var movedMain = moved.Regions[0].Bounds;
        Assert.True(firstMain.MaxX - firstMain.MinX > firstHands.MaxX - firstHands.MinX,
            "The independent first-person FOV should enclose a narrower horizontal receiver extent.");
        AssertContains(firstMain, new(0, 0, -2));
        AssertContains(firstMain, new(0, 0, -80));
        var newlyVisibleReceiver = new ShadowBounds(512, 0, -2, 512, 0, -2);
        Assert.False(firstMain.Contains(newlyVisibleReceiver));
        Assert.True(movedMain.Contains(newlyVisibleReceiver));

    }
    #endregion

    #region Private
    /// <summary>Requires both eye-near and eye-far receiver points to fit in a conservative published volume.</summary>
    private static void AssertContains(ShadowBounds bounds, Vector3 point)
    {
        var sample = new ShadowBounds(point.X, point.Y, point.Z, point.X, point.Y, point.Z);
        Assert.True(bounds.Contains(sample), $"Expected receiver point {point} inside {bounds}.");
    }

    /// <summary>Checks the production publication interval, category completeness, and both independent camera volumes.</summary>
    private static void AssertPublication(ShadowSurfacePublication publication, long frame)
    {
        Assert.Equal(frame, publication.Frame);
        Assert.Equal(1, publication.World);
        Assert.Equal(0, publication.Dimension);
        Assert.Equal(ShadowSurfaceCategory.All, publication.CompleteCategories);
        Assert.Equal(2, publication.Regions.Length);
        Assert.All(publication.Regions, region => Assert.Equal(ShadowSurfaceCategory.All, region.Category));
        Assert.All(publication.Regions, region => Assert.True(region.Bounds.IsValid));
        Assert.Equal(8, publication.Programs.Length);
        Assert.Equal(8, publication.Programs.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    /// <summary>Builds the light-view snapshot from the exact current camera matrices and render-origin bridge.</summary>
    private static ShadowLightSnapshot LightsFromCurrentFrame(long frame, long world)
    {
        var current = VgeFrameRenderer.Current;
        var view = current.View.ToArray().Select(value => (double)value).ToImmutableArray();
        var origin = current.RenderOriginChunk;
        return new(frame, world, 0, [], default, default, view, origin.X, origin.Y, origin.Z,
            current.RenderOriginRemainder);
    }

    /// <summary>Gives receiver capture the complete finite input and storage allowance it requests.</summary>
    private static ShadowFrameBudget ReceiverBudget() => new(new(Inputs: 64, Bytes: 8192), 0);

    /// <summary>Creates a finite OpenGL perspective matrix with the native clip convention expected by the capture.</summary>
    private static float[] Perspective(float verticalFov, float aspect, float near, float far)
    {
        float focal = 1f / MathF.Tan(verticalFov * .5f);
        float[] result = new float[16];
        result[0] = focal / aspect;
        result[5] = focal;
        result[10] = -(far + near) / (far - near);
        result[11] = -1;
        result[14] = -(2 * far * near) / (far - near);
        return result;
    }

    /// <summary>Sets only the installed platform's window-size backing storage without constructing a window.</summary>
    private static void SetWindowSize(ClientPlatformAbstract platform, int width, int height)
    {
        var property = typeof(ClientPlatformAbstract).GetProperty("WindowSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(ClientPlatformAbstract).FullName, "WindowSize");
        var size = Activator.CreateInstance(property.PropertyType, width, height)
            ?? throw new InvalidOperationException($"Could not create native window-size value {property.PropertyType.FullName}.");
        if (property.SetMethod is { } setter) { setter.Invoke(platform, [size]); return; }
        for (Type? type = platform.GetType(); type is not null; type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (field.FieldType == property.PropertyType && field.Name.Contains("WindowSize", StringComparison.OrdinalIgnoreCase))
                { field.SetValue(platform, size); return; }
        throw new MissingFieldException($"No installed window-size storage backs {property.Name}.");
    }

    /// <summary>Holds mutable camera samples while the installed player and production frame owner retain their native identities.</summary>
    private sealed class MutableCameraState
    {
        internal LumOnCameraState? Current { get; private set; }
        /// <summary>Moves the native camera and player position together.</summary>
        internal void Set(double x) => Current = new(x, 0, 0, x, 0, 0, 0);
    }

    /// <summary>Captures and restores global PBR lighting settings around native shader-owner setup and receiver capture.</summary>
    private sealed class NativeLightingModeScope : IDisposable
    {
        private readonly bool previousMode;
        private readonly bool? previousGeneration;

        /// <summary>Saves both original settings before enabling the forward shader mode needed by the fixture.</summary>
        internal NativeLightingModeScope()
        {
            previousMode = ConfigModSystem.Config.LumOn.Enabled;
            previousGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
            ConfigModSystem.Config.LumOn.Enabled = true;
            PbrShaderLightingMode.GenerationLumOnEnabled = true;
        }

        /// <summary>Restores the exact pre-test settings when the enclosing test exits, including exceptional exits.</summary>
        public void Dispose()
        {
            ConfigModSystem.Config.LumOn.Enabled = previousMode;
            PbrShaderLightingMode.GenerationLumOnEnabled = previousGeneration;
        }
    }

    /// <summary>Owns linked installed shader executables and restores the global program table after capture.</summary>
    private sealed class NativeSurfaceShaderOwners : IDisposable
    {
        private readonly List<GpuProgramObject> executables = [];
        private readonly List<ShaderProgramBase> programs = [];
        private readonly List<(FieldInfo Field, object? Previous)> replacedFields = [];
        internal BinaryShaderApiFixture Assets { get; } = new();
        internal ICoreClientAPI AssetsApi => Assets.Api;
        internal IModLoader ModLoader { get; }

        /// <summary>Links the installed native surface families and gives them the engine's real capability gates.</summary>
        internal NativeSurfaceShaderOwners()
        {
            var modLoader = new Mock<IModLoader>(MockBehavior.Strict);
            var firstPerson = (ModSystemFpHands)RuntimeHelpers.GetUninitializedObject(typeof(ModSystemFpHands));
            ModLoader = modLoader.Object;
            modLoader.Setup(loader => loader.GetModSystem<ModSystemFpHands>()).Returns(firstPerson);
            Install("Chunkopaque", "chunkopaque", forward: false);
            Install("Chunktopsoil", "chunktopsoil", forward: false);
            var standard = Install("Standard", "standard", forward: true);
            Install("Entityanimated", "entityanimated", forward: true);
            Install("Instanced", "instanced", forward: true);
            Install("Chunktransparent", "chunktransparent", forward: true);
            var item = InstallStandaloneForward("fp-item-owner", standard.ProgramId);
            var hand = InstallStandaloneForward("fp-hand-owner", standard.ProgramId);
            AccessTools.Field(typeof(ModSystemFpHands), "fpModeItemShader").SetValue(firstPerson, item);
            AccessTools.Field(typeof(ModSystemFpHands), "fpModeHandShader").SetValue(firstPerson, hand);
        }

        /// <summary>Builds one real installed shader and installs its native typed owner into the engine's program table.</summary>
        private ShaderProgramBase Install(string fieldName, string family, bool forward)
        {
            ShaderCapability declared = ShaderCapability.None;
            int oit = family == "chunktransparent" ? 1 : 0;
            string vertex = InstalledEngineShaderSources.Build(family + ".vsh", 0, oit, 0, 0, 0, value => declared |= value);
            string fragment = InstalledEngineShaderSources.Build(family + ".fsh", 0, oit, 0, 0, 0, value => declared |= value);
            var executable = GpuProgramObject.Adopt(TestShaderCache.CreateGraphics(vertex, fragment));
            executables.Add(executable);
            var field = AccessTools.Field(typeof(ShaderPrograms), fieldName)
                ?? throw new MissingFieldException(typeof(ShaderPrograms).FullName, fieldName);
            var owner = (ShaderProgramBase)RuntimeHelpers.GetUninitializedObject(field.FieldType);
            owner.PassName = family;
            owner.ProgramId = executable.ProgramId;
            replacedFields.Add((field, field.GetValue(null)));
            field.SetValue(null, owner);
            if (forward)
            {
                var native = Assert.IsAssignableFrom<ShaderProgram>(owner);
                ShaderCapabilities.Publish(native, declared | ShaderCapability.SharedVertexDeformation);
                SurfaceShadowPrograms.Register(native);
            }
            else ShaderCapabilities.Publish(owner, declared | ShaderCapability.SharedVertexDeformation | ShaderCapability.SceneMaterialCapture);
            programs.Add(owner);
            return owner;
        }

        /// <summary>Uses a distinct managed first-person shader owner over the actual linked standard-family executable.</summary>
        private ShaderProgram InstallStandaloneForward(string name, int executable)
        {
            var owner = new ShaderProgram { PassName = "standard", ProgramId = executable };
            ShaderCapabilities.Publish(owner, ShaderCapability.SharedVertexDeformation);
            SurfaceShadowPrograms.Register(owner);
            programs.Add(owner);
            return owner;
        }

        /// <summary>Restores static owners and capability registrations before retiring all linked executables.</summary>
        public void Dispose()
        {
            foreach (var (field, previous) in replacedFields.AsEnumerable().Reverse()) field.SetValue(null, previous);
            foreach (var program in programs)
            {
                if (program is ShaderProgram native) SurfaceShadowPrograms.Remove(native);
                ShaderCapabilities.Forget(program);
            }
            foreach (var executable in executables) executable.Dispose();
            Assets.Dispose();
        }
    }
    #endregion
}
