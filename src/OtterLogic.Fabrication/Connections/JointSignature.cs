using OtterLogic.StructuralEngine;

namespace OtterLogic.Fabrication;

/// <summary>
/// Describes every joint of a line model by the same row of numbers, so joints
/// that are the same kind of connection have the same row wherever they are.
/// <para>
/// A joint is where line ends meet, or where a line end bears on the middle of
/// another line — the joints <see cref="StructureGraph"/> finds for the Structural
/// Insight Engine, so the two tools agree about what a joint is. Each joint is read
/// as its <b>arms</b>: a unit direction pointing away from it along every line that
/// meets there, each tagged level, pitched or plumb from the model's own spread of
/// inclinations.
/// </para>
/// <para>
/// The row is built to be unchanged by everything that does not change the
/// connection. Moving the joint, turning it in plan and reordering its lines change
/// nothing, because every term is a count, a sorted angle or an eigenvalue. A
/// mirror image changes nothing, so a left- and right-hand pair share a type — the
/// hand is reported separately. And a member passing through reads the same
/// whether it was drawn as one line or split at the joint, because "through" is
/// never a tag: it is two arms 180 degrees apart, which is what it is on site.
/// Gravity is the one direction it is not blind to, since a base and a roof top
/// differ in exactly that.
/// </para>
/// </summary>
public static class JointSignature
{
    /// <summary>The columns every signature has, in order, before any line attributes.</summary>
    public static readonly string[] BaseFeatures =
    {
        "Arms", "Plumb Up", "Plumb Down", "Level", "Pitched Up", "Pitched Down", "Supported",
        "Largest Angle", "Second Angle", "Third Angle", "Smallest Angle",
        "Spread 1", "Spread 2", "Spread 3", "Vertical Balance", "Level Balance",
    };

    /// <summary>Describes every joint of a line model.</summary>
    /// <param name="starts">n x 3 line start points.</param>
    /// <param name="ends">n x 3 line end points.</param>
    /// <param name="supports">Optional k x 3 supported points.</param>
    /// <param name="lineAttributes">
    /// Optional n x a numbers per line — a section depth, a plate thickness, a profile
    /// code. Each adds two columns to the signature: its largest and smallest over the
    /// joint's arms, so a joint of deep beams is told apart from the same geometry in
    /// shallow ones.
    /// </param>
    /// <param name="attributeNames">A name per attribute column, for the feature names.</param>
    /// <param name="options">Settings; null for the defaults.</param>
    public static JointSignatureResult Describe(
        double[,] starts,
        double[,] ends,
        double[,]? supports = null,
        double[,]? lineAttributes = null,
        IReadOnlyList<string>? attributeNames = null,
        JointSignatureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(ends);
        options ??= new JointSignatureOptions();
        options.Validate();

        int n = starts.GetLength(0);
        if (starts.GetLength(1) != 3 || ends.GetLength(1) != 3 || ends.GetLength(0) != n)
            throw new ArgumentException("Starts and ends must both be n x 3, one row per line.", nameof(ends));
        if (n == 0)
            throw new ArgumentException("Need at least one line.", nameof(starts));
        if (supports is not null && supports.GetLength(1) != 3)
            throw new ArgumentException("Supports must be k x 3.", nameof(supports));

        int attributes = lineAttributes?.GetLength(1) ?? 0;
        if (lineAttributes is not null && lineAttributes.GetLength(0) != n)
            throw new ArgumentException(
                $"Line attributes have {lineAttributes.GetLength(0)} rows for {n} lines; give one row per line.", nameof(lineAttributes));

        for (int i = 0; i < n; i++)
        {
            for (int a = 0; a < 3; a++)
                if (!double.IsFinite(starts[i, a]) || !double.IsFinite(ends[i, a]))
                    throw new ArgumentException($"Line {i} has a coordinate that is not finite.", nameof(starts));
            for (int a = 0; a < attributes; a++)
                if (!double.IsFinite(lineAttributes![i, a]))
                    throw new ArgumentException($"Line {i}'s attribute {a} is not finite.", nameof(lineAttributes));
        }

        var notes = new List<string>();
        var a0 = Enumerable.Range(0, n).Select(i => Vec.Row(starts, i)).ToArray();
        var a1 = Enumerable.Range(0, n).Select(i => Vec.Row(ends, i)).ToArray();

        var orientation = LineOrientations.Classify(a0, a1, options.Tolerance, options.Banding, notes);
        var structure = StructureGraph.Build(starts, ends, Array.Empty<double[,]>(), supports, options.Join);

        int duplicates = structure.DuplicateOf.Take(n).Count(d => d >= 0);
        if (duplicates > 0)
            notes.Add($"{duplicates} line(s) are drawn on top of another, so their joints count those arms twice — "
                + "worth removing before trusting the types.");

        if (structure.StrandedSupports.Length > 0)
            notes.Add($"{structure.StrandedSupports.Length} support(s) are at no joint and were ignored.");

        // Arms: every line leaves its start forwards and its end backwards, and
        // leaves every joint bearing along its middle both ways.
        var arms = new List<(Vec Direction, int Line)>[structure.Joints.Length];
        for (int j = 0; j < arms.Length; j++)
            arms[j] = new List<(Vec, int)>();

        for (int e = 0; e < n; e++)
        {
            if (orientation[e] == LineOrientation.Degenerate || structure.Degenerate[e])
                continue;

            var forward = (a1[e] - a0[e]) / (a1[e] - a0[e]).Length;
            var path = structure.Outline[e];

            for (int p = 0; p < path.Length; p++)
            {
                if (p > 0)
                    arms[path[p]].Add((-1.0 * forward, e));
                if (p < path.Length - 1)
                    arms[path[p]].Add((forward, e));
            }
        }

        var kept = Enumerable.Range(0, arms.Length).Where(j => arms[j].Count > 0).ToArray();
        var names = BaseFeatures
            .Concat(Enumerable.Range(0, attributes).SelectMany(a =>
            {
                string name = attributeNames is not null && a < attributeNames.Count && !string.IsNullOrWhiteSpace(attributeNames[a])
                    ? attributeNames[a]
                    : $"Attribute {a}";
                return new[] { $"{name} Max", $"{name} Min" };
            }))
            .ToArray();

        var joints = new double[kept.Length, 3];
        var signature = new double[kept.Length, names.Length];
        var armLines = new int[kept.Length][];
        var handedness = new int[kept.Length];
        var supported = new bool[kept.Length];

        for (int k = 0; k < kept.Length; k++)
        {
            int j = kept[k];
            var here = arms[j];
            var position = structure.Joints[j];
            (joints[k, 0], joints[k, 1], joints[k, 2]) = (position.X, position.Y, position.Z);

            armLines[k] = here.Select(arm => arm.Line).ToArray();
            supported[k] = structure.Supported[j];
            handedness[k] = Hand(here, orientation);

            var row = Row(here, orientation, supported[k]);
            for (int c = 0; c < row.Length; c++)
                signature[k, c] = row[c];

            for (int a = 0; a < attributes; a++)
            {
                var values = here.Select(arm => lineAttributes![arm.Line, a]).ToArray();
                signature[k, BaseFeatures.Length + 2 * a] = values.Max();
                signature[k, BaseFeatures.Length + 2 * a + 1] = values.Min();
            }
        }

        return new JointSignatureResult(joints, signature, names, armLines, handedness, supported, orientation, notes);
    }

