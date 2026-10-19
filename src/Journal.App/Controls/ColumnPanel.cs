using System.Windows;
using System.Windows.Controls;

namespace Journal.App.Controls;

/// <summary>
/// Lays out its children in equal-width columns, filling each column top to bottom before moving to the
/// next, so a sorted list reads down the columns (ISS-015).
/// </summary>
public sealed class ColumnPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(ColumnPanel),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure), v => (int)v >= 1);

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>Rows needed for the children; the last column may be shorter.</summary>
    public int Rows => (InternalChildren.Count + Columns - 1) / Columns;

    private double _rowHeight;

    protected override Size MeasureOverride(Size available)
    {
        var columnWidth = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width / Columns;
        double widest = 0;
        _rowHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            widest = Math.Max(widest, child.DesiredSize.Width);
            _rowHeight = Math.Max(_rowHeight, child.DesiredSize.Height);
        }
        var width = double.IsInfinity(available.Width) ? widest * Columns : available.Width;
        return new Size(width, _rowHeight * Rows);
    }

    protected override Size ArrangeOverride(Size final)
    {
        int rows = Math.Max(1, Rows);
        var columnWidth = final.Width / Columns;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int column = i / rows, row = i % rows;
            InternalChildren[i].Arrange(new Rect(column * columnWidth, row * _rowHeight, columnWidth, _rowHeight));
        }
        return final;
    }
}
