using Microsoft.CodeAnalysis;
using VanillaGraphicsExpanded.Rendering.Contracts;
using static ShaderContractGenerator.AttributeValues;

namespace ShaderContractGenerator;

/// <summary>Validates explicit stage uses and program assignments through the shared pure contract models.</summary>
internal sealed class ProgramReader(Dictionary<INamedTypeSymbol, OwnerDeclaration> owners)
{
    #region Groups and programs
    /// <summary>Builds shared groups before resolving consumers.</summary>
    public static void ReadGroups(OwnerDeclaration owner)
    {
        foreach (var attribute in Attributes(owner.Symbol, "ShaderGroup"))
        {
            string member = Text(attribute, 0);
            ShaderContractNames.ValidateIdentifier(member);
            var options = Argument(attribute, 2).Values.Select(v => owner.Options[(string)v.Value!]).ToArray();
            owner.Groups.Add(member, (new ShaderOptionGroup(Text(attribute, 1), options.Select(o => o.Model).ToArray()),
                $"new ShaderOptionGroup({Quote(Text(attribute, 1))}{(options.Length == 0 ? "" : ", " + string.Join(", ", options.Select(o => o.KeyExpression)))})"));
        }
    }
    /// <summary>Constructs each program's exact stage list, accepted settings, groups and supported rows.</summary>
    public void Read(OwnerDeclaration owner)
    {
        foreach (var attribute in Attributes(owner.Symbol, "ShaderProgram"))
        {
            string member = Text(attribute, 0), identity = Text(attribute, 1);
            ShaderContractNames.ValidateIdentifier(member);
            int budget = (int)Argument(attribute, 2).Value!;
            string scope = Text(attribute, "Scope", "production")!;
            ShaderContractNames.ValidatePath(scope);
            var stages = Attributes(owner.Symbol, "ShaderStage").Where(a => Text(a, 0) == member).Select(a => ReadStage(owner, member, a, scope)).ToArray();
            var groups = Attributes(owner.Symbol, "ShaderAcceptGroup").Where(a => Text(a, 0) == member).Select(a =>
            {
                var type = (INamedTypeSymbol)Argument(a, 1).Value!;
                string group = Text(a, 2);
                if (!owners.TryGetValue(type, out var target) || !target.Groups.TryGetValue(group, out var declaration))
                    throw new ArgumentException($"Program '{identity}' references unknown group '{type}.{group}'.");
                return (declaration.Model, Expression: target.Name + "." + group);
            }).ToArray();
            var used = stages.SelectMany(s => s.Model.Structural.Concat(s.Model.Specializations.Select(c => c.Option))).GroupBy(o => o.Name).Select(g => g.First()).ToArray();
            var rows = Attributes(owner.Symbol, "ShaderAssignment").Where(a => Text(a, 0) == member).Select(a =>
                (IReadOnlyDictionary<string, string>)Argument(a, 1).Values.Select(v => ((string)v.Value!).Split(new[] { '=' }, 2)).ToDictionary(p => p[0], p => p.Length == 2 ? p[1] : throw new ArgumentException("Assignments require NAME=value pairs."), StringComparer.Ordinal)).ToArray();
            var model = new GpuShaderContract(identity, stages.Select(s => s.Model), budget, used, groups.Select(g => g.Model), rows.Length == 0 ? null : rows);
            string optionsExpression = string.Join(", ", used.Select(o => owner.Options.Values.First(p => p.Model.Name == o.Name).KeyExpression));
            string assignments = rows.Length == 0 ? "null" : "new System.Collections.Generic.IReadOnlyDictionary<string, string>[] { " + string.Join(", ", rows.Select(row =>
                "new System.Collections.Generic.Dictionary<string, string> { " + string.Join(", ", row.Select(p => $"[{Quote(p.Key)}] = {Quote(p.Value)}")) + " }")) + " }";
            string expression = $"new GpuShaderContract({Quote(identity)}, new ShaderStageContract[] {{ {string.Join(", ", stages.Select(s => s.Expression))} }}, {budget}, " +
                $"new ShaderOption[] {{ {optionsExpression} }}, new ShaderOptionGroup[] {{ {string.Join(", ", groups.Select(g => g.Expression))} }}, {assignments})";
            if (owner.Programs.Any(p => p.Member == member)) throw new ArgumentException($"Duplicate contract member '{member}'.");
            owner.Programs.Add(new(member, scope, model, expression));
        }
        // Typos must not silently discard a stage, use, assignment or fixed define.
        foreach (string kind in new[] { "ShaderStage", "ShaderUse", "ShaderFixedDefine", "ShaderAcceptGroup", "ShaderAssignment" })
            foreach (var attribute in Attributes(owner.Symbol, kind))
                if (!owner.Programs.Any(p => p.Member == Text(attribute, 0))) throw new ArgumentException($"{kind} references unknown program member '{Text(attribute, 0)}'.");
        foreach (string kind in new[] { "ShaderUse", "ShaderFixedDefine" })
            foreach (var attribute in Attributes(owner.Symbol, kind))
                if (!owner.Programs.Single(p => p.Member == Text(attribute, 0)).Model.Stages.Any(s => (int)s.Kind == (int)Argument(attribute, 1).Value!))
                    throw new ArgumentException($"{kind} references an undeclared stage.");
    }
    #endregion

