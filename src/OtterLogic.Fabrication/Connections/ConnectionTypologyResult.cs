namespace OtterLogic.Fabrication;

/// <summary>One connection type: joints alike enough to be one detail.</summary>
/// <param name="Joints">The joints of this type, ascending.</param>
/// <param name="Exemplar">
/// The most typical joint of the type — its medoid. The one to detail on behalf of
/// the rest.
/// </param>
/// <param name="Spread">
/// The furthest any joint of the type sits from the exemplar, in prepared units.
/// Zero means every joint is identical; small but not zero means near-identical
/// variants — the place to look for a rationalisation.
/// </param>
/// <param name="LeftHanded">Joints of the type with handedness −1.</param>
/// <param name="RightHanded">Joints of the type with handedness +1.</param>
/// <param name="Arms">The exemplar's members in words: "4 arms — 1 plumb up, 1 plumb down, 2 level".</param>
/// <param name="Description">
/// What sets the type apart from every other joint, from
/// <see cref="OtterLogic.Unsupervised.Clustering.GroupSignature"/> — features, direction,
/// and the type's values against the rest.
/// </param>
public sealed record ConnectionType(int[] Joints, int Exemplar, double Spread, int LeftHanded, int RightHanded, string Arms, string Description)
{
    /// <summary>Number of joints of this type.</summary>
    public int Count => Joints.Length;
}

/// <summary>
/// The connection types a line model's joints fall into, which joints are
/// one-offs, and the signatures it was all read from.
/// </summary>
public sealed class ConnectionTypologyResult
{
    internal ConnectionTypologyResult(
        JointSignatureResult signatures,
        int[] type,
        double[] confidence,
        double[] distanceFromExemplar,
        IReadOnlyList<ConnectionType> types,
        string summary,
        IReadOnlyList<string> notes)
    {
        Signatures = signatures;
        Type = type;
        Confidence = confidence;
        DistanceFromExemplar = distanceFromExemplar;
        Types = types;
        Summary = summary;
        Notes = notes;
    }

    /// <summary>The joints and their signatures, as <see cref="JointSignature"/> read them.</summary>
    public JointSignatureResult Signatures { get; }

    /// <summary>Type per joint, largest type first; −1 for a one-off.</summary>
    public int[] Type { get; }

    /// <summary>Per joint, how firmly it belongs to its type, 0 to 1; 0 for a one-off.</summary>
    public double[] Confidence { get; }

    /// <summary>Per joint, its distance from its type's exemplar in prepared units; NaN for a one-off.</summary>
    public double[] DistanceFromExemplar { get; }

    /// <summary>The types, largest first.</summary>
    public IReadOnlyList<ConnectionType> Types { get; }

    /// <summary>Joints alike to no other, ascending.</summary>
    public int[] OneOffs => Enumerable.Range(0, Type.Length).Where(j => Type[j] < 0).ToArray();

    /// <summary>Every type described in plain lines, then the one-offs.</summary>
    public string Summary { get; }

    /// <summary>
    /// Every joint, grouped: one group per type, largest first, then the one-offs as a
    /// last group of their own when there are any — so every joint is in exactly one
    /// group, and a list of groups is the whole model.
    /// </summary>
    public int[][] Groups()
    {
        var groups = Types.Select(t => t.Joints).ToList();
        var oneOffs = OneOffs;
        if (oneOffs.Length > 0)
            groups.Add(oneOffs);
        return groups.ToArray();
    }

    /// <summary>
    /// A name for each of <see cref="Groups"/>, in the same order: "Type 0 — 12 joints:
    /// 4 arms — 1 plumb up, 1 plumb down, 2 level", and "One-offs — 3 joints" last.
    /// </summary>
    public string[] GroupNames()
    {
        var names = Types.Select((t, i) => $"Type {i} — {t.Count} joints: {t.Arms}").ToList();
        int oneOffs = OneOffs.Length;
        if (oneOffs > 0)
            names.Add($"One-offs — {oneOffs} joint{(oneOffs == 1 ? "" : "s")}, alike to no other");
        return names.ToArray();
    }

    /// <summary>Anything that changed what was found, in plain words.</summary>
    public IReadOnlyList<string> Notes { get; }
}
