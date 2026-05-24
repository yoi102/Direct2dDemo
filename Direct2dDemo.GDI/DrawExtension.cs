using Direct2dDemo.Shared;
using Vanara.PInvoke;
using static Vanara.PInvoke.Gdi32;

namespace Direct2dDemo.GDI;

internal static class DrawExtension
{
    public static void Draw(this IDrawingElement element, GdiWrapper gdiWrapper)
    {
        switch (element)
        {
            case TextElement textElement:
                Draw(textElement, gdiWrapper);
                break;

            case PolygonElement polygonElement:
                Draw(polygonElement, gdiWrapper);
                break;

            case EllipseElement ellipseElement:
                Draw(ellipseElement, gdiWrapper);
                break;
        }
    }

    public static void Draw(TextElement element, GdiWrapper gdiWrapper)
    {
        if (string.IsNullOrEmpty(element.Text))
            return;

        if (element.FontSize <= 0)
            return;

        if (element.Color.A <= 0)
            return;

        var hdc = gdiWrapper.Hdc;
        var font = gdiWrapper.GetOrCreateFont(element.FontFamily, element.FontSize);

        var oldFont = Gdi32.SelectObject(hdc, font);
        var oldBkMode = Gdi32.SetBkMode(hdc, BackgroundMode.TRANSPARENT);
        var oldTextColor = Gdi32.SetTextColor(hdc, GdiWrapper.ToColorRef(element.Color));

        try
        {
            Gdi32.TextOut(
                hdc,
                ToInt(element.Position.X),
                ToInt(element.Position.Y),
                element.Text,
                element.Text.Length);
        }
        finally
        {
            if (oldFont != nint.Zero)
                Gdi32.SelectObject(hdc, oldFont);

            if (oldBkMode != 0)
                Gdi32.SetBkMode(hdc, oldBkMode);

            //if (oldTextColor != Gdi32.CLR_INVALID)
            Gdi32.SetTextColor(hdc, oldTextColor);
        }
    }

    public static void Draw(PolygonElement element, GdiWrapper gdiWrapper)
    {
        if (element.Points.Count < 3)
            return;

        var hasFill = element.IsFilled && element.FillColor.A > 0;
        var hasStroke = element.HasStroke && element.StrokeWidth > 0 && element.StrokeColor.A > 0;

        if (!hasFill && !hasStroke)
            return;

        var hdc = gdiWrapper.Hdc;

        var brush = hasFill
            ? gdiWrapper.GetOrCreateSolidBrush(element.FillColor)
            : Gdi32.GetStockObject(StockObjectType.HOLLOW_BRUSH);

        var pen = hasStroke
            ? gdiWrapper.GetOrCreatePen(element.StrokeColor, element.StrokeWidth)
            : Gdi32.GetStockObject(StockObjectType.NULL_PEN);

        var points = new POINT[element.Points.Count];

        for (var i = 0; i < element.Points.Count; i++)
        {
            var point = element.Points[i];
            points[i] = new POINT(
                ToInt(point.X),
                ToInt(point.Y));
        }

        var oldBrush = Gdi32.SelectObject(hdc, brush);
        var oldPen = Gdi32.SelectObject(hdc, pen);

        try
        {
            Gdi32.Polygon(hdc, points, points.Length);
        }
        finally
        {
            if (oldBrush != nint.Zero)
                Gdi32.SelectObject(hdc, oldBrush);

            if (oldPen != nint.Zero)
                Gdi32.SelectObject(hdc, oldPen);
        }
    }

    public static void Draw(EllipseElement element, GdiWrapper gdiWrapper)
    {
        if (element.RadiusX <= 0 || element.RadiusY <= 0)
            return;

        var hasFill = element.IsFilled && element.FillColor.A > 0;
        var hasStroke = element.HasStroke && element.StrokeWidth > 0 && element.StrokeColor.A > 0;

        if (!hasFill && !hasStroke)
            return;

        var hdc = gdiWrapper.Hdc;

        var brush = hasFill
            ? gdiWrapper.GetOrCreateSolidBrush(element.FillColor)
            : Gdi32.GetStockObject(StockObjectType.HOLLOW_BRUSH);

        var pen = hasStroke
            ? gdiWrapper.GetOrCreatePen(element.StrokeColor, element.StrokeWidth)
            : Gdi32.GetStockObject(StockObjectType.NULL_PEN);

        var left = ToInt(element.Center.X - element.RadiusX);
        var top = ToInt(element.Center.Y - element.RadiusY);
        var right = ToInt(element.Center.X + element.RadiusX);
        var bottom = ToInt(element.Center.Y + element.RadiusY);

        var oldBrush = Gdi32.SelectObject(hdc, brush);
        var oldPen = Gdi32.SelectObject(hdc, pen);

        try
        {
            Gdi32.Ellipse(hdc, left, top, right, bottom);
        }
        finally
        {
            if (oldBrush != nint.Zero)
                Gdi32.SelectObject(hdc, oldBrush);

            if (oldPen != nint.Zero)
                Gdi32.SelectObject(hdc, oldPen);
        }
    }

    private static int ToInt(float value)
    {
        return (int)Math.Round(value);
    }
}