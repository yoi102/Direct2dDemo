using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using Vanara.PInvoke;
using static Vanara.PInvoke.Gdi32;

namespace Direct2dDemo.GDI;

internal sealed class GdiWrapper : IDisposable
{
    private nint _hwnd;
    private int _width;
    private int _height;

    private SafeHDC? _memoryDc;
    private SafeHBITMAP? _memoryBitmap;
    private HGDIOBJ _oldMemoryBitmap;

    private bool _isDrawing;
    private bool _disposed;

    private readonly Dictionary<Color, SafeHBRUSH> _brushCache = new();
    private readonly Dictionary<(Color Color, int Width), SafeHPEN> _penCache = new();
    private readonly Dictionary<(string FontFamily, int FontSize), SafeHFONT> _fontCache = new();

    public int Width => _width;
    public int Height => _height;

    public SafeHDC Hdc
    {
        get
        {
            ThrowIfDisposed();
            EnsureTargetReady();
            return _memoryDc;
        }
    }

    public bool IsTargetReady => _hwnd != nint.Zero && _memoryDc != null && _memoryBitmap != null;

    public void SetTarget(nint hwnd, int width, int height)
    {
        ThrowIfDisposed();

        if (hwnd == nint.Zero)
            throw new ArgumentException("hwnd is zero.", nameof(hwnd));

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_hwnd == hwnd && IsTargetReady)
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

