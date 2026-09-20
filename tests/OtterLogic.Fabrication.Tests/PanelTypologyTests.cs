using OtterLogic.Fabrication;
using Xunit;

namespace OtterLogic.Fabrication.Tests;

public class PanelTypologyTests
{
    private static double[,] Boundary(params (double X, double Y, double Z)[] corners)
    {
        var rows = new double[corners.Length, 3];
        for (int i = 0; i < corners.Length; i++)
            (rows[i, 0], rows[i, 1], rows[i, 2]) = (corners[i].X, corners[i].Y, corners[i].Z);
        return rows;
    }

    /// <summary>A rectangle in the world XY plane, corner at the origin.</summary>
    private static double[,] Rectangle(double length, double width)
        => Boundary((0, 0, 0), (length, 0, 0), (length, width, 0), (0, width, 0));

    /// <summary>A wall with one sloping top edge, from <paramref name="low"/> to <paramref name="high"/>.</summary>
    private static double[,] Sloping(double length, double low, double high)
        => Boundary((0, 0, 0), (length, 0, 0), (length, high, 0), (0, low, 0));

    /// <summary>A circle as the points around it, the way a curved face edge is read.</summary>
    private static double[,] Disc(double radius, int pieces)
    {
        var corners = new (double, double, double)[pieces];
        for (int i = 0; i < pieces; i++)
        {
            double angle = 2.0 * Math.PI * i / pieces;
            corners[i] = (radius + radius * Math.Cos(angle), radius + radius * Math.Sin(angle), 0.0);
        }

        return Boundary(corners);
    }

    /// <summary>A surface cut into an L, so a row of it has a piece missing at one end.</summary>
    private static double[,] Ell()
        => Boundary((0, 0, 0), (12000, 0, 0), (12000, 4000, 0), (4000, 4000, 0), (4000, 9000, 0), (0, 9000, 0));

    private static PanelTypologyOptions Machine(
        double length = 8000, double width = 3000, double gap = 0,
        double standardisation = 1.0, double typeTolerance = 1)
        => new()
        {
            Length = length,
            Width = width,
            Gap = gap,
            Standardisation = standardisation,
            TypeTolerance = typeTolerance,
        };

    /// <summary>The panels of one surface as length x width pairs, in the order they were laid out.</summary>
    private static (double Length, double Width)[] Sizes(PanelTypologyResult result, int surface = 0)
        => result.Panels.Where(p => p.Surface == surface)
            .Select(p => (Math.Round(p.Length, 6), Math.Round(p.Width, 6))).ToArray();

    /// <summary>Whether a point is inside a boundary, counting a point on the edge as inside.</summary>
    private static bool Inside(double[,] boundary, double x, double y, double tolerance)
    {
        int n = boundary.GetLength(0);
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double ax = boundary[j, 0], ay = boundary[j, 1], bx = boundary[i, 0], by = boundary[i, 1];
            double dx = bx - ax, dy = by - ay;
            double square = dx * dx + dy * dy;
            double t = square > 0.0 ? Math.Clamp(((x - ax) * dx + (y - ay) * dy) / square, 0.0, 1.0) : 0.0;
            double px = ax + t * dx - x, py = ay + t * dy - y;
            if (Math.Sqrt(px * px + py * py) <= tolerance)
                return true;

            if ((by > y) != (ay > y) && x < (ax - bx) * (y - by) / (ay - by) + bx)
                inside = !inside;
        }

