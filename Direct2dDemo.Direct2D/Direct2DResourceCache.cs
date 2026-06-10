using Direct2dDemo.Shared.Elements.GeometryElements;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using DrawingColor = System.Drawing.Color;
using HatchStyle = Direct2dDemo.Shared.Enums.HatchStyle;
using SharedCapStyle = Direct2dDemo.Shared.Enums.CapStyle;
using SharedDashStyle = Direct2dDemo.Shared.Enums.DashStyle;
using SharedLineJoin = Direct2dDemo.Shared.Enums.LineJoin;

namespace Direct2dDemo.Direct2D;

internal class Direct2DResourceCache(ID2D1Factory d2D1Factory, IDWriteFactory iDWriteFactory, ID2D1DeviceContext d2dContext)
{
    private readonly ID2D1Factory _d2dFactory = d2D1Factory;
    private readonly IDWriteFactory _dwriteFactory = iDWriteFactory;
    private readonly ID2D1DeviceContext _d2dContext = d2dContext;

    #region Cache

    private readonly Dictionary<DrawingColor, ID2D1SolidColorBrush> _solidColorBrushCache = [];
    private readonly Dictionary<(string FontFamily, float FontSize), IDWriteTextFormat> _textFormatCache = [];
    private readonly Dictionary<PolygonGeometryElement, ID2D1PathGeometry> _polygonGeometryCache = [];
    private readonly Dictionary<RectangleGeometryElement, ID2D1RectangleGeometry> _rectangleGeometryCache = [];
    private readonly Dictionary<EllipseGeometryElement, ID2D1EllipseGeometry> _ellipseGeometryCache = [];
    private readonly Dictionary<StrokeStyleProperties, ID2D1StrokeStyle> _strokeStyleCache = [];
    private readonly Dictionary<(HatchStyle HatchStyle, DrawingColor HatchColor, DrawingColor BackgroundColor, int CellSize, int LineWidth), ID2D1BitmapBrush> _hatchBrushCache = [];

