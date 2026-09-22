using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Fabrication;

/// <summary>
/// Settings for <see cref="JointSignature"/>. The claims about geometry are here;
/// how line inclinations are banded is the nested <see cref="Banding"/>.
/// </summary>
public sealed record JointSignatureOptions
{
    /// <summary>
    /// The document's absolute tolerance: points closer than this are the same
    /// point, and a line shorter than this has no direction.
    /// </summary>
    public double Tolerance { get; init; } = 0.001;

    /// <summary>
    /// Line ends closer than this are one joint, and a line end this close to the
    /// middle of another line bears on it. Null reads it as ten times
    /// <see cref="Tolerance"/>, as the Structural Insight Engine does, so the two
    /// tools find the same joints in the same model.
    /// </summary>
    public double? JoinDistance { get; init; }

    /// <summary>How line inclinations are banded into level, pitched and plumb.</summary>
    public ValueBandsOptions Banding { get; init; } = new();

    internal double Join => JoinDistance ?? 10.0 * Tolerance;

    /// <summary>Checks these settings.</summary>
    public void Validate()
    {
        if (!double.IsFinite(Tolerance) || Tolerance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Tolerance), Tolerance, "Tolerance must be above zero.");
        if (JoinDistance is { } join && (!double.IsFinite(join) || join < Tolerance))
            throw new ArgumentOutOfRangeException(nameof(JoinDistance), join, "Join distance must be at least the tolerance.");
        ArgumentNullException.ThrowIfNull(Banding);
        Banding.Validate();
    }
}
