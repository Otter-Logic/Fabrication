namespace OtterLogic.Fabrication;

/// <summary>
/// Settings for <see cref="ConnectionTypology"/>. How joints are read is the
/// nested <see cref="Signature"/>; what counts as a type is here.
/// </summary>
public sealed record ConnectionTypologyOptions
{
    /// <summary>How joints are found and described.</summary>
    public JointSignatureOptions Signature { get; init; } = new();

    /// <summary>
    /// Fewest joints a type needs. Two by default, because that is what a type is
    /// in fabrication — a connection made more than once. A joint alike to no other
    /// is a one-off: reported as such rather than filed with whichever type it is
    /// least unlike. Raise it to see only the types that repeat enough to matter.
    /// </summary>
    public int MinimumTypeSize { get; init; } = 2;

    /// <summary>Checks these settings.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Signature);
        Signature.Validate();
        if (MinimumTypeSize < 2)
            throw new ArgumentOutOfRangeException(nameof(MinimumTypeSize), MinimumTypeSize,
                "A type is a connection made at least twice.");
    }
}
