using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.HeldLighting;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Verifies Harmony transformations against the installed engine collector and chunk mutation IL.</summary>
[Collection("CPU state")]
public sealed class NativeCollectionHookContractTests
{
    #region Public API
    /// <summary>The installed local-light collector retains both its entity and registered provider at admission.</summary>
    [Fact]
    public void InstalledLightCollectorRoutesEntityAndProviderOwners()
    {
        // Arrange: obtain the actual installed engine collector body and its original call instructions.
        var collector = AccessTools.Method(typeof(SystemRenderPlayerEffects), "onBeforeRender");
        var original = ReadInstructions(collector);
        int entityPositionCalls = original.Count(code => code.Calls(AccessTools.PropertyGetter(typeof(Entity), nameof(Entity.Pos))));
        int providerColorCalls = original.Count(code => code.Calls(AccessTools.PropertyGetter(typeof(IPointLight), nameof(IPointLight.Color))));
        int providerPositionCalls = original.Count(code => code.Calls(AccessTools.PropertyGetter(typeof(IPointLight), nameof(IPointLight.Pos))));

        // Act: run the production Harmony transpiler over that installed source contract.
        var transformed = HeldLightHooks.Collect(original).ToArray();
        var entityCall = AccessTools.Method(typeof(HeldLightSystem), nameof(HeldLightSystem.AddEntityLight));
        var providerCall = AccessTools.Method(typeof(HeldLightSystem), nameof(HeldLightSystem.AddProviderLight));
        var entityPosition = AccessTools.PropertyGetter(typeof(Entity), nameof(Entity.Pos));
        var providerColor = AccessTools.PropertyGetter(typeof(IPointLight), nameof(IPointLight.Color));
        var providerPosition = AccessTools.PropertyGetter(typeof(IPointLight), nameof(IPointLight.Pos));

        // Assert: transformed calls retain native owners and remove their anonymous payload getters.
        Assert.Single(transformed.Where(code => code.Calls(entityCall)));
        Assert.Single(transformed.Where(code => code.Calls(providerCall)));
        Assert.Equal(entityPositionCalls - 1, transformed.Count(code => code.Calls(entityPosition)));
        Assert.Equal(providerColorCalls - 1, transformed.Count(code => code.Calls(providerColor)));
        Assert.Equal(providerPositionCalls - 1, transformed.Count(code => code.Calls(providerPosition)));
    }

    /// <summary>Load, replacement and unload observers match current native dictionary mutation sites.</summary>
    [Theory]
    [InlineData(typeof(ClientWorldMap), "loadChunkMT", "Set")]
    [InlineData(typeof(ClientWorldMap), "OverloadChunkMT", "Set")]
    [InlineData(typeof(SystemUnloadChunks), "HandleChunkUnload", "Remove")]
    public void InstalledChunkOwnershipMutationsAreIntercepted(Type type, string methodName, string expectedObserver)
    {
        // Arrange: read the exact method body from the installed engine assembly.
        var source = AccessTools.Method(type, methodName);

        // Act: apply the production dictionary observer to actual engine IL.
        var transformed = NativeShadowWorldHooks.ObserveDictionary(ReadInstructions(source)).ToArray();

        // Assert: every native ownership route calls the corresponding adapter observer.
        Assert.Contains(transformed, instruction => instruction.operand is MethodInfo called
            && called.Name == expectedObserver && called.DeclaringType == typeof(NativeShadowWorldHooks));
    }
    #endregion

    #region Private
    /// <summary>Asks Harmony to decode the installed method body without executing game lifecycle code.</summary>
    private static List<CodeInstruction> ReadInstructions(MethodBase method)
    {
        var reader = typeof(PatchProcessor).GetMethod("GetOriginalInstructions",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, binder: null,
            types: [typeof(MethodBase), typeof(ILGenerator).MakeByRefType()], modifiers: null)
            ?? throw new MissingMethodException(typeof(PatchProcessor).FullName, "GetOriginalInstructions");
        var generator = new DynamicMethod("VgeShadowIlFixture", typeof(void), Type.EmptyTypes,
            typeof(NativeCollectionHookContractTests).Module).GetILGenerator();
        object?[] arguments = [method, generator];
        return (List<CodeInstruction>)(reader.Invoke(null, arguments)
            ?? throw new InvalidOperationException("Harmony returned no engine instructions."));
    }
    #endregion
}
