using Direct2dDemo.Shared.Elements;
using Direct2dDemo.Shared.Elements.DrawingElements;
using Direct2dDemo.Shared.Elements.GeometryElements;
using Direct2dDemo.Shared.Enums;
using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dDemo.Direct2D;

// ※DrawLine：CapStyle.Round 大量绘制时可能明显变慢。
internal static class DrawExtension
{
    public static void Draw(this IDrawingElement element, Direct2DWrapper direct2DWrapper, float offsetX, float offsetY, float scale)
    {
        if (direct2DWrapper.Context == null)
            return;

        if (scale <= 0)
            return;

        switch (element)
        {
            case PolygonGeometryElement polygonElement:
                Draw(polygonElement, direct2DWrapper, offsetX, offsetY, scale);
                break;

            case EllipseElement ellipseElement:
                Draw(ellipseElement, direct2DWrapper, offsetX, offsetY, scale);
                break;

            case RectangleElement rectangleElement:
                Draw(rectangleElement, direct2DWrapper, offsetX, offsetY, scale);
                break;

            case RectangleGeometryElement rectangleGeometryElement:
                Draw(rectangleGeometryElement, direct2DWrapper, offsetX, offsetY, scale);
                break;

            case EllipseGeometryElement ellipseGeometryElement:
                Draw(ellipseGeometryElement, direct2DWrapper, offsetX, offsetY, scale);
                break;

            case TextElement textElement:
                Draw(textElement, direct2DWrapper, offsetX, offsetY, scale);
                break;

            case LineElement lineElement:
                Draw(lineElement, direct2DWrapper, offsetX, offsetY, scale);
                break;
        }
    }

    public static void Draw(LineElement element, Direct2DWrapper direct2DWrapper)
    {
        Draw(element, direct2DWrapper, 0.0f, 0.0f, 1.0f);
    }

    public static void Draw(LineElement element, Direct2DWrapper direct2DWrapper, float offsetX, float offsetY, float scale)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (scale <= 0)
            return;

        if (element.StrokeWidth <= 0)
            return;

        if (element.StrokeColor.A <= 0)
            return;

        var strokeBrush = direct2DWrapper.GetOrCreateSolidColorBrush(element.StrokeColor);
        var strokeStyle = direct2DWrapper.GetOrCreateStrokeStyle(
            element.CapStyle,
            element.DashStyle,
            element.LineJoin);

