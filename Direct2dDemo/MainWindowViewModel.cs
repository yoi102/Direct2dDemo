using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dDemo.Direct2D;
using Direct2dDemo.GDI;
using Direct2dDemo.Shared;
using System.Diagnostics;
using System.Drawing;

namespace Direct2dDemo;

internal partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private int _context_initialized_count;

    public MainWindowViewModel()
    {
        Direct2dContext.Initialized += async (s, e) =>
        {
            _context_initialized_count++;
            if (_context_initialized_count < 2)
                return;

            await AddEllipseAsync();
            await AddPolygonAsync();
            await AddTextAsync();
        };
        GdiContext.Initialized += async (s, e) =>
        {
            _context_initialized_count++;
            if (_context_initialized_count < 2)
                return;
            await AddEllipseAsync();
            await AddPolygonAsync();
            await AddTextAsync();
        };
    }

    [ObservableProperty]
    public partial bool Running { get; set; } = false;

    [ObservableProperty]
    public partial int AddCount { get; set; } = 1000;

    [ObservableProperty]
    public partial int EllipseCount { get; set; } = 0;

    [ObservableProperty]
    public partial int PolygonCount { get; set; } = 0;

    [ObservableProperty]
    public partial int TextCount { get; set; } = 0;

    [ObservableProperty]
    public partial double DataGenerationTime { get; set; } = 0;

    [ObservableProperty]
    public partial double Direct2dRenderingTime { get; set; } = 0;

    [ObservableProperty]
    public partial double GdiRenderingTime { get; set; } = 0;

    public Direct2dContext Direct2dContext { get; } = new Direct2dContext();
    public GdiContext GdiContext { get; } = new GdiContext();

    private static readonly Random _random = new Random();

    [RelayCommand]
    private async Task ClearAsync()
    {
        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        Direct2dContext.ClearData();
        GdiContext.ClearData();
        await this.RefreshAsync();
        EllipseCount = 0;
        PolygonCount = 0;
        TextCount = 0;
    }

    [RelayCommand]
    private async Task AddEllipseAsync()
    {
        if (Running)
            return;

        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        var hwndWidth = Direct2dContext.Width;
        var hwndHeight = Direct2dContext.Height;
        var addCount = AddCount;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;
        var stopwatch = Stopwatch.StartNew();

        List<IDrawingElement> elements = await GenEllipse(hwndWidth, hwndHeight, addCount);
        Direct2dContext.DrawingElements.AddRange(elements);
        GdiContext.DrawingElements.AddRange(elements);

        this.EllipseCount += elements.Count;
        stopwatch.Stop();
        this.DataGenerationTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        Direct2dContext.Render();
        stopwatch.Stop();
        this.Direct2dRenderingTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        GdiContext.Render();
        stopwatch.Stop();
        GdiRenderingTime = stopwatch.ElapsedMilliseconds;
    }

    private static async Task<List<IDrawingElement>> GenEllipse(int hwndWidth, int hwndHeight, int addCount)
    {
        var elements = await Task.Run(() =>
        {
            var result = new List<IDrawingElement>(addCount);

            var maxRadiusX = Math.Max(4, hwndWidth / 8);
            var maxRadiusY = Math.Max(4, hwndHeight / 8);

            for (var i = 0; i < addCount; i++)
            {
                var radiusX = NextFloat(4, maxRadiusX);
                var radiusY = NextFloat(4, maxRadiusY);

                // 防止窗口太小时 radius 超过窗口尺寸
                radiusX = Math.Min(radiusX, hwndWidth / 2.0f);
                radiusY = Math.Min(radiusY, hwndHeight / 2.0f);

                var centerX = NextFloat(radiusX, hwndWidth - radiusX);
                var centerY = NextFloat(radiusY, hwndHeight - radiusY);

                var ellipse = new EllipseElement
                {
                    Center = new PointF(centerX, centerY),
                    RadiusX = radiusX,
                    RadiusY = radiusY,

                    IsFilled = true,
                    FillColor = RandomColor(),

                    HasStroke = true,
                    StrokeColor = RandomColor(),
                    StrokeWidth = NextFloat(1.0f, 4.0f)
                };

                result.Add(ellipse);
            }

            return result;
        });
        return elements;
    }

    [RelayCommand]
    private async Task AddPolygonAsync()
    {
        if (Running)
            return;

        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        var hwndWidth = Direct2dContext.Width;
        var hwndHeight = Direct2dContext.Height;
        var addCount = AddCount;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        List<IDrawingElement> elements = await GenPolygon(hwndWidth, hwndHeight, addCount);

        Direct2dContext.DrawingElements.AddRange(elements);
        GdiContext.DrawingElements.AddRange(elements);
        PolygonCount += elements.Count;

        stopwatch.Stop();
        DataGenerationTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        Direct2dContext.Render();
        stopwatch.Stop();
        Direct2dRenderingTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        GdiContext.Render();
        stopwatch.Stop();
        GdiRenderingTime = stopwatch.ElapsedMilliseconds;
    }

    private static async Task<List<IDrawingElement>> GenPolygon(int hwndWidth, int hwndHeight, int addCount)
    {
        var elements = await Task.Run(() =>
        {
            var result = new List<IDrawingElement>(addCount);

            var minWindowSize = Math.Min(hwndWidth, hwndHeight);

            // 多边形不要太大，尽量保证在窗口里面
            var minRadius = Math.Max(6.0f, minWindowSize * 0.01f);
            var maxRadius = Math.Max(minRadius + 1.0f, minWindowSize * 0.08f);

            for (var i = 0; i < addCount; i++)
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

                var polygon = new PolygonElement
                {
                    Points = points,

                    IsFilled = NextBool(),
                    FillColor = RandomColor(),

                    HasStroke = true,
                    StrokeColor = RandomColor(),
                    StrokeWidth = NextFloat(1.0f, 4.0f)
                };

                result.Add(polygon);
            }

            return result;
        });

        return elements;
    }

    [RelayCommand]
    private async Task AddTextAsync()
    {
        if (Running)
            return;

        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        var hwndWidth = Direct2dContext.Width;
        var hwndHeight = Direct2dContext.Height;
        var addCount = AddCount;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        List<IDrawingElement> elements = await GenText(hwndWidth, hwndHeight, addCount);

        Direct2dContext.DrawingElements.AddRange(elements);
        GdiContext.DrawingElements.AddRange(elements);
        TextCount += elements.Count;

        stopwatch.Stop();
        DataGenerationTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        Direct2dContext.Render();
        stopwatch.Stop();
        Direct2dRenderingTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        GdiContext.Render();
        stopwatch.Stop();
        GdiRenderingTime = stopwatch.ElapsedMilliseconds;
    }

    private static async Task<List<IDrawingElement>> GenText(int hwndWidth, int hwndHeight, int addCount)
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
            var result = new List<IDrawingElement>(addCount);

            for (var i = 0; i < addCount; i++)
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

    [RelayCommand]
    private async Task ExportToPdfAsync()
    {
        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        Direct2dContext.Render();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        var stopwatch = Stopwatch.StartNew();
        stopwatch.Restart();
        Direct2dContext.Render();
        stopwatch.Stop();
        this.Direct2dRenderingTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        GdiContext.Render();
        stopwatch.Stop();
        GdiRenderingTime = stopwatch.ElapsedMilliseconds;
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

    private static bool NextBool()
    {
        lock (_random)
        {
            return _random.Next(0, 2) == 0;
        }
    }

    public void Dispose()
    {
        Direct2dContext.Dispose();
        GdiContext.Dispose();
    }
}