        CreateBackBuffer();
    }

    public void TargetResized(int width, int height)
    {
        ThrowIfDisposed();

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_width == width && _height == height && IsTargetReady)
            return;

        if (_isDrawing)
            throw new InvalidOperationException("Cannot resize target between BeginDraw and EndDraw.");

        _width = width;
        _height = height;

        if (_hwnd == nint.Zero)
            return;

        ReleaseBackBuffer();
        CreateBackBuffer();
    }

    public void BeginDraw()
    {
        ThrowIfDisposed();
        EnsureTargetReady();

        if (_isDrawing)
            throw new InvalidOperationException("BeginDraw has already been called.");

        _isDrawing = true;
    }

    public void EndDraw()
    {
        ThrowIfDisposed();

        if (!_isDrawing)
            throw new InvalidOperationException("BeginDraw has not been called.");

        _isDrawing = false;
    }

    public void Present()
    {
        ThrowIfDisposed();
        EnsureTargetReady();

        var windowDc = User32.GetDC(_hwnd);
        if (windowDc == nint.Zero)
            throw new InvalidOperationException("GetDC failed.");

        try
        {
            if (!Gdi32.BitBlt(
                    windowDc,
                    0,
                    0,
                    _width,
                    _height,
                    _memoryDc,
                    0,
                    0,
                    RasterOperationMode.SRCCOPY))
            {
                throw new InvalidOperationException("BitBlt failed.");
            }
        }
        finally
        {
            User32.ReleaseDC(_hwnd, windowDc);
        }
    }

    public void DrawFrame(Action<SafeHDC> drawAction)
    {
        if (drawAction == null)
            throw new ArgumentNullException(nameof(drawAction));
        if (_memoryDc is null)
            throw new InvalidOperationException("Back buffer is not ready. Call SetTarget first.");

        BeginDraw();
        try
        {
            drawAction(_memoryDc);
        }
        finally
        {
            EndDraw();
        }

        Present();
    }

    public void Clear(Color color)
    {
        ThrowIfDisposed();
        EnsureTargetReady();

        if (!_isDrawing)
            throw new InvalidOperationException("Clear must be called between BeginDraw and EndDraw.");

        var brush = GetOrCreateSolidBrush(color);

        var rect = new RECT
        {
            left = 0,
            top = 0,
            right = _width,
            bottom = _height
        };

        User32.FillRect(_memoryDc, rect, brush);
    }

    public SafeHBRUSH GetOrCreateSolidBrush(Color color)
    {
        ThrowIfDisposed();

        if (_brushCache.TryGetValue(color, out var cached))
            return cached;

        var brush = Gdi32.CreateSolidBrush(ToColorRef(color));
        if (brush is null)
            throw new InvalidOperationException("CreateSolidBrush failed.");

        _brushCache[color] = brush;
        return brush;
    }

    public SafeHPEN GetOrCreatePen(Color color, float width)
    {
        ThrowIfDisposed();

        var penWidth = Math.Max(1, (int)Math.Round(width));
        var key = (color, penWidth);

        if (_penCache.TryGetValue(key, out var cached))
            return cached;

        var pen = Gdi32.CreatePen(Gdi32.PenStyle.PS_SOLID, penWidth, ToColorRef(color));
        if (pen == nint.Zero)
            throw new InvalidOperationException("CreatePen failed.");

        _penCache[key] = pen;
        return pen;
    }

    public SafeHFONT GetOrCreateFont(string? fontFamily, float fontSize)
    {
        ThrowIfDisposed();

        if (fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        var family = string.IsNullOrWhiteSpace(fontFamily)
            ? "Meiryo"
            : fontFamily.Trim();

        var size = Math.Max(1, (int)Math.Round(fontSize));
        var key = (family, size);

        if (_fontCache.TryGetValue(key, out var cached))
            return cached;

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

        _fontCache[key] = font;
        return font;
    }

    public void ClearCache()
    {
        foreach (var brush in _brushCache.Values)
            brush.Dispose();
        _brushCache.Clear();

        foreach (var pen in _penCache.Values)
            pen.Dispose();
        _penCache.Clear();

        foreach (var font in _fontCache.Values)
            font.Dispose();
        _fontCache.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_isDrawing)
            _isDrawing = false;

        ReleaseHwndTarget();
        ClearCache();

        _disposed = true;
    }

    [MemberNotNull(nameof(_memoryDc), nameof(_memoryBitmap))]
    private void EnsureTargetReady()
    {
        if (_hwnd == nint.Zero)
            throw new InvalidOperationException("HWND is not set. Call SetTarget first.");

        if (_memoryDc == null || _memoryBitmap == null)
            throw new InvalidOperationException("GDI back buffer is not created. Call SetTarget first.");
    }

    private void CreateBackBuffer()
    {
        if (_hwnd == nint.Zero)
            throw new InvalidOperationException("HWND is not set.");

        var windowDc = User32.GetDC(_hwnd);
        if (windowDc == nint.Zero)
            throw new InvalidOperationException("GetDC failed.");

        try
        {
            _memoryDc = Gdi32.CreateCompatibleDC(windowDc);
            if (_memoryDc == nint.Zero)
                throw new InvalidOperationException("CreateCompatibleDC failed.");

            _memoryBitmap = Gdi32.CreateCompatibleBitmap(windowDc, _width, _height);
            if (_memoryBitmap == nint.Zero)
                throw new InvalidOperationException("CreateCompatibleBitmap failed.");

            _oldMemoryBitmap = Gdi32.SelectObject(_memoryDc, _memoryBitmap);
            if (_oldMemoryBitmap == nint.Zero)
                throw new InvalidOperationException("SelectObject bitmap failed.");
        }
        finally
        {
            User32.ReleaseDC(_hwnd, windowDc);
        }
    }

    private void ReleaseHwndTarget()
    {
        ReleaseBackBuffer();

        _hwnd = nint.Zero;
        _width = 0;
        _height = 0;
    }

    private void ReleaseBackBuffer()
    {
        if (_memoryDc != null)
        {
            if (_oldMemoryBitmap != nint.Zero)
            {
                Gdi32.SelectObject(_memoryDc, _oldMemoryBitmap);
                _oldMemoryBitmap = nint.Zero;
            }
        }

        if (_memoryBitmap != null)
        {
            //Gdi32.DeleteObject(_memoryBitmap);
            _memoryBitmap.Dispose();
        }

        if (_memoryDc != null)
        {
            _memoryDc.Dispose();
            //Gdi32.DeleteDC(_memoryDc);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GdiWrapper));
    }

    internal static int ToColorRef(Color color)
    {
        // COLORREF is 0x00BBGGRR. GDI ignores alpha.
        return color.R | (color.G << 8) | (color.B << 16);
    }
}