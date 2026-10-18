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
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns synthetic cloud publications for opt-in complete production scheduler timing.</summary>
internal sealed class CloudPerformanceScene : IDisposable
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
    private readonly int mapWidth;
    private readonly string pattern, viewpoint;
    private readonly float[] view = new float[16], inverseView = new float[16], viewProjection = new float[16];
    private readonly ArrayGraphicsGeometry geometry;
    private readonly DynamicTexture3D? previousAerial = AtmosphereModSystem.AerialRadianceTexture;
    private readonly DynamicTexture3D? previousLoss = AtmosphereModSystem.AerialAttenuationTexture;
    private readonly float[] projection = [1,0,0,0,0,1,0,0,0,0,-1.00001f,-1,0,0,-.200001f,0];
    private readonly float[] inverse = new float[16];
    private AtmosphereLighting lighting = new(Vector3.UnitY, new(30), new(10), Vector3.Zero, Vector3.Zero, ImmutableArray<float>.Empty)
        { Moon = -Vector3.UnitY, Lunar = new(3), CalibrationGeneration = 1, LookupStorageScale = LightingStorage.PersistentScale };

    #region Public API
    #region Fixture publication
    /// <summary>Reports the last certified synthetic native publication.</summary>
    internal CloudMapInput Input { get; private set; }
    /// <summary>Reports actual history admission staged by the most recent production draw.</summary>
    internal bool HistoryEnabled => BitConverter.ToSingle(trace.Temporal.Bytes.Slice(96,4))==1;

    /// <summary>Creates real cloud binaries and main-view publication without starting native weather or the game.</summary>
    internal CloudPerformanceScene(ComponentShaderPrograms programs, int viewWidth, int viewHeight, int mapWidth, string pattern, string viewpoint)
    {
        this.mapWidth=mapWidth; this.pattern=pattern; this.viewpoint=viewpoint;
        projection[5]=1/MathF.Tan(75*MathF.PI/360);
        projection[0]=projection[5]*viewHeight/viewWidth;
        float pitch=viewpoint=="ground"?.18f:viewpoint=="above"?-.18f:0;
        float c=MathF.Cos(pitch), s=MathF.Sin(pitch);
        float[] rotation=[1,0,0,0,0,c,s,0,0,-s,c,0,0,0,0,1];
        rotation.CopyTo(inverseView,0); Mat4f.Invert(view,inverseView); Mat4f.Mul(viewProjection,projection,view);
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
        map = Own(DynamicTexture2D.Create(mapWidth, mapWidth, PixelInternalFormat.Rgba32f));
        aerial = Own(DynamicTexture3D.Create(1, 2, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D));
        loss = Own(DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D));
        aerial.UploadDataImmediate(new float[8], 0, 0, 0, 1, 2, 1, 0); loss.UploadDataImmediate(new float[4], 0, 0, 0, 1, 1, 1, 0);
        SetAtmosphere(nameof(AtmosphereModSystem.AerialRadianceTexture), aerial);
        SetAtmosphere(nameof(AtmosphereModSystem.AerialAttenuationTexture), loss);
        Resize(viewWidth, viewHeight);
        geometry = Own(new ArrayGraphicsGeometry(CloudVolumeRenderer.Layout, PrimitiveType.Triangles, new Dictionary<int, GpuVbo>(), proceduralVertices: 3));
    }

    /// <summary>Authors bounded moving weather and light inputs identically for each compared production owner.</summary>
    internal CloudMapInput Capture(uint frame)
    {
        VgeFrameRenderer.Current.Capture(projection, view, inverse, inverseView, viewProjection, viewProjection, new(width, height), 0, frame, Vector3.Zero, Vector3.Zero, 0, new(.1f, 20000));
        storage.Capture(1, frame);
        float angle=frame*.025f;
        // Hold each immutable lighting publication for several frames so valid temporal history
        // executes between sun/moon updates rather than measuring only rejected-history frames.
        if(frame%8==0) lighting=lighting with { Sun=Vector3.Normalize(new(MathF.Cos(angle),.2f,MathF.Sin(angle))), Moon=Vector3.Normalize(new(-MathF.Cos(angle),.15f,-MathF.Sin(angle))) };
        float[] columns = new float[mapWidth * mapWidth * 4];
        for (int z=0;z<mapWidth;z++) for(int x=0;x<mapWidth;x++)
        {
            int i=z*mapWidth+x;
            if(pattern=="empty" || pattern=="sparse" && ((x/3+z/4)%7!=0)) continue;
            float lower=pattern=="separated" && ((x/3+z/3)&1)!=0?850:300;
            float undulation=5*MathF.Sin(frame*.05f+x*.1f+z*.17f);
            columns[i*4]=.125f; columns[i*4+2]=(lower+undulation)/50; columns[i*4+3]=(lower+200+undulation)/50;
        }
        map.UploadDataImmediate(columns);
        float altitude=viewpoint=="ground"?0:viewpoint=="above"?1300:400;
        float offsetX=viewpoint=="edge"?-mapWidth*25+25:0;
        Input = new(this, 1, 1, 1, 1, frame + 1, frame, new(map.TextureId, 2, 3, 4, mapWidth, 50, 5),
            new(0, 0, 0, 0, 0, 0, 0, 0, new(offsetX, -altitude, 0), 0));
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
        float[] empty=Enumerable.Repeat(1f,width*height).ToArray();
        depth.UploadDataImmediate(empty); visibility.UploadDataImmediate(empty); liquid.UploadDataImmediate(empty);
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
        AccessTools.Field(typeof(CloudVolumeReconstruction), "primaryNoise").SetValue(reconstruction, CloudBlueNoiseTexture.Create());
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
    /// <summary>Returns live allocation identities for unchanged-size reuse qualification.</summary>
    internal int[] AllocationIds(CloudVolumeReconstruction reconstruction)
    {
        var storage=Assert.IsType<CloudVolumeStorage>(AccessTools.Field(typeof(CloudVolumeReconstruction),"storage").GetValue(reconstruction));
        return [storage.Array.TextureId,..storage.Blocks.Select(block=>block.View.TextureId)];
    }
    /// <summary>Reads the last successfully committed history block without redrawing it.</summary>
    internal float[][] ReadCommittedHistory(CloudVolumeReconstruction reconstruction)
    {
        var storage=Assert.IsType<CloudVolumeStorage>(AccessTools.Field(typeof(CloudVolumeReconstruction),"storage").GetValue(reconstruction));
        int cursor=(int)AccessTools.Field(typeof(CloudVolumeReconstruction),"cursor").GetValue(reconstruction)!;
        return CloudPayloadReadback.Read(storage.Blocks[(cursor+1)%3]);
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
    #endregion
}
