using Direct2dDemo.Shared.Elements;
using Direct2dDemo.Shared.Elements.DrawingElements;
using Direct2dDemo.Shared.Elements.GeometryElements;
using Direct2dDemo.Shared.Enums;
using static Vanara.PInvoke.D2d1;
using static Vanara.PInvoke.DXGI;

namespace Direct2dDemo.Direct2D;

//Resource!!!!!!
//CreateSolidColorBrush
//CreatePathGeometry
//CreateTextFormat
//各10_000 有cache 时，372ms  ,无cache时，441ms

internal static class DrawExtension
{
    public static void Draw(this IDrawingElement element, Direct2DWrapper direct2DWrapper)
    {
        if (direct2DWrapper.Context == null) return;
        if (direct2DWrapper.DwriteFactory == null) return;

        switch (element)
        {
            case TextElement textElement:
                Draw(textElement, direct2DWrapper);
                break;

            case PolygonGeometryElement polygonElement:
                Draw(polygonElement, direct2DWrapper);
                break;

            case EllipseElement ellipseElement:
                Draw(ellipseElement, direct2DWrapper);
                break;
        }
    }

    public static void Draw(TextElement element, Direct2DWrapper direct2DWrapper)
    {
        if (string.IsNullOrEmpty(element.Text))
            return;

        if (element.FontSize <= 0)
            return;

        if (element.Color.A <= 0)
            return;

        try
        {
            var context = direct2DWrapper.Context;
            if (context == null) return;
            var factory = direct2DWrapper.DwriteFactory;
            if (factory is null) return;

            var fontFamily = string.IsNullOrWhiteSpace(element.FontFamily)
                ? "Meiryo"
                : element.FontFamily;

            //not good
            var textFormat = direct2DWrapper.GetOrCreateTextFormat(fontFamily, element.FontSize);
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
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL
            );
        }
        finally
        {
        }
    }

    public static void Draw(PolygonGeometryElement element, Direct2DWrapper direct2DWrapper)
    {
        if (element.Points.Count < 3)
            return;
        var context = direct2DWrapper.Context;
        if (context == null) return;

        try
        {
            var geometry = direct2DWrapper.GetOrCreatePathGeometry(element);

            if (element.FillStyle == FillStyle.Solid && element.FillColor.A > 0)
            {
                var fillBrush = direct2DWrapper.GetOrCreateSolidColorBrush(element.FillColor);
                context.FillGeometry(geometry, fillBrush);
            }

            if ( element.StrokeWidth > 0 && element.StrokeColor.A > 0)
            {
                var strokeBrush = direct2DWrapper.GetOrCreateSolidColorBrush(element.StrokeColor);
                context.DrawGeometry(geometry, strokeBrush, element.StrokeWidth);
            }
        }
        finally
        {
        }
    }

    public static void Draw(EllipseElement element, Direct2DWrapper direct2DWrapper)
    {
        var context = direct2DWrapper.Context;
        if (context == null) return;

        if (element.RadiusX <= 0 || element.RadiusY <= 0)
            return;

        var ellipse = new D2D1_ELLIPSE
        {
            point = element.Center,
            radiusX = element.RadiusX,
            radiusY = element.RadiusY
        };

        try
        {
            if (element.FillStyle == Shared.Enums.FillStyle.Solid && element.FillColor.A > 0)
            {
                var fillBrush = direct2DWrapper.GetOrCreateSolidColorBrush(element.FillColor);
                context.FillEllipse(ellipse, fillBrush);
            }

            if ( element.StrokeWidth > 0 && element.StrokeColor.A > 0)
            {
                var strokeBrush = direct2DWrapper.GetOrCreateSolidColorBrush(element.StrokeColor);
                context.DrawEllipse(ellipse, strokeBrush, element.StrokeWidth);
            }
        }
        finally
        {
        }
    }
}