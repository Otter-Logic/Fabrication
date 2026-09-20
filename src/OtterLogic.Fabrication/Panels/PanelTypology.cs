using OtterLogic.MachineLearning.Shapes;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Fabrication;

/// <summary>
/// Breaks surfaces into panels a machine can make, and finds the types that
/// repeat.
/// <para>
/// One rectangle is given — the largest panel the machine can cast — and the
/// surface is laid out in rows and columns of it. Nothing is fitted to the edge:
/// where the edge of the surface runs through a panel the panel is simply
/// <b>cut</b> there, so a curve is followed as closely as the surface is drawn, a
/// slope comes out as a triangle without being asked for, and the panels together
/// cover the whole surface but for the joints.
/// </para>
/// <para>
/// Each surface is laid out in <b>its own frame</b>, the smallest rectangle its
/// outline fits in, so a panel's length runs along the surface's long edge
/// whichever way it was drawn or turned in space.
/// </para>
/// <para>
/// Where the cuts go is the only choice in the layout, and it is made by <b>search,
/// not by a rule</b>: each row is a little graph whose nodes are the places a cut
/// could fall and whose edges are the panels between them, and the cheapest path
/// across it is the row. What a panel costs is what the user asked for —
/// <see cref="PanelTypologyOptions.Standardisation"/> prices being off the
/// machine's full size against being small — so the same search gives a wall of
/// full panels with a sliver at the end, or a few equal bespoke ones, or anything
/// between, without a threshold anywhere.
/// </para>
/// <para>
/// Which panels are the <b>same type</b> is then a grouping question, and it is
/// answered without anybody deciding in advance what makes two panels alike.
/// <see cref="ShapeSignature"/> reads every panel's outline and learns what this
/// population of panels actually varies by; the panels are then clustered on that.
/// A list of measurements picked by hand — length, width, area, how far off-centre
/// — cannot tell a panel notched at its corners from one notched in its middle,
/// because they agree on every one of them. A learned signature can, and catches a
/// curve or a raked corner the same way, without either having been anticipated.
/// </para>
/// <para>
/// The cut is complete linkage at the type tolerance, which carries a promise a
/// fabricator can use: <b>no two panels of a type are more than the tolerance
/// apart, averaged around their outlines</b>. Square-cut panels, triangles and
/// other cut shapes are grouped apart, and a panel is never grouped with its own
/// mirror image, because those are two moulds rather than one.
/// </para>
/// </summary>
public static class PanelTypology
{
    /// <summary>
    /// How many points each panel's outline is read at when its signature is taken.
    /// <para>
    /// A panel has three or four corners most of the time and a dozen or so where it
    /// has been cut to a curve, so this carries every corner of the common cases and
    /// the shape of the uncommon ones. Reading finer costs time quadratically and
    /// tells a cutting list nothing more.
    /// </para>
    /// </summary>
    private const int ReadAt = 32;

    /// <summary>
    /// Panelises surfaces.
    /// </summary>
    /// <param name="surfaces">Each surface's boundary, k x 3 corners in order — a curved edge as the points along it. Planar; a warped one is read as its best-fit plane and said so.</param>
    /// <param name="options">The machine's rectangle, the gap, how hard to hold to it, and what counts as one type.</param>
    public static PanelTypologyResult Layout(IReadOnlyList<double[,]> surfaces, PanelTypologyOptions options)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (surfaces.Count == 0)
            throw new ArgumentException("Need at least one surface to panelise.", nameof(surfaces));

        var notes = new List<string>();
        var panels = new List<Panel>();
        var outlines = new List<double[,]>();
        var surfaceArea = new double[surfaces.Count];
        var panelArea = new double[surfaces.Count];

