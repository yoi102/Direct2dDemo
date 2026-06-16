using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dDemo.Direct2D;
using Direct2dDemo.GDI;
using Direct2dDemo.Shared;
using System.Diagnostics;

namespace Direct2dDemo.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    public MainWindowViewModel()
    {
        Direct2dContext.Rendered += (s, time) =>
        {
            Direct2dRenderingTime = time;
        };
        GdiContext.Rendered += (s, time) =>
        {
            GdiRenderingTime = time;
        };
    }

    public async Task InitAsync()
    {
        Running = true;
        using var _ = DeferAction.Create(() => Running = false);
        var hwndWidth = Direct2dContext.Width;
        var hwndHeight = Direct2dContext.Height;
        var addCount = CountToAdd;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;
        var stopwatch = Stopwatch.StartNew();

        var ellipse_elements = await ShapeElementGenerator.GenEllipseElement(hwndWidth, hwndHeight, addCount);
        Direct2dContext.DrawingElements.AddRange(ellipse_elements);
        GdiContext.DrawingElements.AddRange(ellipse_elements);
        this.EllipseCount += ellipse_elements.Count;

        var ellipse_geometry_elements = await ShapeElementGenerator.GenEllipseGeometryElement(hwndWidth, hwndHeight, addCount);
        Direct2dContext.DrawingElements.AddRange(ellipse_geometry_elements);
        GdiContext.DrawingElements.AddRange(ellipse_geometry_elements);
        this.EllipseGeometryCount += ellipse_geometry_elements.Count;

        var polygon_elements = await ShapeElementGenerator.GenPolygonGeometryElement(hwndWidth, hwndHeight, addCount);

        Direct2dContext.DrawingElements.AddRange(polygon_elements);
        GdiContext.DrawingElements.AddRange(polygon_elements);
        PolygonCount += polygon_elements.Count;

        var rectangle_elements = await ShapeElementGenerator.GenRectangleElement(hwndWidth, hwndHeight, addCount);
        Direct2dContext.DrawingElements.AddRange(rectangle_elements);
        GdiContext.DrawingElements.AddRange(rectangle_elements);
        this.RectangleCount += rectangle_elements.Count;

        var rectangle_geometry_elements = await ShapeElementGenerator.GenRectangleGeometryElement(hwndWidth, hwndHeight, addCount);
        Direct2dContext.DrawingElements.AddRange(rectangle_geometry_elements);
        GdiContext.DrawingElements.AddRange(rectangle_geometry_elements);
        this.RectangleGeometryCount += rectangle_geometry_elements.Count;

        var line_elements = await ShapeElementGenerator.GenLineElement(hwndWidth, hwndHeight, addCount);
        Direct2dContext.DrawingElements.AddRange(line_elements);
        GdiContext.DrawingElements.AddRange(line_elements);
        this.LineCount += line_elements.Count;

        var text_elements = await ShapeElementGenerator.GenTextElement(hwndWidth, hwndHeight, addCount);

        Direct2dContext.DrawingElements.AddRange(text_elements);
        GdiContext.DrawingElements.AddRange(text_elements);
        TextCount += text_elements.Count;

        stopwatch.Stop();
        this.DataGenerationTime = stopwatch.ElapsedMilliseconds;

        await Direct2dContext.RenderAsync();

        GdiContext.RenderAsync();
    }

    [ObservableProperty]
    public partial bool Running { get; set; } = false;

    private bool _Direct2DEnableMultiThread;

    public bool Direct2DEnableMultiThread
    {
        get { return _Direct2DEnableMultiThread; }
        set
        {
            if (SetProperty(ref _Direct2DEnableMultiThread, value))
            {
                Direct2dContext.EnableMultiThread = value;
            }
        }
    }

    [ObservableProperty]
    public partial int CountToAdd { get; set; } = 1000;

    [ObservableProperty]
    public partial int LineCount { get; set; } = 0;

    [ObservableProperty]
    public partial int EllipseCount { get; set; } = 0;

    [ObservableProperty]
    public partial int EllipseGeometryCount { get; set; } = 0;

    [ObservableProperty]
    public partial int PolygonCount { get; set; } = 0;

    [ObservableProperty]
    public partial int TextCount { get; set; } = 0;

    [ObservableProperty]
    public partial int RectangleCount { get; set; } = 0;

    [ObservableProperty]
    public partial int RectangleGeometryCount { get; set; } = 0;

    [ObservableProperty]
    public partial double DataGenerationTime { get; set; } = 0;

    [ObservableProperty]
    public partial double Direct2dRenderingTime { get; set; } = 0;

    [ObservableProperty]
    public partial double GdiRenderingTime { get; set; } = 0;

    public Direct2dContext Direct2dContext { get; } = new Direct2dContext();
    public GdiContext GdiContext { get; } = new GdiContext();

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
        RectangleCount = 0;
        RectangleGeometryCount = 0;
        EllipseGeometryCount = 0;
        LineCount = 0;
    }

    [RelayCommand]
    private async Task AddEllipseGeometryAsync()
    {
        if (Running)
            return;

        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        var hwndWidth = Direct2dContext.Width;
        var hwndHeight = Direct2dContext.Height;
        var addCount = CountToAdd;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;
        var stopwatch = Stopwatch.StartNew();

        var elements = await ShapeElementGenerator.GenEllipseGeometryElement(hwndWidth, hwndHeight, addCount);
        Direct2dContext.DrawingElements.AddRange(elements);
        GdiContext.DrawingElements.AddRange(elements);

        this.EllipseGeometryCount += elements.Count;
        stopwatch.Stop();
        this.DataGenerationTime = stopwatch.ElapsedMilliseconds;

        await Direct2dContext.RenderAsync();

        await GdiContext.RenderAsync();
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
        var addCount = CountToAdd;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;
        var stopwatch = Stopwatch.StartNew();

        var elements = await ShapeElementGenerator.GenEllipseElement(hwndWidth, hwndHeight, addCount);
        Direct2dContext.DrawingElements.AddRange(elements);
        GdiContext.DrawingElements.AddRange(elements);

        this.EllipseCount += elements.Count;
        stopwatch.Stop();
        this.DataGenerationTime = stopwatch.ElapsedMilliseconds;

        await Direct2dContext.RenderAsync();

        await GdiContext.RenderAsync();
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
        var addCount = CountToAdd;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        var elements = await ShapeElementGenerator.GenPolygonGeometryElement(hwndWidth, hwndHeight, addCount);

        Direct2dContext.DrawingElements.AddRange(elements);
        GdiContext.DrawingElements.AddRange(elements);
        PolygonCount += elements.Count;

        stopwatch.Stop();
        DataGenerationTime = stopwatch.ElapsedMilliseconds;

        await Direct2dContext.RenderAsync();

        await GdiContext.RenderAsync();
    }

    [RelayCommand]
    private async Task AddRectangleGeometryAsync()
    {
        if (Running)
            return;

        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        var hwndWidth = Direct2dContext.Width;
        var hwndHeight = Direct2dContext.Height;
        var addCount = CountToAdd;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        var elements = await ShapeElementGenerator.GenRectangleGeometryElement(hwndWidth, hwndHeight, addCount);

        Direct2dContext.DrawingElements.AddRange(elements);
        GdiContext.DrawingElements.AddRange(elements);
        RectangleGeometryCount += elements.Count;

        stopwatch.Stop();
        DataGenerationTime = stopwatch.ElapsedMilliseconds;

        await Direct2dContext.RenderAsync();

        await GdiContext.RenderAsync();
    }

    [RelayCommand]
    private async Task AddRectangleAsync()
    {
        if (Running)
            return;

        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        var hwndWidth = Direct2dContext.Width;
        var hwndHeight = Direct2dContext.Height;
        var addCount = CountToAdd;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        var elements = await ShapeElementGenerator.GenRectangleElement(hwndWidth, hwndHeight, addCount);

        Direct2dContext.DrawingElements.AddRange(elements);
        GdiContext.DrawingElements.AddRange(elements);
        RectangleCount += elements.Count;

        stopwatch.Stop();
        DataGenerationTime = stopwatch.ElapsedMilliseconds;

        await Direct2dContext.RenderAsync();

        await GdiContext.RenderAsync();
    }

    [RelayCommand]
    private async Task AddLineAsync()
    {
        if (Running)
            return;

        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        var hwndWidth = Direct2dContext.Width;
        var hwndHeight = Direct2dContext.Height;
        var addCount = CountToAdd;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        var elements = await ShapeElementGenerator.GenLineElement(hwndWidth, hwndHeight, addCount);

        Direct2dContext.DrawingElements.AddRange(elements);
        GdiContext.DrawingElements.AddRange(elements);
        LineCount += elements.Count;

        stopwatch.Stop();
        DataGenerationTime = stopwatch.ElapsedMilliseconds;

        await Direct2dContext.RenderAsync();

        await GdiContext.RenderAsync();
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
        var addCount = CountToAdd;

        if (hwndWidth <= 0 || hwndHeight <= 0 || addCount <= 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        var elements = await ShapeElementGenerator.GenTextElement(hwndWidth, hwndHeight, addCount);

        Direct2dContext.DrawingElements.AddRange(elements);
        GdiContext.DrawingElements.AddRange(elements);
        TextCount += elements.Count;

        stopwatch.Stop();
        DataGenerationTime = stopwatch.ElapsedMilliseconds;

        await Direct2dContext.RenderAsync();

        await GdiContext.RenderAsync();
    }

    [RelayCommand]
    private async Task ExportToPdfAsync()
    {
        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        await Direct2dContext.RenderAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Running = true;
        using var _ = DeferAction.Create(() => Running = false);

        await Direct2dContext.RenderAsync();

        await GdiContext.RenderAsync();
    }

    public void Dispose()
    {
        Direct2dContext.Dispose();
        GdiContext.Dispose();
    }
}