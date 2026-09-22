using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Constructs bounded finite configuration sets independently of specialization availability.</summary>
internal static class ShaderAssignments
{
    #region Assignment construction
    /// <summary>Validates explicit complete rows or expands the Cartesian product after checking its budget.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, ShaderScalar>> Create(GpuShaderContract program,
        IEnumerable<IReadOnlyDictionary<string, string>>? assignments)
    {
        var rows = new List<IReadOnlyDictionary<string, ShaderScalar>>();
        if (assignments != null)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var supplied in assignments)
            {
                if (rows.Count >= program.VariantBudget) throw Budget(program);
                var row = new Dictionary<string, ShaderScalar>(StringComparer.Ordinal);
                foreach (var pair in supplied)
                {
                    var option = program.FindOption(pair.Key);
                    if (pair.Key != option.Name || !program.Structural.Any(o => o.Equivalent(option)))
                        throw new ArgumentException($"Program '{program.Identity}' assignment requires canonical structural options; invalid '{pair.Key}'.");
                    try { row.Add(option.Name, option.Parse(pair.Value)); }
                    catch (ArgumentException e) { throw new ArgumentException($"Program '{program.Identity}' assignment: {e.Message}", e); }
                }
                if (row.Count != program.Structural.Count)
                    throw new ArgumentException($"Program '{program.Identity}' assignment is missing structural values.");
                if (!keys.Add(Key(row))) throw new ArgumentException($"Program '{program.Identity}' repeats assignment '{Key(row)}'.");
                rows.Add(new ReadOnlyDictionary<string, ShaderScalar>(row));
            }
        }
        else
        {
            long count = 1;
            foreach (var option in program.Structural)
            {
                count *= option.Domain!.Count;
                if (count > program.VariantBudget) throw Budget(program);
            }
            IEnumerable<Dictionary<string, ShaderScalar>> product = [new(StringComparer.Ordinal)];
            foreach (var option in program.Structural)
                product = product.SelectMany(row => option.Domain!.Select(value =>
                    new Dictionary<string, ShaderScalar>(row, StringComparer.Ordinal) { [option.Name] = value })).ToArray();
            rows.AddRange(product.Select(row => new ReadOnlyDictionary<string, ShaderScalar>(row)));
        }
        string defaultKey = Key(program.Structural.ToDictionary(o => o.Name, o => o.Default, StringComparer.Ordinal));
        if (!rows.Any(row => Key(row) == defaultKey))
            throw new ArgumentException($"Program '{program.Identity}' supported assignments omit default '{defaultKey}'.");
        return Array.AsReadOnly(rows.OrderBy(Key, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Reports variant expansion at the owning program.</summary>
    private static ArgumentException Budget(GpuShaderContract program) =>
        new($"Program '{program.Identity}' exceeds variant budget '{program.VariantBudget}'.");

    /// <summary>Formats canonical structural values in stable ordinal order.</summary>
    internal static string Key(IEnumerable<KeyValuePair<string, ShaderScalar>> values) =>
        string.Join(";", values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value.Canonical));
    #endregion
}
