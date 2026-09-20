namespace OtterLogic.Fabrication;

/// <summary>
/// Settings for <see cref="PanelTypology"/>: the rectangle the machine can make,
/// the gap between panels, how hard to hold to that rectangle, and what counts as
/// the same panel.
/// </summary>
public sealed record PanelTypologyOptions
{
    /// <summary>The longest panel the machine can make. Panels run this way along a surface's long axis.</summary>
    public double Length { get; init; } = 1.0;

    /// <summary>The widest panel the machine can make.</summary>
    public double Width { get; init; } = 1.0;

    /// <summary>The joint between one panel and the next. Panels sit flush to the edges of the surface.</summary>
    public double Gap { get; init; }

    /// <summary>
    /// How hard to hold to the machine's full rectangle, 0 to 1 — the only judgement
    /// in the layout, and the user's to make.
    /// <para>
    /// At <b>1</b> a row is cut into as many full-size panels as it will take and
    /// whatever is left over is left over, however small: the most repeats, and some
    /// slivers at the edge. At <b>0</b> the leftover is shared out instead, so a row
    /// comes out as a few equal panels of no particular size: fewer panels, no
    /// slivers, more bespoke sizes. In between it trades one for the other.
    /// </para>
    /// </summary>
    public double Standardisation { get; init; } = 1.0;

    /// <summary>
    /// How far two panels may differ and still be one type — the fabrication
    /// tolerance, not a property of the model.
    /// <para>
    /// Measured around the whole outline rather than on length and width, so it is
    /// the distance between two panels averaged point for corresponding point. A
    /// notch or a raked corner therefore counts as a difference, which a pair of
    /// overall dimensions cannot see. Raising it is the rationalisation question:
    /// how much would have to be accepted to have fewer types.
    /// </para>
    /// </summary>
    public double TypeTolerance { get; init; } = 0.001;

    /// <summary>Points closer than this are the same point, and a surface flatter than this is flat.</summary>
    public double Tolerance { get; init; } = 0.001;

    /// <summary>Checks these settings.</summary>
    public void Validate()
    {
        if (!double.IsFinite(Length) || Length <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Length), Length, "The panel length must be above zero.");
        if (!double.IsFinite(Width) || Width <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Width), Width, "The panel width must be above zero.");
        if (!double.IsFinite(Gap) || Gap < 0.0)
            throw new ArgumentOutOfRangeException(nameof(Gap), Gap, "The gap must be zero or above.");
        if (!double.IsFinite(Standardisation) || Standardisation < 0.0 || Standardisation > 1.0)
            throw new ArgumentOutOfRangeException(nameof(Standardisation), Standardisation,
                "Standardisation runs from 0, share the leftover out, to 1, hold to the full panel.");
        if (!double.IsFinite(TypeTolerance) || TypeTolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(TypeTolerance), TypeTolerance, "The type tolerance must be zero or above.");
        if (!double.IsFinite(Tolerance) || Tolerance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Tolerance), Tolerance, "The tolerance must be above zero.");
    }
}
