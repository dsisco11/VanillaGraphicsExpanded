using System.Text.Json;
using ShaderBuildTool.Spirv;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;

namespace VgeShaderTestPreparation;

/// <summary>Prepares offline transform-feedback fixtures from the actual expanded producer shaders.</summary>
internal static class Program
{
    #region Private
    private static readonly (string Producer, string Fixture)[] Observations =
    [
        ("shadows/static.vsh", "tests/shadow-static-feedback.vsh"),
        ("shadows/entity.vsh", "tests/shadow-entity-feedback.vsh"),
        ("shadows/terrain.vsh", "tests/shadow-terrain-feedback.vsh")
    ];

    /// <summary>Expands current owned shader inputs, derives observation stages, and records provenance receipts.</summary>
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: VgeShaderTestPreparation <assets-root> <receipt-directory>");
            return 2;
        }

        string assetsRoot = Path.GetFullPath(args[0]);
        string receiptDirectory = Path.GetFullPath(args[1]);
        var preprocessor = new ShaderSourcePreprocessor(assetsRoot, "vanillagraphicsexpanded");
        foreach (var (producerPath, fixturePath) in Observations)
        {
            ShaderExpandedSource producer = preprocessor.Expand(producerPath);
            string prepared = ShaderObservationSource.Capture(producer.Text);
            string fixtureFile = Path.Combine(assetsRoot, "vanillagraphicsexpanded", "shaders",
                fixturePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fixtureFile)!);
            File.WriteAllText(fixtureFile, SourceCodeImportsProcessor.StripNonAscii(prepared));

            // The receipt binds each generated observation to every original source consumed by its producer.
            Directory.CreateDirectory(receiptDirectory);
            string receiptFile = Path.Combine(receiptDirectory,
                Path.GetFileNameWithoutExtension(fixturePath) + ".source-receipt.json");
            File.WriteAllText(receiptFile, JsonSerializer.Serialize(new
            {
                Version = 1,
                Producer = producer.Root,
                Fixture = fixturePath,
                ProducerGraph = producer.Graph,
                ProducerInputs = producer.Inputs
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return 0;
    }

    #endregion
}
