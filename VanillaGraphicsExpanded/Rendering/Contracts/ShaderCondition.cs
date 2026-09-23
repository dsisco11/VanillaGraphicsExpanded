using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Closed declarative Boolean expressions over a stage's structural options.</summary>
internal sealed class ShaderCondition
{
    private enum Operation { Equal, All, Any, Not }
    private readonly Operation operation;
    private readonly ShaderOption? option;
    private readonly ShaderScalar value;
    private readonly ShaderCondition[] children;

    #region Construction
    /// <summary>Copies expression children so callers cannot mutate published conditions.</summary>
    private ShaderCondition(Operation operation, ShaderOption? option, ShaderScalar value, params ShaderCondition[] children)
    {
        if (children.Any(c => c == null)) throw new ArgumentException("Null shader condition child.");
        this.operation = operation; this.option = option; this.value = value; this.children = children.ToArray();
    }
    /// <summary>Tests one typed structural value.</summary>
    public static ShaderCondition Equal<T>(ShaderOption<T> option, T value) where T : struct =>
        new(Operation.Equal, option, option.Validate(ShaderScalar.From(value)));
    /// <summary>Requires every child expression.</summary>
    public static ShaderCondition All(params ShaderCondition[] children) => new(Operation.All, null, default, children);
    /// <summary>Requires at least one child expression.</summary>
    public static ShaderCondition Any(params ShaderCondition[] children) => new(Operation.Any, null, default, children);
    /// <summary>Inverts one expression.</summary>
    public static ShaderCondition Not(ShaderCondition child) => new(Operation.Not, null, default, child);
    #endregion

    #region Validation and evaluation
    /// <summary>Ensures all dependencies are declared structural uses in the owning stage.</summary>
    internal void Validate(IReadOnlyList<ShaderOption> structural, string stage)
    {
        if (option != null && !structural.Any(o => o.Equivalent(option)))
            throw new ArgumentException($"Stage '{stage}' condition references unknown or nonstructural option '{option.Name}'.");
        foreach (var child in children) child.Validate(structural, stage);
    }
    /// <summary>Evaluates the same structural expression during build and runtime projection.</summary>
    internal bool Evaluate(IReadOnlyDictionary<string, ShaderScalar> values) => operation switch
    {
        Operation.Equal => values[option!.Name] == value,
        Operation.All => children.All(c => c.Evaluate(values)),
        Operation.Any => children.Any(c => c.Evaluate(values)),
        _ => !children[0].Evaluate(values)
    };
    /// <summary>Compares the declared expression tree when validating shared stage definitions.</summary>
    internal bool Equivalent(ShaderCondition other) => operation == other.operation && value == other.value &&
        (option == null ? other.option == null : other.option != null && option.Equivalent(other.option)) &&
        children.Length == other.children.Length && children.Zip(other.children, (left, right) => left.Equivalent(right)).All(equal => equal);
    #endregion
}
