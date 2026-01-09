using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>
/// Utility class for comparing pixel data from GPU readback.
/// Provides detailed mismatch reporting for debugging shader tests.
/// </summary>
public static class PixelComparer
{
    /// <summary>
    /// Default epsilon for float comparisons (1e-4).
    /// </summary>
    public const float DefaultEpsilon = 1e-4f;

    /// <summary>
    /// Maximum number of mismatches to report in detail.
    /// </summary>
    public const int MaxMismatchesToReport = 10;

    /// <summary>
    /// Asserts that two pixel arrays match within the specified tolerance.
    /// </summary>
    /// <param name="expected">Expected pixel values.</param>
    /// <param name="actual">Actual pixel values from GPU readback.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="channels">Number of channels per pixel (1-4).</param>
    /// <param name="epsilon">Tolerance for float comparison.</param>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown when pixels don't match.</exception>
    public static void AssertPixelsMatch(
        float[] expected,
        float[] actual,
        int width,
        int height,
        int channels = 4,
        float epsilon = DefaultEpsilon)
    {
        AssertPixelsMatch(expected, actual, width, height, channels,
            new ChannelEpsilon(epsilon, epsilon, epsilon, epsilon));
    }

    /// <summary>
    /// Asserts that two pixel arrays match with per-channel tolerance.
    /// </summary>
    /// <param name="expected">Expected pixel values.</param>
    /// <param name="actual">Actual pixel values from GPU readback.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="channels">Number of channels per pixel (1-4).</param>
    /// <param name="channelEpsilon">Per-channel tolerance values.</param>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown when pixels don't match.</exception>
    public static void AssertPixelsMatch(
        float[] expected,
        float[] actual,
        int width,
        int height,
        int channels,
        ChannelEpsilon channelEpsilon)
    {
        int expectedSize = width * height * channels;

        if (expected.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Expected array size {expected.Length} doesn't match dimensions {width}×{height}×{channels} = {expectedSize}",
                nameof(expected));
        }

        if (actual.Length != expectedSize)
        {
            Assert.Fail($"Array size mismatch: expected {expectedSize}, got {actual.Length}");
            return;
        }

