namespace OtterLogic.Fabrication;

/// <summary>A point in a surface's own plane.</summary>
internal readonly record struct Flat(double X, double Y)
{
    public static Flat operator +(Flat a, Flat b) => new(a.X + b.X, a.Y + b.Y);

    public static Flat operator -(Flat a, Flat b) => new(a.X - b.X, a.Y - b.Y);

    public static Flat operator *(double s, Flat a) => new(s * a.X, s * a.Y);

    public double Cross(Flat other) => X * other.Y - Y * other.X;

    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>A rectangle in a surface's own frame, low corner and high corner.</summary>
internal sealed record Patch(Flat Low, Flat High);

/// <summary>A point in model space.</summary>
internal readonly record struct Space(double X, double Y, double Z)
{
    public static Space operator +(Space a, Space b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Space operator -(Space a, Space b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Space operator *(double s, Space a) => new(s * a.X, s * a.Y, s * a.Z);

    public double Dot(Space other) => X * other.X + Y * other.Y + Z * other.Z;

    public Space Cross(Space other) => new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);

    public double Length => Math.Sqrt(Dot(this));

    public static Space Row(double[,] rows, int i) => new(rows[i, 0], rows[i, 1], rows[i, 2]);
}

/// <summary>
/// A surface read as a flat outline: the plane it lies in, its corners in that
/// plane, and the frame the panels are laid out in.
/// <para>
/// The frame is the outline's own minimum-area rectangle, not the world axes and
/// not the plane's arbitrary axes. A rectangular surface then lays out along its
/// own edges whichever way it was drawn or turned, which is what a fabricator
/// means by the length of a panel.
/// </para>
/// </summary>
internal sealed class Outline
{
    private Outline(Space origin, Space along, Space across, Space normal, Flat[] corners, double flatness)
    {
        Origin = origin;
        Along = along;
        Across = across;
        Normal = normal;
        Corners = corners;
        Flatness = flatness;
        Low = new Flat(corners.Min(c => c.X), corners.Min(c => c.Y));
        High = new Flat(corners.Max(c => c.X), corners.Max(c => c.Y));
    }

    /// <summary>Where the frame starts in model space.</summary>
    public Space Origin { get; }

    /// <summary>The frame's long axis — a panel's length runs this way.</summary>
    public Space Along { get; }

    /// <summary>The frame's short axis — a panel's width runs this way.</summary>
    public Space Across { get; }

    /// <summary>The plane's normal.</summary>
    public Space Normal { get; }

    /// <summary>The outline in the frame, in order.</summary>
    public Flat[] Corners { get; }

    /// <summary>Furthest any corner sits off the fitted plane. Zero for a flat surface.</summary>
    public double Flatness { get; }

    /// <summary>The outline's extent in the frame.</summary>
    public Flat Low { get; }

    /// <summary>The outline's extent in the frame.</summary>
    public Flat High { get; }

    public double Length => High.X - Low.X;

    public double Width => High.Y - Low.Y;

    /// <summary>Area of the outline, however it is wound.</summary>
    public double Area
    {
        get
        {
            double twice = 0.0;
            for (int i = 0; i < Corners.Length; i++)
                twice += Corners[i].Cross(Corners[(i + 1) % Corners.Length]);
            return Math.Abs(twice) / 2.0;
        }
    }

    /// <summary>A point in the frame, back in model space.</summary>
    public Space At(Flat p) => Origin + p.X * Along + p.Y * Across;

    /// <summary>
    /// Reads a boundary as an outline. The plane is fitted by Newell's method, which
    /// gives the same normal for any winding and averages out a corner slightly off
    /// plane rather than being thrown by it.
    /// </summary>
    public static Outline? Read(double[,] boundary, double tolerance)
    {
        int n = boundary.GetLength(0);
        var points = new List<Space>(n);
        for (int i = 0; i < n; i++)
        {
            var p = Space.Row(boundary, i);
            if (points.Count == 0 || (p - points[^1]).Length > tolerance)
                points.Add(p);
        }

        // A boundary given closed repeats its first corner at the end.
        if (points.Count > 1 && (points[^1] - points[0]).Length <= tolerance)
            points.RemoveAt(points.Count - 1);

        if (points.Count < 3)
            return null;

        var centroid = points.Aggregate(new Space(0, 0, 0), (a, b) => a + b);
        centroid = (1.0 / points.Count) * centroid;

        var normal = new Space(0, 0, 0);
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i] - centroid;
            var b = points[(i + 1) % points.Count] - centroid;
            normal = normal + a.Cross(b);
        }

        if (normal.Length <= 0.0)
            return null;

        normal = (1.0 / normal.Length) * normal;

        // Any two axes in the plane will do to measure with; the frame is chosen below.
        var seed = Math.Abs(normal.X) < 0.9 ? new Space(1, 0, 0) : new Space(0, 1, 0);
        var u = seed - normal.Dot(seed) * normal;
        u = (1.0 / u.Length) * u;
        var v = normal.Cross(u);

        double flatness = points.Max(p => Math.Abs((p - centroid).Dot(normal)));
        var flat = points.Select(p => new Flat((p - centroid).Dot(u), (p - centroid).Dot(v))).ToArray();

        var (cos, sin) = SmallestRectangle(flat);
        var along = cos * u + sin * v;
        var across = -sin * u + cos * v;
        var framed = points.Select(p => new Flat((p - centroid).Dot(along), (p - centroid).Dot(across))).ToArray();

        return new Outline(centroid, along, across, normal, framed, flatness);
    }

    /// <summary>
    /// The direction of the outline's smallest-area bounding rectangle, by rotating
    /// calipers over its convex hull: a rectangle's own edge direction, and for any
    /// other shape the direction that wastes least. Returned as the cosine and sine of
    /// the angle, with the rectangle's longer side first.
    /// </summary>
    private static (double Cos, double Sin) SmallestRectangle(Flat[] points)
    {
        var hull = ConvexHull(points);
        if (hull.Length < 2)
            return (1.0, 0.0);

        double bestArea = double.PositiveInfinity;
        (double Cos, double Sin) best = (1.0, 0.0);

        for (int i = 0; i < hull.Length; i++)
        {
            var edge = hull[(i + 1) % hull.Length] - hull[i];
            double length = edge.Length;
            if (length <= 0.0)
                continue;

            double cos = edge.X / length, sin = edge.Y / length;
            double lowX = double.MaxValue, highX = double.MinValue, lowY = double.MaxValue, highY = double.MinValue;

            foreach (var p in hull)
            {
                double x = p.X * cos + p.Y * sin;
                double y = -p.X * sin + p.Y * cos;
                lowX = Math.Min(lowX, x);
                highX = Math.Max(highX, x);
                lowY = Math.Min(lowY, y);
                highY = Math.Max(highY, y);
            }

            double area = (highX - lowX) * (highY - lowY);
            if (area >= bestArea)
                continue;

            bestArea = area;

            // The long side runs along the panel's length; turn the frame a quarter if
            // the rectangle came out taller than it is wide.
            best = highX - lowX >= highY - lowY ? (cos, sin) : (-sin, cos);
        }

        return best;
    }

    /// <summary>The convex hull, counter-clockwise, by Andrew's monotone chain.</summary>
    private static Flat[] ConvexHull(Flat[] points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (sorted.Length < 3)
            return sorted;

        var hull = new List<Flat>();
        // Enumerable.Reverse, not the array's own: that one is a span extension that
        // reverses in place and returns nothing.
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToArray() })
        {
            int start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && (hull[^1] - hull[^2]).Cross(p - hull[^2]) <= 0.0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull.ToArray();
    }

    /// <summary>
    /// Where a line across the frame at <paramref name="x"/> crosses the outline, as
    /// the edge crossed and the height it crosses at, low to high. A boundary
    /// alternates in and out, so the crossings pair off into the pieces of the
    /// surface that the line passes through.
    /// </summary>
    public List<(int Edge, double Y)> Crossings(double x)
    {
        var crossings = new List<(int Edge, double Y)>();
        for (int i = 0; i < Corners.Length; i++)
        {
            var a = Corners[i];
            var b = Corners[(i + 1) % Corners.Length];
            if (a.X == b.X)
                continue;

            // Half-open in x, so a corner is counted once and a vertical edge never.
            double low = Math.Min(a.X, b.X), high = Math.Max(a.X, b.X);
            if (x < low || x >= high)
                continue;

            crossings.Add((i, a.Y + (x - a.X) / (b.X - a.X) * (b.Y - a.Y)));
        }

        crossings.Sort((p, q) => p.Y.CompareTo(q.Y));
        return crossings;
    }

    /// <summary>How high one edge of the outline runs at <paramref name="x"/>.</summary>
    public double Height(int edge, double x)
    {
        var a = Corners[edge];
        var b = Corners[(edge + 1) % Corners.Length];
        return Math.Abs(b.X - a.X) > 0.0 ? a.Y + (x - a.X) / (b.X - a.X) * (b.Y - a.Y) : a.Y;
    }

    /// <summary>Where an edge is at height <paramref name="y"/>, or null if it never is.</summary>
    public double? Meets(int edge, double y)
    {
        var a = Corners[edge];
        var b = Corners[(edge + 1) % Corners.Length];
        if (a.Y == b.Y || y < Math.Min(a.Y, b.Y) || y > Math.Max(a.Y, b.Y))
            return null;

        return a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
    }

    /// <summary>
    /// Where a line across the frame at <paramref name="x"/> is inside the outline, as
    /// ranges of the across axis, low to high.
    /// </summary>
    public List<(double Low, double High)> Inside(double x)
    {
        var crossings = Crossings(x);
        var ranges = new List<(double, double)>();
        for (int i = 0; i + 1 < crossings.Count; i += 2)
            ranges.Add((crossings[i].Y, crossings[i + 1].Y));

        return ranges;
    }

    /// <summary>Whether the whole band from <paramref name="low"/> to <paramref name="high"/> at <paramref name="x"/> is inside.</summary>
    public bool Covers(double x, double low, double high)
        => Inside(x).Any(range => range.Low <= low && range.High >= high);
}
