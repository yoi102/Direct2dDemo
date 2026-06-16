using Direct2dDemo.Shared;
using Direct2dDemo.Shared.Elements;
using System.Diagnostics;
using System.Drawing;

namespace Direct2dDemo.GDI;

public class GdiContext : IDrawingContext, IDrawingGdiContext, ICanvasContext
{
    private readonly GdiWrapper gdiWrapper = new();

    /// <summary>
    /// 保护 GDI back buffer。
    /// BeginDraw / EndDraw / BitBlt / Resize / Dispose 都不能并发。
    /// </summary>
    private readonly object _gdiLock = new();

    /// <summary>
    /// 保护 offset / scale / DrawingElements 快照。
    /// </summary>
    private readonly object _stateLock = new();

    /// <summary>
    /// 等待某一帧完成的调用者。
    /// </summary>
    private readonly object _renderWaitersLock = new();
    private readonly List<RenderWaiter> _renderWaiters = new();

    /// <summary>
    /// 最新请求的渲染版本。
    /// 每次 RequestRender 都 +1。
    /// </summary>
    private long _requestedRenderVersion;

    /// <summary>
    /// 已经完成的渲染版本。
    /// </summary>
    private long _completedRenderVersion;

    /// <summary>
    /// 渲染循环是否正在运行。
    /// 0 = 没有运行
    /// 1 = 正在运行
    /// </summary>
    private int _renderLoopRunning;

    /// <summary>
    /// 是否已经释放。
    /// </summary>
    private int _disposed;

    /// <summary>
    /// 用于把 Rendered 事件切回创建 GdiContext 的线程。
    /// 一般就是 UI 线程。
    /// </summary>
    private readonly SynchronizationContext? _syncContext;

    public event EventHandler<double>? Rendered;

    public int Width
    {
        get
        {
            lock (_gdiLock)
                return gdiWrapper.Width;
        }
    }

    public int Height
    {
        get
        {
            lock (_gdiLock)
                return gdiWrapper.Height;
        }
    }

    public List<IDrawingElement> DrawingElements { get; } = new();

    private static readonly Color background = Color.White;

    private float _panStartX;
    private float _panStartY;
    private float _offsetX;
    private float _offsetY;
    private float _panStartOffsetX;
    private float _panStartOffsetY;

    private float _scale = 1.0f;

    private const float MinScale = 0.05f;
    private const float MaxScale = 100.0f;

    public GdiContext()
    {
        _syncContext = SynchronizationContext.Current;
    }

    public Task RenderAsync()
    {
        return RenderCurrentViewAsync();
    }

    /// <summary>
    /// 请求渲染，并等待当前请求对应的帧完成。
    /// </summary>
    private Task RenderCurrentViewAsync()
    {
        if (IsDisposed)
            return Task.CompletedTask;

        var targetVersion = Interlocked.Increment(ref _requestedRenderVersion);

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_renderWaitersLock)
        {
            // 如果刚好已经完成，就直接返回。
            if (Volatile.Read(ref _completedRenderVersion) >= targetVersion)
            {
                tcs.TrySetResult(true);
            }
            else
            {
                _renderWaiters.Add(new RenderWaiter(targetVersion, tcs));
            }
        }

        StartRenderLoop();

