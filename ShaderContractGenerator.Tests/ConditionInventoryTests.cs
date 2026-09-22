namespace ShaderContractGenerator.Tests;

/// <summary>Preserves inventory availability and binary selection while migrating only condition authoring.</summary>
public sealed class ConditionInventoryTests
{
    #region Inventory scenarios
    /// <summary>All structural assignments retain numeric selections and match the previous typed condition trees.</summary>
    [Fact]
    public void InventoryConditionsPreserveSelectionsAndBinaryIdentity()
    {
        const string source = """
            [ShaderProgram("Contract", "inventory", 32)]
            [ShaderStage("Contract", ShaderStageKind.Compute, "inventory.csh")]
            [ShaderUse("Contract", ShaderStageKind.Compute, nameof(Dimensions), SpecializationId = 14, When = "WorldProbes")]
            [ShaderUse("Contract", ShaderStageKind.Compute, nameof(Sky), SpecializationId = 6, When = "!NearField")]
            [ShaderUse("Contract", ShaderStageKind.Compute, nameof(Explore), SpecializationId = 8, When = "ImportanceSampling && !BatchSlicing && !UniformMask")]
            [ShaderUse("Contract", ShaderStageKind.Compute, nameof(WorldProbes))]
            [ShaderUse("Contract", ShaderStageKind.Compute, nameof(NearField))]
            [ShaderUse("Contract", ShaderStageKind.Compute, nameof(ImportanceSampling))]
            [ShaderUse("Contract", ShaderStageKind.Compute, nameof(BatchSlicing))]
            [ShaderUse("Contract", ShaderStageKind.Compute, nameof(UniformMask))]
            internal static partial class Shader {
                [ShaderOption("WORLD", false)] internal static partial ShaderOption<bool> WorldProbes { get; }
                [ShaderOption("NEAR", false)] internal static partial ShaderOption<bool> NearField { get; }
                [ShaderOption("PIS", false)] internal static partial ShaderOption<bool> ImportanceSampling { get; }
                [ShaderOption("BATCH", false)] internal static partial ShaderOption<bool> BatchSlicing { get; }
                [ShaderOption("UNIFORM", false)] internal static partial ShaderOption<bool> UniformMask { get; }
                [ShaderOption("DIMENSIONS", 20)] internal static partial ShaderOption<int> Dimensions { get; }
                [ShaderOption("SKY", 0.5f)] internal static partial ShaderOption<float> Sky { get; }
                [ShaderOption("EXPLORE", -1)] internal static partial ShaderOption<int> Explore { get; }
            }
            """;
        const string proof = """
            public static class Proof { public static string Run() {
                var resolver = new ShaderVariantResolver(new[] { Shader.Contract });
                var stage = Shader.Contract.Stages[0];
                var previousConditions = new[] {
                    ShaderCondition.Equal(Shader.WorldProbes, true),
                    ShaderCondition.Equal(Shader.NearField, false),
                    ShaderCondition.All(ShaderCondition.Equal(Shader.ImportanceSampling, true), ShaderCondition.Equal(Shader.BatchSlicing, false), ShaderCondition.Equal(Shader.UniformMask, false))
                };
                var paths = new System.Collections.Generic.HashSet<string>();
                int worldCount=0, skyCount=0, exploreCount=0;
                for(int bits=0;bits<32;bits++) {
                    var settings=new ShaderSettings(Shader.Contract).With(Shader.WorldProbes,(bits&1)!=0).With(Shader.NearField,(bits&2)!=0)
                        .With(Shader.ImportanceSampling,(bits&4)!=0).With(Shader.BatchSlicing,(bits&8)!=0).With(Shader.UniformMask,(bits&16)!=0)
                        .With(Shader.Dimensions,27).With(Shader.Sky,0.75f).With(Shader.Explore,5);
                    var selected=resolver.Resolve(settings)[0]; paths.Add(selected.BinaryPath);
                    for(int i=0;i<3;i++) if(stage.Specializations[i].Condition!.Evaluate(settings.Values)!=previousConditions[stage.Specializations[i].Id == 14 ? 0 : stage.Specializations[i].Id == 6 ? 1 : 2].Evaluate(settings.Values)) throw new System.Exception("Availability changed");
                    bool world=(bits&1)!=0, sky=(bits&2)==0, explore=(bits&4)!=0&&(bits&24)==0;
                    if(selected.Specializations.Count != (world?1:0)+(sky?1:0)+(explore?1:0)) throw new System.Exception("Wrong active constants");
                    foreach(var input in selected.Specializations) {
                        if(input.Id==14) {worldCount++; if(input.Value.Canonical!="27") throw new System.Exception("Lost dimensions");}
                        if(input.Id==6) {skyCount++; if(input.Value.Canonical!="0.75") throw new System.Exception("Lost sky");}
                        if(input.Id==8) {exploreCount++; if(input.Value.Canonical!="5") throw new System.Exception("Lost exploration");}
                    }
                    var numericChange=resolver.Resolve(settings.With(Shader.Dimensions,31).With(Shader.Sky,0.25f).With(Shader.Explore,9))[0];
                    if(selected.BinaryPath!=numericChange.BinaryPath) throw new System.Exception("Numeric binary identity changed");
                    var disabled=settings.With(Shader.WorldProbes,false).With(Shader.NearField,true).With(Shader.ImportanceSampling,false);
                    if(resolver.Resolve(disabled)[0].Specializations.Count!=0 || disabled.Values["DIMENSIONS"].Canonical!="27" || disabled.Values["SKY"].Canonical!="0.75" || disabled.Values["EXPLORE"].Canonical!="5") throw new System.Exception("Inactive values lost");
                    var restored=disabled.With(Shader.WorldProbes,world).With(Shader.NearField,!sky).With(Shader.ImportanceSampling,(bits&4)!=0);
                    if(!selected.SameInputs(resolver.Resolve(restored)[0])) throw new System.Exception("Reenabled selection changed");
                }
                string defaultPath=resolver.Resolve(new ShaderSettings(Shader.Contract))[0].BinaryPath;
                return Shader.Contract.Assignments.Count+":"+resolver.Binaries.Count+":"+paths.Count+":"+worldCount+":"+skyCount+":"+exploreCount+":"+defaultPath;
            } }
            """;
        foreach(bool offline in new[]{false,true})
            Assert.Equal("32:32:32:16:16:4:inventory.csh.spv", ConditionTests.Execute(source,proof,offline));
    }
    #endregion
}

