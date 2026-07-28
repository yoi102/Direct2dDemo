using Direct2dDemo.Shared.Elements;
using Direct2dDemo.Shared.Elements.DrawingElements;
using Direct2dDemo.Shared.Elements.GeometryElements;
using System.Drawing;

namespace Direct2dDemo.Direct2D;

/// <summary>
/// 为并行 tile 分桶提供保守的屏幕空间边界。
/// 边界宁可稍大，不能小于真实绘制范围，否则 tile 边缘会漏画。
/// </summary>
internal static class DrawingElementBounds
{
    private const float AntialiasPadding = 2.0f;
    private const float MiterLimit = 10.0f;

    public static bool TryGetScreenBounds(
        IDrawingElement element,
        float offsetX,
        float offsetY,
        float scale,
        out ScreenBounds bounds)
    {
        bounds = default;

        if (element is null || scale <= 0 || !float.IsFinite(scale))
            return false;

        return element switch
        {
            LineElement line => TryGetLineBounds(line, offsetX, offsetY, scale, out bounds),
            RectangleElement rectangle => TryGetRectangleBounds(
                rectangle.TopLeft,
                rectangle.Width,
                rectangle.Height,
                rectangle.StrokeWidth,
                offsetX,
                offsetY,
                scale,
                out bounds),
            RectangleGeometryElement rectangle => TryGetRectangleBounds(
                rectangle.TopLeft,
                rectangle.Width,
                rectangle.Height,
                rectangle.StrokeWidth,
                offsetX,
                offsetY,
                scale,
                out bounds),
            EllipseElement ellipse => TryGetEllipseBounds(
                ellipse.Center,
                ellipse.RadiusX,
                ellipse.RadiusY,
                ellipse.StrokeWidth,
                offsetX,
                offsetY,
                scale,
                out bounds),
            EllipseGeometryElement ellipse => TryGetEllipseBounds(
                ellipse.Center,
                ellipse.RadiusX,
                ellipse.RadiusY,
                ellipse.StrokeWidth,
                offsetX,
                offsetY,
                scale,
                out bounds),
            PolygonGeometryElement polygon => TryGetPolygonBounds(
                polygon,
                offsetX,
                offsetY,
                scale,
                out bounds),
            TextElement text => TryGetTextBounds(text, offsetX, offsetY, scale, out bounds),
            _ => false
        };
    }

    private static bool TryGetLineBounds(
        LineElement element,
        float offsetX,
        float offsetY,
        float scale,
        out ScreenBounds bounds)
    {
        var startX = Transform(element.StartPoint.X, offsetX, scale);
        var startY = Transform(element.StartPoint.Y, offsetY, scale);
        var endX = Transform(element.EndPoint.X, offsetX, scale);
        var endY = Transform(element.EndPoint.Y, offsetY, scale);
        var padding = GetStrokePadding(element.StrokeWidth, scale);

        return TryCreate(
            MathF.Min(startX, endX) - padding,
            MathF.Min(startY, endY) - padding,
            MathF.Max(startX, endX) + padding,
            MathF.Max(startY, endY) + padding,
            out bounds);
    }

    private static bool TryGetRectangleBounds(
        PointF topLeft,
        float width,
        float height,
        float strokeWidth,
        float offsetX,
        float offsetY,
        float scale,
        out ScreenBounds bounds)
    {
        var left = Transform(topLeft.X, offsetX, scale);
        var top = Transform(topLeft.Y, offsetY, scale);
        var right = left + width * scale;
        var bottom = top + height * scale;
        var padding = GetStrokePadding(strokeWidth, scale);

        return TryCreate(
            MathF.Min(left, right) - padding,
            MathF.Min(top, bottom) - padding,
            MathF.Max(left, right) + padding,
            MathF.Max(top, bottom) + padding,
            out bounds);
    }

