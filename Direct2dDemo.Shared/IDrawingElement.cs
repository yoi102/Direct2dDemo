using System.Drawing;

namespace Direct2dDemo.Shared;

public interface IDrawingElement
{
}

public abstract class ShapeElement : IDrawingElement
{
    public Color StrokeColor { get; init; } = Color.Black;

    public Color FillColor { get; init; } = Color.Transparent;

    public float StrokeWidth { get; init; } = 1.0f;

    public bool HasStroke { get; init; } = true;

    public bool IsFilled { get; init; } = false;
}

public sealed class TextElement : IDrawingElement
{
    public string Text { get; init; } = string.Empty;

    public string FontFamily { get; init; } = string.Empty;

    public PointF Position { get; init; }

    public Color Color { get; init; } = Color.Black;

    public float FontSize { get; init; } = 12.0f;
}

public sealed class EllipseElement : ShapeElement
{
    public PointF Center { get; init; }

    public float RadiusX { get; init; }

    public float RadiusY { get; init; }
}

public sealed class PolygonElement : ShapeElement
{
    public IReadOnlyList<PointF> Points { get; init; } = Array.Empty<PointF>();
}