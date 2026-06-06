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
                element.CapStyle,
                element.LineJoin);

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
            element.StrokeColor,
            element.StrokeWidth,
            element.DashStyle,
            element.CapStyle,
            element.LineJoin);
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
            element.DashStyle,
            element.CapStyle,
            element.LineJoin,
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
            element.StrokeColor,
            element.StrokeWidth,
            element.DashStyle,
            element.CapStyle,
            element.LineJoin);
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
            element.DashStyle,
            element.CapStyle,
            element.LineJoin,
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
            element.DashStyle,
            element.CapStyle,
            element.LineJoin,
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
        Color hatchColor,
        Color strokeColor,
        float strokeWidth,
        DashStyle dashStyle,
        CapStyle capStyle,
        LineJoin lineJoin)
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
            hatchColor,
            strokeColor,
            strokeWidth,
            dashStyle,
            capStyle,
            lineJoin,
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
        DashStyle dashStyle,
        CapStyle capStyle,
        LineJoin lineJoin,
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
            ? createdPen = CreatePen(strokeColor, strokeWidth, dashStyle, capStyle, lineJoin)
            : Gdi32.GetStockObject(StockObjectType.NULL_PEN);

        var oldBrush = Gdi32.SelectObject(hdc, brush);
        var oldPen = Gdi32.SelectObject(hdc, pen);

        // GDI HatchBrush: hatchColor is brush color; fillColor is background color.
        // 如果 hatchColor 透明但 fillColor 不透明，这里会退化为 SolidBrush，不需要改 BkMode。
        var needBkMode =
            hasFill &&
            fillStyle == FillStyle.Hatch &&
            hatchStyle.HasValue &&
            hatchColor.A > 0;

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
                hatchStyle.HasValue && (fillColor.A > 0 || hatchColor.A > 0),

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

            FillStyle.Hatch when hatchStyle.HasValue && hatchColor.A > 0 =>
                CreateHatchBrush(hatchStyle.Value, hatchColor),

            FillStyle.Hatch when hatchStyle.HasValue && fillColor.A > 0 =>
                CreateSolidBrush(fillColor),

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
        return CreatePen(color, width, DashStyle.Solid, CapStyle.Flat, LineJoin.Miter);
    }

    public static SafeHPEN CreatePen(
        Color color,
        float width,
        DashStyle dashStyle,
        CapStyle capStyle)
    {
        return CreatePen(color, width, dashStyle, capStyle, LineJoin.Miter);
    }

    public static SafeHPEN CreatePen(
        Color color,
        float width,
        DashStyle dashStyle,
        CapStyle capStyle,
        LineJoin lineJoin)
    {
        var penWidth = Math.Max(1, (int)Math.Round(width));

        // CreatePen 基本不能表达 RoundCap / LineJoin。
        // 这里统一用 ExtCreatePen + PS_GEOMETRIC 来支持 CapStyle / LineJoin。
        var userStyle = CreateGdiUserStyle(dashStyle, penWidth);
        var hasUserStyle = userStyle.Length > 0;

        var penStyle =
            (uint)Gdi32.PenType.PS_GEOMETRIC |
            (hasUserStyle
                ? (uint)Gdi32.PenStyle.PS_USERSTYLE
                : (uint)Gdi32.PenStyle.PS_SOLID) |
            (uint)ToGdiEndCapStyle(capStyle) |
            (uint)ToGdiLineJoinStyle(lineJoin);

        var logBrush = new LOGBRUSH
        {
            lbStyle = BrushStyle.BS_SOLID,
            lbColor = unchecked((uint)ToColorRef(color)),
            lbHatch = IntPtr.Zero
        };

        var pen = Gdi32.ExtCreatePen(
           penStyle,
           (uint)penWidth,
           logBrush,
           hasUserStyle ? (uint)userStyle.Length : 0,
           hasUserStyle ? userStyle : null);

        if (pen is null || pen.IsInvalid)
            throw new InvalidOperationException("ExtCreatePen failed.");

        return pen;
    }

    private static uint[] CreateGdiUserStyle(DashStyle dashStyle, int penWidth)
    {
        var unit = (uint)Math.Max(1, penWidth);

        return dashStyle switch
        {
            DashStyle.Solid =>
                Array.Empty<uint>(),

            DashStyle.Dash =>
                new[] { 4u * unit, 2u * unit },

            DashStyle.Dot =>
                new[] { 1u * unit, 2u * unit },

            DashStyle.DashDot =>
                new[] { 4u * unit, 2u * unit, 1u * unit, 2u * unit },

            DashStyle.DashDotDot =>
                new[] { 4u * unit, 2u * unit, 1u * unit, 2u * unit, 1u * unit, 2u * unit },

            _ =>
                Array.Empty<uint>()
        };
    }

    private static Gdi32.ExtPenStyle ToGdiEndCapStyle(CapStyle capStyle)
    {
        return capStyle switch
        {
            CapStyle.Flat =>
                Gdi32.ExtPenStyle.PS_ENDCAP_FLAT,

            CapStyle.Square =>
                Gdi32.ExtPenStyle.PS_ENDCAP_SQUARE,

            CapStyle.Round =>
                Gdi32.ExtPenStyle.PS_ENDCAP_ROUND,

            //// GDI 没有 TriangleCap。这里退化为 Square，至少保持“向端点外延伸”的行为。
            //CapStyle.Triangle =>
            //    Gdi32.ExtPenStyle.PS_ENDCAP_SQUARE,

            _ =>
                Gdi32.ExtPenStyle.PS_ENDCAP_FLAT
        };
    }

    private static Gdi32.ExtPenStyle ToGdiLineJoinStyle(LineJoin lineJoin)
    {
        return lineJoin switch
        {
            LineJoin.Miter =>
                Gdi32.ExtPenStyle.PS_JOIN_MITER,

            LineJoin.Bevel =>
                Gdi32.ExtPenStyle.PS_JOIN_BEVEL,

            LineJoin.Round =>
                Gdi32.ExtPenStyle.PS_JOIN_ROUND,

            //// GDI 没有 MiterOrBevel。这里先映射为 Miter。
            //LineJoin.MiterOrBevel =>
            //    Gdi32.ExtPenStyle.PS_JOIN_MITER,

            _ =>
                Gdi32.ExtPenStyle.PS_JOIN_MITER
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