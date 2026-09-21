using System.Reflection;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Exercises the actual bounds renderer's stage wiring and projection construction without a game world.</summary>
public sealed class WorldCellBoundsCameraTests
{
    #region Scene camera contract
    /// <summary>World lines subscribe to the terrain camera stage and retire the same registration.</summary>
    [Fact]
    public void BoundsUseSceneStageAndUnregisterIt()
    {
        var calls = new List<(string Name, EnumRenderStage Stage)>();
        var events = Proxy<IClientEventAPI>((method, args) =>
        {
            if (method.Name is "RegisterRenderer" or "UnregisterRenderer")
                calls.Add((method.Name, (EnumRenderStage)args![1]!));
            return null;
        });
        var api = Client(events, null);
        using (var renderer = CreateRenderer(api))
        {
            Assert.Equal(new[] { ("RegisterRenderer", EnumRenderStage.OIT) }, calls);
            // A late overlay callback must not query camera, shader, or world services.
            renderer.OnRenderFrame(.016f, EnumRenderStage.AfterBlit);
        }
        Assert.Equal(new[] { ("RegisterRenderer", EnumRenderStage.OIT), ("UnregisterRenderer", EnumRenderStage.OIT) }, calls);
    }

    /// <summary>Nonzero eye/bob translations survive the real matrix producer and project a fixed point like terrain.</summary>
    [Theory]
    [InlineData(-.25f)]
    [InlineData(0f)]
    [InlineData(.25f)]
    public void BoundsPreserveFullSceneViewTranslation(float bob)
    {
        float[] projection = [2,0,0,0, 0,3,0,0, 0,0,-1,-1, 0,0,-2,0];
        float[] view = [1,0,0,0, 0,1,0,0, 0,0,1,0, -.3f,-1.6f-bob,.2f,1];
        var render = Proxy<IRenderAPI>((method, _) => method.Name switch
        {
            "get_CurrentProjectionMatrix" => projection,
            "get_CameraMatrixOriginf" => view,
            _ => throw new InvalidOperationException(method.Name)
        });
        using var renderer = CreateRenderer(Client(Proxy<IClientEventAPI>((_, _) => null), render));
        var type = renderer.GetType();
        type.GetMethod("UpdateCurrentViewProjMatrix", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(renderer, null);
        var matrix = (float[])type.GetField("currentViewProjMatrix", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(renderer)!;
        // Independent scalar oracle applies view then projection, with a nonzero position and homogeneous W.
        float[] point = [1.25f, 2.5f, -8, 1];
        float[] expected = Transform(projection, Transform(view, point));
        var parameters = new VgeDebugLinesParamsUbo { ModelViewProjectionMatrix = matrix };
        float[] uploadedMatrix = MemoryMarshal.Cast<byte, float>(parameters.Bytes)[..16].ToArray();
        float[] actual = Transform(uploadedMatrix, point);
        for (int i = 0; i < 4; i++) Assert.InRange(actual[i], expected[i] - .00001f, expected[i] + .00001f);
    }
    #endregion

    #region API fixture
    /// <summary>Constructs the production nested renderer, retaining its constructor and disposal behavior.</summary>
    private static IRenderer CreateRenderer(ICoreClientAPI api)
    {
        var type = typeof(VgeBuiltInDebugViews).GetNestedType("VgeWorldCellBoundsWireframeRenderer", BindingFlags.NonPublic)!;
        return (IRenderer)Activator.CreateInstance(type, api)!;
    }

    /// <summary>Supplies only constructor and matrix dependencies; unexpected access fails the fixture.</summary>
    private static ICoreClientAPI Client(IClientEventAPI events, IRenderAPI? render)
    {
        var loader = Proxy<IModLoader>((method, _) => method.Name == "GetModSystem" ? null : throw new InvalidOperationException(method.Name));
        return Proxy<ICoreClientAPI>((method, _) => method.Name switch
        {
            "get_Event" => events,
            "get_ModLoader" => loader,
            "get_Render" => render,
            _ => throw new InvalidOperationException(method.Name)
        });
    }

    /// <summary>Applies a column-major matrix with an independent scalar multiplication oracle.</summary>
    private static float[] Transform(float[] matrix, float[] point)
    {
        var result = new float[4];
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++) result[row] += matrix[column * 4 + row] * point[column];
        return result;
    }

    /// <summary>Creates a narrowly scripted API interface without implementing unrelated engine behavior.</summary>
    private static T Proxy<T>(System.Func<MethodInfo, object?[]?, object?> invoke) where T : class
    {
        T instance = DispatchProxy.Create<T, ApiProxy>();
        ((ApiProxy)(object)instance).Handler = invoke;
        return instance;
    }

    /// <summary>Routes interface calls to an individual scenario's explicit behavior.</summary>
    public class ApiProxy : DispatchProxy
    {
        public System.Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        /// <summary>Forwards calls while preserving their actual method and arguments.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
    #endregion
}
