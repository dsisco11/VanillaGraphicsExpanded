using System.Reflection;
using HarmonyLib;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using VanillaGraphicsExpanded.HarmonyPatches;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies installed graphics call coverage without creating a game or GL context.</summary>
[Collection("GPU")]
public sealed class EngineStateInventoryTests
{
    #region Public API
    /// <summary>Inspects engine IL so routing coverage can be checked against installed game binaries.</summary>
    [Fact]
    public void InventoryEngineCalls()
    {
        var rows = new List<string>();
        using var dependencies = new EngineDependencyResolution();
        var gamePath = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        var assemblies = new[] { typeof(ClientPlatformWindows).Assembly, typeof(Vintagestory.API.Client.IRenderAPI).Assembly }
            .Concat(new[] { "VSEssentials", "VSSurvivalMod", "VSCreativeMod" }.Select(name => Path.Combine(gamePath, "Mods", name + ".dll")).Select(Assembly.LoadFrom)).ToArray();
        var targets = EngineStateSwitchingHook.TargetMethods().ToHashSet();
        var supportedNames = EngineStateCallMap.Replacements.Keys.Select(method => method.Name).ToHashSet();
        foreach (var assembly in assemblies)
        {
            var graphicsReferences = assembly.GetReferencedAssemblies().Where(name => name.Name!.StartsWith("OpenTK.Graphics")).ToArray();
            rows.Add($"ASSEMBLY {assembly.GetName().Name}: OpenTK.Graphics references = {string.Join(',', graphicsReferences.Select(name => name.Name))}");
            // An assembly without an OpenTK.Graphics reference cannot directly call the GL binding API.
            if (assembly != typeof(ClientPlatformWindows).Assembly)
            {
                // Read metadata instead of resolving unrelated third-party method signatures in mod IL.
                using var stream = File.OpenRead(assembly.Location);
                using var image = new PEReader(stream);
                var metadata = image.GetMetadataReader();
                foreach (var handle in metadata.MemberReferences)
                {
                    var member = metadata.GetMemberReference(handle);
                    if (member.Parent.Kind != HandleKind.TypeReference) continue;
                    var owner = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                    string space = metadata.GetString(owner.Namespace);
                    if (space.StartsWith("OpenTK.Graphics."))
                    {
                        rows.Add($"REFERENCE {assembly.GetName().Name}: {space}.{metadata.GetString(owner.Name)}.{metadata.GetString(member.Name)}");
                        if (supportedNames.Contains(metadata.GetString(member.Name)))
                            Assert.Contains((MethodInfo)assembly.ManifestModule.ResolveMethod(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle))!, EngineStateCallMap.Replacements.Keys);
                    }
                }
                continue;
            }
            var types = assembly.GetTypes();
            foreach (var type in types)
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)))
            {
                // Inspect the original managed body; no engine methods are invoked.
                if (method.GetMethodBody() is null || method.ContainsGenericParameters) continue;
                var instructions = PatchProcessor.GetOriginalInstructions(method);
                foreach (var instruction in instructions)
                    if (instruction.operand is MethodInfo call && call.DeclaringType?.FullName?.StartsWith("OpenTK.Graphics.") == true)
                    {
                        rows.Add($"{call} | {type.FullName}.{method.Name} | {call.DeclaringType.FullName}");
                        if (supportedNames.Contains(call.Name))
                        {
                            Assert.Contains(call, EngineStateCallMap.Replacements.Keys);
                            Assert.Contains(method, targets);
                        }
                    }
            }
        }
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts"));
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "engine-gl-inventory.txt"), rows.Order());
        Assert.Contains(rows, row => row.StartsWith("Void BindTexture("));
    }
    #endregion
}
