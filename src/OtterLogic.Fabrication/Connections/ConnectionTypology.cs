using System.Globalization;
using System.Text;
using OtterLogic.MachineLearning.Preprocessing;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Fabrication;

/// <summary>
/// Finds the connection types a line model actually has: every joint described by
/// <see cref="JointSignature"/>, the descriptions clustered, and every type explained
/// and given an exemplar to detail.
/// <para>
/// Nothing here knows what a corner, a splice or a base plate is. The types are
/// whatever the model's own joints repeat; what each is called is left to whoever
/// reads the description.
/// </para>
/// <para>
/// HDBSCAN does the grouping, with its smallest cluster set to
/// <see cref="ConnectionTypologyOptions.MinimumTypeSize"/> — two, because a type is a
/// connection made more than once. That is chosen over <see cref="ClusterSelector"/>
/// on evidence, not taste: joint signatures are mostly exact repeats with a few
/// genuine one-offs, and on the worked frame the selector, sizing clusters for data
/// in general, found 4 types and 11 one-offs where HDBSCAN at two found the 10 types
/// and the one joint a person would, and filed a beam skewed by a tenth of a degree
/// with the type it nearly is. A joint alike to no other stays unplaced — the one-off
/// list is half of what this is for.
/// </para>
/// </summary>
public static class ConnectionTypology
{
    /// <summary>Features listed per type in the summary.</summary>
    private const int DescribedFeatures = 3;

    /// <summary>
    /// A spread below this is rounding, not a variant. Angles are taken from the sine
    /// and cosine together, so a joint turned in plan matches its original to far
    /// below this; a real variant — the brace-top beam skewed a tenth of a degree in
    /// the example frame, which only nudges its plan balance — measured 1e-5, and a
    /// skewed corner beam 0.004.
    /// </summary>
    private const double NumericalNoise = 1e-6;

    /// <summary>Finds the connection types of a line model.</summary>
    /// <param name="starts">n x 3 line start points.</param>
    /// <param name="ends">n x 3 line end points.</param>
    /// <param name="supports">Optional k x 3 supported points.</param>
    /// <param name="lineAttributes">Optional n x a numbers per line — section depth, profile code.</param>
    /// <param name="attributeNames">A name per attribute column.</param>
    /// <param name="options">Settings; null for the defaults.</param>
    public static ConnectionTypologyResult Classify(
        double[,] starts,
        double[,] ends,
        double[,]? supports = null,
        double[,]? lineAttributes = null,
        IReadOnlyList<string>? attributeNames = null,
        ConnectionTypologyOptions? options = null)
    {
        options ??= new ConnectionTypologyOptions();
        options.Validate();

        var signatures = JointSignature.Describe(starts, ends, supports, lineAttributes, attributeNames, options.Signature);
        var notes = signatures.Notes.ToList();
        int m = signatures.JointCount;

        var raw = signatures.Signature;
        int[] labels;
        double[] confidence;
        double[,] prepared;

        // Columns every joint shares say nothing about which joints differ; with all
        // of them shared there is one type.
        bool anyVariation = Enumerable.Range(0, raw.GetLength(1)).Any(c =>
            Enumerable.Range(1, Math.Max(m - 1, 0)).Any(j => raw[j, c] != raw[0, c]));

        if (m < options.MinimumTypeSize || !anyVariation)
        {
            prepared = new double[m, 1];
            labels = new int[m];
            if (m < options.MinimumTypeSize)
                Array.Fill(labels, -1);
            confidence = labels.Select(l => l < 0 ? 0.0 : 1.0).ToArray();

            notes.Add(m < options.MinimumTypeSize
                ? $"Only {m} joint(s) — fewer than a type needs — so every joint is a one-off."
                : "Every joint has the same signature: one type.");
        }
        else
        {
            prepared = FeaturePipeline.Fit(raw).Transform(raw);
            var density = Hdbscan.Fit(prepared, new HdbscanOptions { MinimumClusterSize = options.MinimumTypeSize });

            labels = ClusterLabels.Canonical(density.Labels);
            confidence = Enumerable.Range(0, m).Select(j => labels[j] < 0 ? 0.0 : density.Probabilities[j]).ToArray();

            if (labels.All(l => l < 0))
                notes.Add("No two joints are alike enough to share a type: every joint is a one-off.");
        }

        var medoids = ClusterExemplars.Medoids(prepared, labels);
        var distance = ClusterExemplars.DistanceFromMedoid(prepared, labels, medoids);
        var members = ClusterLabels.Members(labels);

        // A description needs something to set a type against: at least one type, and
        // at least one joint outside it.
        bool comparable = members.Length > 0 && !(members.Length == 1 && members[0].Length == m);
        var explained = comparable ? GroupSignature.Describe(raw, labels) : null;

        var types = new List<ConnectionType>(members.Length);
        for (int t = 0; t < members.Length; t++)
        {
            var joints = members[t];
            double spread = joints.Length == 0 ? 0.0 : joints.Max(j => distance[j]);
            if (spread <= NumericalNoise)
                spread = 0.0;
            types.Add(new ConnectionType(
                joints,
                medoids[t],
                spread,
                joints.Count(j => signatures.Handedness[j] < 0),
                joints.Count(j => signatures.Handedness[j] > 0),
                Arms(signatures, medoids[t]),
                explained is null ? "The only type: nothing to set it apart from." : Describe(explained, t, signatures.FeatureNames)));
        }

        string summary = Summarise(types, labels, signatures);
        return new ConnectionTypologyResult(signatures, labels, confidence, distance, types, summary, notes);
    }

