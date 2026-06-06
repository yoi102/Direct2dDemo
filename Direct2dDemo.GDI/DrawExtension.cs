using Direct2dDemo.Shared.Elements;
using Direct2dDemo.Shared.Elements.DrawingElements;
using Direct2dDemo.Shared.Elements.GeometryElements;
using Direct2dDemo.Shared.Enums;
using System.Drawing;
using Vanara.PInvoke;
using static Vanara.PInvoke.Gdi32;
using SharedHatchStyle = Direct2dDemo.Shared.Enums.HatchStyle;

namespace Direct2dDemo.GDI;

internal static class DrawExtension
{
    public static void Draw(this IDrawingElement element, GdiWrapper gdiWrapper)
    {
        switch (element)
        {
            case PolygonGeometryElement polygonElement:
                Draw(polygonElement, gdiWrapper);
                break;

            case EllipseElement ellipseElement:
                Draw(ellipseElement, gdiWrapper);
                break;

            case RectangleElement rectangleElement:
                Draw(rectangleElement, gdiWrapper);
                break;

            case RectangleGeometryElement rectangleGeometryElement:
                Draw(rectangleGeometryElement, gdiWrapper);
                break;

            case EllipseGeometryElement ellipseGeometryElement:
                Draw(ellipseGeometryElement, gdiWrapper);
                break;

            case TextElement textElement:
                Draw(textElement, gdiWrapper);
                break;

            case LineElement lineElement:
                Draw(lineElement, gdiWrapper);
                break;
        }
    }

    public static void Draw(LineElement element, GdiWrapper gdiWrapper)
    {
        if (element.StrokeWidth <= 0)
            return;

        if (element.StrokeColor.A <= 0)
            return;

        var hdc = gdiWrapper.Hdc;

        SafeHPEN? createdPen = null;

        try
        {
            createdPen = CreatePen(
                element.StrokeColor,
                element.StrokeWidth,
                element.DashStyle,
                element.CapStyle);

            var oldPen = Gdi32.SelectObject(hdc, createdPen);

            try
            {
                Gdi32.MoveToEx(
                    hdc,
                    ToInt(element.StartPoint.X),
                    ToInt(element.StartPoint.Y),
                    out _);

                Gdi32.LineTo(
                    hdc,
                    ToInt(element.EndPoint.X),
                    ToInt(element.EndPoint.Y));
            }
            finally
            {
                if (oldPen != nint.Zero)
                    Gdi32.SelectObject(hdc, oldPen);
            }
        }
        finally
        {
            createdPen?.Dispose();
        }
    }

    public static void Draw(RectangleGeometryElement element, GdiWrapper gdiWrapper)
    {
        if (element.Width <= 0 || element.Height <= 0)
            return;

        DrawRectangleCore(
            gdiWrapper,
            element.TopLeft,
            element.Width,
            element.Height,
            element.FillStyle,
            element.FillColor,
            element.HatchStyle,
            element.StrokeColor,
            element.StrokeWidth);
    }

    public static void Draw(EllipseGeometryElement element, GdiWrapper gdiWrapper)
    {
        if (element.RadiusX <= 0 || element.RadiusY <= 0)
            return;

        var left = ToInt(element.Center.X - element.RadiusX);
        var top = ToInt(element.Center.Y - element.RadiusY);
        var right = ToInt(element.Center.X + element.RadiusX);
        var bottom = ToInt(element.Center.Y + element.RadiusY);

        var hdc = gdiWrapper.Hdc;

        DrawWithFillAndStroke(
            gdiWrapper,
            element.FillStyle,
            element.FillColor,
            element.HatchStyle,
            element.StrokeColor,
            element.StrokeColor,
            element.StrokeWidth,
            () => Gdi32.Ellipse(hdc, left, top, right, bottom));
    }

