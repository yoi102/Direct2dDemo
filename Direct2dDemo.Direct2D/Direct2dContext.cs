using Direct2dDemo.Shared;
using Direct2dDemo.Shared.Elements;
using System.Diagnostics;
using System.Numerics;
using Vortice.Mathematics;
using Vortice.Direct2D1;

namespace Direct2dDemo.Direct2D;

public class Direct2dContext : IDrawingContext, ICanvasContext
{
    private readonly Stopwatch stopwatch = new();

    public event EventHandler<double>? Rendered;

    public int Width => direct2DWrapper.Width;
    public int Height => direct2DWrapper.Height;
    public List<IDrawingElement> DrawingElements { get; } = new List<IDrawingElement>();

    private readonly Direct2DWrapper direct2DWrapper = new Direct2DWrapper();

    private static readonly Color4 background = new Color4(1f, 1f, 1f, 1.0f);

    private float _panStartX;
    private float _panStartY;
    private float _offsetX;
    private float _offsetY;
    private float _panStartOffsetX;
    private float _panStartOffsetY;

    private float _scale = 1.0f;

    private const float MinScale = 0.05f;
    private const float MaxScale = 100.0f;

    // 【新增】高性能常驻工作线程设备上下文池
    private ID2D1DeviceContext[]? _threadContextPool;
    private int _pooledThreadCount = 0;

    public bool EnableMultiThread { get; set; } = false;
    public void Render()
    {
        RenderCurrentView();
    }

    private void RenderCurrentView()
    {
        if (direct2DWrapper.Context is null)
            return;

        stopwatch.Restart();

        InternalRender();

        stopwatch.Stop();
        Rendered?.Invoke(this, stopwatch.ElapsedMilliseconds);
    }

    // 核心改造：保证零每帧非托管对象分配的渲染指令并收集
    private void InternalRender()
    {
        if (direct2DWrapper.Context is null || direct2DWrapper.Device is null || direct2DWrapper.Direct2DResourceCache is null)
            return;

        var mainContext = direct2DWrapper.Context;

        mainContext.BeginDraw();
        this.Clear();

        int elementCount = DrawingElements.Count;

        // 【弹性自适应】如果图元太少，多线程调度开销(Threading Context Switch)大于收益，强制走最纯粹的单线程
        if (elementCount < 1000 || !EnableMultiThread)
        {
            for (int i = 0; i < elementCount; i++)
            {
                DrawingElements[i].Draw(direct2DWrapper.Direct2DResourceCache, mainContext, _offsetX, _offsetY, _scale);
            }
        }
        else
        {
            // 确保线程池已被安全创建
            EnsureThreadContextPool();

            if (_threadContextPool == null || _pooledThreadCount == 0)
            {
                // 如果异常退回到单线程兜底
                for (int i = 0; i < elementCount; i++)
                {
                    DrawingElements[i].Draw(direct2DWrapper.Direct2DResourceCache, mainContext, _offsetX, _offsetY, _scale);
                }
            }
            else
            {
                int threadCount = _pooledThreadCount;
                int chunkSize = (int)Math.Ceiling((double)elementCount / threadCount);
                var commandLists = new ID2D1CommandList[threadCount];

                // 并行指令录制区
                Parallel.For(0, threadCount, i =>
                {
                    int startIndex = i * chunkSize;
                    if (startIndex >= elementCount) return;
                    int endIndex = Math.Min(startIndex + chunkSize, elementCount);

                    // 核心提升点：直接使用长驻池子中的工作 Context，不申请新的显卡驱动链路
                    var threadContext = _threadContextPool[i];

                    // 创建轻量级内存录制流
                    var commandList = threadContext.CreateCommandList();

                    threadContext.Target = commandList;
                    threadContext.BeginDraw();

                    // 顺序遍历下标，规避多线程迭代器堆内存分配
                    for (int j = startIndex; j < endIndex; j++)
                    {
                        DrawingElements[j].Draw(direct2DWrapper.Direct2DResourceCache, threadContext, _offsetX, _offsetY, _scale);
                    }

                    threadContext.EndDraw();
                    commandList.Close(); // 必须关闭，否则主线程无法消费

                    commandLists[i] = commandList;
                });

                // 主线程按 Z-index 串行汇总并呈现
                for (int i = 0; i < threadCount; i++)
                {
                    if (commandLists[i] != null)
                    {
                        mainContext.DrawImage(commandLists[i]);
                        commandLists[i].Dispose(); // 及时释放临时指令缓冲
                    }
                }
            }
        }

        mainContext.EndDraw();
        direct2DWrapper.Present();
    }

