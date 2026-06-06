using Direct2dDemo.Shared.Enums;
using System.Drawing;

namespace Direct2dDemo.Shared.Elements.GeometryElements;

public sealed record PolygonGeometryElement : IGeometryElement
{
    public Color StrokeColor { get; init; } = Color.Black;

    public Color FillColor { get; init; } = Color.Transparent;

    public float StrokeWidth { get; init; } = 1.0f;

    public FillStyle FillStyle { get; init; } = FillStyle.None;
    public HatchStyle? HatchStyle { get; init; }

    public IReadOnlyList<PointF> Points { get; init; } = Array.Empty<PointF>();

    public DashStyle DashStyle { get; init; } = DashStyle.Solid;
    public CapStyle CapStyle { get; init; } = CapStyle.Flat;
    public LineJoin LineJoin { get; init; } = LineJoin.Miter;
}