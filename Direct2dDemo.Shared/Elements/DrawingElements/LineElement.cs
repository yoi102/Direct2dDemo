using Direct2dDemo.Shared.Enums;
using System.Drawing;

namespace Direct2dDemo.Shared.Elements.DrawingElements;

public record LineElement : IDirectDrawElement
{
    public PointF StartPoint { get; init; }
    public PointF EndPoint { get; init; }

    public DashStyle DashStyle { get; init; } = DashStyle.Solid;

    public CapStyle CapStyle { get; init; } = CapStyle.Flat;

    public float StrokeWidth { get; init; } = 1.0f;
    public Color StrokeColor { get; init; } = Color.Black;
}