    public ID2D1BitmapBrush GetOrCreateHatchStyle(
        HatchStyle hatchStyle,
        DrawingColor hatchColor,
        DrawingColor backgroundColor,
        int cellSize = 8,
        int lineWidth = 1)
    {
        if (_d2dContext is null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        if (cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize));

        if (lineWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(lineWidth));

        var key = (hatchStyle, hatchColor, backgroundColor, cellSize, lineWidth);

        if (_hatchBrushCache.TryGetValue(key, out var cached))
            return cached;

        var bitmap = CreateHatchBitmap(
            hatchStyle,
            hatchColor,
            backgroundColor,
            cellSize,
            lineWidth);

        ID2D1BitmapBrush? brush = null;

        try
        {
            var bitmapBrushProperties = new BitmapBrushProperties
            {
                ExtendModeX = ExtendMode.Wrap,
                ExtendModeY = ExtendMode.Wrap,
                InterpolationMode = BitmapInterpolationMode.NearestNeighbor
            };

            var brushProperties = new BrushProperties
            {
                Opacity = 1.0f,
                Transform = Matrix3x2.Identity
            };

            brush = _d2dContext.CreateBitmapBrush(
                bitmap,
                bitmapBrushProperties,
                brushProperties);

            _hatchBrushCache[key] = brush;
            return brush;
        }
        catch
        {
            brush?.Dispose();
            throw;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private ID2D1Bitmap CreateHatchBitmap(
        HatchStyle hatchStyle,
        DrawingColor hatchColor,
        DrawingColor backgroundColor,
        int cellSize,
        int lineWidth)
    {
        if (_d2dContext is null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        var data = new byte[cellSize * cellSize * 4];

        for (var y = 0; y < cellSize; y++)
        {
            for (var x = 0; x < cellSize; x++)
            {
                WritePixel(data, cellSize, x, y, backgroundColor);
            }
        }

        for (var y = 0; y < cellSize; y++)
        {
            for (var x = 0; x < cellSize; x++)
            {
                if (IsHatchPixel(hatchStyle, x, y, cellSize, lineWidth))
                    WritePixel(data, cellSize, x, y, hatchColor);
            }
        }

        var bitmapProperties = new BitmapProperties1
        {
            PixelFormat = new PixelFormat(
                Format.B8G8R8A8_UNorm,
                Vortice.DCommon.AlphaMode.Premultiplied),
            DpiX = 96.0f,
            DpiY = 96.0f,
            BitmapOptions = BitmapOptions.None
        };

        var dataPtr = nint.Zero;

        try
        {
            dataPtr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, dataPtr, data.Length);

            return _d2dContext.CreateBitmap(
                new SizeI(cellSize, cellSize),
                dataPtr,
                (uint)(cellSize * 4),
                bitmapProperties);
        }
        finally
        {
            if (dataPtr != nint.Zero)
                Marshal.FreeHGlobal(dataPtr);
        }
    }

    private static void WritePixel(
        byte[] data,
        int width,
        int x,
        int y,
        DrawingColor color)
    {
        var offset = (y * width + x) * 4;
        var alpha = color.A / 255.0f;

        data[offset + 0] = (byte)(color.B * alpha);
        data[offset + 1] = (byte)(color.G * alpha);
        data[offset + 2] = (byte)(color.R * alpha);
        data[offset + 3] = color.A;
    }

    private static bool IsHatchPixel(
        HatchStyle hatchStyle,
        int x,
        int y,
        int cellSize,
        int lineWidth)
    {
        return hatchStyle switch
        {
            HatchStyle.Horizontal =>
                y < lineWidth,

            HatchStyle.Vertical =>
                x < lineWidth,

            HatchStyle.Cross =>
                y < lineWidth ||
                x < lineWidth,

            HatchStyle.ForwardDiagonal =>
                Math.Abs(x - y) < lineWidth,

            HatchStyle.BackwardDiagonal =>
                Math.Abs(x + y - (cellSize - 1)) < lineWidth,

            HatchStyle.DiagCross =>
                Math.Abs(x - y) < lineWidth ||
                Math.Abs(x + y - (cellSize - 1)) < lineWidth,

            _ => throw new ArgumentOutOfRangeException(nameof(hatchStyle))
        };
    }

    public ID2D1StrokeStyle GetOrCreateStrokeStyle(
        SharedCapStyle capStyle,
        SharedDashStyle dashStyle,
        SharedLineJoin lineJoin = SharedLineJoin.Miter)
    {
        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        var props = new StrokeStyleProperties
        {
            StartCap = (CapStyle)capStyle,
            EndCap = (CapStyle)capStyle,
            DashCap = (CapStyle)capStyle,
            MiterLimit = 10.0f,
            DashStyle = (DashStyle)dashStyle,
            LineJoin = (LineJoin)lineJoin,
            DashOffset = 0.0f
        };

        if (_strokeStyleCache.TryGetValue(props, out var cached))
            return cached;

        var strokeStyle = _d2dFactory.CreateStrokeStyle(props);
        _strokeStyleCache[props] = strokeStyle;
        return strokeStyle;
    }

    public ID2D1EllipseGeometry GetOrCreateEllipseGeometryElement(EllipseGeometryElement element)
    {
        if (_ellipseGeometryCache.TryGetValue(element, out var cached))
            return cached;

        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        if (element.RadiusX <= 0)
            throw new ArgumentOutOfRangeException(nameof(element.RadiusX));

        if (element.RadiusY <= 0)
            throw new ArgumentOutOfRangeException(nameof(element.RadiusY));

        ID2D1EllipseGeometry? geometry = null;

        try
        {
            var ellipse = new Ellipse(
                new Vector2(element.Center.X, element.Center.Y),
                element.RadiusX,
                element.RadiusY);

            geometry = _d2dFactory.CreateEllipseGeometry(ellipse);
            _ellipseGeometryCache[element] = geometry;
            return geometry;
        }
        catch
        {
            geometry?.Dispose();
            throw;
        }
    }

    public ID2D1RectangleGeometry GetOrCreateRectangleGeometry(RectangleGeometryElement element)
    {
        if (_rectangleGeometryCache.TryGetValue(element, out var cached))
            return cached;

        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        if (element.Width <= 0)
            throw new ArgumentOutOfRangeException(nameof(element.Width));

        if (element.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(element.Height));

        ID2D1RectangleGeometry? geometry = null;

        try
        {
            var rectangle = new RectangleF(
                element.TopLeft.X,
                element.TopLeft.Y,
                element.Width,
                element.Height);

            geometry = _d2dFactory.CreateRectangleGeometry(rectangle);
            _rectangleGeometryCache[element] = geometry;
            return geometry;
        }
        catch
        {
            geometry?.Dispose();
            throw;
        }
    }

    public ID2D1PathGeometry GetOrCreatePolygonGeometry(PolygonGeometryElement element)
    {
        // not good to use PolygonElement as key directly, but for demo purpose it's fine.
        if (_polygonGeometryCache.TryGetValue(element, out var cached))
            return cached;

        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        var geometry = _d2dFactory.CreatePathGeometry();
        ID2D1GeometrySink? sink = null;

        try
        {
            sink = geometry.Open();

            sink.SetFillMode(FillMode.Winding);

            sink.BeginFigure(
                element.Points[0].ToVector2(),
                FigureBegin.Filled);

            for (var i = 1; i < element.Points.Count; i++)
            {
                sink.AddLine(element.Points[i].ToVector2());
            }

            sink.EndFigure(FigureEnd.Closed);
            sink.Close();

            _polygonGeometryCache[element] = geometry;
            return geometry;
        }
        catch
        {
            geometry?.Dispose();
            throw;
        }
        finally
        {
            sink?.Dispose();
        }
    }

    public IDWriteTextFormat GetOrCreateTextFormat(string fontFamily, float fontSize)
    {
        if (_dwriteFactory is null)
            throw new InvalidOperationException("DirectWrite factory is not created.");

        if (fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        fontFamily = string.IsNullOrWhiteSpace(fontFamily)
            ? "Meiryo"
            : fontFamily.Trim();

        var key = (fontFamily, fontSize);

        if (_textFormatCache.TryGetValue(key, out var cached))
            return cached;

        var textFormat = _dwriteFactory.CreateTextFormat(
            fontFamily,
            null,
            FontWeight.Normal,
            FontStyle.Normal,
            FontStretch.Normal,
            fontSize,
            "ja-JP");

        textFormat.TextAlignment = TextAlignment.Leading;
        textFormat.ParagraphAlignment = ParagraphAlignment.Near;

        _textFormatCache[key] = textFormat;
        return textFormat;
    }

    public ID2D1SolidColorBrush GetOrCreateSolidColorBrush(DrawingColor color)
    {
        if (_solidColorBrushCache.TryGetValue(color, out var cache))
            return cache;

        var context = _d2dContext;
        if (context is null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        var newBrush = context.CreateSolidColorBrush(ToD2DColor(color));
        if (newBrush is null)
            throw new InvalidOperationException("Failed to create solid color brush.");

        _solidColorBrushCache[color] = newBrush;
        return newBrush;
    }
    public ID2D1TransformedGeometry CreateTransformedGeometry(
        ID2D1Geometry sourceGeometry,
        Matrix3x2 transform)
    {

        if (sourceGeometry is null)
            throw new ArgumentNullException(nameof(sourceGeometry));

        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        return _d2dFactory.CreateTransformedGeometry(sourceGeometry, transform);
    }

    private static Vortice.Mathematics.Color4 ToD2DColor(DrawingColor color)
    {
        return new Vortice.Mathematics.Color4(
            color.R / 255.0f,
            color.G / 255.0f,
            color.B / 255.0f,
            color.A / 255.0f);
    }

    public void ClearCache()
    {
        foreach (var item in _solidColorBrushCache.Values)
        {
            item?.Dispose();
        }
        _solidColorBrushCache.Clear();

        foreach (var item in _textFormatCache.Values)
        {
            item?.Dispose();
        }
        _textFormatCache.Clear();

        foreach (var item in _polygonGeometryCache.Values)
        {
            item?.Dispose();
        }
        _polygonGeometryCache.Clear();

        foreach (var item in _rectangleGeometryCache.Values)
        {
            item?.Dispose();
        }
        _rectangleGeometryCache.Clear();

        foreach (var item in _ellipseGeometryCache.Values)
        {
            item?.Dispose();
        }
        _ellipseGeometryCache.Clear();

        foreach (var item in _strokeStyleCache.Values)
        {
            item?.Dispose();
        }
        _strokeStyleCache.Clear();

        foreach (var item in _hatchBrushCache.Values)
        {
            item?.Dispose();
        }
        _hatchBrushCache.Clear();
    }

    #endregion Cache


}
