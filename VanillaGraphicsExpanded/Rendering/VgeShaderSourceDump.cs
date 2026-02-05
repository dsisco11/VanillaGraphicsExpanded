using System;
using System.IO;
using System.Linq;
using System.Text;

using Vintagestory.API.Config;

namespace VanillaGraphicsExpanded.Rendering;

internal static class VgeShaderSourceDump
{
    public static bool TryDumpSingleStage(string shaderName, string stageExtension, string source, out string? dumpPath, out string? error)
    {
        dumpPath = null;
        error = null;

        if (string.IsNullOrWhiteSpace(shaderName))
        {
            error = "Shader name was empty";
            return false;
        }

        if (string.IsNullOrEmpty(source))
        {
            error = "Shader source was empty";
            return false;
        }

        try
        {
            // Requirement: keep dump filename stable as <shader-name>.dump.txt in GamePaths.Logs/VGE.
            // The stage is captured inside the dump file content instead of in the filename.
            string baseName = SanitizeFileName(shaderName.Trim());

            string dir = Path.Combine(GamePaths.Logs, "VGE");
            Directory.CreateDirectory(dir);

            dumpPath = Path.Combine(dir, $"{baseName}.dump.txt");

            var sb = new StringBuilder(capacity: Math.Max(16 * 1024, source.Length + 256));
            sb.AppendLine("// VGE shader dump");
            sb.Append("// Shader: ").AppendLine(shaderName);
            if (!string.IsNullOrWhiteSpace(stageExtension))
            {
                sb.Append("// Stage: ").AppendLine(stageExtension.Trim());
            }
            sb.Append("// UTC: ").AppendLine(DateTime.UtcNow.ToString("O"));
            sb.AppendLine();
            sb.AppendLine(source);
            if (!source.EndsWith("\n", StringComparison.Ordinal))
            {
                sb.AppendLine();
            }

            File.WriteAllText(dumpPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            dumpPath = null;
            return false;
        }
    }

    public static bool TryDumpProgram(string programName, string? vertexSource, string? fragmentSource, string? geometrySource, out string? dumpPath, out string? error)
    {
        dumpPath = null;
        error = null;

        if (string.IsNullOrWhiteSpace(programName))
        {
            error = "Program name was empty";
            return false;
        }

        bool any = !(string.IsNullOrEmpty(vertexSource) && string.IsNullOrEmpty(fragmentSource) && string.IsNullOrEmpty(geometrySource));
        if (!any)
        {
            error = "All stage sources were empty";
            return false;
        }

        try
        {
            string baseName = SanitizeFileName(programName.Trim());

            string dir = Path.Combine(GamePaths.Logs, "VGE");
            Directory.CreateDirectory(dir);

            dumpPath = Path.Combine(dir, $"{baseName}.dump.txt");

            var sb = new StringBuilder(capacity: 16 * 1024);
            sb.AppendLine("// VGE shader dump");
            sb.Append("// Program: ").AppendLine(programName);
            sb.Append("// UTC: ").AppendLine(DateTime.UtcNow.ToString("O"));
            sb.AppendLine();

            AppendStage(sb, "VERTEX", vertexSource);
            AppendStage(sb, "FRAGMENT", fragmentSource);
            AppendStage(sb, "GEOMETRY", geometrySource);

            File.WriteAllText(dumpPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            dumpPath = null;
            return false;
        }
    }

    private static void AppendStage(StringBuilder sb, string stageLabel, string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        sb.AppendLine("============================================================");
        sb.Append("// ").Append(stageLabel).AppendLine(" SHADER");
        sb.AppendLine("============================================================");
        sb.AppendLine(source);
        if (!source.EndsWith("\n", StringComparison.Ordinal))
        {
            sb.AppendLine();
        }
        sb.AppendLine();
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "shader";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "shader" : cleaned;
    }
}