    public static void Draw(RectangleElement element, GdiWrapper gdiWrapper)
    {
        if (element.Width <= 0 || element.Height <= 0)
            return;

        DrawRectangleCore(
            gdiWrapper,
            element.TopLeft,
            element.Width,
            element.Height,
            element.FillStyle,
            element.FillColor,
            element.HatchStyle,
            element.StrokeColor,
            element.StrokeWidth);
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

        SafeHFONT? font = null;

        font = CreateFont(element.FontFamily, element.FontSize);

        var oldFont = Gdi32.SelectObject(hdc, font);
        var oldBkMode = Gdi32.SetBkMode(hdc, BackgroundMode.TRANSPARENT);
        var oldTextColor = Gdi32.SetTextColor(hdc, ToColorRef(element.Color));

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

            Gdi32.SetTextColor(hdc, oldTextColor);

            font?.Dispose();
        }
    }

    public static void Draw(PolygonGeometryElement element, GdiWrapper gdiWrapper)
    {
        if (element.Points.Count < 3)
            return;

        var hdc = gdiWrapper.Hdc;

        var points = new POINT[element.Points.Count];

        for (var i = 0; i < element.Points.Count; i++)
        {
            var point = element.Points[i];

            points[i] = new POINT(
                ToInt(point.X),
                ToInt(point.Y));
        }

        DrawWithFillAndStroke(
            gdiWrapper,
            element.FillStyle,
            element.FillColor,
            element.HatchStyle,
            element.StrokeColor,
            element.StrokeColor,
            element.StrokeWidth,
            () => Gdi32.Polygon(hdc, points, points.Length));
    }

    public static void Draw(EllipseElement element, GdiWrapper gdiWrapper)
    {
        if (element.RadiusX <= 0 || element.RadiusY <= 0)
            return;

        var left = ToInt(element.Center.X - element.RadiusX);
        var top = ToInt(element.Center.Y - element.RadiusY);
        var right = ToInt(element.Center.X + element.RadiusX);
        var bottom = ToInt(element.Center.Y + element.RadiusY);

        var hdc = gdiWrapper.Hdc;

        DrawWithFillAndStroke(
            gdiWrapper,
            element.FillStyle,
            element.FillColor,
            element.HatchStyle,
            element.StrokeColor,
            element.StrokeColor,
            element.StrokeWidth,
            () => Gdi32.Ellipse(hdc, left, top, right, bottom));
    }

    private static void DrawRectangleCore(
        GdiWrapper gdiWrapper,
        PointF topLeft,
        float width,
        float height,
        FillStyle fillStyle,
        Color fillColor,
        SharedHatchStyle? hatchStyle,
        Color strokeColor,
        float strokeWidth)
    {
        var left = ToInt(topLeft.X);
        var top = ToInt(topLeft.Y);
        var right = ToInt(topLeft.X + width);
        var bottom = ToInt(topLeft.Y + height);

        var hdc = gdiWrapper.Hdc;

        DrawWithFillAndStroke(
            gdiWrapper,
            fillStyle,
            fillColor,
            hatchStyle,
            strokeColor,
            strokeColor,
            strokeWidth,
            () => Gdi32.Rectangle(hdc, left, top, right, bottom));
    }

    private static void DrawWithFillAndStroke(
        GdiWrapper gdiWrapper,
        FillStyle fillStyle,
        Color fillColor,
        SharedHatchStyle? hatchStyle,
        Color hatchColor,
        Color strokeColor,
        float strokeWidth,
        Action drawAction)
    {
        var hasFill = HasFill(fillStyle, fillColor, hatchStyle, hatchColor);
        var hasStroke = strokeWidth > 0 && strokeColor.A > 0;

        if (!hasFill && !hasStroke)
            return;

        var hdc = gdiWrapper.Hdc;

        SafeHBRUSH? createdBrush = null;
        SafeHPEN? createdPen = null;

        var brush = hasFill
            ? createdBrush = CreateFillBrush(fillStyle, fillColor, hatchStyle, hatchColor)
            : Gdi32.GetStockObject(StockObjectType.HOLLOW_BRUSH);

        var pen = hasStroke
            ? createdPen = CreatePen(strokeColor, strokeWidth)
            : Gdi32.GetStockObject(StockObjectType.NULL_PEN);

        var oldBrush = Gdi32.SelectObject(hdc, brush);
        var oldPen = Gdi32.SelectObject(hdc, pen);

        var needBkMode = hasFill && fillStyle == FillStyle.Hatch;
        var oldBkMode = default(BackgroundMode);
        var oldBkColor = new COLORREF();

        if (needBkMode)
        {
            oldBkMode = Gdi32.SetBkMode(
                hdc,
                fillColor.A > 0
                    ? BackgroundMode.OPAQUE
                    : BackgroundMode.TRANSPARENT);

            oldBkColor = Gdi32.SetBkColor(hdc, ToColorRef(fillColor));
        }

        try
        {
            drawAction();
        }
        finally
        {
            if (needBkMode)
            {
                if (oldBkMode != 0)
                    Gdi32.SetBkMode(hdc, oldBkMode);

                Gdi32.SetBkColor(hdc, oldBkColor);
            }

            if (oldBrush != nint.Zero)
                Gdi32.SelectObject(hdc, oldBrush);

            if (oldPen != nint.Zero)
                Gdi32.SelectObject(hdc, oldPen);

            createdBrush?.Dispose();
            createdPen?.Dispose();
        }
    }

    private static bool HasFill(
        FillStyle fillStyle,
        Color fillColor,
        SharedHatchStyle? hatchStyle,
        Color hatchColor)
    {
        return fillStyle switch
        {
            FillStyle.Solid =>
                fillColor.A > 0,

            FillStyle.Hatch =>
                hatchStyle.HasValue && hatchColor.A > 0,

            _ => false
        };
    }

    private static SafeHBRUSH CreateFillBrush(
        FillStyle fillStyle,
        Color fillColor,
        SharedHatchStyle? hatchStyle,
        Color hatchColor)
    {
        return fillStyle switch
        {
            FillStyle.Solid =>
                CreateSolidBrush(fillColor),

            FillStyle.Hatch when hatchStyle.HasValue =>
                CreateHatchBrush(hatchStyle.Value, hatchColor),

            _ => throw new ArgumentOutOfRangeException(nameof(fillStyle))
        };
    }

    public static SafeHBRUSH CreateSolidBrush(Color color)
    {
        var brush = Gdi32.CreateSolidBrush(ToColorRef(color));

        if (brush is null)
            throw new InvalidOperationException("CreateSolidBrush failed.");

        return brush;
    }

    public static SafeHBRUSH CreateHatchBrush(
        SharedHatchStyle hatchStyle,
        Color hatchColor)
    {
        var brush = Gdi32.CreateHatchBrush(
            (Gdi32.HatchStyle)(uint)hatchStyle,
            ToColorRef(hatchColor));

        if (brush is null)
            throw new InvalidOperationException("CreateHatchBrush failed.");

        return brush;
    }

    public static SafeHPEN CreatePen(Color color, float width)
    {
        return CreatePen(color, width, DashStyle.Solid, CapStyle.Flat);
    }

    public static SafeHPEN CreatePen(
        Color color,
        float width,
        DashStyle dashStyle,
        CapStyle capStyle)
    {
        var penWidth = Math.Max(1, (int)Math.Round(width));
        var penStyle = ToGdiPenStyle(dashStyle);

        var pen = Gdi32.CreatePen(
            penStyle,
            penWidth,
            ToColorRef(color));

        if (pen == nint.Zero)
            throw new InvalidOperationException("CreatePen failed.");

        return pen;
    }

    private static Gdi32.PenStyle ToGdiPenStyle(DashStyle dashStyle)
    {
        return dashStyle switch
        {
            DashStyle.Solid => Gdi32.PenStyle.PS_SOLID,
            DashStyle.Dash => Gdi32.PenStyle.PS_DASH,
            DashStyle.Dot => Gdi32.PenStyle.PS_DOT,
            DashStyle.DashDot => Gdi32.PenStyle.PS_DASHDOT,
            DashStyle.DashDotDot => Gdi32.PenStyle.PS_DASHDOTDOT,
            _ => Gdi32.PenStyle.PS_SOLID
        };
    }

    public static SafeHFONT CreateFont(string? fontFamily, float fontSize)
    {
        if (fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        var family = string.IsNullOrWhiteSpace(fontFamily)
            ? "Meiryo"
            : fontFamily.Trim();

        var size = Math.Max(1, (int)Math.Round(fontSize));

        // Negative height means "character height" in logical pixels.
        var font = Gdi32.CreateFont(
            -size,
            0,
            0,
            0,
            Gdi32.FW_NORMAL,
            false,
            false,
            false,
            CharacterSet.DEFAULT_CHARSET,
            OutputPrecision.OUT_DEFAULT_PRECIS,
            ClippingPrecision.CLIP_DEFAULT_PRECIS,
            OutputQuality.CLEARTYPE_QUALITY,
            PitchAndFamily.DEFAULT_PITCH | PitchAndFamily.FF_DONTCARE,
            family);

        if (font == nint.Zero)
            throw new InvalidOperationException("CreateFont failed.");

        return font;
    }

    internal static int ToColorRef(Color color)
    {
        // COLORREF is 0x00BBGGRR. GDI ignores alpha.
        return color.R | (color.G << 8) | (color.B << 16);
    }

    private static int ToInt(float value)
    {
        return (int)Math.Round(value);
    }
}