using Direct2dDemo.Shared.Enums;
using System.Drawing;

namespace Direct2dDemo.Shared.Elements.DrawingElements;

public sealed record EllipseElement : IDirectDrawElement
{
    public Color StrokeColor { get; init; } = Color.Black;

    public Color FillColor { get; init; } = Color.Transparent;

    public float StrokeWidth { get; init; } = 1.0f;
    public FillStyle FillStyle { get; init; } = FillStyle.None;
    public HatchStyle? HatchStyle { get; init; }

    public PointF Center { get; init; }

    public float RadiusX { get; init; }

    public float RadiusY { get; init; }
}