        return inside;
    }

    private static void AssertInside(PanelTypologyResult result, double[,] boundary, double tolerance = 1e-6)
    {
        foreach (var panel in result.Panels)
            for (int c = 0; c < panel.Corners.GetLength(0); c++)
                Assert.True(Inside(boundary, panel.Corners[c, 0], panel.Corners[c, 1], tolerance),
                    $"A corner at ({panel.Corners[c, 0]:G6}, {panel.Corners[c, 1]:G6}) sits outside the surface.");
    }

    [Fact]
    public void ASurfaceThatFitsWholePanelsIsAllStandard()
    {
        var result = PanelTypology.Layout(new[] { Rectangle(24000, 6000) }, Machine());

        Assert.Equal(6, result.Panels.Count);
        Assert.All(result.Panels, p => Assert.Equal((8000.0, 3000.0), (p.Length, p.Width)));
        var type = Assert.Single(result.Types);
        Assert.True(type.IsStandard);
        Assert.Equal("P1", type.Name);
        Assert.Equal(1.0, result.Coverage()[0], 9);
    }

    /// <summary>
    /// Holding to the machine's panel, 20 m of an 8 m machine is two whole panels and a
    /// 4 m one, not three of 6.67 m.
    /// </summary>
    [Fact]
    public void FullPanelsFirstThenTheLeftover()
    {
        var result = PanelTypology.Layout(new[] { Rectangle(20000, 3000) }, Machine());

        Assert.Equal(new[] { (8000.0, 3000.0), (8000.0, 3000.0), (4000.0, 3000.0) }, Sizes(result));
        Assert.Equal(2, result.Types.Count);
        Assert.True(result.Types[0].IsStandard);
        Assert.Equal(4000.0, result.Types[1].Length, 6);
    }

    /// <summary>
    /// The other end of the lever: the same wall shared out rather than held to the
    /// machine's size, which is the same number of panels but all one type.
    /// </summary>
    [Fact]
    public void SharingTheLeftoverOutMakesOnePanelType()
    {
        var result = PanelTypology.Layout(new[] { Rectangle(20000, 3000) }, Machine(standardisation: 0.0));

        Assert.Equal(3, result.Panels.Count);
        var type = Assert.Single(result.Types);
        Assert.False(type.IsStandard);
        Assert.Equal(20000.0 / 3.0, type.Length, 6);
    }

    /// <summary>
    /// What the lever is for: standardising buys repeats and pays in slivers, sharing
    /// out buys even panels and pays in bespoke sizes. Both are right; which one is
    /// the user's to say.
    /// </summary>
    [Fact]
    public void StandardisationTradesRepeatsForSlivers()
    {
        var wall = new[] { Rectangle(25000, 7000) };

        var held = PanelTypology.Layout(wall, Machine());
        var shared = PanelTypology.Layout(wall, Machine(standardisation: 0.0));

        Assert.True(held.Standard > 0, "Holding to the machine's panel should make some of them.");
        Assert.Equal(0, shared.Standard);
        Assert.True(shared.Types.Count < held.Types.Count,
            $"Sharing out should need fewer types, not {shared.Types.Count} against {held.Types.Count}.");
        Assert.True(shared.Panels.Min(p => p.Area) > held.Panels.Min(p => p.Area),
            "Sharing out should leave nothing as small as the sliver standardising leaves.");
    }

    [Fact]
    public void GapsSitBetweenPanelsAndNotAtTheEdges()
    {
        var result = PanelTypology.Layout(new[] { Rectangle(20000, 3000) }, Machine(gap: 20));

        // Three panels and two joints still cover the wall exactly.
        Assert.Equal(20000.0 - 2 * 20.0, Sizes(result).Sum(s => s.Length), 6);
        Assert.All(result.Panels, p => Assert.Equal(3000.0, p.Width, 6));
        AssertInside(result, Rectangle(20000, 3000));
    }

    [Fact]
    public void NothingComesOutBiggerThanTheMachine()
    {
        foreach (double standardisation in new[] { 1.0, 0.5, 0.0 })
        {
            var options = Machine(gap: 20, standardisation: standardisation);
            var result = PanelTypology.Layout(new[] { Sloping(30000, 3000, 9000), Disc(15000, 64) }, options);

            Assert.All(result.Panels, p =>
            {
                Assert.True(p.Length <= options.Length + 1e-6, $"A panel is {p.Length:G6} long.");
                Assert.True(p.Width <= options.Width + 1e-6, $"A panel is {p.Width:G6} wide.");
            });
        }
    }

    /// <summary>
    /// The promise the whole engine rests on: a panel is cut where the surface ends,
    /// so no corner of one is ever outside it.
    /// </summary>
    [Fact]
    public void NoPanelCrossesTheOutline()
    {
        var sloping = Sloping(30000, 3000, 9000);
        var disc = Disc(15000, 64);

        AssertInside(PanelTypology.Layout(new[] { sloping }, Machine(gap: 20)), sloping);
        AssertInside(PanelTypology.Layout(new[] { disc }, Machine(4000, 3400)), disc);
    }

    /// <summary>
    /// And the other half of it: what the panels do not cover is the joints, and
    /// nothing else. With no joint asked for, a curved or notched surface comes out
    /// whole.
    /// </summary>
    [Fact]
    public void TheWholeSurfaceIsCovered()
    {
        var disc = PanelTypology.Layout(new[] { Disc(15000, 64) }, Machine(4000, 3400));
        Assert.Equal(1.0, disc.Coverage()[0], 6);

        var ell = PanelTypology.Layout(new[] { Ell() }, Machine(4000, 3000));
        Assert.Equal(1.0, ell.Coverage()[0], 6);
    }

    [Fact]
    public void ARowThatIsInTwoPartsIsPanelisedApart()
    {
        // A U: the upper rows are two towers, the lower ones one run.
        var u = Boundary(
            (0, 0, 0), (12000, 0, 0), (12000, 9000, 0), (8000, 9000, 0),
            (8000, 4000, 0), (4000, 4000, 0), (4000, 9000, 0), (0, 9000, 0));

        var result = PanelTypology.Layout(new[] { u }, Machine(4000, 3000));

        Assert.Equal(1.0, result.Coverage()[0], 6);
        AssertInside(result, u);

        // Nothing spans the gap between the two towers.
        Assert.DoesNotContain(result.Panels, p =>
        {
            double low = double.MaxValue, high = double.MinValue, bottom = double.MaxValue;
            for (int c = 0; c < p.Corners.GetLength(0); c++)
            {
                low = Math.Min(low, p.Corners[c, 0]);
                high = Math.Max(high, p.Corners[c, 0]);
                bottom = Math.Min(bottom, p.Corners[c, 1]);
            }

            return bottom > 4000.0 + 1e-6 && low < 4000.0 - 1e-6 && high > 8000.0 + 1e-6;
        });
    }

    /// <summary>
    /// A sloping edge needs no special case any more: a panel the edge runs across
    /// comes out with three corners because that is what is left of it.
    /// </summary>
    [Fact]
    public void ASlopingEdgeLeavesTrianglesAndCutPanels()
    {
        var result = PanelTypology.Layout(new[] { Sloping(30000, 3000, 9000) }, Machine(gap: 20));

        Assert.Contains(result.Panels, p => p.Shape == PanelShape.Triangle);
        Assert.Contains(result.Panels, p => p.Shape == PanelShape.Rectangle);
        Assert.All(result.Types.Where(t => t.Shape == PanelShape.Triangle), t => Assert.StartsWith("T", t.Name));
        Assert.All(result.Types.Where(t => t.Shape == PanelShape.Cut), t => Assert.StartsWith("C", t.Name));
    }

    /// <summary>
    /// A curved edge is cut, not fitted to: the panels against it have the points of
    /// the curve along their edge rather than one straight line across them, and the
    /// ones well inside are still the machine's full panel.
    /// </summary>
    [Fact]
    public void ACurvedEdgeIsFollowed()
    {
        var result = PanelTypology.Layout(new[] { Disc(15000, 64) }, Machine(4000, 3400));

        Assert.Contains(result.Panels, p => p.Corners.GetLength(0) > 4);
        Assert.Contains(result.Types, t => t.IsStandard && t.Count > 1);
    }

    /// <summary>
    /// What the learned signature buys. This surface is cut into exactly two panels:
    /// one notched at both its top corners, one notched once in the middle of its
    /// top edge, the same area taken out of the same band of each. They have the
    /// same stock size, the same area and the same centre of area, so no list of
    /// measurements picked by hand separates them — and they are plainly two
    /// different panels to make.
    /// </summary>
    [Fact]
    public void PanelsNoHandPickedMeasurementCanTellApartAreTwoTypes()
    {
        var notched = Boundary(
            (0, 0, 0), (8000, 0, 0), (8000, 3000, 0),
            (6500, 3000, 0), (6500, 2500, 0), (5500, 2500, 0), (5500, 3000, 0),
            (4000, 3000, 0), (4000, 2500, 0), (3500, 2500, 0), (3500, 3000, 0),
            (500, 3000, 0), (500, 2500, 0), (0, 2500, 0));

        var result = PanelTypology.Layout(new[] { notched }, Machine(4000, 3000));

        Assert.Equal(2, result.Panels.Count);

        var one = result.Panels[0];
        var two = result.Panels[1];
        Assert.Equal(one.Length, two.Length, 6);
        Assert.Equal(one.Width, two.Width, 6);
        Assert.Equal(one.Area, two.Area, 6);

        Assert.Equal(2, result.Types.Count);
        Assert.NotEqual(one.Type, two.Type);
    }

    /// <summary>
    /// A panel and its mirror image are two moulds, not one, so they are two types —
    /// which is why the grouping measures where a panel's weight sits and not only
    /// how big it is.
    /// </summary>
    [Fact]
    public void APanelAndItsMirrorAreNotTheSameType()
    {
        // A symmetrical trapezoid: the wedge at each end is the other one turned over.
        var trapezoid = Boundary((0, 0, 0), (20000, 0, 0), (16000, 3000, 0), (4000, 3000, 0));
        var result = PanelTypology.Layout(new[] { trapezoid }, Machine(4000, 3000));

        var wedges = result.Types.Where(t => t.Shape == PanelShape.Triangle).ToArray();
        Assert.Equal(2, wedges.Length);
        Assert.All(wedges, t => Assert.Equal(1, t.Count));
        Assert.Equal(wedges[0].Length, wedges[1].Length, 6);
        Assert.Equal(wedges[0].Width, wedges[1].Width, 6);
    }

    /// <summary>
    /// The rationalisation lever: accepting more tolerance between panels buys fewer
    /// types to make.
    /// </summary>
    [Fact]
    public void RaisingTheTypeToleranceMergesTypes()
    {
        var wall = new[] { Sloping(30000, 3000, 9000) };

        int tight = PanelTypology.Layout(wall, Machine(gap: 20, typeTolerance: 1)).Types.Count;
        int loose = PanelTypology.Layout(wall, Machine(gap: 20, typeTolerance: 400)).Types.Count;

        Assert.True(loose < tight, $"400 of tolerance should merge types, not leave {loose} against {tight}.");
    }

    /// <summary>A size that turns up on four surfaces is one type made four times.</summary>
    [Fact]
    public void TypesAreSharedAcrossSurfaces()
    {
        var walls = Enumerable.Repeat(Rectangle(20000, 3000), 4).ToArray();
        var result = PanelTypology.Layout(walls, Machine());

        Assert.Equal(2, result.Types.Count);
        Assert.Equal(8, result.Types[0].Count);
        Assert.Equal(4, result.Types[1].Count);

        var tally = result.Tally();
        for (int s = 0; s < 4; s++)
            Assert.Equal(new[] { 2, 1 }, new[] { tally[s, 0], tally[s, 1] });
    }

    /// <summary>
    /// A panel's length runs along the surface's own long edge, not the world's axes:
    /// the same wall turned on the spot is the same job.
    /// </summary>
    [Fact]
    public void PanelsRunAlongTheSurfacesOwnLongEdge()
    {
        double turn = 0.7;
        double cos = Math.Cos(turn), sin = Math.Sin(turn);
        var flat = Rectangle(24000, 6000);
        var turned = new double[4, 3];
        for (int i = 0; i < 4; i++)
        {
            turned[i, 0] = flat[i, 0] * cos - flat[i, 1] * sin;
            turned[i, 1] = flat[i, 0] * sin + flat[i, 1] * cos;
            turned[i, 2] = 5000.0;
        }

        var result = PanelTypology.Layout(new[] { turned }, Machine());

        Assert.Equal(6, result.Panels.Count);
        Assert.All(result.Panels, p => Assert.Equal((8000.0, 3000.0), (Math.Round(p.Length, 6), Math.Round(p.Width, 6))));
        Assert.All(result.Panels, p =>
        {
            for (int c = 0; c < p.Corners.GetLength(0); c++)
                Assert.Equal(5000.0, p.Corners[c, 2], 6);
        });
    }

    [Fact]
    public void AWarpedSurfaceIsPanelisedOnItsBestPlaneAndSaidSo()
    {
        var warped = Boundary((0, 0, 0), (20000, 0, 0), (20000, 6000, 400), (0, 6000, 0));
        var result = PanelTypology.Layout(new[] { warped }, Machine());

        Assert.NotEmpty(result.Panels);
        Assert.Contains(result.Notes, n => n.Contains("not flat"));
    }

    [Fact]
    public void ASurfaceWithNoAreaIsLeftOutAndSaidSo()
    {
        var result = PanelTypology.Layout(
            new[] { Rectangle(20000, 3000), Boundary((0, 0, 0), (1000, 0, 0), (2000, 0, 0)) }, Machine());

        Assert.All(result.Panels, p => Assert.Equal(0, p.Surface));
        Assert.Contains(result.Notes, n => n.Contains("no area"));
        Assert.Equal(0.0, result.Coverage()[1]);
    }

    /// <summary>
    /// Slivers are not hidden. Whether any panel is on a smaller scale than the rest
    /// is read from the panels themselves, not from a size written into the code.
    /// </summary>
    [Fact]
    public void SmallPanelsAreRemarkedOn()
    {
        var held = PanelTypology.Layout(new[] { Sloping(30000, 3000, 9000) }, Machine(gap: 20));
        Assert.Contains(held.Notes, n => n.Contains("smaller scale"));

        var shared = PanelTypology.Layout(new[] { Sloping(30000, 3000, 9000) }, Machine(gap: 20, standardisation: 0.0));
        Assert.DoesNotContain(shared.Notes, n => n.Contains("smaller scale"));
    }

    [Theory]
    [InlineData(0, 3000)]
    [InlineData(8000, 0)]
    [InlineData(-1, 3000)]
    public void AMachineWithNoPanelIsRefused(double length, double width)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => PanelTypology.Layout(new[] { Rectangle(20000, 3000) }, new PanelTypologyOptions { Length = length, Width = width }));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void StandardisationOutsideItsRangeIsRefused(double standardisation)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => PanelTypology.Layout(new[] { Rectangle(20000, 3000) }, Machine(standardisation: standardisation)));

    [Fact]
    public void NoSurfacesIsRefused()
        => Assert.Throws<ArgumentException>(() => PanelTypology.Layout(Array.Empty<double[,]>(), Machine()));

    [Fact]
    public void ABoundaryThatIsNotCornersIsRefused()
        => Assert.Throws<ArgumentException>(() => PanelTypology.Layout(new[] { new double[3, 2] }, Machine()));

    [Fact]
    public void ACornerThatIsNotFiniteIsRefused()
    {
        var broken = Rectangle(20000, 3000);
        broken[2, 1] = double.NaN;
        Assert.Throws<ArgumentException>(() => PanelTypology.Layout(new[] { broken }, Machine()));
    }

    [Fact]
    public void TheSummaryNamesEveryTypeAndSurface()
    {
        var result = PanelTypology.Layout(new[] { Rectangle(20000, 3000), Rectangle(24000, 6000) }, Machine());
        string summary = result.Summary();

        foreach (var type in result.Types)
            Assert.Contains(type.Name, summary);
        Assert.Contains("Surface 0", summary);
        Assert.Contains("Surface 1", summary);
        Assert.Contains("the machine's full panel", summary);
    }
}
