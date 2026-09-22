using Xunit;

namespace OtterLogic.Fabrication.Tests;

public class ConnectionTypologyTests
{
    /// <summary>Lines, supports and per-line attributes as the arrays a component hands over.</summary>
    private sealed class Model
    {
        public readonly List<double[]> Starts = new();
        public readonly List<double[]> Ends = new();
        public readonly List<double[]> Supports = new();
        public readonly List<double> Depths = new();

        public int Line(double x1, double y1, double z1, double x2, double y2, double z2, double depth = 400)
        {
            Starts.Add(new[] { x1, y1, z1 });
            Ends.Add(new[] { x2, y2, z2 });
            Depths.Add(depth);
            return Starts.Count - 1;
        }

        public void Support(double x, double y, double z) => Supports.Add(new[] { x, y, z });

        public static double[,] Rows(IReadOnlyList<double[]> points)
        {
            var rows = new double[points.Count, 3];
            for (int i = 0; i < points.Count; i++)
                for (int a = 0; a < 3; a++)
                    rows[i, a] = points[i][a];
            return rows;
        }

        public JointSignatureResult Signatures()
            => JointSignature.Describe(Rows(Starts), Rows(Ends), Supports.Count > 0 ? Rows(Supports) : null);

        public ConnectionTypologyResult Typology(bool withDepth = false)
        {
            double[,]? attributes = null;
            if (withDepth)
            {
                attributes = new double[Depths.Count, 1];
                for (int i = 0; i < Depths.Count; i++)
                    attributes[i, 0] = Depths[i];
            }

            return ConnectionTypology.Classify(Rows(Starts), Rows(Ends), Supports.Count > 0 ? Rows(Supports) : null,
                attributes, withDepth ? new[] { "Depth" } : null);
        }

        /// <summary>
        /// A regular frame: columns split at every floor, beams both ways, supported at
        /// the base. Every joint type is known from the grid alone.
        /// </summary>
        public static Model Frame(int nx, int ny, int storeys, Func<double, double, (double X, double Y)>? place = null)
        {
            place ??= (x, y) => (x, y);
            var model = new Model();
            const double bay = 6000, storey = 4000;

            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= ny; j++)
                {
                    var p = place(i * bay, j * bay);
                    model.Support(p.X, p.Y, 0);
                    for (int s = 0; s < storeys; s++)
                        model.Line(p.X, p.Y, s * storey, p.X, p.Y, (s + 1) * storey);
                }

            for (int s = 1; s <= storeys; s++)
            {
                double z = s * storey;
                for (int j = 0; j <= ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        var a = place(i * bay, j * bay);
                        var b = place((i + 1) * bay, j * bay);
                        model.Line(a.X, a.Y, z, b.X, b.Y, z);
                    }

                for (int i = 0; i <= nx; i++)
                    for (int j = 0; j < ny; j++)
                    {
                        var a = place(i * bay, j * bay);
                        var b = place(i * bay, (j + 1) * bay);
                        model.Line(a.X, a.Y, z, b.X, b.Y, z);
                    }
            }

