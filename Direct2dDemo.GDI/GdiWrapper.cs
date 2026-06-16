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

    public void BitBlt()
    {
        ThrowIfDisposed();
        EnsureTargetReady();
        var hdc = User32.GetDC(_hwnd);
        if (hdc == nint.Zero)
            throw new InvalidOperationException("GetDC failed.");

        if (!Gdi32.BitBlt(
                hdc,
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


        hdc?.Dispose();
    }
    public void BitBlt(nint hdc)
    {
        ThrowIfDisposed();
        try
        {
            EnsureTargetReady();
        }
        catch (Exception)
        {
            return;
        }
        if (hdc == nint.Zero)
            throw new InvalidOperationException("GetDC failed.");

        if (!Gdi32.BitBlt(
                hdc,
                0,
                0,
                _width,
                _height,
                _memoryDc,
                0,
                0,
                RasterOperationMode.SRCCOPY))
        {
            //throw new InvalidOperationException("BitBlt failed.");
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
    }

    public void Clear(Color color)
    {
        ThrowIfDisposed();
        EnsureTargetReady();

        if (!_isDrawing)
            throw new InvalidOperationException("Clear must be called between BeginDraw and EndDraw.");

        using var brush = DrawExtension.CreateSolidBrush(color);

        var rect = new RECT
        {
            left = 0,
            top = 0,
            right = _width,
            bottom = _height
        };

        User32.FillRect(_memoryDc, rect, brush);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_isDrawing)
            _isDrawing = false;

        ReleaseHwndTarget();

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
}