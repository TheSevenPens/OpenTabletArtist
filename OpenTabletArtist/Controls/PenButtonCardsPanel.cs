using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Controls;

/// <summary>
/// Lays the pen button cards out in equal columns across the page, or one under another when the page is too
/// narrow for that. The decision is <see cref="PenDiagramGeometry.Columns"/>, the same rule the pen picture uses
/// to decide whether to draw its lines, so the two always agree. Cards in a row share the tallest card's height,
/// so a card that carries a warning does not leave its neighbours ragged.
/// </summary>
public sealed class PenButtonCardsPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<PenButtonCardsPanel, double>(nameof(Spacing), PenDiagramGeometry.CardGap);

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    static PenButtonCardsPanel() => AffectsMeasure<PenButtonCardsPanel>(SpacingProperty);

    private (int Columns, double ColumnWidth) Plan(double width, int count)
    {
        int columns = PenDiagramGeometry.Columns(width, count);
        // With one button the card keeps a column's width (see PenDiagramGeometry.Slots), not the whole page.
        int slots = columns == 1 ? 1 : PenDiagramGeometry.Slots(count);
        return (columns, (width - (slots - 1) * Spacing) / slots);
    }

    private List<Control> Cards() => Children.Where(c => c.IsVisible).ToList();

    protected override Size MeasureOverride(Size availableSize)
    {
        var cards = Cards();
        double width = double.IsInfinity(availableSize.Width) ? PenDiagramGeometry.NaturalWidth : availableSize.Width;
        if (cards.Count == 0) return new Size(width, 0);

        var (columns, columnWidth) = Plan(width, cards.Count);
        double total = 0;
        for (int start = 0; start < cards.Count; start += columns)
        {
            double rowHeight = 0;
            foreach (var card in cards.Skip(start).Take(columns))
            {
                card.Measure(new Size(columnWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, card.DesiredSize.Height);
            }
            total += rowHeight + (start > 0 ? Spacing : 0);
        }
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cards = Cards();
        if (cards.Count == 0) return finalSize;

        var (columns, columnWidth) = Plan(finalSize.Width, cards.Count);
        double y = 0;
        for (int start = 0; start < cards.Count; start += columns)
        {
            var row = cards.Skip(start).Take(columns).ToList();
            double rowHeight = row.Max(c => c.DesiredSize.Height);
            for (int i = 0; i < row.Count; i++)
                row[i].Arrange(new Rect(i * (columnWidth + Spacing), y, columnWidth, rowHeight));
            y += rowHeight + Spacing;
        }
        return finalSize;
    }
}
