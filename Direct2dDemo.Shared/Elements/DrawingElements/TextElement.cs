using System.Drawing;

namespace Direct2dDemo.Shared.Elements.DrawingElements;

public sealed record TextElement : IDirectDrawElement
{
    public string Text { get; init; } = string.Empty;

    public string FontFamily { get; init; } = string.Empty;

    public PointF Position { get; init; }

    public Color Color { get; init; } = Color.Black;

    public float FontSize { get; init; } = 12.0f;
}