        context.DrawLine(
            ToD2DPoint(element.StartPoint, offsetX, offsetY, scale),
            ToD2DPoint(element.EndPoint, offsetX, offsetY, scale),
            strokeBrush,
            ScaleLength(element.StrokeWidth, scale),
            strokeStyle);
    }

    public static void Draw(RectangleGeometryElement element, Direct2DWrapper direct2DWrapper)
    {
        Draw(element, direct2DWrapper, 0.0f, 0.0f, 1.0f);
    }

    public static void Draw(RectangleGeometryElement element, Direct2DWrapper direct2DWrapper, float offsetX, float offsetY, float scale)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (scale <= 0)
            return;

        if (element.Width <= 0 || element.Height <= 0)
            return;

        var geometry = direct2DWrapper.GetOrCreateRectangleGeometry(element);
        DrawGeometryWithViewTransform(
            geometry,
            element.FillStyle,
            element.FillColor,
            element.StrokeColor,
            element.HatchStyle,
            element.StrokeWidth,
            element.CapStyle,
            element.DashStyle,
            element.LineJoin,
            direct2DWrapper,
            offsetX,
            offsetY,
            scale);
    }

    public static void Draw(EllipseGeometryElement element, Direct2DWrapper direct2DWrapper)
    {
        Draw(element, direct2DWrapper, 0.0f, 0.0f, 1.0f);
    }

    public static void Draw(EllipseGeometryElement element, Direct2DWrapper direct2DWrapper, float offsetX, float offsetY, float scale)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (scale <= 0)
            return;

        if (element.RadiusX <= 0 || element.RadiusY <= 0)
            return;

        var geometry = direct2DWrapper.GetOrCreateEllipseGeometryElement(element);
        DrawGeometryWithViewTransform(
            geometry,
            element.FillStyle,
            element.FillColor,
            element.StrokeColor,
            element.HatchStyle,
            element.StrokeWidth,
            element.CapStyle,
            element.DashStyle,
            element.LineJoin,
            direct2DWrapper,
            offsetX,
            offsetY,
            scale);
    }

    public static void Draw(TextElement element, Direct2DWrapper direct2DWrapper)
    {
        Draw(element, direct2DWrapper, 0.0f, 0.0f, 1.0f);
    }

    public static void Draw(TextElement element, Direct2DWrapper direct2DWrapper, float offsetX, float offsetY, float scale)
    {
        if (string.IsNullOrEmpty(element.Text))
            return;

        if (scale <= 0)
            return;

        if (element.FontSize <= 0)
            return;

        if (element.Color.A <= 0)
            return;

        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (direct2DWrapper.DwriteFactory == null)
            return;

        var scaledFontSize = ScaleLength(element.FontSize, scale);
        if (scaledFontSize <= 0)
            return;

        var fontFamily = string.IsNullOrWhiteSpace(element.FontFamily)
            ? "Meiryo"
            : element.FontFamily;

        var textFormat = direct2DWrapper.GetOrCreateTextFormat(
            fontFamily,
            scaledFontSize);

        var brush = direct2DWrapper.GetOrCreateSolidColorBrush(element.Color);

        var position = TransformPoint(element.Position, offsetX, offsetY, scale);
        var rect = new Rect
        {
            Left = position.X,
            Top = position.Y,
            Width = ScaleLength(10000.0f, scale),
            Height = ScaleLength(10000.0f, scale)
        };

        context.DrawText(
            element.Text,
            textFormat,
            rect,
            brush);
    }

    public static void Draw(PolygonGeometryElement element, Direct2DWrapper direct2DWrapper)
    {
        Draw(element, direct2DWrapper, 0.0f, 0.0f, 1.0f);
    }

    public static void Draw(PolygonGeometryElement element, Direct2DWrapper direct2DWrapper, float offsetX, float offsetY, float scale)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (scale <= 0)
            return;

        if (element.Points.Count < 3)
            return;

        var geometry = direct2DWrapper.GetOrCreatePolygonGeometry(element);
        DrawGeometryWithViewTransform(
            geometry,
            element.FillStyle,
            element.FillColor,
            element.StrokeColor,
            element.HatchStyle,
            element.StrokeWidth,
            element.CapStyle,
            element.DashStyle,
            element.LineJoin,
            direct2DWrapper,
            offsetX,
            offsetY,
            scale);
    }

    public static void Draw(EllipseElement element, Direct2DWrapper direct2DWrapper)
    {
        Draw(element, direct2DWrapper, 0.0f, 0.0f, 1.0f);
    }

    public static void Draw(EllipseElement element, Direct2DWrapper direct2DWrapper, float offsetX, float offsetY, float scale)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (scale <= 0)
            return;

        if (element.RadiusX <= 0 || element.RadiusY <= 0)
            return;

        var ellipse = new Ellipse(
             ToD2DPoint(element.Center, offsetX, offsetY, scale),
             ScaleLength(element.RadiusX, scale),
             ScaleLength(element.RadiusY, scale)
        );

        FillEllipse(
            ellipse,
            element.FillStyle,
            element.FillColor,
            element.StrokeColor,
            element.HatchStyle,
            direct2DWrapper);

        if (element.StrokeWidth > 0 && element.StrokeColor.A > 0)
        {
            var strokeBrush = direct2DWrapper.GetOrCreateSolidColorBrush(element.StrokeColor);
            var strokeStyle = direct2DWrapper.GetOrCreateStrokeStyle(
                element.CapStyle,
                element.DashStyle,
                element.LineJoin);

            context.DrawEllipse(
                ellipse,
                strokeBrush,
                ScaleLength(element.StrokeWidth, scale),
                strokeStyle);
        }
    }

    public static void Draw(RectangleElement element, Direct2DWrapper direct2DWrapper)
    {
        Draw(element, direct2DWrapper, 0.0f, 0.0f, 1.0f);
    }

    public static void Draw(RectangleElement element, Direct2DWrapper direct2DWrapper, float offsetX, float offsetY, float scale)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (scale <= 0)
            return;

        if (element.Width <= 0 || element.Height <= 0)
            return;

        var rectangle = ToD2DRect(
            element.TopLeft,
            element.Width,
            element.Height,
            offsetX,
            offsetY,
            scale);

        FillRectangle(
            rectangle,
            element.FillStyle,
            element.FillColor,
            element.StrokeColor,
            element.HatchStyle,
            direct2DWrapper);

        if (element.StrokeWidth > 0 && element.StrokeColor.A > 0)
        {
            var strokeBrush = direct2DWrapper.GetOrCreateSolidColorBrush(element.StrokeColor);
            var strokeStyle = direct2DWrapper.GetOrCreateStrokeStyle(
                element.CapStyle,
                element.DashStyle,
                element.LineJoin);

            context.DrawRectangle(
                rectangle,
                strokeBrush,
                ScaleLength(element.StrokeWidth, scale),
                strokeStyle);
        }
    }

    private static void DrawGeometryWithViewTransform(
        ID2D1Geometry geometry,
        FillStyle fillStyle,
        System.Drawing.Color fillColor,
        System.Drawing.Color strokeColor,
        HatchStyle? hatchStyle,
        float strokeWidth,
        Shared.Enums.CapStyle capStyle,
        Shared.Enums.DashStyle dashStyle,
        Shared.Enums.LineJoin lineJoin,
        Direct2DWrapper direct2DWrapper,
        float offsetX,
        float offsetY,
        float scale)
    {
        ID2D1TransformedGeometry? transformedGeometry = null;

        try
        {
            var renderGeometry = geometry;

            if (!IsIdentityView(offsetX, offsetY, scale))
            {
                transformedGeometry = direct2DWrapper.CreateTransformedGeometry(
                    geometry,
                    CreateViewTransform(offsetX, offsetY, scale));

                renderGeometry = transformedGeometry;
            }

            FillGeometry(
                renderGeometry,
                fillStyle,
                fillColor,
                strokeColor,
                hatchStyle,
                direct2DWrapper);

            DrawGeometryStroke(
                renderGeometry,
                strokeColor,
                ScaleLength(strokeWidth, scale),
                capStyle,
                dashStyle,
                lineJoin,
                direct2DWrapper);
        }
        finally
        {
            transformedGeometry?.Dispose();
        }
    }

    private static void FillGeometry(
        ID2D1Geometry geometry,
        FillStyle fillStyle,
        System.Drawing.Color fillColor,
        System.Drawing.Color hatchColor,
        HatchStyle? hatchStyle,
        Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        switch (fillStyle)
        {
            case FillStyle.None:
                return;

            case FillStyle.Solid:
                {
                    if (fillColor.A <= 0)
                        return;

                    var fillBrush = direct2DWrapper.GetOrCreateSolidColorBrush(fillColor);
                    context.FillGeometry(geometry, fillBrush);
                    return;
                }

            case FillStyle.Hatch:
                {
                    if (hatchStyle == null)
                        return;

                    if (fillColor.A <= 0 && hatchColor.A <= 0)
                        return;

                    var hatchBrush = direct2DWrapper.GetOrCreateHatchStyle(
                        hatchStyle.Value,
                        hatchColor,
                        fillColor);

                    context.FillGeometry(geometry, hatchBrush);
                    return;
                }
        }
    }

    private static void FillRectangle(
        Rect rectangle,
        FillStyle fillStyle,
        System.Drawing.Color fillColor,
        System.Drawing.Color hatchColor,
        HatchStyle? hatchStyle,
        Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        switch (fillStyle)
        {
            case FillStyle.None:
                return;

            case FillStyle.Solid:
                {
                    if (fillColor.A <= 0)
                        return;

                    var fillBrush = direct2DWrapper.GetOrCreateSolidColorBrush(fillColor);
                    context.FillRectangle(rectangle, fillBrush);
                    return;
                }

            case FillStyle.Hatch:
                {
                    if (hatchStyle == null)
                        return;

                    if (fillColor.A <= 0 && hatchColor.A <= 0)
                        return;

                    var hatchBrush = direct2DWrapper.GetOrCreateHatchStyle(
                        hatchStyle.Value,
                        hatchColor,
                        fillColor);

                    context.FillRectangle(rectangle, hatchBrush);
                    return;
                }
        }
    }

    private static void FillEllipse(
        Ellipse ellipse,
        FillStyle fillStyle,
        System.Drawing.Color fillColor,
        System.Drawing.Color hatchColor,
        HatchStyle? hatchStyle,
        Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        switch (fillStyle)
        {
            case FillStyle.None:
                return;

            case FillStyle.Solid:
                {
                    if (fillColor.A <= 0)
                        return;

                    var fillBrush = direct2DWrapper.GetOrCreateSolidColorBrush(fillColor);
                    context.FillEllipse(ellipse, fillBrush);
                    return;
                }

            case FillStyle.Hatch:
                {
                    if (hatchStyle == null)
                        return;

                    if (fillColor.A <= 0 && hatchColor.A <= 0)
                        return;

                    var hatchBrush = direct2DWrapper.GetOrCreateHatchStyle(
                        hatchStyle.Value,
                        hatchColor,
                        fillColor);

                    context.FillEllipse(ellipse, hatchBrush);
                    return;
                }
        }
    }

    private static void DrawGeometryStroke(
        ID2D1Geometry geometry,
        System.Drawing.Color strokeColor,
        float strokeWidth,
        Shared.Enums.CapStyle capStyle,
        Shared.Enums.DashStyle dashStyle,
        Shared.Enums.LineJoin lineJoin,
        Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (strokeWidth <= 0)
            return;

        if (strokeColor.A <= 0)
            return;

        var strokeBrush = direct2DWrapper.GetOrCreateSolidColorBrush(strokeColor);
        var strokeStyle = direct2DWrapper.GetOrCreateStrokeStyle(
            capStyle,
            dashStyle,
            lineJoin);

        context.DrawGeometry(
            geometry,
            strokeBrush,
            strokeWidth,
            strokeStyle);
    }

    private static PointF TransformPoint(PointF point, float offsetX, float offsetY, float scale)
    {
        return new PointF(
            point.X * scale + offsetX,
            point.Y * scale + offsetY);
    }

    private static Vector2 ToD2DPoint(PointF point, float offsetX, float offsetY, float scale)
    {
        var transformed = TransformPoint(point, offsetX, offsetY, scale);

        return new Vector2
        {
            X = transformed.X,
            Y = transformed.Y
        };
    }

    private static Rect ToD2DRect(PointF topLeft, float width, float height, float offsetX, float offsetY, float scale)
    {
        var leftTop = TransformPoint(topLeft, offsetX, offsetY, scale);

        return new Rect
        {
            X = leftTop.X,
            Y = leftTop.Y,
            Width = ScaleLength(width, scale),
            Height = ScaleLength(height, scale)
        };
    }

    private static float ScaleLength(float value, float scale)
    {
        return value * scale;
    }

    private static bool IsIdentityView(float offsetX, float offsetY, float scale)
    {
        return Math.Abs(offsetX) <= 0.000001f &&
               Math.Abs(offsetY) <= 0.000001f &&
               Math.Abs(scale - 1.0f) <= 0.000001f;
    }

    private static Matrix3x2 CreateViewTransform(float offsetX, float offsetY, float scale)
    {
        return new Matrix3x2(
            scale, 0.0f,
            0.0f, scale,
            offsetX, offsetY);
    }
}