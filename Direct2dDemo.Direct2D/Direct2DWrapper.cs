using Direct2dDemo.Shared.Elements.GeometryElements;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Mathematics;
using D2D = Vortice.Direct2D1;
using D3D = Vortice.Direct3D;
using D3D11 = Vortice.Direct3D11;
using DCommon = Vortice.DCommon;
using DrawingColor = System.Drawing.Color;
using DWrite = Vortice.DirectWrite;
using DXGI = Vortice.DXGI;
using HatchStyle = Direct2dDemo.Shared.Enums.HatchStyle;
using SharedCapStyle = Direct2dDemo.Shared.Enums.CapStyle;
using SharedDashStyle = Direct2dDemo.Shared.Enums.DashStyle;
using SharedLineJoin = Direct2dDemo.Shared.Enums.LineJoin;

namespace Direct2dDemo.Direct2D;

internal sealed class Direct2DWrapper : IDisposable
{
    private D2D.ID2D1Factory1? _d2dFactory;
    private D2D.ID2D1Device? _d2dDevice;
    private D2D.ID2D1DeviceContext? _d2dContext;

    private D3D11.ID3D11Device? _d3dDevice;
    private D3D11.ID3D11DeviceContext? _d3dContext;

    private DXGI.IDXGIDevice? _dxgiDevice;
    private DXGI.IDXGIFactory2? _dxgiFactory;
    private DXGI.IDXGISwapChain1? _swapChain;
    private DWrite.IDWriteFactory? _dwriteFactory;

    private D2D.ID2D1Bitmap1? _targetBitmap;

    private nint _hwnd;
    private int _width;
    private int _height;

    private D3D.FeatureLevel _featureLevel;
    private bool _usingWarp;
    private bool _disposed;
    private bool _isDrawing;

    public int Width => _width;
    public int Height => _height;
    public DWrite.IDWriteFactory? DwriteFactory => _dwriteFactory;

    public D2D.ID2D1DeviceContext? Context
    {
        get
        {
            ThrowIfDisposed();
            return _d2dContext;
        }
    }

    public bool UsingWarp => _usingWarp;
    public D3D.FeatureLevel FeatureLevel => _featureLevel;
    public bool IsTargetReady => _swapChain != null && _targetBitmap != null;

    public Direct2DWrapper()
    {
        CreateD2DFactory();
        CreateD3DDevice();
        CreateD2DDeviceAndContext();
        GetDWriteFactory();
    }

    /// <summary>
    /// Bind the wrapper to a HWND.
    /// If the HWND is unchanged, only resize is performed.
    /// If the HWND changes, the swap chain and target bitmap are recreated.
    /// </summary>
    public void SetTarget(nint hwnd, int width, int height)
    {
        ThrowIfDisposed();

        if (hwnd == nint.Zero)
            throw new ArgumentException("hwnd is zero.", nameof(hwnd));

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_swapChain != null && _hwnd == hwnd)
        {
            TargetResized(width, height);
            return;
        }

        if (_isDrawing)
            throw new InvalidOperationException("Cannot change target while drawing.");

        ReleaseHwndTarget();

        _hwnd = hwnd;
        _width = width;
        _height = height;

        CreateSwapChain();
        CreateRenderTargetBitmap();

