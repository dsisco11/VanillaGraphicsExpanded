namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Integrates the normal-view GGX hemisphere independently of the production importance sampler and lookup.</summary>
internal static class MicrofacetEnergyReference
{
    #region Public API
    /// <summary>Returns single-scattering reflectance and its Schlick grazing coefficient by Simpson hemisphere quadrature.</summary>
    internal static (double E, double Ef) NormalEnergy(double roughness)
    {
        const int intervals = 65536;
        double r = Math.Clamp(roughness, .04, 1), a2 = Math.Pow(r, 4);
        double sum = 0, fresnelSum = 0;
        // At normal view the azimuth integral is exactly 2*pi. Integrating light cosine,
        // rather than sampling half vectors, gives a separate numerical reference.
        for (int index = 0; index <= intervals; index++)
        {
            double nl = (double)index / intervals;
            double h2 = (1 + nl) * .5;
            double denominator = (1 - h2) + a2 * h2;
            double distribution = a2 / (Math.PI * denominator * denominator);
            double visibility = .5 / (nl + Math.Sqrt(a2 + (1 - a2) * nl * nl));
            double projected = distribution * visibility * nl * 2 * Math.PI;
            double weight = index is 0 or intervals ? 1 : index % 2 == 0 ? 2 : 4;
            sum += weight * projected;
            fresnelSum += weight * projected * Math.Pow(1 - Math.Sqrt(h2), 5);
        }
        return (sum / (3 * intervals), fresnelSum / (3 * intervals));
    }

    /// <summary>Calculates the compensated normal-view lobe scale and its integrated reflection budget.</summary>
    internal static (double Gain, double Reflection) NormalResponse(double roughness, double f0)
    {
        var energy = NormalEnergy(roughness);
        double gain = 1 + f0 * (1 - energy.E) / energy.E;
        return (gain, gain * (energy.E * f0 + energy.Ef * (1 - f0)));
    }
    #endregion
}