        for (int s = 0; s < surfaces.Count; s++)
        {
            var boundary = surfaces[s];
            if (boundary is null || boundary.GetLength(1) != 3)
                throw new ArgumentException($"Surface {s} must be a k x 3 boundary of corners.", nameof(surfaces));

            for (int i = 0; i < boundary.GetLength(0); i++)
                for (int a = 0; a < 3; a++)
                    if (!double.IsFinite(boundary[i, a]))
                        throw new ArgumentException($"Surface {s} has a corner that is not finite.", nameof(surfaces));

            var outline = Outline.Read(boundary, options.Tolerance);
            if (outline is null)
            {
                notes.Add($"Surface {s} has no area at this tolerance and was left out.");
                continue;
            }

            if (outline.Flatness > options.Tolerance)
                notes.Add($"Surface {s} is not flat — its corners sit up to {outline.Flatness:G3} off one plane — "
                    + "so it was panelised on the plane that fits them best.");

            surfaceArea[s] = outline.Area;
            panelArea[s] = Panelise(s, outline, options, panels, outlines);
        }

        var (types, typeOf) = Types(panels, outlines, options);
        var marked = panels.Select((p, i) => p with { Type = typeOf[i] }).ToList();

        if (marked.Count == 0)
            notes.Add("No panel fits in any of these surfaces: they have no area at this tolerance.");

