using Direct2dDemo.Shared;
using System.Drawing;
using System.Runtime.InteropServices;
using static Vanara.PInvoke.D2d1;
using static Vanara.PInvoke.Dwrite;
using static Vanara.PInvoke.DXGI;
using D2D1_COLOR_F = Vanara.PInvoke.DXGI.D3DCOLORVALUE;

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
                Draw(textElement, direct2DWrapper.Context, direct2DWrapper.DwriteFactory);
                break;

            case PolygonElement polygonElement:
                Draw(polygonElement, direct2DWrapper.Context);
                break;

            case EllipseElement ellipseElement:
                Draw(ellipseElement, direct2DWrapper.Context);
                break;
        }
    }

    public static void Draw(TextElement element, ID2D1DeviceContext7 context, IDWriteFactory factory)
    {
        if (string.IsNullOrEmpty(element.Text))
            return;

        if (element.FontSize <= 0)
            return;

        if (element.Color.A <= 0)
            return;

        IDWriteTextFormat? textFormat = null;
        ID2D1SolidColorBrush? brush = null;

        try
        {
            var fontFamily = string.IsNullOrWhiteSpace(element.FontFamily)
                ? "Meiryo"
                : element.FontFamily;

            //not good
            textFormat = factory.CreateTextFormat(
                fontFamily,
                null,
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
                DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                element.FontSize,
                "en-us"
            );

            textFormat.SetTextAlignment(
                DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING
            );

            textFormat.SetParagraphAlignment(
                DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR
            );

            brush = CreateSolidColorBrush(context, element.Color);

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
            SafeRelease(ref brush);
            SafeRelease(ref textFormat);
        }
    }

    public static void Draw(PolygonElement element, ID2D1DeviceContext7 context)
    {
        if (element.Points.Count < 3)
            return;

        ID2D1PathGeometry? geometry = null;
        ID2D1SolidColorBrush? fillBrush = null;
        ID2D1SolidColorBrush? strokeBrush = null;

        try
        {
            geometry = CreateClosedPathGeometry(context, element.Points);

            if (element.IsFilled && element.FillColor.A > 0)
            {
                fillBrush = CreateSolidColorBrush(context, element.FillColor);
                context.FillGeometry(geometry, fillBrush);
            }

            if (element.HasStroke && element.StrokeWidth > 0 && element.StrokeColor.A > 0)
            {
                strokeBrush = CreateSolidColorBrush(context, element.StrokeColor);
                context.DrawGeometry(geometry, strokeBrush, element.StrokeWidth);
            }
        }
        finally
        {
            SafeRelease(ref strokeBrush);
            SafeRelease(ref fillBrush);
            SafeRelease(ref geometry);
        }
    }

    public static void Draw(EllipseElement element, ID2D1DeviceContext7 context)
    {
        if (element.RadiusX <= 0 || element.RadiusY <= 0)
            return;

        var ellipse = new D2D1_ELLIPSE
        {
            point = element.Center,
            radiusX = element.RadiusX,
            radiusY = element.RadiusY
        };

        ID2D1SolidColorBrush? fillBrush = null;
        ID2D1SolidColorBrush? strokeBrush = null;

        try
        {
            if (element.IsFilled && element.FillColor.A > 0)
            {
                fillBrush = CreateSolidColorBrush(context, element.FillColor);
                context.FillEllipse(ellipse, fillBrush);
            }

            if (element.HasStroke && element.StrokeWidth > 0 && element.StrokeColor.A > 0)
            {
                strokeBrush = CreateSolidColorBrush(context, element.StrokeColor);
                context.DrawEllipse(ellipse, strokeBrush, element.StrokeWidth);
            }
        }
        finally
        {
            SafeRelease(ref strokeBrush);
            SafeRelease(ref fillBrush);
        }
    }

    private static ID2D1PathGeometry CreateClosedPathGeometry(
        ID2D1DeviceContext7 context,
        IReadOnlyList<PointF> points)
    {
        ID2D1Factory? factory = null;
        ID2D1PathGeometry? geometry = null;
        ID2D1GeometrySink? sink = null;

        try
        {
            context.GetFactory(out factory);

            geometry = factory.CreatePathGeometry();
            sink = geometry.Open();

            sink.SetFillMode(D2D1_FILL_MODE.D2D1_FILL_MODE_WINDING);

            sink.BeginFigure(
                points[0],
                D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);

            for (var i = 1; i < points.Count; i++)
            {
                sink.AddLine(points[i]);
            }

            sink.EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
            sink.Close().ThrowIfFailed();

            var result = geometry;
            geometry = null;
            return result;
        }
        finally
        {
            SafeRelease(ref sink);
            SafeRelease(ref geometry);
            SafeRelease(ref factory);
        }
    }

    public static D2D1_COLOR_F ToD2DColor(Color color)
    {
        return new D2D1_COLOR_F
        {
            r = color.R / 255.0f,
            g = color.G / 255.0f,
            b = color.B / 255.0f,
            a = color.A / 255.0f
        };
    }

    private static ID2D1SolidColorBrush CreateSolidColorBrush(
        ID2D1DeviceContext7 context,
        Color color)
    {
        return context.CreateSolidColorBrush(ToD2DColor(color));
    }

    private static void SafeRelease<T>(ref T? comObject) where T : class
    {
        var obj = comObject;
        comObject = null;

        if (obj == null)
            return;

        try
        {
            if (Marshal.IsComObject(obj))
            {
                Marshal.ReleaseComObject(obj);
            }
        }
        catch (InvalidComObjectException)
        {
            // 已经释放过，清理阶段可以忽略。
        }
    }
}