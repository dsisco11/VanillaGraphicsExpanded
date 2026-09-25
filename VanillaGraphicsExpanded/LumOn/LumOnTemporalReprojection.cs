using System;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Pairs temporal matrices with their render origins and rebases history for current positions.</summary>
internal sealed class LumOnTemporalReprojection
{
    private readonly float[] currentMatrix = new float[16];
    private readonly float[] previousMatrix = new float[16];
    private double currentX, currentY, currentZ;
    private double previousX, previousY, previousZ;
    private bool hasHistory;

    #region Public API

    /// <summary>Projects current render-relative positions into the committed previous frame.</summary>
    public float[] PreviousViewProjection { get; } = new float[16];

    /// <summary>Captures the current matrix and prepares history without modifying the committed frame.</summary>
    public void Capture(ReadOnlySpan<float> currentViewProjection, double originX, double originY, double originZ)
    {
        if (currentViewProjection.Length != 16)
            throw new ArgumentException("Expected a 4x4 matrix.", nameof(currentViewProjection));

        currentViewProjection.CopyTo(currentMatrix);
        currentX = originX;
        currentY = originY;
        currentZ = originZ;
        if (!hasHistory)
        {
            currentViewProjection.CopyTo(PreviousViewProjection);
            return;
        }

        // Subtract absolute origins in double precision before converting the small displacement.
        // Pprev * T(currentOrigin - previousOrigin) accepts current render-relative positions.
        float dx = (float)(currentX - previousX);
        float dy = (float)(currentY - previousY);
        float dz = (float)(currentZ - previousZ);
        previousMatrix.CopyTo(PreviousViewProjection, 0);
        for (int row = 0; row < 4; row++)
            PreviousViewProjection[12 + row] = previousMatrix[row] * dx
                + previousMatrix[4 + row] * dy + previousMatrix[8 + row] * dz
                + previousMatrix[12 + row];
    }

    /// <summary>Commits the captured matrix and its exact origin after its frame has been rendered.</summary>
    public void Commit()
    {
        currentMatrix.CopyTo(previousMatrix, 0);
        previousX = currentX;
        previousY = currentY;
        previousZ = currentZ;
        hasHistory = true;
    }

    #endregion
}
