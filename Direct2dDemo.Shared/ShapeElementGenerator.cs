using Direct2dDemo.Shared.Elements.DrawingElements;
using Direct2dDemo.Shared.Elements.GeometryElements;
using Direct2dDemo.Shared.Enums;
using System.Drawing;

namespace Direct2dDemo.Shared;

public class ShapeElementGenerator
{
    private static readonly Random _random = new Random();

    public static async Task<List<TextElement>> GenTextElement(int hwndWidth, int hwndHeight, int count)
    {
        var texts = new[]
        {
        "你好",
        "Hello",
        "こんにちは",
        "안녕하세요",
        "Bonjour",
        "Ciao",
        "Привет"
         };

        var fontFamilies = new[]
        {
        "Segoe UI",
        "Arial",
        "Calibri",
        "Times New Roman",
        "Consolas",
        "Microsoft YaHei",
        "SimSun",
        "Meiryo",
        "Yu Gothic",
        "Malgun Gothic"
        };

        var elements = await Task.Run(() =>
        {
            var result = new List<TextElement>(count);

            for (var i = 0; i < count; i++)
            {
                var text = texts[NextInt(0, texts.Length)];
                var fontFamily = fontFamilies[NextInt(0, fontFamilies.Length)];
                var fontSize = NextFloat(12.0f, 42.0f);

                var estimatedWidth = EstimateTextWidth(text, fontSize);
                var estimatedHeight = fontSize * 1.5f;

                var maxX = Math.Max(0.0f, hwndWidth - estimatedWidth);
                var maxY = Math.Max(0.0f, hwndHeight - estimatedHeight);

                var x = NextFloat(0, maxX);
                var y = NextFloat(0, maxY);

                var textElement = new TextElement
                {
                    Text = text,
                    FontFamily = fontFamily,
                    Position = new PointF(x, y),
                    FontSize = fontSize,
                    Color = RandomColor()
                };

                result.Add(textElement);
            }

            return result;
        });

        return elements;
    }

    public static async Task<List<EllipseGeometryElement>> GenEllipseGeometryElement(int hwndWidth, int hwndHeight, int count)
    {
        var ellipseElements = await GenEllipseElement(hwndWidth, hwndHeight, count);

        return ellipseElements.Select(e => new EllipseGeometryElement
        {
            Center = e.Center,
            RadiusX = e.RadiusX,
            RadiusY = e.RadiusY,
            FillStyle = e.FillStyle,
            FillColor = e.FillColor,
            HatchStyle = e.HatchStyle,
            StrokeColor = e.StrokeColor,
            StrokeWidth = e.StrokeWidth
        }).ToList();
    }

    public static async Task<List<EllipseElement>> GenEllipseElement(int hwndWidth, int hwndHeight, int count)
    {
        var elements = await Task.Run(() =>
        {
            var result = new List<EllipseElement>(count);

            var maxRadiusX = Math.Max(4, hwndWidth / 8);
            var maxRadiusY = Math.Max(4, hwndHeight / 8);

            for (var i = 0; i < count; i++)
            {
                var radiusX = NextFloat(4, maxRadiusX);
                var radiusY = NextFloat(4, maxRadiusY);

                // 防止窗口太小时 radius 超过窗口尺寸
                radiusX = Math.Min(radiusX, hwndWidth / 2.0f);
                radiusY = Math.Min(radiusY, hwndHeight / 2.0f);

                var centerX = NextFloat(radiusX, hwndWidth - radiusX);
                var centerY = NextFloat(radiusY, hwndHeight - radiusY);

                var fillStyle = RandomFillStyle();

                var ellipse = new EllipseElement
                {
                    Center = new PointF(centerX, centerY),
                    RadiusX = radiusX,
                    RadiusY = radiusY,

                    FillStyle = fillStyle,
                    FillColor = fillStyle == FillStyle.None
                        ? Color.Transparent
                        : RandomColor(),

                    HatchStyle = fillStyle == FillStyle.Hatch
                        ? RandomHatchStyle()
                        : null,

                    StrokeColor = RandomColor(),
                    StrokeWidth = NextFloat(1.0f, 4.0f)
                };

                result.Add(ellipse);
            }

            return result;
        });

        return elements;
    }

