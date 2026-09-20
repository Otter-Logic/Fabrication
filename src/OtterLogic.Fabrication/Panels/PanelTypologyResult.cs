using System.Globalization;
using System.Text;

namespace OtterLogic.Fabrication;

/// <summary>What a panel is cut as — read off its outline, not decided in advance.</summary>
public enum PanelShape
{
    /// <summary>Square-cut, four corners: what the machine makes without fuss.</summary>
    Rectangle,

    /// <summary>Three corners: the wedge left where an edge runs across a panel.</summary>
    Triangle,

    /// <summary>Anything else — a panel the edge of the surface has been cut through.</summary>
    Cut,
}

/// <summary>One panel, as it would be cast and where it goes.</summary>
/// <param name="Surface">The surface it came from.</param>
/// <param name="Type">Its type, an index into <see cref="PanelTypologyResult.Types"/>.</param>
/// <param name="Shape">Whether it is square-cut, a triangle, or cut to some other outline.</param>
/// <param name="Length">The stock it is cut from, along the surface's long axis.</param>
/// <param name="Width">The stock it is cut from, across.</param>
/// <param name="Area">The area of the panel itself, which is less than length x width wherever it is cut.</param>
/// <param name="Corners">Its corners in model space, in order: k x 3, four for a square-cut panel and more where an edge is followed.</param>
public sealed record Panel(
    int Surface, int Type, PanelShape Shape, double Length, double Width, double Area, double[,] Corners);

/// <summary>A panel type: every panel of one size and shape, made one setting of the machine.</summary>
/// <param name="Name">Its mark, from <see cref="PanelNaming"/>.</param>
/// <param name="Shape">Whether its panels are square-cut, triangles, or cut to some other outline.</param>
/// <param name="Length">The stock length its panels are cut from — the median of them.</param>
/// <param name="Width">The stock width.</param>
/// <param name="Area">The median area of its panels.</param>
/// <param name="Panels">Indices into <see cref="PanelTypologyResult.Panels"/>, ascending.</param>
/// <param name="IsStandard">Whether this is the machine's full rectangle, square-cut: the panel to repeat.</param>
public sealed record PanelType(
    string Name, PanelShape Shape, double Length, double Width, double Area, int[] Panels, bool IsStandard)
{
    /// <summary>How many panels of this type.</summary>
    public int Count => Panels.Length;
}

/// <summary>The panels a set of surfaces breaks into, and the types they repeat.</summary>
public sealed class PanelTypologyResult
{
    internal PanelTypologyResult(
        IReadOnlyList<Panel> panels, IReadOnlyList<PanelType> types, int surfaceCount,
        double[] surfaceArea, double[] panelArea, IReadOnlyList<string> notes)
    {
        Panels = panels;
        Types = types;
        SurfaceCount = surfaceCount;
        SurfaceArea = surfaceArea;
        PanelArea = panelArea;
        Notes = notes;
    }

    /// <summary>Every panel, surface by surface.</summary>
    public IReadOnlyList<Panel> Panels { get; }

    /// <summary>The types, the machine's standard first, then most used.</summary>
    public IReadOnlyList<PanelType> Types { get; }

    /// <summary>How many surfaces were read.</summary>
    public int SurfaceCount { get; }

    /// <summary>Per surface, its own area.</summary>
    public double[] SurfaceArea { get; }

    /// <summary>Per surface, the area its panels cover.</summary>
    public double[] PanelArea { get; }

    /// <summary>Anything that changed what was found, in plain words.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>Per surface, the share of it covered by panels, 0 to 1 — the rest is the joints.</summary>
    public double[] Coverage()
        => Enumerable.Range(0, SurfaceCount).Select(s => SurfaceArea[s] > 0.0 ? PanelArea[s] / SurfaceArea[s] : 0.0).ToArray();

    /// <summary>The panels of one type on one surface, ascending; empty when there are none.</summary>
    public int[] Of(int surface, int type)
        => Enumerable.Range(0, Panels.Count).Where(p => Panels[p].Surface == surface && Panels[p].Type == type).ToArray();

    /// <summary>How many panels of each type are on each surface: surfaces x types.</summary>
    public int[,] Tally()
    {
        var tally = new int[SurfaceCount, Types.Count];
        foreach (var panel in Panels)
            tally[panel.Surface, panel.Type]++;
        return tally;
    }

    /// <summary>How many panels are the machine's full rectangle, square-cut.</summary>
    public int Standard => Types.Where(t => t.IsStandard).Sum(t => t.Count);

    /// <summary>The types, their sizes and counts, then how each surface is made up.</summary>
    public string Summary()
    {
        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        string Size(double value) => value.ToString("G6", invariant);

        text.AppendLine($"{Panels.Count} panel(s) of {Types.Count} type(s) over {SurfaceCount} surface(s), "
            + $"{Standard} of them the machine's full panel.");

        if (Types.Count > 0)
        {
            text.AppendLine();
            foreach (var type in Types)
                text.AppendLine($"{type.Name}: {Size(type.Length)} x {Size(type.Width)}"
                    + (type.Shape == PanelShape.Rectangle ? string.Empty : $" {type.Shape.ToString().ToLowerInvariant()}")
                    + $" — {type.Count} panel(s)"
                    + (type.IsStandard ? ", the machine's full panel" : string.Empty));
        }

        var tally = Tally();
        var coverage = Coverage();
        text.AppendLine();
        for (int s = 0; s < SurfaceCount; s++)
        {
            var made = Enumerable.Range(0, Types.Count).Where(t => tally[s, t] > 0)
                .Select(t => $"{tally[s, t]} x {Types[t].Name}");
            text.AppendLine($"Surface {s}: {string.Join(", ", made.DefaultIfEmpty("no panels"))} "
                + $"({coverage[s]:P1} covered)");
        }

        return text.ToString().TrimEnd();
    }
}
