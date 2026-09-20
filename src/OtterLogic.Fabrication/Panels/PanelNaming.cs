namespace OtterLogic.Fabrication;

/// <summary>
/// The only hard-coded convention in panelising: what the types are called, and
/// the order they are listed in.
/// <para>
/// Everything that decides which panels exist and which of them are the same type
/// is measured. Marks are a house style — one office numbers from the standard
/// out, another letters them by surface — so they live in one file, easy to change.
/// </para>
/// </summary>
public static class PanelNaming
{
    /// <summary>
    /// The mark of a type: P1, P2 for square-cut panels, T1, T2 for triangles and
    /// C1, C2 for anything cut to the edge of the surface, so a cutting list says at
    /// a glance which are straightforward and which are not.
    /// </summary>
    public static string Mark(int index, PanelShape shape)
        => $"{shape switch { PanelShape.Rectangle => "P", PanelShape.Triangle => "T", _ => "C" }}{index + 1}";

    /// <summary>
    /// How types are ordered before they are marked: the machine's full panel first
    /// because it is the one to repeat, then the simplest shape, then the most used,
    /// then the largest.
    /// </summary>
    public static IEnumerable<T> Order<T>(
        IEnumerable<T> types, Func<T, bool> standard, Func<T, PanelShape> shape, Func<T, int> count, Func<T, double> area)
        => types.OrderByDescending(standard).ThenBy(t => (int)shape(t)).ThenByDescending(count).ThenByDescending(area);
}
