using Direct2dDemo.Shared.Enums;
using System.Drawing;

namespace Direct2dDemo.Shared.Elements.DrawingElements;

public sealed record RectangleElement : IDirectDrawElement
{
    public Color StrokeColor { get; init; } = Color.Black;
    public Color FillColor { get; init; } = Color.Transparent;
    public float StrokeWidth { get; init; } = 1.0f;
    public FillStyle FillStyle { get; init; } = FillStyle.None;
    public HatchStyle? HatchStyle { get; init; }
    public PointF TopLeft { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public DashStyle DashStyle { get; init; } = DashStyle.Solid;
    public CapStyle CapStyle { get; init; } = CapStyle.Flat;
    public LineJoin LineJoin { get; init; } = LineJoin.Miter;
}