    #region Stage construction
    /// <summary>Declares layout identity independently of settings and validates conditional specialization membership.</summary>
    private static (ShaderStageContract Model, string Expression) ReadStage(OwnerDeclaration owner, string program, AttributeData attribute, string scope)
    {
        var kind = (ShaderStageKind)(int)Argument(attribute, 1).Value!;
        string source = Text(attribute, 2), identity = Text(attribute, "Identity", source)!;
        string layout = Text(attribute, "Layout", source.Contains('.') ? source.Substring(0, source.LastIndexOf('.')) : source)!;
        string entry = Text(attribute, "EntryPoint", "main")!, binary = Text(attribute, "BinaryAsset", identity)!;
        var uses = Attributes(owner.Symbol, "ShaderUse").Where(a => Text(a, 0) == program && (int)Argument(a, 1).Value! == (int)kind).ToArray();
        // Gather structural dependencies first so attribute order cannot change condition validity.
        var structural = uses.Where(a => Named(a, "SpecializationId").Value is not int id || id == -1)
            .Select(a => owner.Options.TryGetValue(Text(a, 2), out var option) ? option : throw new ArgumentException($"Program '{program}' references unknown option member '{Text(a, 2)}'.")).ToList();
        var constants = new List<(ShaderSpecialization Model, string Expression)>();
        foreach (var use in uses)
        {
            if (!owner.Options.TryGetValue(Text(use, 2), out var option)) throw new ArgumentException($"Program '{program}' references unknown option member '{Text(use, 2)}'.");
            int id = Named(use, "SpecializationId").Value is int number ? number : -1;
            string? when = Text(use, "When");
            if (id == -1)
            {
                if (when != null) throw new ConditionDeclarationException($"Program '{program}', stage '{identity}', expression '{when}': Structural uses cannot have availability conditions.",
                    use.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None);
            }
            else
            {
                (ShaderCondition Model, string Expression)? condition = null;
                if (when != null)
                {
                    try { condition = new ConditionReader(owner, structural.Select(o => o.Model).ToArray()).Read(when); }
                    catch (ArgumentException error)
                    {
                        throw new ConditionDeclarationException($"Program '{program}', stage '{identity}', expression '{when}': {error.Message}",
                            use.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None, error);
                    }
                }
                constants.Add((new ShaderSpecialization(id, option.Model, condition?.Model),
                    $"new ShaderSpecialization({id}, {option.KeyExpression}, {condition?.Expression ?? "null"})"));
            }
        }
        var fixedValues = new Dictionary<string, ShaderScalar>(StringComparer.Ordinal) { ["VGE_SPIRV_BUILD"] = ShaderScalar.From(1) };
        var fixedExpressions = new Dictionary<string, string>(StringComparer.Ordinal) { ["VGE_SPIRV_BUILD"] = "ShaderScalar.From(1)" };
        foreach (var define in Attributes(owner.Symbol, "ShaderFixedDefine").Where(a => Text(a, 0) == program && (int)Argument(a, 1).Value! == (int)kind))
        {
            string name = Text(define, 2);
            fixedValues.Add(name, Scalar(Argument(define, 3)));
            fixedExpressions.Add(name, "ShaderScalar.From(" + Literal(Argument(define, 3)) + ")");
        }
        var model = new ShaderStageContract(identity, source, kind, new GpuBindingContract(), structural.Select(o => o.Model), constants.Select(c => c.Model), fixedValues, entry, binary);
        string expression = $"new ShaderStageContract({Quote(identity)}, {Quote(source)}, ShaderStageKind.{kind}, GpuShaderContracts.DeclareBindings({Quote(layout)}), " +
            $"new ShaderOption[] {{ {string.Join(", ", structural.Select(o => o.KeyExpression))} }}, new ShaderSpecialization[] {{ {string.Join(", ", constants.Select(c => c.Expression))} }}, " +
            "new System.Collections.Generic.Dictionary<string, ShaderScalar> { " + string.Join(", ", fixedExpressions.Select(p => $"[{Quote(p.Key)}] = {p.Value}")) +
            $" }}, {Quote(entry)}, {Quote(binary)})";
        return (model, $"ShaderStageDeclarations.Share({expression}, {Quote(scope)})");
    }
    #endregion
}