        return tcs.Task;
    }

    /// <summary>
    /// 请求渲染，但不等待。
    /// 如果后续需要立即 BitBlt 最新画面，不要用这个。
    /// </summary>
    private void RequestRender()
    {
        if (IsDisposed)
            return;

        Interlocked.Increment(ref _requestedRenderVersion);

        StartRenderLoop();
    }

    private void WaitRenderCompleted()
    {
        RenderCurrentViewAsync().GetAwaiter().GetResult();
    }

    private void StartRenderLoop()
    {
        if (IsDisposed)
            return;

        if (Interlocked.CompareExchange(ref _renderLoopRunning, 1, 0) == 0)
        {
            _ = RenderLoopAsync();
        }
    }

    /// <summary>
    /// 后台渲染循环。
    /// 如果 Pan / Zoom 连续触发，会按版本推进，等待者只会在自己的版本完成后返回。
    /// </summary>
    private async Task RenderLoopAsync()
    {
        try
        {
            while (!IsDisposed)
            {
                var requestedVersion = Volatile.Read(ref _requestedRenderVersion);
                var completedVersion = Volatile.Read(ref _completedRenderVersion);

                if (requestedVersion <= completedVersion)
                    break;

                await RenderCurrentViewCoreAsync(requestedVersion).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            CompleteAllRenderWaiters(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _renderLoopRunning, 0);

            // 防止退出瞬间又来了新的请求。
            if (!IsDisposed)
            {
                var requestedVersion = Volatile.Read(ref _requestedRenderVersion);
                var completedVersion = Volatile.Read(ref _completedRenderVersion);

                if (requestedVersion > completedVersion &&
                    Interlocked.CompareExchange(ref _renderLoopRunning, 1, 0) == 0)
                {
                    _ = RenderLoopAsync();
                }
            }
        }
    }

    private async Task RenderCurrentViewCoreAsync(long targetVersion)
    {
        if (IsDisposed)
        {
            CompleteRenderWaitersUpTo(targetVersion, null);
            return;
        }

        bool targetReady;

        lock (_gdiLock)
        {
            targetReady = gdiWrapper.IsTargetReady;
        }

        if (!targetReady)
        {
            CompleteRenderWaitersUpTo(targetVersion, null);
            return;
        }

        IDrawingElement[] elements;
        float offsetX;
        float offsetY;
        float scale;

        lock (_stateLock)
        {
            // 避免后台线程 foreach 时 UI 线程修改 List。
            elements = DrawingElements.ToArray();

            // 固定当前视图参数。
            offsetX = _offsetX;
            offsetY = _offsetY;
            scale = _scale;
        }

        var stopwatch = Stopwatch.StartNew();
        Exception? renderException = null;

        try
        {
            await InternalRenderAsync(
                    elements,
                    offsetX,
                    offsetY,
                    scale)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            renderException = ex;
            Debug.WriteLine(ex);
        }

        stopwatch.Stop();

        CompleteRenderWaitersUpTo(targetVersion, renderException);

        if (renderException is null)
            RaiseRendered(stopwatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// 真正绘制在后台线程执行。
    /// 调用方可以等待这个 Task，但绘制本身不在 UI 线程。
    /// </summary>
    private async Task InternalRenderAsync(
        IReadOnlyList<IDrawingElement> elements,
        float offsetX,
        float offsetY,
        float scale)
    {
        await Task.Run(() =>
        {
            if (IsDisposed)
                return;

            lock (_gdiLock)
            {
                if (IsDisposed)
                    return;

                if (!gdiWrapper.IsTargetReady)
                    return;

                var beginDrawCalled = false;

                try
                {
                    gdiWrapper.BeginDraw();
                    beginDrawCalled = true;

                    gdiWrapper.Clear(background);

                    DrawSnapshot(
                        elements,
                        offsetX,
                        offsetY,
                        scale);
                }
                finally
                {
                    if (beginDrawCalled)
                    {
                        try
                        {
                            gdiWrapper.EndDraw();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex);
                        }
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    private void DrawSnapshot(
        IReadOnlyList<IDrawingElement> elements,
        float offsetX,
        float offsetY,
        float scale)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            elements[i].Draw(
                gdiWrapper,
                offsetX,
                offsetY,
                scale);
        }
    }

    public void Initialize(nint hwnd, int width, int height)
    {
        if (IsDisposed)
            return;

        lock (_gdiLock)
        {
            if (IsDisposed)
                return;

            gdiWrapper.SetTarget(hwnd, width, height);
        }

        // 初始化后等待第一帧画完。
        WaitRenderCompleted();
    }

    public void HwndResized(int width, int height)
    {
        if (IsDisposed)
            return;

        lock (_gdiLock)
        {
            if (IsDisposed)
                return;

            gdiWrapper.TargetResized(width, height);
        }

        // Resize 后等待新尺寸对应的帧画完。
        WaitRenderCompleted();
    }

    public void ClearData()
    {
        if (IsDisposed)
            return;

        lock (_stateLock)
        {
            DrawingElements.Clear();

            _offsetX = 0;
            _offsetY = 0;
            _panStartX = 0;
            _panStartY = 0;
            _panStartOffsetX = 0;
            _panStartOffsetY = 0;
            _scale = 1.0f;
        }

        // 清空后等待空画面画完。
        WaitRenderCompleted();
    }

    public void BitBlt()
    {
        if (IsDisposed)
            return;

        // 这里用 lock，不用 TryEnter。
        // 因为你要求等待完成，所以如果后台正在绘制，BitBlt 会等绘制结束。
        lock (_gdiLock)
        {
            if (!IsDisposed && gdiWrapper.IsTargetReady)
                gdiWrapper.BitBlt();
        }
    }

    public void BitBlt(nint hdc)
    {
        if (IsDisposed)
            return;

        if (hdc == nint.Zero)
            return;

        // 这里用 lock，不用 TryEnter。
        // 防止拷贝到半帧。
        lock (_gdiLock)
        {
            if (!IsDisposed && gdiWrapper.IsTargetReady)
                gdiWrapper.BitBlt(hdc);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        CompleteAllRenderWaiters(new ObjectDisposedException(nameof(GdiContext)));

        lock (_gdiLock)
        {
            gdiWrapper.Dispose();
        }
    }

    public void BeginPan(int x, int y)
    {
        if (IsDisposed)
            return;

        lock (_stateLock)
        {
            _panStartX = x;
            _panStartY = y;
            _panStartOffsetX = _offsetX;
            _panStartOffsetY = _offsetY;
        }
    }

    public void Pan(int x, int y)
    {
        if (IsDisposed)
            return;

        lock (_stateLock)
        {
            var deltaX = x - _panStartX;
            var deltaY = y - _panStartY;

            _offsetX = _panStartOffsetX + deltaX;
            _offsetY = _panStartOffsetY + deltaY;
        }

        // 你要求 Pan 等待绘制完成，所以这里等待。
        WaitRenderCompleted();
    }

    public void EndPan(int x, int y)
    {
        if (IsDisposed)
            return;

        lock (_stateLock)
        {
            _panStartX = 0;
            _panStartY = 0;
        }
    }

    public void Zoom(float zoomFactor, int centerX, int centerY)
    {
        if (IsDisposed)
            return;

        if (zoomFactor <= 0)
            return;

        lock (_stateLock)
        {
            var oldScale = _scale;
            var newScale = Clamp(oldScale * zoomFactor, MinScale, MaxScale);

            if (AlmostSame(oldScale, newScale))
                return;

            // screen = world * scale + offset
            // 保持鼠标所在 world 点缩放前后仍落在 centerX / centerY。
            var worldX = (centerX - _offsetX) / oldScale;
            var worldY = (centerY - _offsetY) / oldScale;

            _scale = newScale;
            _offsetX = centerX - worldX * newScale;
            _offsetY = centerY - worldY * newScale;
        }

        // 你要求 Zoom 等待绘制完成，所以这里等待。
        WaitRenderCompleted();
    }

    private void CompleteRenderWaitersUpTo(long completedVersion, Exception? exception)
    {
        List<RenderWaiter> completedWaiters = new();

        lock (_renderWaitersLock)
        {
            if (completedVersion > _completedRenderVersion)
                _completedRenderVersion = completedVersion;

            for (var i = _renderWaiters.Count - 1; i >= 0; i--)
            {
                var waiter = _renderWaiters[i];

                if (waiter.TargetVersion <= completedVersion)
                {
                    completedWaiters.Add(waiter);
                    _renderWaiters.RemoveAt(i);
                }
            }
        }

        foreach (var waiter in completedWaiters)
        {
            if (exception is null)
                waiter.Completion.TrySetResult(true);
            else
                waiter.Completion.TrySetException(exception);
        }
    }

    private void CompleteAllRenderWaiters(Exception? exception)
    {
        List<RenderWaiter> waiters;

        lock (_renderWaitersLock)
        {
            if (_renderWaiters.Count == 0)
                return;

            waiters = new List<RenderWaiter>(_renderWaiters);
            _renderWaiters.Clear();
        }

        foreach (var waiter in waiters)
        {
            if (exception is null)
                waiter.Completion.TrySetResult(true);
            else
                waiter.Completion.TrySetException(exception);
        }
    }

    private void RaiseRendered(double elapsedMilliseconds)
    {
        var handler = Rendered;
        if (handler is null)
            return;

        if (_syncContext is not null)
        {
            _syncContext.Post(_ =>
            {
                handler.Invoke(this, elapsedMilliseconds);
            }, null);
        }
        else
        {
            handler.Invoke(this, elapsedMilliseconds);
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }

    private static bool AlmostSame(float a, float b)
    {
        return Math.Abs(a - b) <= 0.000001f;
    }

    private sealed class RenderWaiter
    {
        public RenderWaiter(
            long targetVersion,
            TaskCompletionSource<bool> completion)
        {
            TargetVersion = targetVersion;
            Completion = completion;
        }

        public long TargetVersion { get; }

        public TaskCompletionSource<bool> Completion { get; }
    }
}