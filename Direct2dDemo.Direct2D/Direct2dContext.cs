using Direct2dDemo.Shared;
using Direct2dDemo.Shared.Elements;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dDemo.Direct2D;

public class Direct2dContext : IDrawingContext, ICanvasContext
{
    private readonly Stopwatch _stopwatch = new();
    private readonly Direct2DWrapper _direct2DWrapper = new();
    private readonly MultiDeviceRenderer _multiDeviceRenderer;

    /// <summary>
    /// 保护 DrawingElements、offset、scale 等状态。
    /// 外部批量添加请使用 AddDrawingElements，避免渲染线程创建快照时与 List 写入冲突。
    /// </summary>
    private readonly object _stateLock = new();

    /// <summary>
    /// 保证“检查 Dispose 状态”和“投递/唤醒渲染线程”是一个原子操作。
    /// </summary>
    private readonly object _lifecycleLock = new();

    /// <summary>
    /// 渲染线程命令队列。Resize、Reset、Dispose 等 Direct2D 资源操作都放到渲染线程执行。
    /// </summary>
    private readonly ConcurrentQueue<RenderThreadAction> _renderActions = new();

    /// <summary>
    /// 通知渲染线程有新任务。
    /// </summary>
    private readonly AutoResetEvent _renderSignal = new(false);

    /// <summary>
    /// 专用后台渲染线程。
    /// </summary>
    private readonly Thread _renderThread;

    /// <summary>
    /// 等待某一帧渲染完成的调用者。
    /// </summary>
    private readonly object _renderWaitersLock = new();
    private readonly List<TaskCompletionSource<bool>> _renderWaiters = new();

    /// <summary>
    /// 是否有渲染请求。
    /// 0 = 无请求，1 = 有请求。
    /// 多次 Pan / Zoom 会被合并成最新一帧。
    /// </summary>
    private int _renderRequested;

    /// <summary>
    /// Dispose 是否已经开始。
    /// </summary>
    private int _disposeStarted;

    /// <summary>
    /// 这些字段保存调用线程期望的配置；真正的 GPU 资源变更统一在渲染线程应用。
    /// </summary>
    private int _multiThreadEnabled;
    private int _multiThreadDeviceCount = MultiDeviceRenderer.DefaultDeviceCount;
    private int _multiThreadThreshold = MultiDeviceRenderer.DefaultThreshold;
    private int _multiThreadPartitionMode = (int)MultiThreadPartitionMode.Auto;

    /// <summary>
    /// 用于把 Rendered 事件切回创建 Direct2dContext 的线程。
    /// 一般是 UI 线程。
    /// </summary>
    private readonly SynchronizationContext? _syncContext;

    private static readonly Color4 BackgroundColor = new(1f, 1f, 1f, 1.0f);

    private const float MinScale = 0.05f;
    private const float MaxScale = 100.0f;

    private float _panStartX;
    private float _panStartY;
    private float _offsetX;
    private float _offsetY;
    private float _panStartOffsetX;
    private float _panStartOffsetY;
    private float _scale = 1.0f;

    public Direct2dContext()
    {
        _syncContext = SynchronizationContext.Current;

        _multiDeviceRenderer = new MultiDeviceRenderer(_direct2DWrapper);

        _renderThread = new Thread(RenderThreadMain)
        {
            IsBackground = true,
            Name = "Direct2D Background Render Thread"
        };

        _renderThread.Start();
    }

    public event EventHandler<double>? Rendered;

    public int Width => _direct2DWrapper.Width;
    public int Height => _direct2DWrapper.Height;

    public List<IDrawingElement> DrawingElements { get; } = new();

    public void AddDrawingElements(IEnumerable<IDrawingElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        if (IsDisposing)
            return;

        lock (_stateLock)
        {
            DrawingElements.AddRange(elements);
        }
    }

    /// <summary>
    /// 开启后：元素数量超过 MultiThreadThreshold 时，使用多个独立 Device 离屏并行绘制。
    /// </summary>
    public bool EnableMultiThread
    {
        get => Volatile.Read(ref _multiThreadEnabled) != 0;
        set
        {
            var newValue = value ? 1 : 0;
            if (Interlocked.Exchange(ref _multiThreadEnabled, newValue) == newValue)
                return;

            QueueMultiThreadSettingsUpdate();
        }
    }