        EnsureTargetReady();
        _d2dContext.Target = _targetBitmap;
    }

    public D2D.ID2D1Bitmap1? CreateBitmap()
    {
        ThrowIfDisposed();

        if (_targetBitmap is null || _d2dContext is null)
            return null;

        var pixelSize = _targetBitmap.PixelSize;
        _targetBitmap.GetDpi(out var dpiX, out var dpiY);

        var bitmapProperties = new D2D.BitmapProperties1
        {
            PixelFormat = _targetBitmap.PixelFormat,
            DpiX = dpiX,
            DpiY = dpiY,
            BitmapOptions = D2D.BitmapOptions.Target
        };

        return _d2dContext.CreateBitmap(
            pixelSize,
            nint.Zero,
            0,
            bitmapProperties);
    }

    public void TargetResized(int width, int height)
    {
        ThrowIfDisposed();
        EnsureTargetReady();

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_swapChain == null)
        {
            _width = width;
            _height = height;
            return;
        }

        if (_width == width && _height == height)
            return;

        if (_isDrawing)
            throw new InvalidOperationException("Cannot resize target between BeginDraw and EndDraw.");

        _width = width;
        _height = height;

        ReleaseTargetBitmapOnly();

        _swapChain.ResizeBuffers(
            0,
            (uint)_width,
            (uint)_height,
            DXGI.Format.Unknown,
            DXGI.SwapChainFlags.None
        ).CheckError();

        CreateRenderTargetBitmap();
        _d2dContext.Target = _targetBitmap;
    }

    public void BeginDraw()
    {
        ThrowIfDisposed();
        EnsureTargetReady();

        if (_isDrawing)
            throw new InvalidOperationException("BeginDraw has already been called.");

        _d2dContext.BeginDraw();
        _isDrawing = true;
    }

    public void EndDraw()
    {
        if (_d2dContext is null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        ThrowIfDisposed();

        if (!_isDrawing)
            throw new InvalidOperationException("BeginDraw has not been called.");

        try
        {
            _d2dContext.EndDraw();
        }
        finally
        {
            _isDrawing = false;
        }
    }

    public void Present()
    {
        ThrowIfDisposed();
        EnsureTargetReady();

        _swapChain.Present(1, DXGI.PresentFlags.None).CheckError();
    }

    public void DrawFrame(Action<D2D.ID2D1DeviceContext> drawAction)
    {
        if (drawAction == null)
            throw new ArgumentNullException(nameof(drawAction));

        EnsureTargetReady();

        BeginDraw();
        drawAction(_d2dContext);
        EndDraw();
        Present();
    }

    public D2D.ID2D1TransformedGeometry CreateTransformedGeometry(
        D2D.ID2D1Geometry sourceGeometry,
        Matrix3x2 transform)
    {
        ThrowIfDisposed();

        if (sourceGeometry is null)
            throw new ArgumentNullException(nameof(sourceGeometry));

        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        return _d2dFactory.CreateTransformedGeometry(sourceGeometry, transform);
    }

    public void Clear(float r, float g, float b, float a)
    {
        ThrowIfDisposed();

        if (!_isDrawing)
            throw new InvalidOperationException("Clear must be called between BeginDraw and EndDraw.");

        _d2dContext?.Clear(new Vortice.Mathematics.Color4(r, g, b, a));
    }

    [MemberNotNull(nameof(_d2dFactory))]
    private void CreateD2DFactory()
    {
        _d2dFactory = D2D.D2D1.D2D1CreateFactory<D2D.ID2D1Factory1>(D2D.FactoryType.SingleThreaded);
    }

    private DWrite.IDWriteFactory GetDWriteFactory()
    {
        if (_dwriteFactory != null)
            return _dwriteFactory;

        _dwriteFactory = DWrite.DWrite.DWriteCreateFactory<DWrite.IDWriteFactory>();
        return _dwriteFactory;
    }

    private void CreateD3DDevice()
    {
        var featureLevelsWith11_1 = new[]
        {
            D3D.FeatureLevel.Level_11_1,
            D3D.FeatureLevel.Level_11_0,
            D3D.FeatureLevel.Level_10_1,
            D3D.FeatureLevel.Level_10_0,
            D3D.FeatureLevel.Level_9_3,
            D3D.FeatureLevel.Level_9_2,
            D3D.FeatureLevel.Level_9_1
        };

        var featureLevelsWithout11_1 = new[]
        {
            D3D.FeatureLevel.Level_11_0,
            D3D.FeatureLevel.Level_10_1,
            D3D.FeatureLevel.Level_10_0,
            D3D.FeatureLevel.Level_9_3,
            D3D.FeatureLevel.Level_9_2,
            D3D.FeatureLevel.Level_9_1
        };

        if (TryCreateD3DDevice(D3D.DriverType.Hardware, featureLevelsWith11_1, false))
            return;

        if (TryCreateD3DDevice(D3D.DriverType.Hardware, featureLevelsWithout11_1, false))
            return;

        if (TryCreateD3DDevice(D3D.DriverType.Warp, featureLevelsWithout11_1, true))
            return;

        throw new InvalidOperationException("Failed to create D3D11 device.");
    }

    private bool TryCreateD3DDevice(
        D3D.DriverType driverType,
        D3D.FeatureLevel[] featureLevels,
        bool usingWarp)
    {
        D3D11.ID3D11Device? d3dDevice = null;
        D3D11.ID3D11DeviceContext? d3dContext = null;

        try
        {
            var flags = D3D11.DeviceCreationFlags.BgraSupport;

            var result = D3D11.D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                driverType,
                flags,
                featureLevels,
                out d3dDevice,
                out _featureLevel,
                out d3dContext);

            if (result.Failure)
                return false;

            _d3dDevice = d3dDevice;
            _d3dContext = d3dContext;
            _usingWarp = usingWarp;
            _dxgiDevice = _d3dDevice.QueryInterface<DXGI.IDXGIDevice>();
            return true;
        }
        catch
        {
            d3dContext?.Dispose();
            d3dDevice?.Dispose();
            _dxgiDevice?.Dispose();
            _dxgiDevice = null;
            _d3dContext?.Dispose();
            _d3dContext = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
            return false;
        }
    }

    private void CreateD2DDeviceAndContext()
    {
        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        if (_dxgiDevice is null)
            throw new InvalidOperationException("DXGI device is not created.");

        _d2dDevice = _d2dFactory.CreateDevice(_dxgiDevice);
        if (_d2dDevice is null)
            throw new InvalidOperationException("Failed to create Direct2D device.");

        _d2dContext = _d2dDevice.CreateDeviceContext();
        if (_d2dContext is null)
            throw new InvalidOperationException("Failed to create Direct2D device context.");
    }

    private void CreateSwapChain()
    {
        if (_dxgiDevice is null)
            throw new InvalidOperationException("DXGI device is not created.");

        DXGI.IDXGIAdapter? adapter = null;

        try
        {
            adapter = _dxgiDevice.GetAdapter();
            _dxgiFactory = adapter.GetParent<DXGI.IDXGIFactory2>();
            if (_dxgiFactory is null)
                throw new InvalidOperationException("Failed to create DXGI factory.");

            var desc = new DXGI.SwapChainDescription1
            {
                Width = (uint)_width,
                Height = (uint)_height,
                Format = DXGI.Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new DXGI.SampleDescription(1, 0),
                BufferUsage = DXGI.Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = DXGI.Scaling.Stretch,
                SwapEffect = DXGI.SwapEffect.FlipSequential,
                AlphaMode = DXGI.AlphaMode.Ignore,
                Flags = DXGI.SwapChainFlags.None
            };

            if (_d3dDevice is null)
                throw new InvalidOperationException("D3D device is not created.");

            _swapChain = _dxgiFactory.CreateSwapChainForHwnd(
                _d3dDevice,
                _hwnd,
                desc,
                null,
                null);

            // Prevent DXGI from handling Alt+Enter automatically.
            _dxgiFactory.MakeWindowAssociation(_hwnd, DXGI.WindowAssociationFlags.IgnoreAltEnter);
        }
        finally
        {
            adapter?.Dispose();
        }
    }

    private void CreateRenderTargetBitmap()
    {
        if (_d2dContext is null)
            throw new InvalidOperationException("Direct2D device context is not created.");
        if (_swapChain is null)
            throw new InvalidOperationException("Swap chain is not created.");

        DXGI.IDXGISurface? backBuffer = null;

        try
        {
            backBuffer = _swapChain.GetBuffer<DXGI.IDXGISurface>(0);

            var bitmapProperties = new D2D.BitmapProperties1
            {
                PixelFormat = new DCommon.PixelFormat(
                    DXGI.Format.B8G8R8A8_UNorm,
                    DCommon.AlphaMode.Ignore),
                DpiX = 96.0f,
                DpiY = 96.0f,
                BitmapOptions = D2D.BitmapOptions.Target | D2D.BitmapOptions.CannotDraw
            };

            _targetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(
                backBuffer,
                bitmapProperties);
        }
        finally
        {
            backBuffer?.Dispose();
        }
    }

    private void ReleaseTargetBitmapOnly()
    {
        if (_d2dContext != null)
            _d2dContext.Target = null;

        _targetBitmap?.Dispose();
        _targetBitmap = null;
    }

    private void ReleaseHwndTarget()
    {
        ReleaseTargetBitmapOnly();
        _swapChain?.Dispose();
        _swapChain = null;
        _dxgiFactory?.Dispose();
        _dxgiFactory = null;
    }

    private void ReleaseDeviceResources()
    {
        ClearCache();

        _dwriteFactory?.Dispose();
        _dwriteFactory = null;

        _d2dContext?.Dispose();
        _d2dContext = null;
        _d2dDevice?.Dispose();
        _d2dDevice = null;
        _d2dFactory?.Dispose();
        _d2dFactory = null;
        _dxgiDevice?.Dispose();
        _dxgiDevice = null;
        _d3dContext?.Dispose();
        _d3dContext = null;
        _d3dDevice?.Dispose();
        _d3dDevice = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_isDrawing)
        {
            try
            {
                _d2dContext?.EndDraw();
            }
            catch
            {
                // Ignore during dispose.
            }
            finally
            {
                _isDrawing = false;
            }
        }

        ReleaseHwndTarget();
        ReleaseDeviceResources();

        _hwnd = IntPtr.Zero;
        _width = 0;
        _height = 0;
        _disposed = true;
    }

    [MemberNotNull(nameof(_d2dContext), nameof(_swapChain), nameof(_targetBitmap))]
    private void EnsureTargetReady()
    {
        if (_d2dContext == null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        if (_swapChain == null)
            throw new InvalidOperationException("DXGI swap chain is not created. Call SetTarget first.");

        if (_targetBitmap == null)
            throw new InvalidOperationException("Direct2D target bitmap is not created. Call SetTarget first.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DWrapper));
    }

    #region Cache

    // 各 10_000：有 cache 时约 372ms，无 cache 时约 441ms。
    private readonly Dictionary<DrawingColor, D2D.ID2D1SolidColorBrush> _solidColorBrushCache = new();

    private readonly Dictionary<(string FontFamily, float FontSize), DWrite.IDWriteTextFormat> _textFormatCache = new();
    private readonly Dictionary<PolygonGeometryElement, D2D.ID2D1PathGeometry> _polygonGeometryCache = new();
    private readonly Dictionary<RectangleGeometryElement, D2D.ID2D1RectangleGeometry> _rectangleGeometryCache = new();
    private readonly Dictionary<EllipseGeometryElement, D2D.ID2D1EllipseGeometry> _ellipseGeometryCache = new();
    private readonly Dictionary<D2D.StrokeStyleProperties, D2D.ID2D1StrokeStyle> _strokeStyleCache = new();
    private readonly Dictionary<(HatchStyle HatchStyle, DrawingColor HatchColor, DrawingColor BackgroundColor, int CellSize, int LineWidth), D2D.ID2D1BitmapBrush> _hatchBrushCache = new();

    public D2D.ID2D1BitmapBrush GetOrCreateHatchStyle(
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

        D2D.ID2D1BitmapBrush? brush = null;

        try
        {
            var bitmapBrushProperties = new D2D.BitmapBrushProperties
            {
                ExtendModeX = D2D.ExtendMode.Wrap,
                ExtendModeY = D2D.ExtendMode.Wrap,
                InterpolationMode = D2D.BitmapInterpolationMode.NearestNeighbor
            };

            var brushProperties = new D2D.BrushProperties
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

    private D2D.ID2D1Bitmap CreateHatchBitmap(
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

        var bitmapProperties = new D2D.BitmapProperties1
        {
            PixelFormat = new DCommon.PixelFormat(
                DXGI.Format.B8G8R8A8_UNorm,
                DCommon.AlphaMode.Premultiplied),
            DpiX = 96.0f,
            DpiY = 96.0f,
            BitmapOptions = D2D.BitmapOptions.None
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

    public D2D.ID2D1StrokeStyle GetOrCreateStrokeStyle(
        SharedCapStyle capStyle,
        SharedDashStyle dashStyle,
        SharedLineJoin lineJoin = SharedLineJoin.Miter)
    {
        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        var props = new D2D.StrokeStyleProperties
        {
            StartCap = (D2D.CapStyle)capStyle,
            EndCap = (D2D.CapStyle)capStyle,
            DashCap = (D2D.CapStyle)capStyle,
            MiterLimit = 10.0f,
            DashStyle = (D2D.DashStyle)dashStyle,
            LineJoin = (D2D.LineJoin)lineJoin,
            DashOffset = 0.0f
        };

        if (_strokeStyleCache.TryGetValue(props, out var cached))
            return cached;

        var strokeStyle = _d2dFactory.CreateStrokeStyle(props);
        _strokeStyleCache[props] = strokeStyle;
        return strokeStyle;
    }

    public D2D.ID2D1EllipseGeometry GetOrCreateEllipseGeometryElement(EllipseGeometryElement element)
    {
        if (_ellipseGeometryCache.TryGetValue(element, out var cached))
            return cached;

        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        if (element.RadiusX <= 0)
            throw new ArgumentOutOfRangeException(nameof(element.RadiusX));

        if (element.RadiusY <= 0)
            throw new ArgumentOutOfRangeException(nameof(element.RadiusY));

        D2D.ID2D1EllipseGeometry? geometry = null;

        try
        {
            var ellipse = new D2D.Ellipse(
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

    public D2D.ID2D1RectangleGeometry GetOrCreateRectangleGeometry(RectangleGeometryElement element)
    {
        if (_rectangleGeometryCache.TryGetValue(element, out var cached))
            return cached;

        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        if (element.Width <= 0)
            throw new ArgumentOutOfRangeException(nameof(element.Width));

        if (element.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(element.Height));

        D2D.ID2D1RectangleGeometry? geometry = null;

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

    public D2D.ID2D1PathGeometry GetOrCreatePolygonGeometry(PolygonGeometryElement element)
    {
        // not good to use PolygonElement as key directly, but for demo purpose it's fine.
        if (_polygonGeometryCache.TryGetValue(element, out var cached))
            return cached;

        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        var geometry = _d2dFactory.CreatePathGeometry();
        D2D.ID2D1GeometrySink? sink = null;

        try
        {
            sink = geometry.Open();

            sink.SetFillMode(D2D.FillMode.Winding);

            sink.BeginFigure(
                element.Points[0].ToVector2(),
                D2D.FigureBegin.Filled);

            for (var i = 1; i < element.Points.Count; i++)
            {
                sink.AddLine(element.Points[i].ToVector2());
            }

            sink.EndFigure(D2D.FigureEnd.Closed);
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

    public DWrite.IDWriteTextFormat GetOrCreateTextFormat(string fontFamily, float fontSize)
    {
        if (DwriteFactory is null)
            throw new InvalidOperationException("DirectWrite factory is not created.");

        if (fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        fontFamily = string.IsNullOrWhiteSpace(fontFamily)
            ? "Meiryo"
            : fontFamily.Trim();

        var key = (fontFamily, fontSize);

        if (_textFormatCache.TryGetValue(key, out var cached))
            return cached;

        var textFormat = DwriteFactory.CreateTextFormat(
            fontFamily,
            null,
            DWrite.FontWeight.Normal,
            DWrite.FontStyle.Normal,
            DWrite.FontStretch.Normal,
            fontSize,
            "ja-JP");

        textFormat.TextAlignment = DWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = DWrite.ParagraphAlignment.Near;

        _textFormatCache[key] = textFormat;
        return textFormat;
    }

    public D2D.ID2D1SolidColorBrush GetOrCreateSolidColorBrush(DrawingColor color)
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
