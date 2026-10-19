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
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises production cloud scheduling with real binaries and controlled borrowed publications.</summary>
[Collection("GPU")][Trait("Category", "GPU")]
public sealed class CloudPipelineIntegrationTests(HeadlessGLFixture fixture,ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Each rotated production pass publishes the intended disjoint bindings and restores inherited bindings afterward.</summary>
    [Fact]
    public void RotatingArrayBlocksPublishDisjointBindings()
    {
        EnsureShaderTestAvailable();using var scene=new ScheduledScene(Programs);using var reconstruction=scene.CreateReconstruction();
        using var sentinel=DynamicTexture3D.Create(1,1,1,PixelInternalFormat.Rgba32f);
        using var currentScope=StateCache.Current.BindTextureScope(TextureTarget.Texture2DArray,0,sentinel.TextureId);
        using var historyScope=StateCache.Current.BindTextureScope(TextureTarget.Texture2DArray,7,sentinel.TextureId);
        using var capture=new CloudArrayBindingCapture();var records=new List<object>();
        for(uint frame=0;frame<4;frame++)
        {
            var input=scene.Capture(frame);capture.Draws.Clear();scene.Clear();
            Assert.True(scene.Draw(reconstruction,input,null,()=>true));Assert.Equal(3,capture.Draws.Count);
            var blocks=ReadPrivate<CloudVolumeStorage>(reconstruction,"storage").Blocks;int cursor=(int)(frame%3);
            Assert.Equal(blocks[cursor].Target.FboId,capture.Draws[0].Framebuffer);
            Assert.Equal(blocks[(cursor+2)%3].Target.FboId,capture.Draws[1].Framebuffer);
            Assert.Equal(blocks[cursor].View.TextureId,capture.Draws[1].Current);
            Assert.Equal(blocks[(cursor+1)%3].View.TextureId,capture.Draws[1].History);
            Assert.Equal(blocks[(cursor+2)%3].View.TextureId,capture.Draws[2].History);
            Assert.Equal((sentinel.TextureId,sentinel.TextureId),CloudArrayBindingCapture.ReadBindings());
            records.Add(new{frame,cursor,array=ReadPrivate<CloudVolumeStorage>(reconstruction,"storage").Array.TextureId,
                blocks=blocks.Select(block=>new{view=block.View.TextureId,baseLayer=block.View.BaseLayer,layers=block.View.LayerCount,framebuffer=block.Target.FboId}).ToArray(),
                draws=capture.Draws.Select(draw=>new{framebuffer=draw.Framebuffer,currentPayload=draw.Current,historyPayload=draw.History}).ToArray()});
        }
        // Fail before temporal submission after trace has already written its current block.
        var failedInput=scene.Capture(4);int heldCursor=ReadPrivate<int>(reconstruction,"cursor");
        capture.Draws.Clear();capture.FailAtDraw=2;
        Assert.Throws<InvalidOperationException>(()=>scene.Draw(reconstruction,failedInput,null,()=>true));
        Assert.Equal(heldCursor,ReadPrivate<int>(reconstruction,"cursor"));
        Assert.False(ReadPrivate<bool>(ReadPrivate<CloudTemporalHistory>(reconstruction,"history"),"valid"));
        Assert.Equal((sentinel.TextureId,sentinel.TextureId),CloudArrayBindingCapture.ReadBindings());
        capture.FailAtDraw=-1;capture.Draws.Clear();
        Assert.True(scene.Draw(reconstruction,scene.Capture(5),null,()=>true));Assert.False(scene.HistoryEnabled);
        // Static payload attachments retain allocation guards until Reset; successful retirement proves failed-pass borrows ended.
        var retired=ReadPrivate<CloudVolumeStorage>(reconstruction,"storage");
        Assert.Throws<InvalidOperationException>(()=>retired.Array.Resize(2,2,9));reconstruction.Reset();Assert.False(retired.Array.IsValid);
        scene.AssertBorrowedInputsAlive();scene.AttachPrograms(reconstruction);capture.Draws.Clear();
        Assert.True(scene.Draw(reconstruction,scene.Capture(6),null,()=>true));Assert.False(scene.HistoryEnabled);
        output.WriteLine("ARRAY_BINDING_CAPTURE="+System.Text.Json.JsonSerializer.Serialize(new{frames=records,
            failedSubmission=2,cursorHeld=true,historyInvalidated=true,inheritedBindingsRestored=true,
            recoveryHistoryRejected=true,resizeBlockedUntilReset=true,resetRetiredStorage=true,resetRecovery=true}));
        GlDebug.ThrowIfErrors("Cloud pipeline test");
    }
    /// <summary>Quarter storage keeps fixed trace and alternating history roles, and retires both owners on layout changes.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void QuarterLayoutsReuseAndRetireOwnedStorage(bool lumOn)
    {
        EnsureShaderTestAvailable();bool? oldMode=PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled=lumOn;
            using var scene=new ScheduledScene(Programs);using var reconstruction=scene.CreateReconstruction();
            var quality=CloudVolumeSettings.Default with {TraceScale=CloudTraceScale.Quarter};
            CloudVolumeStorage? histories=null,traced=null;
            for(uint frame=0;frame<4;frame++)
            {
                scene.Clear();Assert.True(scene.Draw(reconstruction,scene.Capture(frame),null,()=>true,quality));
                var current=ReadPrivate<CloudVolumeStorage>(reconstruction,"storage");
                var currentTrace=ReadPrivate<CloudVolumeStorage>(reconstruction,"traceStorage");
                if(histories is not null){Assert.Same(histories,current);Assert.Same(traced,currentTrace);}
                histories=current;traced=currentTrace;
                Assert.Equal(2,histories.Blocks.Length);Assert.Single(traced.Blocks);
                Assert.Equal(5,histories.Array.Width);Assert.Equal(4,histories.Array.Height);
                Assert.Equal(3,traced.Array.Width);Assert.Equal(2,traced.Array.Height);
                Assert.Equal((int)((frame+1)%2),ReadPrivate<int>(reconstruction,"cursor"));
                Assert.Equal(frame>0,scene.HistoryEnabled);
            }
            int held=ReadPrivate<int>(reconstruction,"cursor");
            Assert.False(scene.Draw(reconstruction,scene.Capture(4),null,()=>false,quality));
            Assert.Equal(held,ReadPrivate<int>(reconstruction,"cursor"));
            Assert.True(scene.Draw(reconstruction,scene.Capture(5),null,()=>true,quality));Assert.False(scene.HistoryEnabled);
            Assert.True(scene.Draw(reconstruction,scene.Capture(6),null,()=>true));
            Assert.False(histories!.Array.IsValid);Assert.False(traced!.Array.IsValid);Assert.False(scene.HistoryEnabled);
            var half=ReadPrivate<CloudVolumeStorage>(reconstruction,"storage");
            Assert.True(scene.Draw(reconstruction,scene.Capture(7),null,()=>true,quality));Assert.False(half.Array.IsValid);
            histories=ReadPrivate<CloudVolumeStorage>(reconstruction,"storage");traced=ReadPrivate<CloudVolumeStorage>(reconstruction,"traceStorage");
            scene.Resize(11,9);Assert.True(scene.Draw(reconstruction,scene.Capture(8),null,()=>true,quality));
            Assert.False(histories.Array.IsValid);Assert.False(traced.Array.IsValid);Assert.False(scene.HistoryEnabled);
            histories=ReadPrivate<CloudVolumeStorage>(reconstruction,"storage");traced=ReadPrivate<CloudVolumeStorage>(reconstruction,"traceStorage");
            reconstruction.Reset();Assert.False(histories.Array.IsValid);Assert.False(traced.Array.IsValid);
            scene.AssertBorrowedInputsAlive();scene.AttachPrograms(reconstruction);
            Assert.True(scene.Draw(reconstruction,scene.Capture(9),null,()=>true,quality));Assert.False(scene.HistoryEnabled);
        }
        finally {PbrShaderLightingMode.GenerationLumOnEnabled=oldMode;}
    }
    /// <summary>Default attenuation preserves exact optical metadata and recovers from revoked sources in either lighting selection.</summary>
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
            using var failedCache=scene.CreateReconstruction(()=>throw new InvalidOperationException("Unavailable optional cloud attenuation"));
            using var hierarchy = new CloudHierarchy(() => CloudHierarchyComputeShader.Create(Programs.Api));
            using var unavailable = new CloudHierarchy(() => throw new InvalidOperationException("Unavailable optional hierarchy"));
            for (uint frame = 0; frame < 5; frame++)
            {
                var input = scene.Capture(frame);
                // Cross the former axis switch, then change dominant source while
                // ordinary map heights evolve. History remains a visible-cloud decision.
                float angle=frame==0?.16f:frame==1?.14f:0;
                scene.Lighting=scene.Lighting with {Sun=new(MathF.Sin(angle),MathF.Cos(angle),0),
                    Moon=-Vector3.UnitY,Solar=new(frame==4?1:30),Lunar=new(frame==4?30:3)};
                if(frame==1)input=input with {Placement=input.Placement with {CenterX=1,Offset=new(50,0,-50)}};
                Assert.True(hierarchy.TryBuild(input, () => true), hierarchy.Failure);
                Assert.True(hierarchy.TryGetCurrent(input, out var bounds));
                CloudHierarchy selected = frame == 3 ? unavailable : hierarchy;
                if (frame == 2) hierarchy.ObserveSource(input with { Generation = input.Generation + 1 });
                if (frame == 3) Assert.False(unavailable.TryBuild(input, () => true));

                scene.Clear();
                Assert.True(scene.Draw(original, input, null, () => true));
                var expected = scene.ReadOutput();
                var expectedTrace = CloudPayloadReadback.Read(LastTrace(original));
                scene.Clear();
                Assert.True(scene.Draw(accelerated, input, selected, () => true));
                Assert.Equal((int)((frame+1)%3),ReadPrivate<int>(accelerated,"cursor"));
                Assert.Equal(frame is not (2 or 3), scene.HierarchyEnabled);
                Assert.Equal(frame is not (2 or 3), scene.ShadowCacheEnabled);
                if(frame is not (2 or 3))Assert.Equal(frame==4?2u:1u,scene.ShadowCacheSource);
                Assert.Equal(frame is not (0 or 2), scene.HistoryEnabled);
                var actual = scene.ReadOutput();
                // Cache lighting is an intentional approximation. Native optical factors and
                // accumulation alpha retain their specified arithmetic independently of radiance.
                for (int layer = 0; layer < 6; layer++)
                for (int channel=0;channel<actual[layer].Length;channel++)
                {
                    Assert.True(float.IsFinite(actual[layer][channel])&&actual[layer][channel]>=0);
                    if(layer<3||channel%4==3)Assert.Equal(expected[layer][channel],actual[layer][channel]);
                }
                var traced = LastTrace(accelerated);
                var raw=CloudPayloadReadback.Read(traced);
                Assert.Equal(expectedTrace[1],raw[1]);Assert.Equal(expectedTrace[2],raw[2]);
                for(int channel=0;channel<raw[0].Length;channel++)
                {
                    Assert.True(float.IsFinite(raw[0][channel])&&raw[0][channel]>=0);
                    if(channel%4==3)Assert.Equal(expectedTrace[0][channel],raw[0][channel]);
                }
                var cache=ReadPrivate<CloudShadowCache>(accelerated,"shadowCache");
                var cacheTexture=ReadPrivate<DynamicTexture2D?>(cache,"texture");
                if(frame is not (2 or 3))
                {
                    Assert.NotNull(cacheTexture);Assert.True(cacheTexture.IsValid);
                    Assert.Equal(input.Frame,ReadPrivate<CloudMapInput?>(cache,"allocation")!.Value.Frame);
                }
                else Assert.Null(cacheTexture);
                Assert.Contains(actual[1], value => value < 1);

                // Optional generator failure retains the original finite shadow algorithm and
                // records the reason without preventing the production cloud passes from drawing.
                if(frame==0)
                {
                    scene.Clear();Assert.True(scene.Draw(failedCache,input,hierarchy,()=>true));
                    Assert.False(scene.ShadowCacheEnabled);
                    Assert.Contains("Unavailable optional cloud attenuation",ReadPrivate<CloudShadowCache>(failedCache,"shadowCache").Failure);
                    var fallback=CloudPayloadReadback.Read(LastTrace(failedCache));
                    for(int layer=0;layer<3;layer++)Assert.Equal(expectedTrace[layer],fallback[layer]);
                }

                // Revocation must reject all three passes, leave OIT untouched and never commit history.
                if (frame == 1)
                {
                    int cursor = ReadPrivate<int>(accelerated, "cursor");
                    Assert.False(scene.Draw(accelerated, input, hierarchy, () => false));
                    Assert.Equal(cursor, ReadPrivate<int>(accelerated, "cursor"));
                    Assert.False(ReadPrivate<bool>(ReadPrivate<CloudTemporalHistory>(accelerated, "history"), "valid"));
                    Assert.False(cacheTexture!.IsValid);Assert.Null(ReadPrivate<DynamicTexture2D?>(cache,"texture"));
                    var afterRejection = scene.ReadOutput();
                    for (int layer = 0; layer < 6; layer++) Assert.Equal(actual[layer], afterRejection[layer]);
                    Assert.Throws<InvalidOperationException>(()=>scene.Draw(accelerated,input,hierarchy,()=>throw new InvalidOperationException("Revoked draw")));
                    Assert.Equal(cursor,ReadPrivate<int>(accelerated,"cursor"));
                    Assert.False(ReadPrivate<bool>(ReadPrivate<CloudTemporalHistory>(accelerated,"history"),"valid"));
                    original.Invalidate();
                }
                Assert.True(bounds!.IsValid);
            }

            // Reset is the common resize/reload boundary. Both domains retire owned storage only.
            var oldStorage = ReadPrivate<CloudVolumeStorage>(accelerated, "storage");
            var oldCache=ReadPrivate<DynamicTexture2D>(ReadPrivate<CloudShadowCache>(accelerated,"shadowCache"),"texture");
            Assert.True(hierarchy.TryGetCurrent(scene.Input, out var retired));
            accelerated.Reset(); hierarchy.Reset();
            Assert.False(oldStorage.Array.IsValid);Assert.All(oldStorage.Blocks,block=>Assert.False(block.View.IsValid));
            Assert.False(retired!.IsValid);
            Assert.False(oldCache.IsValid);
            scene.AssertBorrowedInputsAlive();
            scene.AttachPrograms(accelerated);
            scene.Resize(11, 9);
            var replacement = scene.Capture(6) with { World = 2, Allocation = 2, Owner = new object() };
            Assert.True(hierarchy.TryBuild(replacement, () => true), hierarchy.Failure);
            scene.Clear(); Assert.True(scene.Draw(accelerated, replacement, hierarchy, () => true));
            Assert.NotSame(oldStorage, ReadPrivate<CloudVolumeStorage>(accelerated, "storage"));
            Assert.True(scene.HierarchyEnabled);
            Assert.True(scene.ShadowCacheEnabled);
            Assert.Equal(6, LastTrace(accelerated).Target.Width);
            Assert.False(scene.HistoryEnabled);
            if(!lumOn)
            {
                // One lifecycle sequence covers allocation changes in both directions and
                // zero-quality retirement without multiplying the lighting-mode matrix.
                DynamicTexture2D? previousCache=null;
                uint qualityFrame=10;
                foreach(int quality in new[]{3,2,0,1})
                {
                    var controls=CloudVolumeSettings.Default with {ShadowQuality=quality};
                    var qualityInput=scene.Capture(qualityFrame++);
                    Assert.True(hierarchy.TryBuild(qualityInput,()=>true),hierarchy.Failure);
                    scene.Clear();Assert.True(scene.Draw(accelerated,qualityInput,hierarchy,()=>true,controls));
                    var cache=ReadPrivate<CloudShadowCache>(accelerated,"shadowCache");
                    var texture=ReadPrivate<DynamicTexture2D?>(cache,"texture");
                    Assert.Equal(quality!=0,scene.ShadowCacheEnabled);
                    Assert.Equal(quality!=0,ReadPrivate<CloudTraceProgram>(accelerated,"traceProgram").SelfShadowing);
                    if(previousCache is not null)Assert.False(previousCache.IsValid);
                    if(quality==0)Assert.Null(texture);
                    else
                    {
                        Assert.NotNull(texture);
                        Assert.Equal(controls.ShadowMapResolution,texture.Width);
                        Assert.Equal(controls.ShadowMapResolution*3,texture.Height);
                    }
                    previousCache=texture;
                }
            }
            GlDebug.ThrowIfErrors("Cloud pipeline test");
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled = previous; }
    }
    #endregion

    #region Private
    /// <summary>Locates the trace written immediately before the successful role rotation.</summary>
    private static CloudVolumeSurface LastTrace(CloudVolumeReconstruction reconstruction) =>
        ReadPrivate<CloudVolumeStorage>(reconstruction,"storage").Blocks[(ReadPrivate<int>(reconstruction,"cursor")+2)%3];
    /// <summary>Observes private scheduler state without adding production diagnostics or alternate execution paths.</summary>
    private static T ReadPrivate<T>(object owner, string name) => (T)AccessTools.Field(owner.GetType(), name).GetValue(owner)!;

    /// <summary>Owns tiny test publications while the production scheduler owns trace and history allocations.</summary>
    private sealed class ScheduledScene : IDisposable
    {
        private readonly Mock<ICoreClientAPI> api = new() { DefaultValue = DefaultValue.Mock };
        private readonly ComponentShaderPrograms programs;
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
        /// <summary>Supplies one completed mutable-by-replacement light publication to the production scheduler.</summary>
        internal AtmosphereLighting Lighting {get;set;} = new(Vector3.UnitY, new(30), new(10), Vector3.Zero, Vector3.Zero, ImmutableArray<float>.Empty)
            { Moon = -Vector3.UnitY, Lunar = new(3), CalibrationGeneration = 1, LookupStorageScale = LightingStorage.PersistentScale };

        #region Public API
        #region Fixture publication
        /// <summary>Reports the last certified synthetic native publication.</summary>
        internal CloudMapInput Input { get; private set; }
        /// <summary>Reads the actual trace contract's acceleration flag after production submission.</summary>
        internal bool HierarchyEnabled => UboPacking.ReadUInt32(((ICloudTraceBindings)trace).TraversalInputs.Bytes, 0) != 0;
        /// <summary>Observes the completed same-frame attenuation binding independently of root availability.</summary>
        internal bool ShadowCacheEnabled => UboPacking.ReadUInt32(((ICloudTraceBindings)trace).TraversalInputs.Bytes, 8) != 0;
        /// <summary>Reports the discrete source certificate staged for the current trace.</summary>
        internal uint ShadowCacheSource=>UboPacking.ReadUInt32(((ICloudTraceBindings)trace).TraversalInputs.Bytes,8);
        /// <summary>Reads the production history-admission flag supplied to all three passes.</summary>
        internal bool HistoryEnabled => BitConverter.ToSingle(trace.Temporal.Bytes.Slice(96, 4)) == 1;

        /// <summary>Creates real cloud binaries and main-view publication without starting native weather or the game.</summary>
        internal ScheduledScene(ComponentShaderPrograms programs)
        {
            this.programs=programs;
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
            AccessTools.Field(typeof(CloudVolumeReconstruction), "primaryNoise").SetValue(reconstruction, CloudBlueNoiseTexture.Create());
        }
        /// <summary>Creates an independent production scheduler using the same controlled borrowed inputs.</summary>
        internal CloudVolumeReconstruction CreateReconstruction(Func<CloudShadowAttenuationComputeShader>? createShadowCache=null)
        {
            var reconstruction = new CloudVolumeReconstruction(api.Object,createShadowCache??(()=>CloudShadowAttenuationComputeShader.Create(programs.Api))); AttachPrograms(reconstruction); return reconstruction;
        }
        /// <summary>Executes production source admission and all three passes against the existing OIT blend contract.</summary>
        internal bool Draw(CloudVolumeReconstruction reconstruction, CloudMapInput input, CloudHierarchy? hierarchy, Func<bool> current, CloudVolumeSettings? settings=null) =>
            reconstruction.Draw(input, Lighting, settings??(CloudVolumeSettings.Default with {TraceScale=CloudTraceScale.Half}), output.Target, depth.TextureId, visibility.TextureId, liquid.TextureId, geometry, current, hierarchy);
        /// <summary>Clears only fixture attachments to the native neutral OIT record before a comparison draw.</summary>
        internal void Clear()
        {
            var pass=new RenderPassDesc(output.Target,[
                new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),
                new(1,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,0,0,0)),
                new(2,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
                new(3,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
                new(4,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
                new(5,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]);
            Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.Clear",[],true,commands=>
            {commands.BeginPass(pass);commands.EndPass();}));
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
