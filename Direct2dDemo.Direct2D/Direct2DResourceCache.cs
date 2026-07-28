using System.Collections.Concurrent;
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


    private readonly ConcurrentDictionary<DrawingColor, Lazy<ID2D1SolidColorBrush>> _solidColorBrushCache = new();
    private readonly ConcurrentDictionary<(string FontFamily, float FontSize), Lazy<IDWriteTextFormat>> _textFormatCache = new();
    private readonly ConcurrentDictionary<PolygonGeometryElement, Lazy<ID2D1PathGeometry>> _polygonGeometryCache = new();
    private readonly ConcurrentDictionary<RectangleGeometryElement, Lazy<ID2D1RectangleGeometry>> _rectangleGeometryCache = new();
    private readonly ConcurrentDictionary<EllipseGeometryElement, Lazy<ID2D1EllipseGeometry>> _ellipseGeometryCache = new();
    private readonly ConcurrentDictionary<StrokeStyleProperties, Lazy<ID2D1StrokeStyle>> _strokeStyleCache = new();
    private readonly ConcurrentDictionary<(HatchStyle HatchStyle, DrawingColor HatchColor, DrawingColor BackgroundColor, int CellSize, int LineWidth), Lazy<ID2D1BitmapBrush>> _hatchBrushCache = new();

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

        return _hatchBrushCache.GetOrAdd(key, k => new Lazy<ID2D1BitmapBrush>(() =>
        {
            var bitmap = CreateHatchBitmap(k.HatchStyle, k.HatchColor, k.BackgroundColor, k.CellSize, k.LineWidth);
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

                return _d2dContext.CreateBitmapBrush(bitmap, bitmapBrushProperties, brushProperties);
            }
            finally
            {
                bitmap?.Dispose();
            }
        })).Value;
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
            HatchStyle.Horizontal => y < lineWidth,
            HatchStyle.Vertical => x < lineWidth,
            HatchStyle.Cross => y < lineWidth || x < lineWidth,
            HatchStyle.ForwardDiagonal => Math.Abs(x - y) < lineWidth,
            HatchStyle.BackwardDiagonal => Math.Abs(x + y - (cellSize - 1)) < lineWidth,
            HatchStyle.DiagCross => Math.Abs(x - y) < lineWidth || Math.Abs(x + y - (cellSize - 1)) < lineWidth,
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

        return _strokeStyleCache.GetOrAdd(props, p => new Lazy<ID2D1StrokeStyle>(() =>
            _d2dFactory.CreateStrokeStyle(p)
        )).Value;
    }

    public ID2D1EllipseGeometry GetOrCreateEllipseGeometryElement(EllipseGeometryElement element)
    {
        return _ellipseGeometryCache.GetOrAdd(element, el => new Lazy<ID2D1EllipseGeometry>(() =>
        {
            if (_d2dFactory is null)
                throw new InvalidOperationException("Direct2D factory is not created.");

            if (el.RadiusX <= 0 || el.RadiusY <= 0)
                throw new ArgumentOutOfRangeException("Radius must be greater than zero.");

            var ellipse = new Ellipse(new Vector2(el.Center.X, el.Center.Y), el.RadiusX, el.RadiusY);
            return _d2dFactory.CreateEllipseGeometry(ellipse);
        })).Value;
    }

    public ID2D1RectangleGeometry GetOrCreateRectangleGeometry(RectangleGeometryElement element)
    {
        return _rectangleGeometryCache.GetOrAdd(element, el => new Lazy<ID2D1RectangleGeometry>(() =>
        {
            if (_d2dFactory is null)
                throw new InvalidOperationException("Direct2D factory is not created.");

            if (el.Width <= 0 || el.Height <= 0)
                throw new ArgumentOutOfRangeException("Size must be greater than zero.");

            var rectangle = new RectangleF(el.TopLeft.X, el.TopLeft.Y, el.Width, el.Height);
            return _d2dFactory.CreateRectangleGeometry(rectangle);
        })).Value;
    }

    public ID2D1PathGeometry GetOrCreatePolygonGeometry(PolygonGeometryElement element)
    {
        return _polygonGeometryCache.GetOrAdd(element, el => new Lazy<ID2D1PathGeometry>(() =>
        {
            if (_d2dFactory is null)
                throw new InvalidOperationException("Direct2D factory is not created.");

            var geometry = _d2dFactory.CreatePathGeometry();
            using var sink = geometry.Open();
            sink.SetFillMode(FillMode.Winding);
            sink.BeginFigure(el.Points[0].ToVector2(), FigureBegin.Filled);

            for (var i = 1; i < el.Points.Count; i++)
            {
                sink.AddLine(el.Points[i].ToVector2());
            }

            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
            return geometry;
        })).Value;
    }

    public IDWriteTextFormat GetOrCreateTextFormat(string fontFamily, float fontSize)
    {
        if (_dwriteFactory is null)
            throw new InvalidOperationException("DirectWrite factory is not created.");

        if (fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        fontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Meiryo" : fontFamily.Trim();
        var key = (fontFamily, fontSize);

        return _textFormatCache.GetOrAdd(key, k => new Lazy<IDWriteTextFormat>(() =>
        {
            var textFormat = _dwriteFactory.CreateTextFormat(
                k.FontFamily,
                null,
                FontWeight.Normal,
                FontStyle.Normal,
                FontStretch.Normal,
                k.FontSize,
                "ja-JP");

            textFormat.TextAlignment = TextAlignment.Leading;
            textFormat.ParagraphAlignment = ParagraphAlignment.Near;
            return textFormat;
        })).Value;
    }

    public ID2D1SolidColorBrush GetOrCreateSolidColorBrush(DrawingColor color)
    {
        return _solidColorBrushCache.GetOrAdd(color, c => new Lazy<ID2D1SolidColorBrush>(() =>
        {
            var context = _d2dContext;
            if (context is null)
                throw new InvalidOperationException("Direct2D device context is not created.");

            var newBrush = context.CreateSolidColorBrush(ToD2DColor(c));
            if (newBrush is null)
                throw new InvalidOperationException("Failed to create solid color brush.");

            return newBrush;
        })).Value;
    }

    public ID2D1TransformedGeometry CreateTransformedGeometry(ID2D1Geometry sourceGeometry, Matrix3x2 transform)
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
        ClearLazyCache(_solidColorBrushCache);
        ClearLazyCache(_textFormatCache);
        ClearLazyCache(_polygonGeometryCache);
        ClearLazyCache(_rectangleGeometryCache);
        ClearLazyCache(_ellipseGeometryCache);
        ClearLazyCache(_strokeStyleCache);
        ClearLazyCache(_hatchBrushCache);
    }

    private static void ClearLazyCache<TKey, TValue>(ConcurrentDictionary<TKey, Lazy<TValue>> cache)
        where TKey : notnull
        where TValue : IDisposable
    {
        foreach (var item in cache.Values)
        {
            if (item.IsValueCreated)
            {
                item.Value?.Dispose();
            }
        }
        cache.Clear();
    }
}