    public static async Task<List<PolygonGeometryElement>> GenPolygonGeometryElement(int hwndWidth, int hwndHeight, int count)
    {
        var elements = await Task.Run(() =>
        {
            var result = new List<PolygonGeometryElement>(count);

            var minWindowSize = Math.Min(hwndWidth, hwndHeight);

            // 多边形不要太大，尽量保证在窗口里面
            var minRadius = Math.Max(6.0f, minWindowSize * 0.01f);
            var maxRadius = Math.Max(minRadius + 1.0f, minWindowSize * 0.08f);

            for (var i = 0; i < count; i++)
            {
                // 3 到 10 边形
                var sideCount = NextInt(3, 11);

                var radius = NextFloat(minRadius, maxRadius);

                // 防止窗口太小时半径越界
                radius = Math.Min(radius, hwndWidth / 2.0f);
                radius = Math.Min(radius, hwndHeight / 2.0f);

                var centerX = NextFloat(radius, hwndWidth - radius);
                var centerY = NextFloat(radius, hwndHeight - radius);

                var startAngle = NextFloat(0, (float)(Math.PI * 2.0));
                var points = new List<PointF>(sideCount);

                for (var j = 0; j < sideCount; j++)
                {
                    var angle = startAngle + (float)(Math.PI * 2.0 * j / sideCount);

                    // 让它不是完全规则多边形，但仍然保证不会超过 radius 范围
                    var localRadius = radius * NextFloat(0.65f, 1.0f);

                    var x = centerX + MathF.Cos(angle) * localRadius;
                    var y = centerY + MathF.Sin(angle) * localRadius;

                    // 双保险：裁剪到窗口内
                    x = Math.Clamp(x, 0, hwndWidth);
                    y = Math.Clamp(y, 0, hwndHeight);

                    points.Add(new PointF(x, y));
                }

                var fillStyle = RandomFillStyle();

                var polygon = new PolygonGeometryElement
                {
                    Points = points,

                    FillStyle = fillStyle,
                    FillColor = fillStyle == FillStyle.None
                        ? Color.Transparent
                        : RandomColor(),

                    HatchStyle = fillStyle == FillStyle.Hatch
                        ? RandomHatchStyle()
                        : null,

                    StrokeColor = RandomColor(),
                    StrokeWidth = NextFloat(1.0f, 4.0f)
                };

                result.Add(polygon);
            }

            return result;
        });

        return elements;
    }

    public static async Task<List<LineElement>> GenLineElement(int hwndWidth, int hwndHeight, int count)
    {
        var elements = await Task.Run(() =>
        {
            var result = new List<LineElement>(count);

            for (var i = 0; i < count; i++)
            {
                var x0 = NextFloat(0, hwndWidth);
                var y0 = NextFloat(0, hwndHeight);
                var x1 = NextFloat(0, hwndWidth);
                var y1 = NextFloat(0, hwndHeight);

                var line = new LineElement
                {
                    Point0 = new PointF(x0, y0),
                    Point1 = new PointF(x1, y1),

                    DashStyle = RandomDashStyle(),
                    CapStyle = RandomCapStyle(),

                    StrokeWidth = NextFloat(1.0f, 4.0f),
                    Color = RandomColor()
                };

                result.Add(line);
            }

            return result;
        });

        return elements;
    }

    public static async Task<List<RectangleGeometryElement>> GenRectangleGeometryElement(int hwndWidth, int hwndHeight, int count)
    {
        var rectangleElements = await GenRectangleElement(hwndWidth, hwndHeight, count);
        return rectangleElements.Select(r => new RectangleGeometryElement
        {
            TopLeft = r.TopLeft,
            Width = r.Width,
            Height = r.Height,
            FillStyle = r.FillStyle,
            FillColor = r.FillColor,
            HatchStyle = r.HatchStyle,
            StrokeColor = r.StrokeColor,
            StrokeWidth = r.StrokeWidth
        }).ToList();
    }