    private static bool TryGetEllipseBounds(
        PointF center,
        float radiusX,
        float radiusY,
        float strokeWidth,
        float offsetX,
        float offsetY,
        float scale,
        out ScreenBounds bounds)
    {
        var centerX = Transform(center.X, offsetX, scale);
        var centerY = Transform(center.Y, offsetY, scale);
        var scaledRadiusX = MathF.Abs(radiusX * scale);
        var scaledRadiusY = MathF.Abs(radiusY * scale);
        var padding = GetStrokePadding(strokeWidth, scale);

        return TryCreate(
            centerX - scaledRadiusX - padding,
            centerY - scaledRadiusY - padding,
            centerX + scaledRadiusX + padding,
            centerY + scaledRadiusY + padding,
            out bounds);
    }

    private static bool TryGetPolygonBounds(
        PolygonGeometryElement element,
        float offsetX,
        float offsetY,
        float scale,
        out ScreenBounds bounds)
    {
        bounds = default;

        if (element.Points.Count == 0)
            return false;

        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;

        foreach (var point in element.Points)
        {
            var x = Transform(point.X, offsetX, scale);
            var y = Transform(point.Y, offsetY, scale);
            left = MathF.Min(left, x);
            top = MathF.Min(top, y);
            right = MathF.Max(right, x);
            bottom = MathF.Max(bottom, y);
        }

        var padding = GetStrokePadding(element.StrokeWidth, scale);
        return TryCreate(
            left - padding,
            top - padding,
            right + padding,
            bottom + padding,
            out bounds);
    }

    private static bool TryGetTextBounds(
        TextElement element,
        float offsetX,
        float offsetY,
        float scale,
        out ScreenBounds bounds)
    {
        bounds = default;

        if (string.IsNullOrEmpty(element.Text) || element.FontSize <= 0)
            return false;

        var lineCount = 1;
        var currentLineLength = 0;
        var maxLineLength = 0;

        for (var i = 0; i < element.Text.Length; i++)
        {
            var character = element.Text[i];

            if (character is '\r' or '\n')
            {
                maxLineLength = Math.Max(maxLineLength, currentLineLength);
                currentLineLength = 0;
                lineCount++;

                if (character == '\r' &&
                    i + 1 < element.Text.Length &&
                    element.Text[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            currentLineLength++;
        }

        maxLineLength = Math.Max(maxLineLength, currentLineLength);

        // 2em/字符、2em/行是有意放大的保守值，覆盖常见 CJK、拉丁字体和字形外伸。
        var width = Math.Max(1, maxLineLength) * element.FontSize * scale * 2.0f;
        var height = lineCount * element.FontSize * scale * 2.0f;
        var left = Transform(element.Position.X, offsetX, scale);
        var top = Transform(element.Position.Y, offsetY, scale);

        return TryCreate(
            left - AntialiasPadding,
            top - AntialiasPadding,
            left + width + AntialiasPadding,
            top + height + AntialiasPadding,
            out bounds);
    }

    private static float Transform(float value, float offset, float scale)
    {
        return value * scale + offset;
    }

    private static float GetStrokePadding(float strokeWidth, float scale)
    {
        // Miter 连接理论上可伸出到 MiterLimit * 半线宽。
        return MathF.Max(
            AntialiasPadding,
            MathF.Abs(strokeWidth * scale) * MiterLimit * 0.5f + AntialiasPadding);
    }

    private static bool TryCreate(
        float left,
        float top,
        float right,
        float bottom,
        out ScreenBounds bounds)
    {
        bounds = default;

        if (!float.IsFinite(left) ||
            !float.IsFinite(top) ||
            !float.IsFinite(right) ||
            !float.IsFinite(bottom))
        {
            return false;
        }

        bounds = new ScreenBounds(left, top, right, bottom);
        return true;
    }
}

internal readonly record struct ScreenBounds(
    float Left,
    float Top,
    float Right,
    float Bottom)
{
    public bool Intersects(int x, int y, int width, int height)
    {
        return Right > x &&
               Bottom > y &&
               Left < x + width &&
               Top < y + height;
    }
}
