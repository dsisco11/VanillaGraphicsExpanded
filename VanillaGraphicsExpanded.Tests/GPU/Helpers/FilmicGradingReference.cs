using System.Numerics;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Evaluates the grading contract in double precision without reading production LUT texels.</summary>
internal static class FilmicGradingReference
{
    #region Public API
    /// <summary>Independently reconstructs trilinear sampling of the half-float 64-cubed display lookup.</summary>
    internal static Vector3 Lookup(Vector3 input, Vector4 controls)
    {
        Vector3 grid = new(Coordinate(input.X), Coordinate(input.Y), Coordinate(input.Z));
        int[] lower = [(int)MathF.Floor(grid.X), (int)MathF.Floor(grid.Y), (int)MathF.Floor(grid.Z)];
        Vector3 fraction = grid - new Vector3(lower[0], lower[1], lower[2]);
        Vector3 result = Vector3.Zero;
        for (int z = 0; z < 2; z++) for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
        {
            Vector3 sample = Display(new(Node(Math.Min(63, lower[0] + x)), Node(Math.Min(63, lower[1] + y)),
                Node(Math.Min(63, lower[2] + z))), controls);
            sample = new((float)(Half)sample.X, (float)(Half)sample.Y, (float)(Half)sample.Z);
            float weight = (x == 0 ? 1 - fraction.X : fraction.X) * (y == 0 ? 1 - fraction.Y : fraction.Y)
                * (z == 0 ? 1 - fraction.Z : fraction.Z);
            result += sample * weight;
        }
        return result;
    }

    /// <summary>Decodes a generation-grid coordinate into exposed scene-linear radiance.</summary>
    internal static float Node(int coordinate) => (float)((Math.Pow(2, 32.0 * coordinate / 63) - 1) / 65536);
    /// <summary>Transforms exposed scene-linear Rec.709 RGB into encoded display RGB.</summary>
    internal static Vector3 Display(Vector3 input, Vector4 controls)
    {
        double[] ap1 =
        [
            .6130974024 * input.X + .3395231462 * input.Y + .0473794514 * input.Z,
            .0701937225 * input.X + .9163538791 * input.Y + .0134523985 * input.Z,
            .0206155929 * input.X + .1095697729 * input.Y + .8698146342 * input.Z
        ];
        for (int channel = 0; channel < 3; channel++)
        {
            double contrasted = .18 * Math.Pow(Math.Max(0, ap1[channel]) / .18, Math.Max(.01, 1 + controls.W));
            ap1[channel] = Math.Pow(contrasted, 1 / Math.Max(.001, controls.X * controls.Y)) * Math.Max(0, controls.Z);
        }
        double luminance = Luminance(ap1);
        for (int channel = 0; channel < 3; channel++) ap1[channel] = Curve(luminance * .04 + ap1[channel] * .96);
        luminance = Luminance(ap1);
        for (int channel = 0; channel < 3; channel++) ap1[channel] = luminance * .07 + ap1[channel] * .93;
        return new((float)Encode(1.7050509927 * ap1[0] - .6217921207 * ap1[1] - .0832588720 * ap1[2]),
            (float)Encode(-.1302564175 * ap1[0] + 1.1408047366 * ap1[1] - .0105483191 * ap1[2]),
            (float)Encode(-.0240033568 * ap1[0] - .1289689761 * ap1[1] + 1.1529723329 * ap1[2]));
    }

    /// <summary>Evaluates the calibrated logarithmic toe, straight segment and shoulder for one channel.</summary>
    internal static double Curve(double value)
    {
        if (value <= 0) return 0;
        const double slope = .88, toeRange = .45, shoulderRange = .78;
        double midpoint = .18 / toeRange - 1;
        double toeJoin = Math.Log10(.18) - .5 * Math.Log((1 + midpoint) / (1 - midpoint)) * toeRange / slope;
        double lineOffset = toeRange / slope - toeJoin;
        double shoulderJoin = .26 / slope - lineOffset;
        double x = Math.Log10(value);
        double line = slope * (x + lineOffset);
        double toe = x < toeJoin ? 2 * toeRange / (1 + Math.Exp(-2 * slope / toeRange * (x - toeJoin))) : line;
        double shoulder = x > shoulderJoin ? 1.04 - 2 * shoulderRange / (1 + Math.Exp(2 * slope / shoulderRange * (x - shoulderJoin))) : line;
        double blend = Math.Clamp((x - toeJoin) / (shoulderJoin - toeJoin), 0, 1);
        if (shoulderJoin < toeJoin) blend = 1 - blend;
        blend *= blend * (3 - 2 * blend);
        return Math.Clamp(toe * (1 - blend) + shoulder * blend, 0, 1);
    }

    /// <summary>Produces the integer output code with the production deterministic ordered dither rank.</summary>
    internal static float Quantize(float value, int x = 0, int y = 0)
    {
        int rank = 0;
        for (int bit = 0; bit < 3; bit++) rank = (rank << 2) | ((((x >> bit) ^ (y >> bit)) & 1) << 1) | ((y >> bit) & 1);
        double offset = ((rank + .5) / 64 - .5) / 255;
        return (float)(Math.Floor(Math.Clamp(value + offset, 0, 1) * 255 + .5) / 255);
    }
    #endregion

    #region Private
    /// <summary>Maps nonnegative scene values to the endpoint-aligned logarithmic lookup grid.</summary>
    private static float Coordinate(float value) => (float)(Math.Clamp(Math.Log2(1 + Math.Max(value, 0) * 65536.0) / 32, 0, 1) * 63);
    /// <summary>Computes AP1 luminance before each desaturation operation.</summary>
    private static double Luminance(double[] rgb) => .2722287168 * rgb[0] + .6740817658 * rgb[1] + .0536895174 * rgb[2];
    /// <summary>Contains the display gamut and applies the piecewise sRGB transfer once.</summary>
    private static double Encode(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value <= .0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - .055;
    }
    #endregion
}