            return model;
        }
    }

    private static double[] RowAt(JointSignatureResult result, double x, double y, double z)
    {
        for (int j = 0; j < result.JointCount; j++)
            if (Math.Abs(result.Joints[j, 0] - x) < 1e-6 && Math.Abs(result.Joints[j, 1] - y) < 1e-6 && Math.Abs(result.Joints[j, 2] - z) < 1e-6)
                return Enumerable.Range(0, result.FeatureNames.Length).Select(c => result.Signature[j, c]).ToArray();

        throw new InvalidOperationException($"No joint at ({x}, {y}, {z}).");
    }

    private static void SameRow(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int c = 0; c < expected.Length; c++)
            Assert.True(Math.Abs(expected[c] - actual[c]) < 1e-6, $"column {c}: {expected[c]} against {actual[c]}");
    }

    /// <summary>A joint turned 37 degrees in plan and moved reads exactly as before.</summary>
    [Fact]
    public void TurningAndMovingAJointChangesNothing()
    {
        double c = Math.Cos(37 * Math.PI / 180), s = Math.Sin(37 * Math.PI / 180);
        var plain = Model.Frame(2, 2, 1).Signatures();
        var turned = Model.Frame(2, 2, 1, (x, y) => (c * x - s * y + 5000, s * x + c * y - 2000)).Signatures();

        var (tx, ty) = (c * 6000 - s * 6000 + 5000, s * 6000 + c * 6000 - 2000);
        SameRow(RowAt(plain, 6000, 6000, 4000), RowAt(turned, tx, ty, 4000));
    }

    /// <summary>Drawing lines in another order, or backwards, changes nothing.</summary>
    [Fact]
    public void LineOrderAndDirectionChangeNothing()
    {
        var model = Model.Frame(2, 1, 1);
        var reversed = new Model();
        for (int i = model.Starts.Count - 1; i >= 0; i--)
            reversed.Line(model.Ends[i][0], model.Ends[i][1], model.Ends[i][2], model.Starts[i][0], model.Starts[i][1], model.Starts[i][2]);
        foreach (var support in model.Supports)
            reversed.Support(support[0], support[1], support[2]);

        SameRow(RowAt(model.Signatures(), 6000, 0, 4000), RowAt(reversed.Signatures(), 6000, 0, 4000));
    }

    /// <summary>
    /// A column drawn as one line past a floor, with the beams bearing on its middle,
    /// reads the same as the column split at the floor: a member passing through is
    /// two arms 180 degrees apart however it was drawn.
    /// </summary>
    [Fact]
    public void SplittingAMemberThatPassesThroughChangesNothing()
    {
        var split = new Model();
        split.Line(0, 0, 0, 0, 0, 4000);
        split.Line(0, 0, 4000, 0, 0, 8000);
        split.Line(0, 0, 4000, 6000, 0, 4000);
        split.Line(0, 0, 4000, -6000, 0, 4000);

        var whole = new Model();
        whole.Line(0, 0, 0, 0, 0, 8000);
        whole.Line(0, 0, 4000, 6000, 0, 4000);
        whole.Line(0, 0, 4000, -6000, 0, 4000);

        SameRow(RowAt(split.Signatures(), 0, 0, 4000), RowAt(whole.Signatures(), 0, 0, 4000));
    }

    /// <summary>
    /// A brace landing on one side of a beam-column joint and its mirror image share
    /// a signature, and take opposite hands.
    /// </summary>
    [Fact]
    public void MirrorImagesShareASignatureAndTakeOppositeHands()
    {
        Model Joint(double side)
        {
            var model = new Model();
            model.Line(0, 0, 0, 0, 0, 4000);
            model.Line(0, 0, 4000, 6000, 0, 4000);
            model.Line(0, 0, 4000, -6000, 0, 4000);
            model.Line(0, 0, 4000, 0, 6000, 4000);
            model.Line(0, 0, 4000, 4000 * side, 3000, 0);
            return model;
        }

        var left = Joint(-1).Signatures();
        var right = Joint(1).Signatures();

        SameRow(RowAt(left, 0, 0, 4000), RowAt(right, 0, 0, 4000));

        int Hand(JointSignatureResult r) => r.Handedness[Enumerable.Range(0, r.JointCount).Single(j => r.Joints[j, 2] == 4000 && r.Joints[j, 0] == 0 && r.Joints[j, 1] == 0)];
        Assert.Equal(-Hand(left), Hand(right));
        Assert.NotEqual(0, Hand(right));
    }

    [Fact]
    public void GravityTellsABaseFromATop()
    {
        var model = new Model();
        model.Line(0, 0, 0, 0, 0, 4000);
        var result = model.Signatures();

        Assert.NotEqual(RowAt(result, 0, 0, 0)[1], RowAt(result, 0, 0, 4000)[1]);
    }

    /// <summary>
    /// A 3 x 3 grid of columns, two storeys. By construction: 9 bases, and on each
    /// floor 4 corners, 4 edges and 1 interior joint. The interior joint happens once
    /// per floor and differs between floor and roof, so each is a one-off — made once
    /// is not a type.
    /// </summary>
    [Fact]
    public void FindsTheTypesOfARegularFrameAndTheOneOffs()
    {
        var result = Model.Frame(2, 2, 2).Typology();

        Assert.Equal(new[] { 9, 4, 4, 4, 4 }, result.Types.Select(t => t.Count));
        Assert.Equal(2, result.OneOffs.Length);
        Assert.All(result.OneOffs, j => Assert.Equal(6000.0, result.Signatures.Joints[j, 0], 6));
        Assert.All(result.Types, t => Assert.Equal(0.0, t.Spread));
    }

    [Fact]
    public void EveryExemplarIsAJointOfItsOwnType()
    {
        var result = Model.Frame(3, 2, 2).Typology();
        Assert.All(result.Types, t => Assert.Contains(t.Exemplar, t.Joints));
    }

    /// <summary>
    /// A corner beam skewed by a fraction of a degree stays in the corner type — and
    /// the type reports a spread, marking it as a near-identical variant.
    /// </summary>
    [Fact]
    public void ANearIdenticalJointStaysInItsTypeAndShowsAsAVariant()
    {
        var model = Model.Frame(2, 2, 1);
        int beam = model.Starts.FindIndex(p => p[0] == 0 && p[1] == 0 && p[2] == 4000);
        model.Ends[beam] = new[] { model.Ends[beam][0], model.Ends[beam][1] + 20, 4000 };

        var result = model.Typology();
        var corners = result.Types.Single(t => t.Joints.Any(j => result.Signatures.Joints[j, 0] == 0 && result.Signatures.Joints[j, 1] == 0 && result.Signatures.Joints[j, 2] == 4000));

        Assert.True(corners.Spread > 0.0);
    }

    /// <summary>The same geometry in deeper beams is a different connection once depth is given.</summary>
    [Fact]
    public void LineAttributesSplitOtherwiseIdenticalJoints()
    {
        var model = Model.Frame(3, 1, 1);
        for (int i = 0; i < model.Starts.Count; i++)
            if (model.Starts[i][2] == 4000 && model.Starts[i][0] >= 12000 && model.Ends[i][0] >= 12000)
                model.Depths[i] = 900;

        int plain = model.Typology().Types.Count;
        int deep = model.Typology(withDepth: true).Types.Count;

        Assert.True(deep > plain, $"{deep} types with depth against {plain} without");
    }

    [Fact]
    public void AMirroredPairIsOneTypeWithOneOfEachHand()
    {
        var model = Model.Frame(3, 1, 1);
        model.Line(0, 0, 0, 6000, 0, 4000);
        model.Line(18000, 0, 0, 12000, 0, 4000);

        var result = model.Typology();

        Assert.Contains(result.Types, t => t.LeftHanded == 1 && t.RightHanded == 1);
    }

    /// <summary>A lone beam's two free ends are the same connection: one type of two.</summary>
    [Fact]
    public void ALoneBeamsFreeEndsAreOneType()
    {
        var model = new Model();
        model.Line(0, 0, 0, 1000, 0, 0);

        var result = model.Typology();

        Assert.Single(result.Types);
        Assert.Empty(result.OneOffs);
    }

    /// <summary>A lone column's base and top differ by gravity alone: two one-offs.</summary>
    [Fact]
    public void ALoneColumnsBaseAndTopAreOneOffs()
    {
        var model = new Model();
        model.Line(0, 0, 0, 0, 0, 4000);

        var result = model.Typology();

        Assert.Empty(result.Types);
        Assert.Equal(2, result.OneOffs.Length);
    }

    [Fact]
    public void FewerJointsThanATypeNeedsAreAllOneOffs()
    {
        var model = new Model();
        model.Line(0, 0, 0, 1000, 0, 0);

        var result = ConnectionTypology.Classify(Model.Rows(model.Starts), Model.Rows(model.Ends),
            options: new ConnectionTypologyOptions { MinimumTypeSize = 3 });

        Assert.Empty(result.Types);
        Assert.Equal(2, result.OneOffs.Length);
    }

    /// <summary>
    /// Every joint in exactly one group: the types, largest first, then the one-offs as
    /// a last group, each named with its members in words.
    /// </summary>
    [Fact]
    public void EveryJointIsInExactlyOneGroupWithOneOffsLast()
    {
        var result = Model.Frame(2, 2, 2).Typology();

        var groups = result.Groups();
        var names = result.GroupNames();

        Assert.Equal(groups.Length, names.Length);
        Assert.Equal(result.Signatures.JointCount, groups.Sum(g => g.Length));
        Assert.Equal(result.Signatures.JointCount, groups.SelectMany(g => g).Distinct().Count());
        Assert.Equal(result.OneOffs, groups[^1]);
        Assert.StartsWith("Type 0 — 9 joints: 1 arm — 1 plumb up, supported", names[0]);
        Assert.StartsWith("One-offs — 2 joints", names[^1]);
    }

    [Fact]
    public void TheSummaryDescribesArmsInWords()
    {
        var summary = Model.Frame(2, 2, 2).Typology().Summary;

        Assert.Contains("5 connection type(s) and 2 one-off(s) among 27 joints.", summary);
        Assert.Contains("1 plumb up, supported", summary);
        Assert.Contains("Distinguished by:", summary);
    }
}
