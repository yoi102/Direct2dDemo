using Direct2dDemo.Shared.Elements.DrawingElements;
using Direct2dDemo.Shared.Enums;
using System.Drawing;

namespace Direct2dDemo.Shared.Elements.GeometryElements;

public sealed record RectangleGeometryElement : IDirectDrawElement
{
    public Color StrokeColor { get; init; } = Color.Black;
    public Color FillColor { get; init; } = Color.Transparent;
    public float StrokeWidth { get; init; } = 1.0f;
    public FillStyle FillStyle { get; init; } = FillStyle.None;
    public HatchStyle? HatchStyle { get; init; }
    public PointF TopLeft { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
}