    /// <summary>
    /// 多 Device 数量。建议 2～4；对象特别多时再提高。
    /// </summary>
    public int MultiThreadDeviceCount
    {
        get => Volatile.Read(ref _multiThreadDeviceCount);
        set
        {
            var newValue = MultiDeviceRenderer.NormalizeDeviceCount(value);
            if (Interlocked.Exchange(ref _multiThreadDeviceCount, newValue) == newValue)
                return;

            QueueMultiThreadSettingsUpdate();
        }
    }

    /// <summary>
    /// 元素太少时，多 Device 的离屏绘制 + 合成成本会超过收益。
    /// </summary>
    public int MultiThreadThreshold
    {
        get => Volatile.Read(ref _multiThreadThreshold);
        set
        {
            var newValue = Math.Max(0, value);
            if (Interlocked.Exchange(ref _multiThreadThreshold, newValue) == newValue)
                return;

            QueueMultiThreadSettingsUpdate();
        }
    }

    /// <summary>
    /// Auto 会根据元素跨 tile 重复率和负载偏斜自动选择 Tiles 或 ElementChunks。
    /// </summary>
    public MultiThreadPartitionMode MultiThreadPartitionMode
    {
        get => (MultiThreadPartitionMode)Volatile.Read(ref _multiThreadPartitionMode);
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            var newValue = (int)value;
            if (Interlocked.Exchange(ref _multiThreadPartitionMode, newValue) == newValue)
                return;
               
            QueueMultiThreadSettingsUpdate();
        }
    }

    public void Initialize(nint hwnd, int width, int height)
    {
        if (IsDisposing)
            return;

        PostRenderAction(() =>
        {
            _multiDeviceRenderer.Reset();
            _direct2DWrapper.SetTarget(hwnd, width, height);
        });

        RequestRender();
    }

    public void HwndResized(int width, int height)
    {
        if (IsDisposing)
            return;

        PostRenderAction(() =>
        {
            // Resize 后，离屏共享 Texture 尺寸已经不匹配，必须重建。
            _multiDeviceRenderer.Reset();

            // DXGI 的 ResizeBuffers 本身就应该响应窗口尺寸变化时调用。
            _direct2DWrapper.TargetResized(width, height);
        });

        RequestRender();
    }

    public Task RenderAsync()
    {
        return RenderCurrentViewAsync();
    }

    public void ClearData()
    {
        if (IsDisposing)
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

        PostRenderAction(() =>
        {
            if (_direct2DWrapper.Context is not null)
                _direct2DWrapper.Context.Transform = Matrix3x2.Identity;

            _direct2DWrapper.Direct2DResourceCache?.ClearCache();
            _multiDeviceRenderer.ClearResourceCaches();
        });

        RequestRender();
    }

    public void Dispose()
    {
        var disposeCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lifecycleLock)
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
                return;

            _renderActions.Enqueue(new RenderThreadAction(
                action: () =>
                {
                    CompleteRenderWaiters(new ObjectDisposedException(nameof(Direct2dContext)));
                    try
                    {
                        _multiDeviceRenderer.Dispose();
                    }
                    finally
                    {
                        _direct2DWrapper.Dispose();
                    }
                },
                completion: disposeCompletion,
                stopAfterExecute: true));

            _renderSignal.Set();
        }

        // Rendered 事件没有 SynchronizationContext 时可能就在渲染线程触发。
        // 此时只投递停止命令，不能等待自己，否则会死锁。
        if (Thread.CurrentThread == _renderThread)
            return;

        try
        {
            disposeCompletion.Task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        _renderThread.Join();
    }

    public void BeginPan(int x, int y)
    {
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
        if (IsDisposing)
            return;

        lock (_stateLock)
        {
            _offsetX = _panStartOffsetX + (x - _panStartX);
            _offsetY = _panStartOffsetY + (y - _panStartY);
        }

        RequestRender();
    }

    public void EndPan(int x, int y)
    {
        lock (_stateLock)
        {
            _panStartX = 0;
            _panStartY = 0;
        }
    }

    public void Zoom(float zoomFactor, int centerX, int centerY)
    {
        if (IsDisposing)
            return;

        if (zoomFactor <= 0)
            return;

        lock (_stateLock)
        {
            var oldScale = _scale;
            var newScale = Clamp(oldScale * zoomFactor, MinScale, MaxScale);

            if (AlmostSame(oldScale, newScale))
                return;

            var worldX = (centerX - _offsetX) / oldScale;
            var worldY = (centerY - _offsetY) / oldScale;

            _scale = newScale;
            _offsetX = centerX - worldX * newScale;
            _offsetY = centerY - worldY * newScale;
        }

        RequestRender();
    }

    /// <summary>
    /// UI 线程调用这个方法时不会执行绘制，只是通知后台渲染线程。
    /// 多次调用会合并，只保留最新一帧。
    /// </summary>
    private void RequestRender()
    {
        lock (_lifecycleLock)
        {
            if (IsDisposing)
                return;

            Interlocked.Exchange(ref _renderRequested, 1);
            _renderSignal.Set();
        }
    }

    /// <summary>
    /// 外部 await RenderAsync() 时，等待后台线程完成一帧。
    /// </summary>
    private Task RenderCurrentViewAsync()
    {
        lock (_lifecycleLock)
        {
            if (IsDisposing)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_renderWaitersLock)
            {
                _renderWaiters.Add(tcs);
            }

            Interlocked.Exchange(ref _renderRequested, 1);
            _renderSignal.Set();

            return tcs.Task;
        }
    }

    /// <summary>
    /// 把 Direct2D 资源相关操作放到后台渲染线程执行。
    /// </summary>
    private void PostRenderAction(Action action)
    {
        lock (_lifecycleLock)
        {
            if (IsDisposing)
                return;

            _renderActions.Enqueue(new RenderThreadAction(action));
            _renderSignal.Set();
        }
    }

    private void QueueMultiThreadSettingsUpdate()
    {
        PostRenderAction(() =>
        {
            // 顺序很重要：先调整池大小和阈值，最后决定是否启用。
            _multiDeviceRenderer.DeviceCount = Volatile.Read(ref _multiThreadDeviceCount);
            _multiDeviceRenderer.Threshold = Volatile.Read(ref _multiThreadThreshold);
            _multiDeviceRenderer.PartitionMode =
                (MultiThreadPartitionMode)Volatile.Read(ref _multiThreadPartitionMode);
            _multiDeviceRenderer.Enabled = Volatile.Read(ref _multiThreadEnabled) != 0;
        });

        RequestRender();
    }

    /// <summary>
    /// 后台渲染线程主循环。
    /// 所有 mainContext.BeginDraw / EndDraw / Present 都在这个线程执行。
    /// </summary>
    private void RenderThreadMain()
    {
        var stop = false;

        try
        {
            while (!stop)
            {
                _renderSignal.WaitOne();

                do
                {
                    while (_renderActions.TryDequeue(out var renderAction))
                    {
                        try
                        {
                            renderAction.Action();
                            renderAction.Completion?.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex);
                            renderAction.Completion?.TrySetException(ex);
                        }

                        if (renderAction.StopAfterExecute)
                        {
                            stop = true;
                            break;
                        }
                    }

                    if (stop)
                        break;

                    if (Interlocked.Exchange(ref _renderRequested, 0) == 1)
                    {
                        ExecuteRenderOnRenderThread();
                    }

                    // 如果渲染过程中又来了 Pan / Zoom / Resize，
                    // 这里会继续处理，不需要 UI 线程等待。
                }
                while (!stop &&
                       (!_renderActions.IsEmpty ||
                        Volatile.Read(ref _renderRequested) == 1));
            }
        }
        finally
        {
            CompleteRenderWaiters(new ObjectDisposedException(nameof(Direct2dContext)));
            _renderSignal.Dispose();
        }
    }

    /// <summary>
    /// 当前已经在后台渲染线程。
    /// </summary>
    private void ExecuteRenderOnRenderThread()
    {
        Exception? renderException = null;
        var rendered = false;
        double elapsedMilliseconds = 0;

        try
        {
            if (_direct2DWrapper.Context is null)
            {
                CompleteRenderWaiters(null);
                return;
            }

            _stopwatch.Restart();

            InternalRender();

            _stopwatch.Stop();

            elapsedMilliseconds = _stopwatch.Elapsed.TotalMilliseconds;
            rendered = true;
        }
        catch (Exception ex)
        {
            renderException = ex;
            Debug.WriteLine(ex);
        }
        finally
        {
            CompleteRenderWaiters(renderException);

            if (rendered)
                RaiseRendered(elapsedMilliseconds);
        }
    }

    /// <summary>
    /// 实际绘制始终由专用渲染线程同步提交。
    /// 不要把 mainContext.BeginDraw / EndDraw 放进 Task.Run。
    /// </summary>
    private void InternalRender()
    {
        var mainContext = _direct2DWrapper.Context;
        var mainCache = _direct2DWrapper.Direct2DResourceCache;

        if (mainContext is null || mainCache is null)
            return;

        IDrawingElement[] elements;
        float offsetX;
        float offsetY;
        float scale;

        lock (_stateLock)
        {
            // 避免绘制时 UI 线程修改 DrawingElements 导致遍历冲突。
            elements = DrawingElements.ToArray();

            // 固定当前视图参数，避免绘制过程中 Pan / Zoom 修改字段。
            offsetX = _offsetX;
            offsetY = _offsetY;
            scale = _scale;
        }

        MultiDeviceFrameLease? multiDeviceLease = null;

        var beginDrawCalled = false;
        var endDrawAttempted = false;

        try
        {
            mainContext.BeginDraw();
            beginDrawCalled = true;

            Clear(mainContext);

            if (!_multiDeviceRenderer.TryDraw(
                    elements,
                    mainContext,
                    offsetX,
                    offsetY,
                    scale,
                    out multiDeviceLease))
            {
                DrawSingleThread(
                    elements,
                    mainCache,
                    mainContext,
                    offsetX,
                    offsetY,
                    scale);
            }

            // EndDraw 可能抛异常，所以先标记已经尝试 EndDraw，
            // 避免 finally 里重复 EndDraw。
            endDrawAttempted = true;
            mainContext.EndDraw();

            // Present 放在 EndDraw 后面。
            _direct2DWrapper.Present();
        }
        finally
        {
            if (beginDrawCalled && !endDrawAttempted)
            {
                try
                {
                    mainContext.EndDraw();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            // DrawImage 命令通常到 EndDraw 才真正提交。
            // 因此 multiDeviceLease 必须在 EndDraw 之后再 Dispose，不能提前释放 KeyedMutex。
            multiDeviceLease?.Dispose();
        }
    }

    private void CompleteRenderWaiters(Exception? exception)
    {
        List<TaskCompletionSource<bool>> waiters;

        lock (_renderWaitersLock)
        {
            if (_renderWaiters.Count == 0)
                return;

            waiters = new List<TaskCompletionSource<bool>>(_renderWaiters);
            _renderWaiters.Clear();
        }

        foreach (var waiter in waiters)
        {
            if (exception is null)
                waiter.TrySetResult(true);
            else
                waiter.TrySetException(exception);
        }
    }

    private void RaiseRendered(double elapsedMilliseconds)
    {
        void InvokeRendered()
        {
            try
            {
                Rendered?.Invoke(this, elapsedMilliseconds);
            }
            catch (Exception ex)
            {
                // 订阅者异常不能终止专用渲染线程，否则后续 RenderAsync 将永远无法完成。
                Debug.WriteLine(ex);
            }
        }

        if (_syncContext is not null)
        {
            try
            {
                _syncContext.Post(_ => InvokeRendered(), null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
        else
        {
            InvokeRendered();
        }
    }

    private static void DrawSingleThread(
        IReadOnlyList<IDrawingElement> elements,
        Direct2DResourceCache cache,
        ID2D1DeviceContext context,
        float offsetX,
        float offsetY,
        float scale)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            elements[i].Draw(cache, context, offsetX, offsetY, scale);
        }
    }

    private static void Clear(ID2D1DeviceContext context)
    {
        context.Transform = Matrix3x2.Identity;
        context.Clear(BackgroundColor);
    }

    private bool IsDisposing => Volatile.Read(ref _disposeStarted) != 0;

    private static float Clamp(float v, float min, float max)
    {
        return v < min ? min : v > max ? max : v;
    }

    private static bool AlmostSame(float a, float b)
    {
        return Math.Abs(a - b) <= 0.000001f;
    }

    private sealed class RenderThreadAction
    {
        public RenderThreadAction(
            Action action,
            TaskCompletionSource<bool>? completion = null,
            bool stopAfterExecute = false)
        {
            Action = action;
            Completion = completion;
            StopAfterExecute = stopAfterExecute;
        }

        public Action Action { get; }

        public TaskCompletionSource<bool>? Completion { get; }

        public bool StopAfterExecute { get; }
    }
}