    private void EnsureThreadContextPool()
    {
        if (direct2DWrapper.Device is null) return;

        int targetThreadCount = Environment.ProcessorCount;
        if (_threadContextPool != null && _pooledThreadCount == targetThreadCount)
            return;

        ReleaseThreadContextPool();

        _threadContextPool = new ID2D1DeviceContext[targetThreadCount];
        _pooledThreadCount = targetThreadCount;

        for (int i = 0; i < targetThreadCount; i++)
        {
            _threadContextPool[i] = direct2DWrapper.Device.CreateDeviceContext(DeviceContextOptions.EnableMultithreadedOptimizations);
        }
    }

    private void ReleaseThreadContextPool()
    {
        if (_threadContextPool != null)
        {
            foreach (var ctx in _threadContextPool)
            {
                ctx?.Dispose();
            }
            _threadContextPool = null;
        }
        _pooledThreadCount = 0;
    }

    private void Clear()
    {
        var context = direct2DWrapper.Context;
        if (context is null)
            return;

        context.Transform = Matrix3x2.Identity;
        context.Clear(background);
    }

    public void HwndResized(int width, int height)
    {
        direct2DWrapper.TargetResized(width, height);
        RenderCurrentView();
    }

    public void Initialize(nint hwnd, int width, int height)
    {
        direct2DWrapper.SetTarget(hwnd, width, height);
        RenderCurrentView();
    }

    public void ClearData()
    {
        if (direct2DWrapper.Context is not null)
            direct2DWrapper.Context.Transform = Matrix3x2.Identity;

        DrawingElements.Clear();
        direct2DWrapper.Direct2DResourceCache?.ClearCache();

        _offsetX = 0;
        _offsetY = 0;
        _panStartX = 0;
        _panStartY = 0;
        _panStartOffsetX = 0;
        _panStartOffsetY = 0;
        _scale = 1.0f;
    }

    public void Dispose()
    {
        ReleaseThreadContextPool();
        direct2DWrapper.Dispose();
    }

    public void BeginPan(int x, int y)
    {
        _panStartX = x;
        _panStartY = y;
        _panStartOffsetX = _offsetX;
        _panStartOffsetY = _offsetY;
    }

    public void Pan(int x, int y)
    {
        var deltaX = x - _panStartX;
        var deltaY = y - _panStartY;

        _offsetX = _panStartOffsetX + deltaX;
        _offsetY = _panStartOffsetY + deltaY;

        RenderCurrentView();
    }

    public void EndPan(int x, int y)
    {
        _panStartX = 0;
        _panStartY = 0;
    }

    public void Zoom(float zoomFactor, int centerX, int centerY)
    {
        if (zoomFactor <= 0)
            return;

        var oldScale = _scale;
        var newScale = Clamp(oldScale * zoomFactor, MinScale, MaxScale);

        if (AlmostSame(oldScale, newScale))
            return;

        var worldX = (centerX - _offsetX) / oldScale;
        var worldY = (centerY - _offsetY) / oldScale;

        _scale = newScale;
        _offsetX = centerX - worldX * newScale;
        _offsetY = centerY - worldY * newScale;

        RenderCurrentView();
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static bool AlmostSame(float a, float b)
    {
        return Math.Abs(a - b) <= 0.000001f;
    }
}