    /// <summary>A type's strongest distinguishing features, one per line.</summary>
    private static string Describe(GroupSignatureResult explained, int type, string[] names)
    {
        var invariant = CultureInfo.InvariantCulture;
        return string.Join("; ", explained.Ranking[type].Take(DescribedFeatures).Select(j =>
        {
            double separation = explained.Separation[type, j];
            string direction = separation > 0.0 ? "more" : separation < 0.0 ? "less" : "same";
            return $"{names[j]} {direction} ({explained.Means[type, j].ToString("0.##", invariant)} "
                + $"against {explained.RestMeans[type, j].ToString("0.##", invariant)})";
        }));
    }

    /// <summary>A joint's arms in words: "5 arms — 1 plumb up, 1 plumb down, 3 level".</summary>
    private static string Arms(JointSignatureResult signatures, int joint)
    {
        double Column(string name) => signatures.Signature[joint, Array.IndexOf(JointSignature.BaseFeatures, name)];

        var parts = new List<string>();
        void Add(string feature, string words)
        {
            int count = (int)Math.Round(Column(feature));
            if (count > 0)
                parts.Add($"{count} {words}");
        }

        Add("Plumb Up", "plumb up");
        Add("Plumb Down", "plumb down");
        Add("Level", "level");
        Add("Pitched Up", "pitched up");
        Add("Pitched Down", "pitched down");

        int arms = (int)Math.Round(Column("Arms"));
        return $"{arms} {(arms == 1 ? "arm" : "arms")} — {string.Join(", ", parts)}"
            + (Column("Supported") > 0.0 ? ", supported" : string.Empty);
    }

    private static string Summarise(List<ConnectionType> types, int[] labels, JointSignatureResult signatures)
    {
        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        int oneOffs = labels.Count(l => l < 0);

        text.AppendLine($"{types.Count} connection type(s) and {oneOffs} one-off(s) among {labels.Length} joints.");

        for (int t = 0; t < types.Count; t++)
        {
            var type = types[t];
            string hands = type.LeftHanded + type.RightHanded > 0
                ? $", {type.LeftHanded} left / {type.RightHanded} right-handed"
                : string.Empty;
            string variants = type.Spread > 0.0
                ? $", near-identical variants (spread {type.Spread.ToString("G2", invariant)})"
                : ", all identical";

            text.AppendLine();
            text.AppendLine($"Type {t}: {type.Count} joints, exemplar joint {type.Exemplar}{variants}{hands}");
            text.AppendLine($"  {type.Arms}");
            text.AppendLine($"  Distinguished by: {type.Description}");
        }

        if (oneOffs > 0)
        {
            text.AppendLine();
            text.AppendLine("One-offs:");
            for (int j = 0; j < labels.Length; j++)
            {
                if (labels[j] >= 0)
                    continue;

                text.AppendLine(
                    $"  Joint {j} at ({signatures.Joints[j, 0].ToString("G6", invariant)}, "
                    + $"{signatures.Joints[j, 1].ToString("G6", invariant)}, {signatures.Joints[j, 2].ToString("G6", invariant)}): "
                    + Arms(signatures, j));
            }
        }

        return text.ToString().TrimEnd();
    }
}
