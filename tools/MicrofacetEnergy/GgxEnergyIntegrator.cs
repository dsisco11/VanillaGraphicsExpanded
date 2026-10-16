using System.Numerics;

/// <summary>Integrates correlated Smith GGX energy using the shared deterministic visible-normal samples.</summary>
internal static class GgxEnergyIntegrator
{
    #region Public API
    /// <summary>Evaluates directional and Fresnel-weighted albedo in double precision.</summary>
    internal static (double E, double Ef) Integrate(double roughness, double nv, GgxIntegrationSamples samples)
    {
        double alpha = roughness * roughness, a2 = alpha * alpha;
        double vx = Math.Sqrt(Math.Max(0, 1 - nv * nv));
        double stretchedLength = Math.Sqrt(a2 * vx * vx + nv * nv);
        double sx = alpha * vx / stretchedLength, sz = nv / stretchedLength;
        double sqrtV = Math.Sqrt(a2 + (1 - a2) * nv * nv);
        double blend = (1 + sz) * .5;
        double sum = 0, fresnelSum = 0;
        ReadOnlySpan<double> x = samples.X, y = samples.Y, capY = samples.CapY;
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            int width = Vector<double>.Count;
            var zero = Vector<double>.Zero;
            var one = new Vector<double>(1);
            var two = new Vector<double>(2);
            var alphaVector = new Vector<double>(alpha);
            var a2Vector = new Vector<double>(a2);
            var vxVector = new Vector<double>(vx);
            var nvVector = new Vector<double>(nv);
            var sxVector = new Vector<double>(sx);
            var szVector = new Vector<double>(sz);
            var blendVector = new Vector<double>(blend);
            var sqrtVVector = new Vector<double>(sqrtV);
            var nvPlusSqrtV = new Vector<double>(nv + sqrtV);
            var oneMinusA2 = new Vector<double>(1 - a2);
            var vectorSum = zero;
            var vectorFresnelSum = zero;
            // Integrate independent directions in parallel lanes. Rejected directions
            // contribute zero rather than branching; NaNs from their unused divisions
            // are masked before either sum receives a value.
            for (; i <= samples.Count - width; i += width)
            {
                var dx = new Vector<double>(x.Slice(i, width));
                var dy = (one - blendVector) * new Vector<double>(capY.Slice(i, width))
                    + blendVector * new Vector<double>(y.Slice(i, width));
                var dz = Vector.SquareRoot(Vector.Max(zero, one - dx * dx - dy * dy));
                var hx = alphaVector * (-szVector * dy + sxVector * dz);
                var hy = alphaVector * dx;
                var hz = Vector.Max(zero, sxVector * dy + szVector * dz);
                var length = Vector.SquareRoot(hx * hx + hy * hy + hz * hz);
                hx /= length; hz /= length;
                var vh = vxVector * hx + nvVector * hz;
                var nl = two * vh * hz - nvVector;
                var sqrtL = Vector.SquareRoot(a2Vector + oneMinusA2 * nl * nl);
                var weight = nl * nvPlusSqrtV / (nl * sqrtVVector + nvVector * sqrtL);
                var accepted = Vector.GreaterThan(vh, zero) & Vector.GreaterThan(nl, zero);
                weight = Vector.ConditionalSelect(accepted, weight, zero);
                // Schlick's integer fifth power needs only three vector multiplies.
                var fresnel = one - Vector.Min(one, Vector.Max(zero, vh));
                var squared = fresnel * fresnel;
                vectorSum += weight;
                vectorFresnelSum += weight * (squared * squared * fresnel);
            }
            // Each lane retains double-precision accumulation. Only the final
            // reduction changes summation order; binary32 asset equality is checked separately.
            for (int lane = 0; lane < width; lane++)
            {
                sum += vectorSum[lane];
                fresnelSum += vectorFresnelSum[lane];
            }
        }
        // The scalar path also handles CPUs without SIMD and non-multiple sample counts.
        for (; i < samples.Count; i++)
        {
            double dx = x[i], dy = (1 - blend) * capY[i] + blend * y[i];
            double dz = Math.Sqrt(Math.Max(0, 1 - dx * dx - dy * dy));
            // Transform the warped disk through the stretched view frame, then undo stretch.
            double hx = alpha * (-sz * dy + sx * dz), hy = alpha * dx;
            double hz = Math.Max(0, sx * dy + sz * dz);
            double length = Math.Sqrt(hx * hx + hy * hy + hz * hz);
            hx /= length; hz /= length;
            double vh = vx * hx + nv * hz, nl = 2 * vh * hz - nv;
            if (vh <= 0 || nl <= 0) continue;
            double sqrtL = Math.Sqrt(a2 + (1 - a2) * nl * nl);
            // Cancel the visible-normal PDF analytically. This also defines the nv=0 limit.
            double weight = nl * (nv + sqrtV) / (nl * sqrtV + nv * sqrtL);
            sum += weight;
            fresnelSum += weight * Math.Pow(1 - Math.Clamp(vh, 0, 1), 5);
        }
        return (sum / samples.Count, fresnelSum / samples.Count);
    }
    #endregion
}
