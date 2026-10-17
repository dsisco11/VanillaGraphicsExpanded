using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises production cloud scheduling with real binaries and controlled borrowed publications.</summary>
[Collection("GPU")][Trait("Category", "GPU")]
public sealed class CloudPipelineIntegrationTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Certified, revoked and failed summaries preserve complete production reconstruction in either lighting selection.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionReconstructionValidatesSourcesAndRecovers(bool lumOn)
    {
        EnsureShaderTestAvailable();
        bool? previous = PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled = lumOn;
            using var scene = new ScheduledScene(Programs);
            using var original = scene.CreateReconstruction();
            using var accelerated = scene.CreateReconstruction();
            using var hierarchy = new CloudHierarchy(() => CloudHierarchyComputeShader.Create(Programs.Api));
            using var unavailable = new CloudHierarchy(() => throw new InvalidOperationException("Unavailable optional hierarchy"));
            for (uint frame = 0; frame < 5; frame++)
            {
                var input = scene.Capture(frame);
                Assert.True(hierarchy.TryBuild(input, () => true), hierarchy.Failure);
                Assert.True(hierarchy.TryGetCurrent(input, out var bounds));
                CloudHierarchy selected = frame == 3 ? unavailable : hierarchy;
                if (frame == 2) hierarchy.ObserveSource(input with { Generation = input.Generation + 1 });
                if (frame == 3) Assert.False(unavailable.TryBuild(input, () => true));

                scene.Clear();
                Assert.True(scene.Draw(original, input, null, () => true));
                var expected = scene.ReadOutput();
                var expectedTrace = ReadPrivate<CloudVolumeSurface>(original, "trace").Images.Select(image => image.ReadPixels()).ToArray();
                scene.Clear();
                Assert.True(scene.Draw(accelerated, input, selected, () => true));
                Assert.Equal(frame is not (2 or 3), scene.HierarchyEnabled);
                Assert.Equal(frame is not (0 or 2), scene.HistoryEnabled);
                var actual = scene.ReadOutput();
                for (int layer = 0; layer < 6; layer++) Assert.Equal(expected[layer], actual[layer]);
                var traced = ReadPrivate<CloudVolumeSurface>(accelerated, "trace");
                for (int layer = 0; layer < 3; layer++) Assert.Equal(expectedTrace[layer], traced.Images[layer].ReadPixels());
                Assert.Contains(actual[1], value => value < 1);

                // Revocation must reject all three passes, leave OIT untouched and never commit history.
                if (frame == 1)
                {
                    bool swapped = ReadPrivate<bool>(accelerated, "swap");
                    Assert.False(scene.Draw(accelerated, input, hierarchy, () => false));
                    Assert.Equal(swapped, ReadPrivate<bool>(accelerated, "swap"));
                    Assert.False(ReadPrivate<bool>(ReadPrivate<CloudTemporalHistory>(accelerated, "history"), "valid"));
                    var afterRejection = scene.ReadOutput();
                    for (int layer = 0; layer < 6; layer++) Assert.Equal(actual[layer], afterRejection[layer]);
                    original.Invalidate();
                }
                Assert.True(bounds!.IsValid);
            }

            // Reset is the common resize/reload boundary. Both domains retire owned storage only.
            var oldTrace = ReadPrivate<CloudVolumeSurface>(accelerated, "trace");
            Assert.True(hierarchy.TryGetCurrent(scene.Input, out var retired));
            accelerated.Reset(); hierarchy.Reset();
            Assert.All(oldTrace.Images, image => Assert.False(image.IsValid));
            Assert.False(retired!.IsValid);
            scene.AssertBorrowedInputsAlive();
            scene.AttachPrograms(accelerated);
            scene.Resize(11, 9);
            var replacement = scene.Capture(6) with { World = 2, Allocation = 2, Owner = new object() };
            Assert.True(hierarchy.TryBuild(replacement, () => true), hierarchy.Failure);
            scene.Clear(); Assert.True(scene.Draw(accelerated, replacement, hierarchy, () => true));
            Assert.NotSame(oldTrace, ReadPrivate<CloudVolumeSurface>(accelerated, "trace"));
            Assert.True(scene.HierarchyEnabled);
            Assert.Equal(6, ReadPrivate<CloudVolumeSurface>(accelerated, "trace").Target.Width);
            Assert.False(scene.HistoryEnabled);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled = previous; }
    }
    #endregion

    #region Private
    /// <summary>Observes private scheduler state without adding production diagnostics or alternate execution paths.</summary>
    private static T ReadPrivate<T>(object owner, string name) => (T)AccessTools.Field(owner.GetType(), name).GetValue(owner)!;

    /// <summary>Owns tiny test publications while the production scheduler owns trace and history allocations.</summary>
    private sealed class ScheduledScene : IDisposable
    {
        private readonly Mock<ICoreClientAPI> api = new() { DefaultValue = DefaultValue.Mock };
        private readonly List<IDisposable> owned = [];
        private readonly CloudTraceProgram trace;
        private readonly CloudTemporalProgram temporal;
        private readonly CloudResolveProgram resolve;
        private readonly VgeFrameRenderer frameOwner;
        private readonly TestFrameLightingPublication storage;
        private readonly DynamicTexture2D map;
        private DynamicTexture2D depth = null!, visibility = null!, liquid = null!;
        private readonly DynamicTexture3D aerial, loss;
        private CloudOitSurface output = null!;
        private int width, height;
        private readonly ArrayGraphicsGeometry geometry;
        private readonly DynamicTexture3D? previousAerial = AtmosphereModSystem.AerialRadianceTexture;
        private readonly DynamicTexture3D? previousLoss = AtmosphereModSystem.AerialAttenuationTexture;
        private readonly float[] projection = [4,0,0,0,0,4,0,0,0,0,-1.00020002f,-1,0,0,-.200020002f,0];
        private readonly float[] inverse = new float[16];
        private readonly float[] identity = Mat4f.Create();
        private readonly AtmosphereLighting lighting = new(Vector3.UnitY, new(30), new(10), Vector3.Zero, Vector3.Zero, ImmutableArray<float>.Empty)
            { Moon = -Vector3.UnitY, Lunar = new(3), CalibrationGeneration = 1, LookupStorageScale = LightingStorage.PersistentScale };

        #region Public API
        #region Fixture publication
        /// <summary>Reports the last certified synthetic native publication.</summary>
        internal CloudMapInput Input { get; private set; }
        /// <summary>Reads the actual trace contract's acceleration flag after production submission.</summary>
        internal bool HierarchyEnabled => UboPacking.ReadUInt32(((ICloudTraceBindings)trace).TraversalInputs.Bytes, 0) != 0;
        /// <summary>Reads the production history-admission flag supplied to all three passes.</summary>
        internal bool HistoryEnabled => BitConverter.ToSingle(trace.Temporal.Bytes.Slice(96, 4)) == 1;

        /// <summary>Creates real cloud binaries and main-view publication without starting native weather or the game.</summary>
        internal ScheduledScene(ComponentShaderPrograms programs)
        {
            api.SetupGet(value => value.Render.FrameWidth).Returns(() => width);
            api.SetupGet(value => value.Render.FrameHeight).Returns(() => height);
            api.SetupGet(value => value.Render.ShaderUniforms).Returns(new DefaultShaderUniforms());
            api.SetupGet(value => value.Ambient.BlendedFogColor).Returns(new Vec4f());
            // The installed player interface cannot be proxied by Castle; retain the engine's
            // lightweight player/data relationship without constructing a running client.
            var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
            var worldData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
            AccessTools.Field(typeof(ClientWorldPlayerData), "entityplayer").SetValue(worldData, new EntityPlayer());
            AccessTools.Field(typeof(ClientPlayer), "worlddata").SetValue(player, worldData);
            api.SetupGet(value => value.World.Player).Returns(player);
            frameOwner = Own(new VgeFrameRenderer(api.Object));
            AccessTools.Field(typeof(VgeFrameRenderer), "published").SetValue(frameOwner, true);
            Mat4f.Invert(inverse, projection);
            storage = Own(new TestFrameLightingPublication());
            trace = programs.Create<CloudTraceProgram>(); temporal = programs.Create<CloudTemporalProgram>(); resolve = programs.Create<CloudResolveProgram>();
            trace.Lighting = storage.Buffer; resolve.Lighting = storage.Buffer;
            map = Own(DynamicTexture2D.Create(21, 21, PixelInternalFormat.Rgba32f));
            aerial = Own(DynamicTexture3D.Create(1, 2, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D));
            loss = Own(DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D));
            aerial.UploadDataImmediate(new float[8], 0, 0, 0, 1, 2, 1, 0); loss.UploadDataImmediate(new float[4], 0, 0, 0, 1, 1, 1, 0);
            SetAtmosphere(nameof(AtmosphereModSystem.AerialRadianceTexture), aerial);
            SetAtmosphere(nameof(AtmosphereModSystem.AerialAttenuationTexture), loss);
            Resize(9, 7);
            geometry = Own(new ArrayGraphicsGeometry(CloudVolumeRenderer.Layout, PrimitiveType.Triangles, new Dictionary<int, GpuVbo>(), proceduralVertices: 3));
        }

        /// <summary>Authors animated sparse native columns and separate current material, particle and liquid clips.</summary>
        internal CloudMapInput Capture(uint frame)
        {
            VgeFrameRenderer.Current.Capture(projection, identity, inverse, identity, projection, projection, new(width, height), 0, frame, Vector3.Zero, Vector3.Zero, 0, new(.1f, 1000));
            storage.Capture(1, frame);
            float[] columns = new float[21 * 21 * 4];
            for (int i = 0; i < 21 * 21; i++)
            {
                if (i % 4 == 0) continue;
                columns[i * 4] = .125f; columns[i * 4 + 2] = -.5f + frame * .001f; columns[i * 4 + 3] = .5f + frame * .001f;
            }
            map.UploadDataImmediate(columns);
            depth.UploadDataImmediate(Enumerable.Range(0, width * height).Select(i => Depth(i % 5 == 0 ? 20 : 1000)).ToArray());
            visibility.UploadDataImmediate(Enumerable.Range(0, width * height).Select(i => Depth(i % 3 == 0 ? 20 : 1000)).ToArray());
            liquid.UploadDataImmediate(Enumerable.Range(0, width * height).Select(i => Depth(i % 7 == 0 ? 60 : 1000)).ToArray());
            Input = new(this, 1, 1, 1, 1, frame + 1, frame, new(map.TextureId, 2, 3, 4, 21, 50, 5),
                new(0, 0, 0, 0, 0, 0, 0, 0, new(0, 0, -50), 0));
            return Input;
        }
        /// <summary>Replaces fixture-owned engine attachments independently of the production resource reset.</summary>
        internal void Resize(int newWidth, int newHeight)
        {
            width = newWidth; height = newHeight;
            depth?.Dispose(); visibility?.Dispose(); liquid?.Dispose(); output?.Dispose();
            depth = Own(DynamicTexture2D.Create(width, height, PixelInternalFormat.R32f));
            visibility = Own(DynamicTexture2D.Create(width, height, PixelInternalFormat.R32f));
            liquid = Own(DynamicTexture2D.Create(width, height, PixelInternalFormat.R32f));
            output = Own(new CloudOitSurface(width, height, "Tests.Cloud.ScheduledOit"));
        }
        #endregion

        #region Production execution
        /// <summary>Injects fixture-loaded executables, retaining the production scheduler and its resource ownership.</summary>
        internal void AttachPrograms(CloudVolumeReconstruction reconstruction)
        {
            AccessTools.Field(typeof(CloudVolumeReconstruction), "traceProgram").SetValue(reconstruction, trace);
            AccessTools.Field(typeof(CloudVolumeReconstruction), "temporalProgram").SetValue(reconstruction, temporal);
            AccessTools.Field(typeof(CloudVolumeReconstruction), "resolveProgram").SetValue(reconstruction, resolve);
            AccessTools.Field(typeof(CloudVolumeReconstruction), "pmjJitter").SetValue(reconstruction, CloudPmjJitterTexture.Create());
        }
        /// <summary>Creates an independent production scheduler using the same controlled borrowed inputs.</summary>
        internal CloudVolumeReconstruction CreateReconstruction()
        {
            var reconstruction = new CloudVolumeReconstruction(api.Object); AttachPrograms(reconstruction); return reconstruction;
        }
        /// <summary>Executes production source admission and all three passes against the existing OIT blend contract.</summary>
        internal bool Draw(CloudVolumeReconstruction reconstruction, CloudMapInput input, CloudHierarchy? hierarchy, Func<bool> current) =>
            reconstruction.Draw(input, lighting, CloudVolumeSettings.Default, output.Target, depth.TextureId, visibility.TextureId, liquid.TextureId, geometry, current, hierarchy);
        /// <summary>Clears only fixture attachments to the native neutral OIT record before a comparison draw.</summary>
        internal void Clear()
        {
            int previous = GL.GetInteger(GetPName.DrawFramebufferBinding);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, output.Target.FboId);
            GL.ClearBuffer(ClearBuffer.Color, 0, new float[] { 1, 1, 1, 1 });
            GL.ClearBuffer(ClearBuffer.Color, 1, new float[] { 1, 0, 0, 0 });
            for (int layer = 2; layer < 6; layer++) GL.ClearBuffer(ClearBuffer.Color, layer, new float[4]);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previous); StateCache.Current.InvalidateAll();
        }
        /// <summary>Reads all native OIT attachments independently of private trace and history storage.</summary>
        internal float[][] ReadOutput() => output.Images.Take(6).Select(image => image.ReadPixels()).ToArray();
        /// <summary>Checks reset/disposal did not delete any borrowed scene or native texture.</summary>
        internal void AssertBorrowedInputsAlive()
        {
            foreach (var image in new[] { map, depth, visibility, liquid }) Assert.True(image.IsValid);
            Assert.True(aerial.IsValid); Assert.True(loss.IsValid);
        }
        /// <summary>Restores ambient publication before releasing fixture-owned resources in reverse dependency order.</summary>
        public void Dispose()
        {
            SetAtmosphere(nameof(AtmosphereModSystem.AerialRadianceTexture), previousAerial);
            SetAtmosphere(nameof(AtmosphereModSystem.AerialAttenuationTexture), previousLoss);
            for (int i = owned.Count - 1; i >= 0; i--) owned[i].Dispose();
        }
        #endregion
        #endregion

        #region Private
        /// <summary>Tracks fixture-owned resources without transferring borrowed production lifetimes.</summary>
        private T Own<T>(T value) where T : IDisposable { owned.Add(value); return value; }
        /// <summary>Replaces only the fixture's temporary atmospheric handles and restores them at teardown.</summary>
        private static void SetAtmosphere(string name, DynamicTexture3D? value) => AccessTools.Property(typeof(AtmosphereModSystem), name).SetValue(null, value);
        /// <summary>Projects a controlled foreground distance through the authored perspective matrix.</summary>
        private static float Depth(float distance) => (1.00020002f - .200020002f / distance) * .5f + .5f;
        #endregion
    }
    #endregion
}