    public static async Task<List<RectangleElement>> GenRectangleElement(int hwndWidth, int hwndHeight, int count)
    {
        var elements = await Task.Run(() =>
        {
            var result = new List<RectangleElement>(count);

            var minWidth = 8.0f;
            var minHeight = 8.0f;

            var maxWidth = Math.Max(minWidth, hwndWidth / 6.0f);
            var maxHeight = Math.Max(minHeight, hwndHeight / 6.0f);

            for (var i = 0; i < count; i++)
            {
                var width = NextFloat(minWidth, maxWidth);
                var height = NextFloat(minHeight, maxHeight);

                // 防止窗口太小时矩形尺寸超过窗口
                width = Math.Min(width, hwndWidth);
                height = Math.Min(height, hwndHeight);

                var maxX = Math.Max(0.0f, hwndWidth - width);
                var maxY = Math.Max(0.0f, hwndHeight - height);

                var x = NextFloat(0, maxX);
                var y = NextFloat(0, maxY);

                var fillStyle = RandomFillStyle();

                var rectangle = new RectangleElement
                {
                    TopLeft = new PointF(x, y),
                    Width = width,
                    Height = height,

                    FillStyle = fillStyle,
                    FillColor = fillStyle == FillStyle.None
                        ? Color.Transparent
                        : RandomColor(),

                    HatchStyle = fillStyle == FillStyle.Hatch
                        ? RandomHatchStyle()
                        : null,

                    StrokeColor = RandomColor(),
                    StrokeWidth = NextFloat(1.0f, 4.0f)
                };

                result.Add(rectangle);
            }

            return result;
        });

        return elements;
    }

    private static float EstimateTextWidth(string text, float fontSize)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var width = 0.0f;

        foreach (var ch in text)
        {
            // CJK / Hangul / Kana 通常比拉丁字母宽一些
            if (IsWideChar(ch))
                width += fontSize;
            else
                width += fontSize * 0.6f;
        }

        return width;
    }

    private static bool IsWideChar(char ch)
    {
        return
            ch >= 0x1100 &&
            (
                ch <= 0x115F ||                    // Hangul Jamo
                ch == 0x2329 || ch == 0x232A ||
                ch is >= (char)0x2E80 and <= (char)0xA4CF || // CJK, Kana
                ch is >= (char)0xAC00 and <= (char)0xD7A3 || // Hangul
                ch is >= (char)0xF900 and <= (char)0xFAFF ||
                ch is >= (char)0xFE10 and <= (char)0xFE19 ||
                ch is >= (char)0xFE30 and <= (char)0xFE6F ||
                ch is >= (char)0xFF00 and <= (char)0xFF60 ||
                ch is >= (char)0xFFE0 and <= (char)0xFFE6
            );
    }

    private static float NextFloat(float min, float max)
    {
        if (max <= min)
            return min;

        lock (_random)
        {
            return (float)(min + _random.NextDouble() * (max - min));
        }
    }

    private static Color RandomColor()
    {
        lock (_random)
        {
            return Color.FromArgb(
                _random.Next(10, 255),
                _random.Next(10, 230),
                _random.Next(10, 230),
                _random.Next(10, 230)
            );
        }
    }

    private static int NextInt(int min, int max)
    {
        lock (_random)
        {
            return _random.Next(min, max);
        }
    }

    private static FillStyle RandomFillStyle()
    {
        return NextInt(0, 3) switch
        {
            0 => FillStyle.None,
            1 => FillStyle.Solid,
            _ => FillStyle.Hatch
        };
    }

    private static HatchStyle RandomHatchStyle()
    {
        return NextInt(0, 6) switch
        {
            0 => HatchStyle.Horizontal,
            1 => HatchStyle.Vertical,
            2 => HatchStyle.ForwardDiagonal,
            3 => HatchStyle.BackwardDiagonal,
            4 => HatchStyle.Cross,
            _ => HatchStyle.DiagCross
        };
    }

    private static DashStyle RandomDashStyle()
    {
        return NextInt(0, 4) switch
        {
            0 => DashStyle.Solid,
            1 => DashStyle.Dash,
            2 => DashStyle.Dot,
            3 => DashStyle.DashDot,
            _ => DashStyle.Solid
            //_ => DashStyle.DashDotDot
        };
    }

    private static CapStyle RandomCapStyle()
    {
        return NextInt(0, 3) switch
        {
            0 => CapStyle.Flat,
            1 => CapStyle.Square,
            2 => CapStyle.Round,
            _ => CapStyle.Flat
            //_ => CapStyle.Triangle
        };
    }
}