    private static double[] Row(List<(Vec Direction, int Line)> arms, LineOrientation[] orientation, bool supported)
    {
        var row = new double[BaseFeatures.Length];
        int count = arms.Count;
        row[0] = count;

        foreach (var (direction, line) in arms)
        {
            switch (orientation[line])
            {
                case LineOrientation.Plumb:
                    row[direction.Z > 0.0 ? 1 : 2]++;
                    break;
                case LineOrientation.Level:
                    row[3]++;
                    break;
                case LineOrientation.Pitched:
                    row[direction.Z > 0.0 ? 4 : 5]++;
                    break;
            }
        }

        row[6] = supported ? 1.0 : 0.0;

        // The three largest angles between arms say how many members pass straight
        // through — each such pair is at 180 — and the smallest how tight the
        // tightest pair is, the brace against the beam. Absent pairs read zero.
        // Angle from the sine and cosine together rather than an arc-cosine alone: the
        // arc-cosine turns last-bit rounding near 180 degrees into a millionth of a
        // degree, which a turned copy of the same joint then fails to match.
        var angles = new List<double>();
        for (int p = 0; p < count; p++)
            for (int q = p + 1; q < count; q++)
            {
                var (u, v) = (arms[p].Direction, arms[q].Direction);
                angles.Add(Math.Atan2(u.Cross(v).Length, u.Dot(v)) * 180.0 / Math.PI);
            }

        angles.Sort((x, y) => y.CompareTo(x));
        for (int t = 0; t < 3; t++)
            row[7 + t] = t < angles.Count ? angles[t] : 0.0;
        row[10] = angles.Count > 0 ? angles[^1] : 0.0;

        // How the arms spread: the eigenvalues of the mean outer product, summing to
        // one — along one line (1, 0, 0), across a plane (½, ½, 0), into space.
        var tensor = new double[3, 3];
        foreach (var (direction, _) in arms)
        {
            var v = new[] { direction.X, direction.Y, direction.Z };
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    tensor[r, c] += v[r] * v[c] / count;
        }

        var values = Eigenvalues3(tensor);
        for (int t = 0; t < 3; t++)
            row[11 + t] = Math.Max(values[t], 0.0);

        // Mean vertical component: rising from a base towards +1, dropping into a
        // roof top towards −1.
        row[14] = arms.Average(arm => arm.Direction.Z);

        // How lopsided the level arms are in plan: a cross of four reads 0, a tee a
        // third, a corner of two at right angles 0.71 — edge, corner and interior told
        // apart without naming them.
        var level = arms.Where(arm => orientation[arm.Line] == LineOrientation.Level).ToList();
        if (level.Count > 0)
        {
            double x = 0.0, y = 0.0;
            foreach (var (direction, _) in level)
            {
                double plan = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                if (plan <= 0.0)
                    continue;
                x += direction.X / plan;
                y += direction.Y / plan;
            }

            row[15] = Math.Sqrt(x * x + y * y) / level.Count;
        }

        return row;
    }

