using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Direct2dDemo.Shared;
using System.Runtime.InteropServices;

namespace Direct2dDemo.CustomControl.Avalonia;

public sealed class Win32DrawingHost : NativeControlHost
{
    private IntPtr _hwnd;
    private IntPtr _oldWndProc;
    private WndProcDelegate? _wndProcDelegate;

    private bool _initialized;

    private int _rightButtonX;
    private int _rightButtonY;

    public IDrawingContext? DrawingContext
    {
        get => GetValue(DrawingContextProperty);
        set => SetValue(DrawingContextProperty, value);
    }

    public static readonly StyledProperty<IDrawingContext?> DrawingContextProperty =
        AvaloniaProperty.Register<Win32DrawingHost, IDrawingContext?>(
            nameof(DrawingContext));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DrawingContextProperty)
        {
            _initialized = false;
            TryInitializeDrawingContext();
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            return base.CreateNativeControlCore(parent);

        _hwnd = CreateWindowEx(
            0,
            "STATIC",
            "",
            WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            0,
            0,
            1,
            1,
            parent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("CreateWindowEx failed.");

        _wndProcDelegate = WndProc;
        _oldWndProc = SetWindowLongPtr(
            _hwnd,
            GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

        TryInitializeDrawingContext();

        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (OperatingSystem.IsWindows())
        {
            if (_hwnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _oldWndProc);
                _oldWndProc = IntPtr.Zero;
            }

            DestroyWindow(control.Handle);
            _hwnd = IntPtr.Zero;
            _initialized = false;
            return;
        }

        base.DestroyNativeControlCore(control);
    }

    private void TryInitializeDrawingContext()
    {
        if (_initialized)
            return;

        if (_hwnd == IntPtr.Zero)
            return;

        if (DrawingContext is null)
            return;

        GetClientRect(_hwnd, out var rect);

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);

        DrawingContext.Initialize(_hwnd, width, height);
        _initialized = true;
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_SIZE:
                {
                    var width = LowWord(lParam);
                    var height = HighWord(lParam);

                    if (!_initialized)
                    {
                        TryInitializeDrawingContext();
                    }
                    else
                    {
                        DrawingContext?.HwndResized(width, height);
                    }

                    return IntPtr.Zero;
                }

            case WM_PAINT:
                {
                    return OnPaint(hwnd);
                }

            case WM_RBUTTONDOWN:
                {
                    _rightButtonX = GetX(lParam);
                    _rightButtonY = GetY(lParam);
                    SetCapture(hwnd);
                    return IntPtr.Zero;
                }

            case WM_RBUTTONUP:
                {
                    ReleaseCapture();
                    return IntPtr.Zero;
                }

            case WM_MOUSEMOVE:
                {
                    if (((int)wParam & MK_RBUTTON) != 0)
                    {
                        var x = GetX(lParam);
                        var y = GetY(lParam);

                        var deltaX = x - _rightButtonX;
                        var deltaY = y - _rightButtonY;

                        if (DrawingContext is ICanvasContext canvasContext)
                        {
                            canvasContext.Move(deltaX, deltaY);
                        }

                        // 建议更新，否则每次都是从右键按下点算总偏移，容易越拖越快
                        _rightButtonX = x;
                        _rightButtonY = y;

                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }

                    return IntPtr.Zero;
                }

            case WM_MOUSEWHEEL:
                {
                    var delta = GetWheelDelta(wParam);

                    var pt = new POINT
                    {
                        X = GetX(lParam),
                        Y = GetY(lParam)
                    };

                    ScreenToClient(hwnd, ref pt);

                    if (DrawingContext is ICanvasContext canvasContext)
                    {
                        canvasContext.Zoom(delta > 0 ? 1.1f : 0.9f, pt.X, pt.Y);
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }

                    return IntPtr.Zero;
                }
        }

        return CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    private IntPtr OnPaint(IntPtr hwnd)
    {
        if (DrawingContext is not IDrawingGdiContext drawingGdiContext)
        {
            return DefWindowProc(hwnd, WM_PAINT, IntPtr.Zero, IntPtr.Zero);
        }

        var ps = new PAINTSTRUCT();
        var hdc = BeginPaint(hwnd, ref ps);

        try
        {
            drawingGdiContext.BitBlt(hdc);
        }
        finally
        {
            EndPaint(hwnd, ref ps);
        }

        return IntPtr.Zero;
    }

    private static int LowWord(IntPtr value)
    {
        return (int)((long)value & 0xffff);
    }

    private static int HighWord(IntPtr value)
    {
        return (int)(((long)value >> 16) & 0xffff);
    }

    private static int GetX(IntPtr lParam)
    {
        return unchecked((short)((long)lParam & 0xffff));
    }

    private static int GetY(IntPtr lParam)
    {
        return unchecked((short)(((long)lParam >> 16) & 0xffff));
    }

    private static int GetWheelDelta(IntPtr wParam)
    {
        return unchecked((short)(((long)wParam >> 16) & 0xffff));
    }

    private delegate IntPtr WndProcDelegate(
        IntPtr hwnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;

    private const int GWLP_WNDPROC = -4;

    private const uint WM_SIZE = 0x0005;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MOUSEWHEEL = 0x020A;

    private const int MK_RBUTTON = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr hWnd,
        int nIndex,
        IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(
        IntPtr lpPrevWndFunc,
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr BeginPaint(
        IntPtr hWnd,
        ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EndPaint(
        IntPtr hWnd,
        ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(
        IntPtr hWnd,
        out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(
        IntPtr hWnd,
        ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(
        IntPtr hWnd,
        IntPtr lpRect,
        bool bErase);
}