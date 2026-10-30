namespace OpenTabletArtist.Controls;

/// <summary>
/// The numbers behind the buttons tab's pen picture and the cards under it: where each barrel button sits on the
/// drawn pen, where each card sits across the row, and the elbow lines that join one to the other. Pure
/// arithmetic, no drawing, so the rules that make the picture readable can be tested: the lines never cross,
/// and a window too narrow for a row of cards stacks them and drops the lines.
/// </summary>
public static class PenDiagramGeometry
{
    public readonly record struct Point2(double X, double Y);

    /// <summary>The elbow line for one button: down from the button, along, and down to the top of its card.</summary>
    public sealed record Connector(int Number, IReadOnlyList<Point2> Points);

    /// <summary>Width of the pen as drawn at its natural size (nib to the end of the eraser cap).</summary>
    public const double NaturalWidth = 606;

    /// <summary>Height of the picture when it carries connectors; without them only the pen is drawn.</summary>
    public const double HeightWithConnectors = 134;
    public const double HeightPenOnly = 62;

    /// <summary>Where the pen is drawn: the tip's end, its length to the eraser's end, and the centre line it lies on.
    /// The outline itself is <see cref="PenArtwork"/>; at this length it is about 55 thick at the nib's swell.</summary>
    /// <remarks>
    /// Three quarters of what it was (#1047): at 806 long and 73 thick the pen was the largest thing on the tab, and
    /// larger than the cards it labels. Only the pen shrank. The buttons keep their size, because they are what is
    /// pointed at and read (a number inside each), and everything that places them follows the pen's new proportions.
    /// </remarks>
    public const double PenLeft = 2, PenLength = 604, PenCenterY = 30;

    /// <summary>Where the buttons sit on the pen: the first is nearest the tip, each next one further along.</summary>
    public const double FirstButtonX = 190, ButtonPitch = 66, ButtonWidth = 44, ButtonHeight = 20;
    public const double ButtonTopY = 20, ButtonBottomY = 40;

    /// <summary>Where the lines end, just above the cards.</summary>
    public const double ConnectorEndY = 128;
    // The first level sits below the pen's body (it reaches about y = 58 under the buttons), so a run across never
    // goes through the pen.
    private const double LevelTop = 74, LevelStep = 14, SameColumn = 0.5;

    /// <summary>The narrowest a card can be before its three choices no longer fit on one line.</summary>
    public const double MinCardWidth = 340;
    public const double CardGap = 16;

    public static double ButtonCenterX(int number) => FirstButtonX + ButtonPitch * (number - 1);

    /// <summary>How many equal columns the cards are laid out in. A pen with one button still gets two, so its
    /// card is half the width and not a slab across the page. Fewer than that fit, one column: the cards stack.</summary>
    public static int Slots(int buttonCount) => Math.Max(buttonCount, 2);

    /// <summary>The number of columns the cards use at this width: all of them side by side, or one.</summary>
    public static int Columns(double width, int buttonCount)
    {
        int slots = Slots(buttonCount);
        return width >= slots * MinCardWidth + (slots - 1) * CardGap ? slots : 1;
    }

    /// <summary>Whether the cards sit side by side at this width, which is when the lines make sense.</summary>
    public static bool IsSideBySide(double width, int buttonCount) =>
        buttonCount > 0 && Columns(width, buttonCount) == Slots(buttonCount);

    /// <summary>The horizontal centre of the card for button <paramref name="number"/> in a row of equal columns.</summary>
    public static double CardCenterX(double width, int buttonCount, int number, double gap = CardGap)
    {
        int slots = Slots(buttonCount);
        double column = (width - (slots - 1) * gap) / slots;
        return (number - 1) * (column + gap) + column / 2;
    }

    /// <summary>
    /// The lines from each button to its card. A line that heads right turns at a level that gets higher the further
    /// along the pen its button is; one that heads left turns at a level that gets lower. That ordering is what keeps
    /// them from crossing, because the buttons are numbered along the pen in the same order as the cards across the row.
    /// </summary>
    public static IReadOnlyList<Connector> Connectors(double width, int buttonCount, double gap = CardGap)
    {
        var heads = Enumerable.Range(1, buttonCount)
            .Select(n => (Number: n, From: ButtonCenterX(n), To: CardCenterX(width, buttonCount, n, gap)))
            .ToList();

        var rightward = heads.Where(h => h.To - h.From > SameColumn).OrderByDescending(h => h.From).ToList();
        var leftward = heads.Where(h => h.From - h.To > SameColumn).OrderBy(h => h.From).ToList();

        var level = new Dictionary<int, double>();
        for (int i = 0; i < rightward.Count; i++) level[rightward[i].Number] = LevelTop + LevelStep * i;
        for (int i = 0; i < leftward.Count; i++) level[leftward[i].Number] = LevelTop + LevelStep * i;

        return heads.Select(h => new Connector(h.Number, level.TryGetValue(h.Number, out var y)
                ? new[]
                {
                    new Point2(h.From, ButtonBottomY), new Point2(h.From, y),
                    new Point2(h.To, y), new Point2(h.To, ConnectorEndY),
                }
                : new[] { new Point2(h.From, ButtonBottomY), new Point2(h.From, ConnectorEndY) }))
            .ToList();
    }
}