        Remark(marked, types, options, notes);
        return new PanelTypologyResult(marked, types, surfaces.Count, surfaceArea, panelArea, notes);
    }

    /// <summary>
    /// Lays one surface out: rows across it, then along each row the stretches of it
    /// that are there, then the panels in each stretch, cut to the outline. Returns
    /// the area covered.
    /// </summary>
    private static double Panelise(
        int surface, Outline outline, PanelTypologyOptions options, List<Panel> panels, List<double[,]> outlines)
    {
        double covered = 0.0;
        var rows = Cuts(outline.Low.Y, outline.High.Y, options.Width, options);

        for (int r = 0; r + 1 < rows.Count; r++)
        {
            var (bottom, top) = Between(rows, r, options.Gap);
            if (top - bottom <= options.Tolerance)
                continue;

            foreach (var (from, to) in Spans(outline, bottom, top, options))
            {
                var columns = Cuts(from, to, options.Length, options);
                for (int c = 0; c + 1 < columns.Count; c++)
                {
                    var (left, right) = Between(columns, c, options.Gap);
                    if (right - left <= options.Tolerance)
                        continue;

                    foreach (var piece in Pieces(outline, left, right, bottom, top, options))
                    {
                        panels.Add(Make(surface, outline, piece, options));

                        // Kept in the surface's own frame, where length already runs
                        // along its long edge, so panels off different surfaces are
                        // comparable without being moved.
                        outlines.Add(Flatten(piece));
                        covered += panels[^1].Area;
                    }
                }
            }
        }

        return covered;
    }

    /// <summary>
    /// What one panel between two cuts spans: the joint is taken half from each side
    /// of a cut, and a panel against the end of the run sits flush with it.
    /// </summary>
    private static (double Low, double High) Between(IReadOnlyList<double> cuts, int k, double gap)
        => (cuts[k] + (k == 0 ? 0.0 : 0.5 * gap), cuts[k + 1] - (k + 2 == cuts.Count ? 0.0 : 0.5 * gap));

    /// <summary>
    /// Where to cut a run into panels no longer than <paramref name="full"/> — the
    /// cheapest path across the places a cut could fall.
    /// <para>
    /// The nodes are the ends of the run, every place a chain of full panels from
    /// either end would land, and every place that sharing what is left between a few
    /// equal panels would land. An edge from one node to another is a panel, allowed
    /// only if it is no longer than the machine makes, and priced by
    /// <see cref="Price"/>. Shortest paths on a line need no queue, so this is a
    /// single sweep left to right.
    /// </para>
    /// </summary>
    internal static List<double> Cuts(double from, double to, double full, PanelTypologyOptions options)
    {
        double gap = options.Gap, tolerance = options.Tolerance;
        if (to - from <= full + tolerance)
            return new List<double> { from, to };

        // Where a run of full panels from the start would put its cuts, and from the end.
        var starts = new List<double> { from };
        for (double cut = from + full + 0.5 * gap; cut < to - tolerance; cut += full + gap)
            starts.Add(cut);

        var candidates = new SortedSet<double>(starts) { to };
        for (double cut = to - full - 0.5 * gap; cut > from + tolerance; cut -= full + gap)
            candidates.Add(cut);

        // And the balanced ways of finishing from each of those: what is left shared
        // between as few panels as will take it, and one or two more than that.
        foreach (double start in Sample(starts, 64))
        {
            double edge = start + (start > from ? 0.5 * gap : 0.0);
            double rest = to - edge;
            if (rest <= tolerance)
                continue;

            int fewest = Math.Max(1, (int)Math.Ceiling((rest + gap) / (full + gap) - 1e-9));
            for (int n = fewest; n <= fewest + 2; n++)
            {
                double each = (rest - (n - 1) * gap) / n;
                if (each <= tolerance)
                    break;

                double at = edge;
                for (int k = 1; k < n; k++)
                {
                    at += each;
                    candidates.Add(at + 0.5 * gap);
                    at += gap;
                }
            }
        }

        var nodes = new List<double> { from };
        foreach (double candidate in candidates)
            if (candidate > from + tolerance && candidate < to - tolerance && candidate - nodes[^1] > tolerance)
                nodes.Add(candidate);

        nodes.Add(to);

        int last = nodes.Count - 1;
        var cost = new double[nodes.Count];
        var came = new int[nodes.Count];
        Array.Fill(cost, double.PositiveInfinity);
        Array.Fill(came, -1);
        cost[0] = 0.0;

        for (int i = 0; i < last; i++)
        {
            if (double.IsPositiveInfinity(cost[i]))
                continue;

            for (int j = i + 1; j <= last; j++)
            {
                double length = nodes[j] - nodes[i] - (i == 0 ? 0.0 : 0.5 * gap) - (j == last ? 0.0 : 0.5 * gap);
                if (length > full + tolerance)
                    break;
                if (length <= tolerance)
                    continue;

                // Ties go to the later cut, so a run that could be cut either way comes
                // out with its full panels first and its leftover at the end, the way a
                // setting-out drawing reads.
                double reached = cost[i] + Price(length, full, options);
                if (reached <= cost[j] + 1e-9)
                {
                    cost[j] = reached;
                    came[j] = i;
                }
            }
        }

        // Sharing the whole run out evenly is always among the paths, so the far end is
        // always reached.
        var cuts = new List<double>();
        for (int at = last; at >= 0; at = came[at])
        {
            cuts.Add(nodes[at]);
            if (at == 0)
                break;
        }

        cuts.Reverse();
        return cuts;
    }

    /// <summary>
    /// What one panel costs: one, because a panel is a panel, plus what the user asked
    /// to avoid.
    /// <para>
    /// <see cref="PanelTypologyOptions.Standardisation"/> is the whole of the
    /// judgement. Its share of the price is paid by any panel that is not the
    /// machine's full size, so at 1 the search buys as many full panels as it can and
    /// lets the last one come out however it comes out. The rest of the price is
    /// <c>full / length - 1</c>, which is nothing for a panel near full size and
    /// unbounded for a sliver, so at 0 the search shares the run out evenly instead.
    /// Between them, a leftover is left to itself only while it is longer than about
    /// <c>(1 - s) / 2s</c> of a full panel: the lever is where the user draws that
    /// line, not a number in the code.
    /// </para>
    /// </summary>
    private static double Price(double length, double full, PanelTypologyOptions options)
    {
        bool standard = Math.Abs(length - full) <= Math.Max(options.TypeTolerance, options.Tolerance);
        return 1.0
            + options.Standardisation * (standard ? 0.0 : 1.0)
            + (1.0 - options.Standardisation) * (full / length - 1.0);
    }

    /// <summary>At most <paramref name="most"/> of a list, evenly spread, both ends kept.</summary>
    private static IEnumerable<double> Sample(List<double> values, int most)
    {
        if (values.Count <= most)
            return values;

        int step = (values.Count + most - 1) / most;
        return values.Where((_, i) => i % step == 0 || i == values.Count - 1);
    }

    /// <summary>
    /// The stretches of one row that have any surface in them, left to right. A row of
    /// an L or a doughnut has more than one, and they are panelised apart.
    /// </summary>
    private static List<(double From, double To)> Spans(
        Outline outline, double bottom, double top, PanelTypologyOptions options)
    {
        var breaks = new SortedSet<double>(outline.Corners.Select(c => c.X));
        for (int e = 0; e < outline.Corners.Length; e++)
        {
            if (outline.Meets(e, bottom) is double low)
                breaks.Add(low);
            if (outline.Meets(e, top) is double high)
                breaks.Add(high);
        }

        var xs = breaks.ToArray();
        var spans = new List<(double From, double To)>();

        for (int i = 0; i + 1 < xs.Length; i++)
        {
            if (xs[i + 1] - xs[i] <= options.Tolerance)
                continue;

            double middle = 0.5 * (xs[i] + xs[i + 1]);
            bool any = outline.Inside(middle)
                .Any(range => Math.Min(range.High, top) - Math.Max(range.Low, bottom) > options.Tolerance);
            if (!any)
                continue;

            if (spans.Count > 0 && Math.Abs(spans[^1].To - xs[i]) <= options.Tolerance)
                spans[^1] = (spans[^1].From, xs[i + 1]);
            else
                spans.Add((xs[i], xs[i + 1]));
        }

        return spans;
    }

    /// <summary>
    /// What one panel's rectangle actually holds: the surface inside it, cut to the
    /// outline. The whole rectangle where the panel is well inside the surface, a
    /// piece of it at the edge, and more than one piece where the outline comes back
    /// on itself.
    /// <para>
    /// The rectangle is sliced at every x where the outline turns a corner or crosses
    /// the top or bottom of the panel, so within a slice the surface is bounded by one
    /// straight edge below and one above and is exactly a trapezoid. Slices that carry
    /// on into one another are chained, so a piece comes out as one polygon following
    /// the edge, however many slices it took.
    /// </para>
    /// </summary>
    private static List<Flat[]> Pieces(
        Outline outline, double left, double right, double bottom, double top, PanelTypologyOptions options)
    {
        double tolerance = options.Tolerance;
        var breaks = new SortedSet<double> { left, right };

        foreach (var corner in outline.Corners)
            if (corner.X > left + tolerance && corner.X < right - tolerance)
                breaks.Add(corner.X);

        for (int e = 0; e < outline.Corners.Length; e++)
            foreach (double? meets in new[] { outline.Meets(e, bottom), outline.Meets(e, top) })
                if (meets is double x && x > left + tolerance && x < right - tolerance)
                    breaks.Add(x);

        var xs = breaks.ToArray();
        var open = new List<List<Column>>();
        var closed = new List<List<Column>>();

        for (int i = 0; i + 1 < xs.Length; i++)
        {
            double a = xs[i], b = xs[i + 1];
            if (b - a <= tolerance)
                continue;

            var carried = new List<List<Column>>();
            var crossings = outline.Crossings(0.5 * (a + b));

            for (int k = 0; k + 1 < crossings.Count; k += 2)
            {
                int under = crossings[k].Edge, over = crossings[k + 1].Edge;
                var starts = Column.Clamp(a, outline.Height(under, a), outline.Height(over, a), bottom, top);
                var ends = Column.Clamp(b, outline.Height(under, b), outline.Height(over, b), bottom, top);

                if (starts.Height <= tolerance && ends.Height <= tolerance)
                    continue;

                // One piece carries on into the next slice when the two overlap at
                // the x they share, not only when they line up exactly. Where the
                // edge of the surface steps — a notch, a corner bitten out — the
                // piece either side of the step is still one piece of surface, and
                // insisting they line up would cut it into a panel per step and
                // invent joints nobody asked for.
                int carry = open.FindIndex(run => !carried.Contains(run) && run[^1].Joins(starts, tolerance));

                if (carry < 0)
                {
                    open.Add(new List<Column> { starts, ends });
                    carry = open.Count - 1;
                }
                else
                {
                    // At a step the piece has two heights at the one x, and both are
                    // corners of it.
                    if (!open[carry][^1].Meets(starts, tolerance))
                        open[carry].Add(starts);

                    open[carry].Add(ends);
                }

                carried.Add(open[carry]);
            }

            for (int r = open.Count - 1; r >= 0; r--)
                if (!carried.Contains(open[r]))
                {
                    closed.Add(open[r]);
                    open.RemoveAt(r);
                }
        }

        closed.AddRange(open);

        var pieces = new List<Flat[]>();
        foreach (var run in closed)
        {
            var points = new List<Flat>();
            foreach (var column in run)
                Append(points, new Flat(column.X, column.Low), tolerance);
            for (int c = run.Count - 1; c >= 0; c--)
                Append(points, new Flat(run[c].X, run[c].High), tolerance);

            var piece = Simplify(points, tolerance);
            double area = Shoelace(piece);
            if (piece.Length >= 3 && Math.Abs(area) > tolerance * tolerance)
                // Enumerable.Reverse, not the array's own: that one reverses in place and
                // returns nothing.
                pieces.Add(area < 0.0 ? Enumerable.Reverse(piece).ToArray() : piece);
        }

        return pieces;
    }

    /// <summary>One end of a slice of a piece: how high the surface runs there, cut to the panel.</summary>
    private readonly record struct Column(double X, double Low, double High)
    {
        public double Height => High - Low;

        /// <summary>The surface between two edges at one x, cut to the panel's top and bottom.</summary>
        public static Column Clamp(double x, double under, double over, double bottom, double top)
        {
            double low = Math.Max(under, bottom), high = Math.Min(over, top);

            // Where the surface has already run out, the piece comes to a point.
            return high < low ? new Column(x, 0.5 * (low + high), 0.5 * (low + high)) : new Column(x, low, high);
        }

        /// <summary>The same place, the same height: one slice runs straight on into the next.</summary>
        public bool Meets(Column other, double tolerance)
            => Math.Abs(X - other.X) <= tolerance
            && Math.Abs(Low - other.Low) <= tolerance
            && Math.Abs(High - other.High) <= tolerance;

        /// <summary>
        /// The same place, and overlapping: the surface is joined across this x even
        /// though its edge steps here, so what is either side is one piece.
        /// </summary>
        public bool Joins(Column other, double tolerance)
            => Math.Abs(X - other.X) <= tolerance
            && (Meets(other, tolerance)
                || Math.Min(High, other.High) - Math.Max(Low, other.Low) > tolerance);
    }

    private static void Append(List<Flat> points, Flat point, double tolerance)
    {
        if (points.Count == 0 || (point - points[^1]).Length > tolerance)
            points.Add(point);
    }

    /// <summary>
    /// The same outline without the corners that are not corners: a panel spanning
    /// several slices of one straight edge is a rectangle, not a polygon with a row of
    /// points along its side.
    /// </summary>
    private static Flat[] Simplify(List<Flat> points, double tolerance)
    {
        var kept = new List<Flat>(points);

        for (bool again = true; again && kept.Count > 3;)
        {
            again = false;
            for (int i = 0; i < kept.Count && kept.Count > 3; i++)
            {
                var before = kept[(i - 1 + kept.Count) % kept.Count];
                var after = kept[(i + 1) % kept.Count];
                var run = after - before;
                double length = run.Length;
                if (length <= tolerance)
                    continue;

                if (Math.Abs(run.Cross(kept[i] - before)) / length > tolerance)
                    continue;

                kept.RemoveAt(i--);
                again = true;
            }
        }

        return kept.ToArray();
    }

    /// <summary>The area inside an outline, positive when it runs counter-clockwise.</summary>
    private static double Shoelace(IReadOnlyList<Flat> points)
    {
        double twice = 0.0;
        for (int i = 0; i < points.Count; i++)
            twice += points[i].Cross(points[(i + 1) % points.Count]);
        return 0.5 * twice;
    }

    /// <summary>
    /// One panel: the stock it is cut from, the area it actually uses, what it is
    /// cut as, and its corners back in model space.
    /// <para>
    /// The shape is read off the outline rather than decided when the panel was
    /// made — three corners is a triangle, four that fill their box is square-cut,
    /// anything else has been cut through by the edge of the surface. That is a
    /// name, not a measurement: what makes two panels the same type is settled by
    /// <see cref="Types"/>.
    /// </para>
    /// </summary>
    private static Panel Make(int surface, Outline outline, Flat[] piece, PanelTypologyOptions options)
    {
        double lowX = piece.Min(p => p.X), highX = piece.Max(p => p.X);
        double lowY = piece.Min(p => p.Y), highY = piece.Max(p => p.Y);
        double length = highX - lowX, width = highY - lowY;
        double area = Math.Abs(Shoelace(piece));

        var shape = piece.Length == 3 ? PanelShape.Triangle
            : piece.Length == 4 && Math.Abs(area - length * width) <= options.Tolerance * (length + width)
                ? PanelShape.Rectangle
                : PanelShape.Cut;

        var corners = new double[piece.Length, 3];
        for (int c = 0; c < piece.Length; c++)
        {
            var point = outline.At(piece[c]);
            (corners[c, 0], corners[c, 1], corners[c, 2]) = (point.X, point.Y, point.Z);
        }

        return new Panel(surface, -1, shape, length, width, area, corners);
    }

    /// <summary>A panel's outline in the surface's frame, for the signature to read.</summary>
    private static double[,] Flatten(Flat[] piece)
    {
        var flat = new double[piece.Length, 2];
        for (int c = 0; c < piece.Length; c++)
            (flat[c, 0], flat[c, 1]) = (piece[c].X, piece[c].Y);

        return flat;
    }

    /// <summary>
    /// Groups the panels into types: complete linkage over a learned signature of
    /// their outlines, cut at the type tolerance, so no two panels of a type are
    /// further apart than that. Square-cut panels, triangles and other cut shapes
    /// are grouped apart, since one is never the other.
    /// </summary>
    private static (IReadOnlyList<PanelType> Types, int[] TypeOf) Types(
        List<Panel> panels, List<double[,]> outlines, PanelTypologyOptions options)
    {
        if (panels.Count == 0)
            return (Array.Empty<PanelType>(), Array.Empty<int>());

        // Every component is kept. The signature's distances are only the distances
        // between the outlines while nothing has been dropped, and that equality is
        // the whole reason the type tolerance can be quoted in millimetres.
        var signature = ShapeSignature.Fit(outlines, new ShapeSignatureOptions
        {
            Points = ReadAt,
            Variance = 1.0,

            // A panel has no up and no down: turn it half way round and it is the
            // panel you already had, one mould for both. Turning it to any other
            // angle is not the same thing, because its length has to run along the
            // stock, and its mirror image is a second mould rather than the same one.
            Turns = 2,
            NormaliseScale = false,
            NormaliseRotation = false,
            AllowReflection = false,
            Tolerance = options.Tolerance,
        });

        var shapes = new double[panels.Count][];
        for (int p = 0; p < panels.Count; p++)
        {
            shapes[p] = new double[signature.Count];
            for (int c = 0; c < signature.Count; c++)
                shapes[p][c] = signature.Scores[p, c];
        }

        var found = new List<(PanelShape Shape, double Length, double Width, double Area, int[] Panels, bool Standard)>();

        foreach (var shape in new[] { PanelShape.Rectangle, PanelShape.Triangle, PanelShape.Cut })
        {
            var of = Enumerable.Range(0, panels.Count).Where(p => panels[p].Shape == shape).ToArray();
            if (of.Length == 0)
                continue;

            // Panels that measure exactly alike are one point, not many: a wall of five
            // hundred identical panels is one thing to group, and clustering them one by
            // one would be a five-hundred-square distance matrix saying nothing.
            var alike = new Dictionary<double[], int>(SameSignature.Default);
            var ofEach = new List<int>();
            var which = new int[of.Length];

            for (int i = 0; i < of.Length; i++)
            {
                if (!alike.TryGetValue(shapes[of[i]], out int at))
                {
                    at = ofEach.Count;
                    alike[shapes[of[i]]] = at;
                    ofEach.Add(of[i]);
                }

                which[i] = at;
            }

            int features = shapes[of[0]].Length;
            var measured = new double[ofEach.Count, features];
            for (int i = 0; i < ofEach.Count; i++)
                for (int f = 0; f < features; f++)
                    measured[i, f] = shapes[ofEach[i]][f];

            int[] grouped;
            if (ofEach.Count == 1)
            {
                grouped = new[] { 0 };
            }
            else
            {
                var tree = HierarchicalClustering.Fit(measured, new HierarchicalClusteringOptions { Linkage = Linkage.Complete });
                grouped = tree.CutAtDistance(Math.Max(options.TypeTolerance, 0.0));
            }

            var labels = which.Select(w => grouped[w]).ToArray();

            foreach (var members in ClusterLabels.Members(labels))
            {
                if (members.Length == 0)
                    continue;

                var mine = members.Select(m => of[m]).OrderBy(p => p).ToArray();
                double length = Median(mine.Select(p => panels[p].Length));
                double width = Median(mine.Select(p => panels[p].Width));
                double area = Median(mine.Select(p => panels[p].Area));
                bool standard = shape == PanelShape.Rectangle
                    && Math.Abs(length - options.Length) <= options.TypeTolerance
                    && Math.Abs(width - options.Width) <= options.TypeTolerance;

                found.Add((shape, length, width, area, mine, standard));
            }
        }

        var ordered = PanelNaming.Order(found, t => t.Standard, t => t.Shape, t => t.Panels.Length, t => t.Area).ToList();

        var typeOf = new int[panels.Count];
        var types = new List<PanelType>(ordered.Count);
        var marks = new Dictionary<PanelShape, int>();

        for (int t = 0; t < ordered.Count; t++)
        {
            foreach (int p in ordered[t].Panels)
                typeOf[p] = t;

            marks.TryGetValue(ordered[t].Shape, out int used);
            marks[ordered[t].Shape] = used + 1;

            types.Add(new PanelType(
                PanelNaming.Mark(used, ordered[t].Shape), ordered[t].Shape,
                ordered[t].Length, ordered[t].Width, ordered[t].Area, ordered[t].Panels, ordered[t].Standard));
        }

        return (types, typeOf);
    }

    /// <summary>
    /// Says so when the layout has left panels on a smaller scale than the rest of
    /// what it had to cut — not merely the smallest ones, but a group separated from
    /// the population by more than the population itself spans. The machine's own
    /// panels are left out of the reckoning, since they are the answer, not the
    /// question. That is what standardising costs, and lowering it is how to be rid
    /// of them.
    /// </summary>
    private static void Remark(
        List<Panel> panels, IReadOnlyList<PanelType> types, PanelTypologyOptions options, List<string> notes)
    {
        if (options.Standardisation <= 0.0)
            return;

        var cut = panels.Where(p => !types[p.Type].IsStandard).ToList();
        if (cut.Count < 3)
            return;

        // Logarithms, because a scale here means a factor: an area a tenth of the rest
        // is the finding, not an area a thousand less than a large number.
        var areas = cut.Select(p => Math.Log(Math.Max(p.Area, double.Epsilon))).ToList();
        var small = ScaleSeparation.Below(areas);
        if (small.Length == 0)
            return;

        // A scale is only a scale against a spread. Where every panel left over is the
        // same size, there is nothing for the small ones to be small against, and any
        // difference at all would read as another scale.
        var rest = Enumerable.Range(0, cut.Count).Except(small).Select(i => cut[i].Area).Distinct().Take(2);
        if (rest.Count() < 2)
            return;

        double largest = small.Max(i => cut[i].Area);
        notes.Add($"{small.Length} of the {cut.Count} panel(s) that had to be cut are on a smaller scale than the "
            + $"others, none of them over {largest:G3} in area. Lower Standardisation to share them out into the "
            + "panels beside them instead.");
    }

    /// <summary>Two panels measure exactly alike, to the bit.</summary>
    private sealed class SameSignature : IEqualityComparer<double[]>
    {
        public static readonly SameSignature Default = new();

        public bool Equals(double[]? a, double[]? b) => a is not null && b is not null && a.AsSpan().SequenceEqual(b);

        public int GetHashCode(double[] signature)
        {
            var hash = new HashCode();
            foreach (double measure in signature)
                hash.Add(measure);
            return hash.ToHashCode();
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : 0.5 * (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]);
    }
}
