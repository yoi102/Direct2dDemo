using Direct2dDemo.Shared.Elements.GeometryElements;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.D2d1;
using static Vanara.PInvoke.D3D11;
using static Vanara.PInvoke.Dwrite;
using static Vanara.PInvoke.DXGI;
using D2D1_COLOR_F = Vanara.PInvoke.DXGI.D3DCOLORVALUE;

namespace Direct2dDemo.Direct2D;

internal sealed class Direct2DWrapper : IDisposable
{
    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;

    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;

    private IDXGIDevice? _dxgiDevice;
    private IDXGIFactory2? _dxgiFactory;
    private IDXGISwapChain1? _swapChain;
    private IDWriteFactory? _dwriteFactory;

    private ID2D1Bitmap1? _targetBitmap;

    private nint _hwnd;
    private int _width;
    private int _height;

    private D3D_FEATURE_LEVEL _featureLevel;
    private bool _usingWarp;
    private bool _disposed;
    private bool _isDrawing;

    public int Width => _width;
    public int Height => _height;
    public IDWriteFactory? DwriteFactory => _dwriteFactory;

    public ID2D1DeviceContext? Context
    {
        get
        {
            ThrowIfDisposed();
            return _d2dContext;
        }
    }

    public bool UsingWarp
    {
        get { return _usingWarp; }
    }

    public D3D_FEATURE_LEVEL FeatureLevel
    {
        get { return _featureLevel; }
    }

    public bool IsTargetReady
    {
        get { return _swapChain != null && _targetBitmap != null; }
    }

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