    /// <summary>
    /// Eigenvalues of a symmetric 3 x 3 matrix, largest first, in closed form
    /// (Smith, 1961): the characteristic cubic solved trigonometrically. Exact, and
    /// no iteration for a matrix this small.
    /// </summary>
    private static double[] Eigenvalues3(double[,] a)
    {
        double off = a[0, 1] * a[0, 1] + a[0, 2] * a[0, 2] + a[1, 2] * a[1, 2];
        if (off <= 1e-30)
            return new[] { a[0, 0], a[1, 1], a[2, 2] }.OrderByDescending(v => v).ToArray();

        double q = (a[0, 0] + a[1, 1] + a[2, 2]) / 3.0;
        double p2 = (a[0, 0] - q) * (a[0, 0] - q) + (a[1, 1] - q) * (a[1, 1] - q) + (a[2, 2] - q) * (a[2, 2] - q) + 2.0 * off;
        double p = Math.Sqrt(p2 / 6.0);

        var b = new double[3, 3];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                b[r, c] = (a[r, c] - (r == c ? q : 0.0)) / p;

        double determinant =
            b[0, 0] * (b[1, 1] * b[2, 2] - b[1, 2] * b[2, 1])
            - b[0, 1] * (b[1, 0] * b[2, 2] - b[1, 2] * b[2, 0])
            + b[0, 2] * (b[1, 0] * b[2, 1] - b[1, 1] * b[2, 0]);

        double phi = Math.Acos(Math.Clamp(determinant / 2.0, -1.0, 1.0)) / 3.0;
        double largest = q + 2.0 * p * Math.Cos(phi);
        double smallest = q + 2.0 * p * Math.Cos(phi + 2.0 * Math.PI / 3.0);
        return new[] { largest, 3.0 * q - largest - smallest, smallest };
    }

    /// <summary>
    /// +1, −1 or 0: which mirror image of its arrangement a joint is.
    /// <para>
    /// Summed over every pair of arms with a plan direction: the difference of their
    /// kind keys times the sine of the plan angle between them. Swapping the two arms
    /// of a pair swaps both signs, so the sum ignores order; turning the joint leaves
    /// every angle between arms alone; mirroring it negates every sine, so the sum
    /// changes sign — and a joint that is its own mirror image sums to zero. Only arms
    /// of different kinds weigh in, so a beam-only arrangement reads 0 even when it has
    /// a hand; a brace on one side of a beam or the other is the case this is for.
    /// </para>
    /// </summary>
    private static int Hand(List<(Vec Direction, int Line)> arms, LineOrientation[] orientation)
    {
        var planar = new List<(double X, double Y, double Key)>();
        foreach (var (direction, line) in arms)
        {
            double key = orientation[line] switch
            {
                LineOrientation.Level => 1.0,
                LineOrientation.Pitched => direction.Z > 0.0 ? 2.0 : 3.0,
                _ => 0.0,
            };

            double plan = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (key == 0.0 || plan < 1e-9)
                continue;

            planar.Add((direction.X / plan, direction.Y / plan, key));
        }

        double sum = 0.0;
        for (int p = 0; p < planar.Count; p++)
            for (int q = p + 1; q < planar.Count; q++)
                sum += 2.0 * (planar[p].Key - planar[q].Key) * (planar[p].X * planar[q].Y - planar[p].Y * planar[q].X);

        return Math.Abs(sum) <= 1e-9 * Math.Max(1, planar.Count * planar.Count) ? 0 : Math.Sign(sum);
    }
}
