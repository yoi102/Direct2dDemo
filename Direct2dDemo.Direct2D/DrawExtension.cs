using Direct2dDemo.Shared.Elements;
using Direct2dDemo.Shared.Elements.DrawingElements;
using Direct2dDemo.Shared.Elements.GeometryElements;
using Direct2dDemo.Shared.Enums;
using System.Drawing;
using System.Xml.Linq;
using static Vanara.PInvoke.D2d1;
using static Vanara.PInvoke.DXGI;

namespace Direct2dDemo.Direct2D;

//※DrawLine！！！！！当CapStyle为CapStyle.Round 时、绘制会非常慢 。比GDI绘制还要慢！！！！

internal static class DrawExtension
{
    public static void Draw(this IDrawingElement element, Direct2DWrapper direct2DWrapper)
    {
        if (direct2DWrapper.Context == null)
            return;

        switch (element)
        {
            case PolygonGeometryElement polygonElement:
                Draw(polygonElement, direct2DWrapper);
                break;

            case EllipseElement ellipseElement:
                Draw(ellipseElement, direct2DWrapper);
                break;

            case RectangleElement rectangleElement:
                Draw(rectangleElement, direct2DWrapper);
                break;

            case RectangleGeometryElement rectangleGeometryElement:
                Draw(rectangleGeometryElement, direct2DWrapper);
                break;

            case EllipseGeometryElement ellipseGeometryElement:
                Draw(ellipseGeometryElement, direct2DWrapper);
                break;

            case TextElement textElement:
                Draw(textElement, direct2DWrapper);
                break;

            case LineElement lineElement:
                Draw(lineElement, direct2DWrapper);
                break;
        }
    }

    public static void Draw(LineElement element, Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
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
            ToD2DPoint(element.StartPoint),
            ToD2DPoint(element.EndPoint),
            strokeBrush,
            element.StrokeWidth,
            strokeStyle);
    }

    public static void Draw(RectangleGeometryElement element, Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (element.Width <= 0 || element.Height <= 0)
            return;

        var geometry = direct2DWrapper.GetOrCreateRectangleGeometry(element);

        FillGeometry(
            geometry,
            element.FillStyle,
            element.FillColor,
            element.StrokeColor,
            element.HatchStyle,
            direct2DWrapper);

        DrawGeometryStroke(
            geometry,
            element.StrokeColor,
            element.StrokeWidth,
            element.CapStyle,
            element.DashStyle,
            element.LineJoin,
            direct2DWrapper);
    }

    public static void Draw(EllipseGeometryElement element, Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (element.RadiusX <= 0 || element.RadiusY <= 0)
            return;

        var geometry = direct2DWrapper.GetOrCreateEllipseGeometryElement(element);

        FillGeometry(
            geometry,
            element.FillStyle,
            element.FillColor,
            element.StrokeColor,
            element.HatchStyle,
            direct2DWrapper);

        DrawGeometryStroke(
            geometry,
            element.StrokeColor,
            element.StrokeWidth,
            element.CapStyle,
            element.DashStyle,
            element.LineJoin,
            direct2DWrapper);
    }

    public static void Draw(TextElement element, Direct2DWrapper direct2DWrapper)
    {
        if (string.IsNullOrEmpty(element.Text))
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

        var fontFamily = string.IsNullOrWhiteSpace(element.FontFamily)
            ? "Meiryo"
            : element.FontFamily;

        var textFormat = direct2DWrapper.GetOrCreateTextFormat(
            fontFamily,
            element.FontSize);

        var brush = direct2DWrapper.GetOrCreateSolidColorBrush(element.Color);

        var rect = new D2D_RECT_F
        {
            left = element.Position.X,
            top = element.Position.Y,

            // TextElement 现在没有 Width / Height，
            // 所以这里先给一个足够大的默认区域。
            right = element.Position.X + 10000.0f,
            bottom = element.Position.Y + 10000.0f
        };

        context.DrawText(
            element.Text,
            (uint)element.Text.Length,
            textFormat,
            rect,
            brush,
            D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE,
            DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL);
    }

    public static void Draw(PolygonGeometryElement element, Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (element.Points.Count < 3)
            return;

        var geometry = direct2DWrapper.GetOrCreatePolygonGeometry(element);

        FillGeometry(
            geometry,
            element.FillStyle,
            element.FillColor,
            element.StrokeColor,
            element.HatchStyle,
            direct2DWrapper);

        DrawGeometryStroke(
            geometry,
            element.StrokeColor,
            element.StrokeWidth,
            element.CapStyle,
            element.DashStyle,
            element.LineJoin,
            direct2DWrapper);
    }

    public static void Draw(EllipseElement element, Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (element.RadiusX <= 0 || element.RadiusY <= 0)
            return;

        var ellipse = new D2D1_ELLIPSE
        {
            point = ToD2DPoint(element.Center),
            radiusX = element.RadiusX,
            radiusY = element.RadiusY
        };

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
            var strokeStyle = direct2DWrapper.GetOrCreateStrokeStyle(element.CapStyle,
                                                                       element.DashStyle,
                                                                       element.LineJoin);
            context.DrawEllipse(ellipse, strokeBrush, element.StrokeWidth, strokeStyle);
        }
    }

    public static void Draw(RectangleElement element, Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null)
            return;

        if (element.Width <= 0 || element.Height <= 0)
            return;

        var rectangle = new D2D_RECT_F
        {
            left = element.TopLeft.X,
            top = element.TopLeft.Y,
            right = element.TopLeft.X + element.Width,
            bottom = element.TopLeft.Y + element.Height
        };

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
            var strokeStyle = direct2DWrapper.GetOrCreateStrokeStyle(element.CapStyle,
                                                                                   element.DashStyle,
                                                                                   element.LineJoin);
            context.DrawRectangle(rectangle, strokeBrush, element.StrokeWidth, strokeStyle);
        }
    }

    private static void FillGeometry(
        ID2D1Geometry geometry,
        FillStyle fillStyle,
        Color fillColor,
        Color hatchColor,
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
        D2D_RECT_F rectangle,
        FillStyle fillStyle,
        Color fillColor,
        Color hatchColor,
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
        D2D1_ELLIPSE ellipse,
        FillStyle fillStyle,
        Color fillColor,
        Color hatchColor,
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
        Color strokeColor,
        float strokeWidth,
        CapStyle capStyle,
        DashStyle dashStyle,
        LineJoin lineJoin,
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

        context.DrawGeometry(geometry, strokeBrush, strokeWidth,strokeStyle);
    }

    private static D2D_POINT_2F ToD2DPoint(PointF point)
    {
        return new D2D_POINT_2F
        {
            x = point.X,
            y = point.Y
        };
    }
}