        _d2dContext.SetTarget(_targetBitmap);
    }


    public ID2D1Bitmap? CreateBitmap()
    {
        ThrowIfDisposed();

        if (_targetBitmap is null)
            return null;

        if (_d2dContext is null)
            return null;

        _targetBitmap.GetPixelSize(out var pixelSize);
        _targetBitmap.GetDpi(out var dpiX, out var dpiY);

        var pixelFormat = _targetBitmap.GetPixelFormat();

        var bitmapProperties = new D2D1_BITMAP_PROPERTIES1
        {
            pixelFormat = pixelFormat,
            dpiX = dpiX,
            dpiY = dpiY,
            bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET
        };



        var newBitmap = _d2dContext.CreateBitmap(
            pixelSize,
            nint.Zero,
            0,
            bitmapProperties
        );

        return newBitmap;
    }


    //public void SaveAsPdf(
    //ID2D1DeviceContext deviceContext,
    //float pageWidthDip,
    //float pageHeightDip,
    //Action<ID2D1DeviceContext> draw)
    //{
    //    IPrintDocumentPackageTarget
    //    this._d2dDevice.CreatePrintControl()

    //    deviceContext.GetTarget(out var oldTarget);

    //    var commandList = deviceContext.CreateCommandList();

    //    deviceContext.SetTarget(commandList);

    //    deviceContext.BeginDraw();

    //    deviceContext.SetTransform(D2D_MATRIX_3X2_F.Identity());
    //    deviceContext.Clear(new D3DCOLORVALUE(1f, 1f, 1f, 1f));

    //    draw(deviceContext);

    //    deviceContext.EndDraw();

    //    commandList.Close();

    //    deviceContext.SetTarget(oldTarget);
    //}

    //public void SaveAsPdf(
    //    ID2D1Device d2D1Device,
    //ID2D1DeviceContext deviceContext,
    //IWICImagingFactory wicFactory,
    //float pageWidthDip,
    //float pageHeightDip,
    //float rasterDPI,
    //string pdfPath,
    //Action<ID2D1DeviceContext> draw)
    //{
    //    const string printerName = "Microsoft Print to PDF";
    //    const string jobName = "Direct2D Save As PDF";

    //    if (File.Exists(pdfPath))
    //        File.Delete(pdfPath);

    //    System.Runtime.InteropServices.ComTypes.IStream? outputStream = null;
    //    IPrintDocumentPackageTarget? packageTarget = null;
    //    ID2D1PrintControl? printControl = null;
    //    ID2D1Image? oldTarget = null;
    //    ID2D1CommandList? commandList = null;

    //    try
    //    {
    //        // 1. 创建输出 PDF 文件流
    //        outputStream = CreateComFileStream(pdfPath);

    //        // 2. 创建 IPrintDocumentPackageTarget
    //        packageTarget =  CreatePrintDocumentPackageTarget(
    //            printerName,
    //            jobName,
    //            outputStream);

    //        var properties = new D2D1_PRINT_CONTROL_PROPERTIES()
    //        {
    //            fontSubset = D2D1_PRINT_FONT_SUBSET_MODE.D2D1_PRINT_FONT_SUBSET_MODE_DEFAULT,
    //            rasterDPI = rasterDPI,
    //            colorSpace = D2D1_COLOR_SPACE.D2D1_COLOR_SPACE_SRGB,

    //        };
    //        printControl = d2D1Device.CreatePrintControl(
    //            wicFactory,
    //            packageTarget,
    //            properties);

    //        // 4. 保存旧 target
    //        deviceContext.GetTarget(out oldTarget);

    //        // 5. 创建 command list
    //        commandList = deviceContext.CreateCommandList();

    //        // 6. 把 command list 设置为绘制目标
    //        deviceContext.SetTarget(commandList);

    //        deviceContext.BeginDraw();

    //        deviceContext.SetTransform(D2D_MATRIX_3X2_F.Identity());
    //        deviceContext.Clear(new D3DCOLORVALUE(1f, 1f, 1f, 1f));

    //        draw(deviceContext);

    //        deviceContext.EndDraw();

    //        // 7. 关闭 command list
    //        commandList.Close();

    //        // 8. 恢复旧 target
    //        deviceContext.SetTarget(oldTarget);

    //        // 9. 添加页面
    //        var pageSize = new D2D_SIZE_F
    //        {
    //            width = pageWidthDip,
    //            height = pageHeightDip
    //        };

    //        printControl.AddPage(
    //            commandList,
    //            pageSize,
    //            null,
    //            out _,
    //            out _);

    //        // 10. 结束打印任务，真正生成 PDF
    //        printControl.Close();
    //    }
    //    finally
    //    {
    //        if (oldTarget != null)
    //        {
    //            try
    //            {
    //                deviceContext.SetTarget(oldTarget);
    //            }
    //            catch
    //            {
    //                // 防止恢复 target 时二次异常
    //            }
    //        }
    //        SafeRelease(ref commandList);
    //        SafeRelease(ref oldTarget);
    //        SafeRelease(ref printControl);
    //        SafeRelease(ref packageTarget);
    //        SafeRelease(ref outputStream);
    //    }
    //}

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
            DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
            0
        );

        CreateRenderTargetBitmap();
        _d2dContext.SetTarget(_targetBitmap);
    }

    public void BeginDraw()
    {
        ThrowIfDisposed();
        EnsureTargetReady();

        if (_isDrawing)
            throw new InvalidOperationException("BeginDraw has already been called.");

        _d2dContext.SetTarget(_targetBitmap);
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
            _d2dContext.EndDraw().ThrowIfFailed();
        }
        catch
        {
            throw;
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

        _swapChain.Present(1, 0).ThrowIfFailed();
    }

    public void DrawFrame(Action<ID2D1DeviceContext> drawAction)
    {
        if (drawAction == null)
            throw new ArgumentNullException(nameof(drawAction));
        EnsureTargetReady();

        BeginDraw();

        drawAction(_d2dContext);

        EndDraw();

        Present();
    }

    public void Clear(float r, float g, float b, float a)
    {
        ThrowIfDisposed();

        if (!_isDrawing)
            throw new InvalidOperationException("Clear must be called between BeginDraw and EndDraw.");

        _d2dContext?.Clear(new D3DCOLORVALUE
        {
            r = r,
            g = g,
            b = b,
            a = a
        });
    }

    [MemberNotNull(nameof(_d2dFactory))]
    private void CreateD2DFactory()
    {
        _d2dFactory = D2D1CreateFactory<ID2D1Factory1>(
            D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED
        );
    }

    private IDWriteFactory GetDWriteFactory()
    {
        if (_dwriteFactory != null)
            return _dwriteFactory;

        DWriteCreateFactory(
            DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
            typeof(IDWriteFactory).GUID,
            out var factory
        ).ThrowIfFailed();

        _dwriteFactory = (IDWriteFactory)factory;
        return _dwriteFactory;
    }

    private void CreateD3DDevice()
    {
        var featureLevelsWith11_1 = new[]
        {
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_2,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1
            };

        var featureLevelsWithout11_1 = new[]
        {
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_2,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1
            };

        // 1. Try hardware with 11_1.
        // 2. If the OS/runtime does not accept 11_1, try hardware without 11_1.
        // 3. Last fallback: WARP.
        if (TryCreateD3DDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, featureLevelsWith11_1, false))
            return;

        if (TryCreateD3DDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, featureLevelsWithout11_1, false))
            return;

        if (TryCreateD3DDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, featureLevelsWithout11_1, true))
            return;

        throw new InvalidOperationException("Failed to create D3D11 device.");
    }

    private bool TryCreateD3DDevice(D3D_DRIVER_TYPE driverType, D3D_FEATURE_LEVEL[] featureLevels, bool usingWarp)
    {
        try
        {
            var flags = D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;

            D3D11CreateDevice(
                null,
                driverType,
                IntPtr.Zero,
                flags,
                featureLevels,
                (uint)featureLevels.Length,
                D3D11_SDK_VERSION,
                out _d3dDevice,
                out _featureLevel,
                out _d3dContext
            ).ThrowIfFailed();

            _usingWarp = usingWarp;
            _dxgiDevice = _d3dDevice as IDXGIDevice;
            return true;
        }
        catch
        {
            SafeRelease(ref _dxgiDevice);
            SafeRelease(ref _d3dContext);
            SafeRelease(ref _d3dDevice);
            return false;
        }
    }

    private void CreateD2DDeviceAndContext()
    {
        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        if (_dxgiDevice is null)
            throw new InvalidOperationException("DXGI device is not created.");

        _d2dFactory.CreateDevice(_dxgiDevice, out ID2D1Device device);
        if (device is null)
            throw new InvalidOperationException("Failed to create Direct2D device.");
        _d2dDevice = device;

        var context = _d2dDevice.CreateDeviceContext(
               D2D1_DEVICE_CONTEXT_OPTIONS.D2D1_DEVICE_CONTEXT_OPTIONS_NONE
           );
        if (context is null)
            throw new InvalidOperationException("Failed to create Direct2D device context.");

        _d2dContext = context;
    }

    private void CreateSwapChain()
    {
        if (_dxgiDevice is null)
            throw new InvalidOperationException("DXGI device is not created.");

        IDXGIAdapter? adapter = null;

        try
        {
            adapter = _dxgiDevice.GetAdapter();
            _dxgiFactory = adapter.GetParent<IDXGIFactory2>();
            if (_dxgiFactory is null)
                throw new InvalidOperationException("Failed to create dxgi factory.");

            var desc = new DXGI_SWAP_CHAIN_DESC1
            {
                Width = (uint)_width,
                Height = (uint)_height,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                Stereo = false,
                SampleDesc = new DXGI_SAMPLE_DESC
                {
                    Count = 1,
                    Quality = 0
                },
                BufferUsage = DXGI_USAGE.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                BufferCount = 2,
                Scaling = DXGI_SCALING.DXGI_SCALING_STRETCH,
                SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL,
                AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE,
                Flags = 0
            };

            if (_d3dDevice is null)
                throw new InvalidOperationException("D3D device is not created.");

            _swapChain = _dxgiFactory.CreateSwapChainForHwnd(
                _d3dDevice,
                _hwnd,
                desc,
                null,
                null
            );

            // Prevent DXGI from handling Alt+Enter automatically.
            // If this enum is unavailable in your Vanara version, this line can be removed safely.
            _dxgiFactory.MakeWindowAssociation(_hwnd, DXGI_MWA.DXGI_MWA_NO_ALT_ENTER);
        }
        finally
        {
            SafeRelease(ref adapter);
        }
    }

    private void CreateRenderTargetBitmap()
    {
        if (_d2dContext is null)
            throw new InvalidOperationException("Direct2D device context is not created.");
        if (_swapChain is null)
            throw new InvalidOperationException("Swap chain is not created.");

        IDXGISurface? backBuffer = null;
        nint bitmapPropertiesPtr = nint.Zero;

        try
        {
            backBuffer = _swapChain.GetBuffer<IDXGISurface>(0);

            var bitmapProperties = new D2D1_BITMAP_PROPERTIES1
            {
                pixelFormat = new D2D1_PIXEL_FORMAT
                {
                    format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_IGNORE
                },
                dpiX = 96.0f,
                dpiY = 96.0f,
                bitmapOptions =
                    D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET |
                    D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_CANNOT_DRAW
            };

            bitmapPropertiesPtr = Marshal.AllocHGlobal(Marshal.SizeOf<D2D1_BITMAP_PROPERTIES1>());
            Marshal.StructureToPtr(bitmapProperties, bitmapPropertiesPtr, false);

            _targetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(
                backBuffer,
                bitmapPropertiesPtr
            );
        }
        finally
        {
            if (bitmapPropertiesPtr != nint.Zero)
                Marshal.FreeHGlobal(bitmapPropertiesPtr);
            SafeRelease(ref backBuffer);
        }
    }

    private void ReleaseTargetBitmapOnly()
    {
        if (_d2dContext != null)
            _d2dContext.SetTarget(null);

        SafeRelease(ref _targetBitmap);
    }

    private void ReleaseHwndTarget()
    {
        ReleaseTargetBitmapOnly();
        SafeRelease(ref _swapChain);
        SafeRelease(ref _dxgiFactory);
    }

    private void ReleaseDeviceResources()
    {
        SafeRelease(ref _dwriteFactory);
        SafeRelease(ref _d2dContext);
        SafeRelease(ref _d2dDevice);
        SafeRelease(ref _d2dFactory);
        SafeRelease(ref _dxgiDevice);
        SafeRelease(ref _d3dContext);
        SafeRelease(ref _d3dDevice);
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

    public static void SafeRelease<T>(ref T? comObject)
        where T : class
    {
        if (comObject == null)
            return;

        try
        {
            if (comObject is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else if (Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
        catch
        {
        }
        finally
        {
            comObject = null;
        }
    }

    #region Cache

    //各10_000 有cache 时，372ms  ,无cache时，441ms
    //耗时差距不太明显
    private Dictionary<Color, ID2D1SolidColorBrush> _solidColorBrushCacahe = new();

    private readonly Dictionary<(string FontFamily, float FontSize), IDWriteTextFormat> _textFormatCache = new();
    private readonly Dictionary<PolygonGeometryElement, ID2D1PathGeometry> _pathGeometryCache = new();

    public ID2D1PathGeometry GetOrCreatePathGeometry(PolygonGeometryElement element)
    {
        //not good to use PolygonElement as key directly, but for demo purpose it's fine.
        if (_pathGeometryCache.TryGetValue(element, out var cached))
            return cached;

        if (_d2dFactory is null)
            throw new InvalidOperationException("Direct2D factory is not created.");

        var geometry = _d2dFactory.CreatePathGeometry();

        ID2D1GeometrySink? sink = null;

        try
        {
            sink = geometry.Open();

            sink.SetFillMode(D2D1_FILL_MODE.D2D1_FILL_MODE_WINDING);

            sink.BeginFigure(
                element.Points[0],
                D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);

            for (var i = 1; i < element.Points.Count; i++)
            {
                sink.AddLine(element.Points[i]);
            }

            sink.EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);

            sink.Close().ThrowIfFailed();

            _pathGeometryCache[element] = geometry;
            return geometry;
        }
        catch
        {
            SafeRelease(ref geometry);
            throw;
        }
        finally
        {
            SafeRelease(ref sink);
        }
    }

    public IDWriteTextFormat GetOrCreateTextFormat(string fontFamily, float fontSize)
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
            DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
            DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
            fontSize,
            "ja-JP"
        );

        textFormat.SetTextAlignment(
            DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING
        );

        textFormat.SetParagraphAlignment(
            DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR
        );

        _textFormatCache[key] = textFormat;
        return textFormat;
    }

    public ID2D1SolidColorBrush GetOrCreateSolidColorBrush(Color color)
    {
        if (_solidColorBrushCacahe.TryGetValue(color, out var cache))
        {
            return cache;
        }

        var context = _d2dContext;
        if (context is null)
            throw new InvalidOperationException("Direct2D device context is not created.");

        var new_brush = context.CreateSolidColorBrush(ToD2DColor(color));
        if (new_brush is null)
            throw new InvalidOperationException("Failed to create solid color brush.");

        _solidColorBrushCacahe[color] = new_brush;

        return new_brush;
    }

    private static D2D1_COLOR_F ToD2DColor(Color color)
    {
        return new D2D1_COLOR_F
        {
            r = color.R / 255.0f,
            g = color.G / 255.0f,
            b = color.B / 255.0f,
            a = color.A / 255.0f
        };
    }

    public void ClearCache()
    {
        foreach (var brush in _solidColorBrushCacahe.Values)
        {
            var item = brush;
            SafeRelease(ref item);
        }
        _solidColorBrushCacahe.Clear();
        foreach (var textFormat in _textFormatCache.Values)
        {
            var item = textFormat;
            SafeRelease(ref item);
        }
        _textFormatCache.Clear();
        foreach (var geometry in _pathGeometryCache.Values)
        {
            var item = geometry;
            SafeRelease(ref item);
        }
        _pathGeometryCache.Clear();
    }

    #endregion Cache
}