        var mismatches = new List<PixelMismatch>();
        float maxDelta = 0f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width + x) * channels;

                for (int c = 0; c < channels; c++)
                {
                    float exp = expected[pixelIndex + c];
                    float act = actual[pixelIndex + c];
                    float delta = MathF.Abs(exp - act);
                    float eps = channelEpsilon.GetEpsilon(c);

                    maxDelta = MathF.Max(maxDelta, delta);

                    if (delta > eps)
                    {
                        mismatches.Add(new PixelMismatch(x, y, c, exp, act, delta));
                    }
                }
            }
        }

        if (mismatches.Count > 0)
        {
            var message = FormatMismatchMessage(mismatches, width, height, channels, maxDelta);
            Assert.Fail(message);
        }
    }

    /// <summary>
    /// Compares two pixel arrays and returns the result without throwing.
    /// </summary>
    /// <param name="expected">Expected pixel values.</param>
    /// <param name="actual">Actual pixel values from GPU readback.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="channels">Number of channels per pixel (1-4).</param>
    /// <param name="epsilon">Tolerance for float comparison.</param>
    /// <returns>Comparison result with match status and any mismatches.</returns>
    public static ComparisonResult Compare(
        float[] expected,
        float[] actual,
        int width,
        int height,
        int channels = 4,
        float epsilon = DefaultEpsilon)
    {
        int expectedSize = width * height * channels;

        if (expected.Length != expectedSize || actual.Length != expectedSize)
        {
            return new ComparisonResult(
                false,
                $"Array size mismatch: expected {expectedSize}, got expected={expected.Length}, actual={actual.Length}",
                [], 0f);
        }

        var mismatches = new List<PixelMismatch>();
        float maxDelta = 0f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width + x) * channels;

                for (int c = 0; c < channels; c++)
                {
                    float exp = expected[pixelIndex + c];
                    float act = actual[pixelIndex + c];
                    float delta = MathF.Abs(exp - act);

                    maxDelta = MathF.Max(maxDelta, delta);

                    if (delta > epsilon)
                    {
                        mismatches.Add(new PixelMismatch(x, y, c, exp, act, delta));
                    }
                }
            }
        }

        bool isMatch = mismatches.Count == 0;
        string? message = isMatch ? null : FormatMismatchMessage(mismatches, width, height, channels, maxDelta);

        return new ComparisonResult(isMatch, message, mismatches, maxDelta);
    }

    /// <summary>
    /// Gets a single pixel value from a pixel array.
    /// </summary>
    /// <param name="data">Pixel data array.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="width">Image width.</param>
    /// <param name="channels">Channels per pixel.</param>
    /// <returns>Array of channel values for the pixel.</returns>
    public static float[] GetPixel(float[] data, int x, int y, int width, int channels = 4)
    {
        int index = (y * width + x) * channels;
        float[] pixel = new float[channels];
        Array.Copy(data, index, pixel, 0, channels);
        return pixel;
    }

    /// <summary>
    /// Formats a pixel value as a string for display.
    /// </summary>
    /// <param name="values">Channel values.</param>
    /// <returns>Formatted string like "(0.500, 1.000, 0.000, 1.000)".</returns>
    public static string FormatPixel(float[] values)
    {
        return "(" + string.Join(", ", Array.ConvertAll(values, v => v.ToString("F3", CultureInfo.InvariantCulture))) + ")";
    }

    /// <summary>
    /// Formats a pixel value as hex color (for 3 or 4 channel data).
    /// </summary>
    /// <param name="values">Channel values (assumed 0-1 range).</param>
    /// <returns>Hex string like "#FF8040" or "#FF8040FF".</returns>
    public static string FormatPixelAsHex(float[] values)
    {
        if (values.Length < 3)
            return FormatPixel(values);

        byte r = (byte)(Math.Clamp(values[0], 0f, 1f) * 255);
        byte g = (byte)(Math.Clamp(values[1], 0f, 1f) * 255);
        byte b = (byte)(Math.Clamp(values[2], 0f, 1f) * 255);

        if (values.Length >= 4)
        {
            byte a = (byte)(Math.Clamp(values[3], 0f, 1f) * 255);
            return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
        }

        return $"#{r:X2}{g:X2}{b:X2}";
    }

    #region Private Helpers

    private static string FormatMismatchMessage(
        List<PixelMismatch> mismatches,
        int width,
        int height,
        int channels,
        float maxDelta)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Pixel comparison failed: {mismatches.Count} mismatches in {width}×{height} image ({channels} channels)");
        sb.AppendLine($"Max delta: {maxDelta:E3}");
        sb.AppendLine();

        int reportCount = Math.Min(mismatches.Count, MaxMismatchesToReport);
        sb.AppendLine($"First {reportCount} mismatches:");

        string[] channelNames = ["R", "G", "B", "A"];

        for (int i = 0; i < reportCount; i++)
        {
            var m = mismatches[i];
            string channelName = m.Channel < channelNames.Length ? channelNames[m.Channel] : $"C{m.Channel}";
            sb.AppendLine($"  [{m.X}, {m.Y}].{channelName}: expected {m.Expected:F6}, got {m.Actual:F6} (Δ={m.Delta:E3})");
        }

        if (mismatches.Count > MaxMismatchesToReport)
        {
            sb.AppendLine($"  ... and {mismatches.Count - MaxMismatchesToReport} more mismatches");
        }

        return sb.ToString();
    }

    #endregion
}

/// <summary>
/// Per-channel epsilon values for HDR tolerance.
/// </summary>
/// <param name="R">Red channel tolerance.</param>
/// <param name="G">Green channel tolerance.</param>
/// <param name="B">Blue channel tolerance.</param>
/// <param name="A">Alpha channel tolerance.</param>
public readonly record struct ChannelEpsilon(float R, float G, float B, float A)
{
    /// <summary>
    /// Creates a uniform epsilon for all channels.
    /// </summary>
    public static ChannelEpsilon Uniform(float epsilon) => new(epsilon, epsilon, epsilon, epsilon);

    /// <summary>
    /// Gets the epsilon for a specific channel index.
    /// </summary>
    public float GetEpsilon(int channel) => channel switch
    {
        0 => R,
        1 => G,
        2 => B,
        3 => A,
        _ => R // Default to R for single-channel
    };
}

/// <summary>
/// Information about a single pixel mismatch.
/// </summary>
/// <param name="X">X coordinate of the mismatched pixel.</param>
/// <param name="Y">Y coordinate of the mismatched pixel.</param>
/// <param name="Channel">Channel index (0=R, 1=G, 2=B, 3=A).</param>
/// <param name="Expected">Expected value.</param>
/// <param name="Actual">Actual value from GPU readback.</param>
/// <param name="Delta">Absolute difference between expected and actual.</param>
public readonly record struct PixelMismatch(int X, int Y, int Channel, float Expected, float Actual, float Delta);

/// <summary>
/// Result of a pixel comparison operation.
/// </summary>
/// <param name="IsMatch">True if all pixels match within tolerance.</param>
/// <param name="Message">Error message if comparison failed, null otherwise.</param>
/// <param name="Mismatches">List of individual pixel mismatches.</param>
/// <param name="MaxDelta">Maximum delta across all pixel comparisons.</param>
public readonly record struct ComparisonResult(
    bool IsMatch,
    string? Message,
    IReadOnlyList<PixelMismatch> Mismatches,
    float MaxDelta);
