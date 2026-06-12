using Direct2dDemo.Shared;
using Direct2dDemo.Shared.Elements;
using System.Diagnostics;
using System.Numerics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Direct2dDemo.Direct2D;

public class Direct2dContext : IDrawingContext, ICanvasContext
{
    /// <summary>
    /// 1 个工作 Slot = 1 个独立 D3D Device + 1 个独立 D2D DeviceContext + 1 张共享离屏 Texture。
    /// 子线程只写自己的 Texture，主线程只负责把各 Texture 合成到 SwapChain。
    /// </summary>
    private sealed class ThreadDeviceSlot : IDisposable
    {
        public ID3D11Device D3DDevice = null!;
        public ID3D11DeviceContext D3DContext = null!;
        public IDXGIDevice DXGIDevice = null!;
        public ID2D1Device D2DDevice = null!;
        public ID2D1DeviceContext D2DContext = null!;
        public Direct2DResourceCache ResourceCache = null!;

        public ID3D11Texture2D SharedTextureForWorker = null!;
        public IDXGIKeyedMutex WorkerMutex = null!;
        public ID2D1Bitmap1 WorkerTargetBitmap = null!;

        public nint SharedHandle;
        public ID3D11Texture2D SharedTextureForMain = null!;
        public IDXGIKeyedMutex MainMutex = null!;
        public ID2D1Bitmap1 MainReadableBitmap = null!;

        public int Width;
        public int Height;

        public void Dispose()
        {
            try { ResourceCache?.ClearCache(); } catch { }
            try { if (D2DContext != null) D2DContext.Target = null; } catch { }

            MainReadableBitmap?.Dispose();
            MainMutex?.Dispose();
            SharedTextureForMain?.Dispose();

            WorkerTargetBitmap?.Dispose();
            WorkerMutex?.Dispose();
            SharedTextureForWorker?.Dispose();

            ResourceCache = null!;

            D2DContext?.Dispose();
            D2DDevice?.Dispose();
            DXGIDevice?.Dispose();

            try { D3DContext?.ClearState(); } catch { }
            D3DContext?.Dispose();
            D3DDevice?.Dispose();
        }
    }

    private readonly Stopwatch stopwatch = new();
    public event EventHandler<double>? Rendered;

    public int Width => direct2DWrapper.Width;
    public int Height => direct2DWrapper.Height;
    public List<IDrawingElement> DrawingElements { get; } = new List<IDrawingElement>();

    private readonly Direct2DWrapper direct2DWrapper = new Direct2DWrapper();
    private static readonly Color4 background = new Color4(1f, 1f, 1f, 1.0f);
    private static readonly Color4 transparent = new Color4(0f, 0f, 0f, 0f);

    private float _panStartX;
    private float _panStartY;
    private float _offsetX;
    private float _offsetY;
    private float _panStartOffsetX;
    private float _panStartOffsetY;
    private float _scale = 1.0f;

    private const float MinScale = 0.05f;
    private const float MaxScale = 100.0f;

    private ThreadDeviceSlot[]? _threadDevicePool;
    private int _pooledDeviceCount;
    private int _pooledWidth;
    private int _pooledHeight;

    private bool _enableMultiThread;

    /// <summary>
    /// 开启后：元素数量超过 MultiThreadThreshold 时，使用多个独立 Device 离屏并行绘制。
    /// </summary>
    public bool EnableMultiThread
    {
        get => _enableMultiThread;
        set
        {
            if (_enableMultiThread == value)
                return;

            _enableMultiThread = value;

            if (!value)
                ReleaseThreadDevicePool();
        }
    }

    /// <summary>
    /// 多 Device 数量。不要默认开到 Environment.ProcessorCount，D3D Device 太多反而会慢。
    /// 建议 2～4；对象特别多时再提高。
    /// </summary>
    private int _multiThreadDeviceCount = Math.Min(4, Math.Max(1, Environment.ProcessorCount));
    public int MultiThreadDeviceCount
    {
        get => _multiThreadDeviceCount;
        set
        {
            var newValue = Math.Clamp(value, 1, Math.Max(1, Environment.ProcessorCount));
            if (_multiThreadDeviceCount == newValue)
                return;

            _multiThreadDeviceCount = newValue;
            ReleaseThreadDevicePool();
        }
    }

    /// <summary>
    /// 元素太少时，多 Device 的离屏绘制 + 合成成本会超过收益。
    /// </summary>
    public int MultiThreadThreshold { get; set; } = 1000;

    public void Render() => RenderCurrentView();

    private void RenderCurrentView()
    {
        if (direct2DWrapper.Context is null)
            return;

        stopwatch.Restart();
        InternalRender();
        stopwatch.Stop();
        Rendered?.Invoke(this, stopwatch.ElapsedMilliseconds);
    }

    private void InternalRender()
    {
        if (direct2DWrapper.Context is null || direct2DWrapper.Direct2DResourceCache is null)
            return;

        var mainContext = direct2DWrapper.Context;

        var elements = DrawingElements.ToArray();
        var elementCount = elements.Length;

        mainContext.BeginDraw();

        ThreadDeviceSlot[]? acquiredMainSlots = null;
        var acquiredMainCount = 0;
        var mainDrawEnded = false;

        try
        {
            Clear(mainContext);

            if (!ShouldUseMultiDevice(elementCount))
            {
                DrawSingleThread(elements, mainContext);
            }
            else
            {
                DrawMultiDevice(elements, mainContext, out acquiredMainSlots, out acquiredMainCount);
            }

            mainContext.EndDraw();
            mainDrawEnded = true;
        }
        finally
        {
            if (!mainDrawEnded)
            {
                try { mainContext.EndDraw(); } catch { }
            }

            ReleaseMainMutexes(acquiredMainSlots, acquiredMainCount);
        }

        direct2DWrapper.Present();
    }

    private bool ShouldUseMultiDevice(int elementCount)
    {
        return EnableMultiThread
            && elementCount >= MultiThreadThreshold
            && MultiThreadDeviceCount > 1
            && Width > 0
            && Height > 0;
    }

    private void DrawSingleThread(IDrawingElement[] elements, ID2D1DeviceContext mainContext)
    {
        var cache = direct2DWrapper.Direct2DResourceCache;
        if (cache is null)
            return;

        for (var i = 0; i < elements.Length; i++)
            elements[i].Draw(cache, mainContext, _offsetX, _offsetY, _scale);
    }

    private void DrawMultiDevice(
        IDrawingElement[] elements,
        ID2D1DeviceContext mainContext,
        out ThreadDeviceSlot[] acquiredMainSlots,
        out int acquiredMainCount)
    {
        acquiredMainSlots = Array.Empty<ThreadDeviceSlot>();
        acquiredMainCount = 0;

        EnsureThreadDevicePool();

        if (_threadDevicePool is null || _pooledDeviceCount <= 1)
        {
            DrawSingleThread(elements, mainContext);
            return;
        }

        var elementCount = elements.Length;
        var deviceCount = Math.Min(_pooledDeviceCount, elementCount);
        var chunkSize = (elementCount + deviceCount - 1) / deviceCount;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = deviceCount
        };

        Parallel.For(0, deviceCount, parallelOptions, deviceIndex =>
        {
            var startIndex = deviceIndex * chunkSize;
            var endIndex = Math.Min(startIndex + chunkSize, elementCount);
            if (startIndex >= endIndex)
                return;

            var slot = _threadDevicePool[deviceIndex];
            DrawChunkOnWorkerDevice(slot, elements, startIndex, endIndex);
        });

        acquiredMainSlots = new ThreadDeviceSlot[deviceCount];

        for (var i = 0; i < deviceCount; i++)
        {
            var slot = _threadDevicePool[i];

            // 等待子 Device 完成本帧写入。子线程 ReleaseSync(1) 后，这里才能读。
            slot.MainMutex.AcquireSync(1, int.MaxValue);
            acquiredMainSlots[acquiredMainCount++] = slot;

            mainContext.DrawImage(slot.MainReadableBitmap);
        }
    }

    private void DrawChunkOnWorkerDevice(
        ThreadDeviceSlot slot,
        IDrawingElement[] elements,
        int startIndex,
        int endIndex)
    {
        var mutexAcquired = false;
        var drawBegun = false;

        try
        {
            // Key 0：子线程可写。
            slot.WorkerMutex.AcquireSync(0, int.MaxValue);
            mutexAcquired = true;

            var context = slot.D2DContext;
            context.BeginDraw();
            drawBegun = true;
            context.Clear(transparent);

            for (var i = startIndex; i < endIndex; i++)
                elements[i].Draw(slot.ResourceCache, context, _offsetX, _offsetY, _scale);

            context.EndDraw();
            drawBegun = false;
        }
        finally
        {
            if (drawBegun)
            {
                try { slot.D2DContext.EndDraw(); } catch { }
            }

            if (mutexAcquired)
            {
                // Key 1：主线程可读。
                slot.WorkerMutex.ReleaseSync(1);
            }
        }
    }

    private static void ReleaseMainMutexes(ThreadDeviceSlot[]? slots, int count)
    {
        if (slots is null)
            return;

        for (var i = 0; i < count; i++)
        {
            try
            {
                // Key 0：归还给子线程，下一帧继续写。
                slots[i].MainMutex.ReleaseSync(0);
            }
            catch
            {
                // 这里不要抛出，避免 EndDraw 异常时导致后续资源无法释放。
            }
        }
    }

    private void EnsureThreadDevicePool()
    {
        if (direct2DWrapper.Factory is null ||
            direct2DWrapper.D3DDevice is null ||
            direct2DWrapper.DwriteFactory is null ||
            direct2DWrapper.Context is null)
        {
            return;
        }

        var targetDeviceCount = Math.Clamp(MultiThreadDeviceCount, 1, Math.Max(1, Environment.ProcessorCount));

        if (_threadDevicePool != null &&
            _pooledDeviceCount == targetDeviceCount &&
            _pooledWidth == Width &&
            _pooledHeight == Height)
        {
            return;
        }

        ReleaseThreadDevicePool();

        _threadDevicePool = new ThreadDeviceSlot[targetDeviceCount];
        _pooledDeviceCount = targetDeviceCount;
        _pooledWidth = Width;
        _pooledHeight = Height;

        using var mainDxgiDevice = direct2DWrapper.D3DDevice.QueryInterface<IDXGIDevice>();
        using var adapter = mainDxgiDevice.GetAdapter();

        for (var i = 0; i < targetDeviceCount; i++)
        {
            _threadDevicePool[i] = CreateThreadDeviceSlot(adapter);
        }
    }

    private ThreadDeviceSlot CreateThreadDeviceSlot(IDXGIAdapter adapter)
    {
        if (direct2DWrapper.Factory is null ||
            direct2DWrapper.D3DDevice is null ||
            direct2DWrapper.DwriteFactory is null ||
            direct2DWrapper.Context is null)
        {
            throw new InvalidOperationException("Direct2D wrapper is not ready.");
        }

        var featureLevels = new[]
        {
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1,
            Vortice.Direct3D.FeatureLevel.Level_10_0
        };

        D3D11.D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out ID3D11Device workerD3DDevice,
            out _,
            out ID3D11DeviceContext workerD3DContext).CheckError();

        var textureDesc = new Texture2DDescription
        {
            Width = (uint)Math.Max(1, Width),
            Height = (uint)Math.Max(1, Height),
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.SharedKeyedMutex
        };

        var sharedTextureForWorker = workerD3DDevice.CreateTexture2D(textureDesc);
        var workerMutex = sharedTextureForWorker.QueryInterface<IDXGIKeyedMutex>();

        using var dxgiResource = sharedTextureForWorker.QueryInterface<IDXGIResource>();
        var sharedHandle = dxgiResource.SharedHandle;

        var workerDxgiDevice = workerD3DDevice.QueryInterface<IDXGIDevice>();
        var workerD2DDevice = direct2DWrapper.Factory.CreateDevice(workerDxgiDevice);
        var workerD2DContext = workerD2DDevice.CreateDeviceContext(DeviceContextOptions.EnableMultithreadedOptimizations);

        using var workerSurface = sharedTextureForWorker.QueryInterface<IDXGISurface>();
        var workerBitmapProperties = new BitmapProperties1
        {
            PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            DpiX = 96.0f,
            DpiY = 96.0f,
            BitmapOptions = BitmapOptions.Target
        };
        var workerTargetBitmap = workerD2DContext.CreateBitmapFromDxgiSurface(workerSurface, workerBitmapProperties);
        workerD2DContext.Target = workerTargetBitmap;

        // 主 Device 只打开一次共享资源，之后每帧直接复用 MainReadableBitmap，避免每帧 OpenSharedResource。
        var sharedTextureForMain = direct2DWrapper.D3DDevice.OpenSharedResource<ID3D11Texture2D>(sharedHandle);
        var mainMutex = sharedTextureForMain.QueryInterface<IDXGIKeyedMutex>();

        using var mainSurface = sharedTextureForMain.QueryInterface<IDXGISurface>();
        var mainBitmapProperties = new BitmapProperties1
        {
            PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            DpiX = 96.0f,
            DpiY = 96.0f,
            BitmapOptions = BitmapOptions.None
        };
        var mainReadableBitmap = direct2DWrapper.Context.CreateBitmapFromDxgiSurface(mainSurface, mainBitmapProperties);

        var resourceCache = new Direct2DResourceCache(
            direct2DWrapper.Factory,
            direct2DWrapper.DwriteFactory,
            workerD2DContext);

        return new ThreadDeviceSlot
        {
            D3DDevice = workerD3DDevice,
            D3DContext = workerD3DContext,
            DXGIDevice = workerDxgiDevice,
            D2DDevice = workerD2DDevice,
            D2DContext = workerD2DContext,
            ResourceCache = resourceCache,
            SharedTextureForWorker = sharedTextureForWorker,
            WorkerMutex = workerMutex,
            WorkerTargetBitmap = workerTargetBitmap,
            SharedHandle = sharedHandle,
            SharedTextureForMain = sharedTextureForMain,
            MainMutex = mainMutex,
            MainReadableBitmap = mainReadableBitmap,
            Width = Width,
            Height = Height
        };
    }

    private void ReleaseThreadDevicePool()
    {
        if (_threadDevicePool != null)
        {
            foreach (var slot in _threadDevicePool)
                slot?.Dispose();

            _threadDevicePool = null;
        }

        _pooledDeviceCount = 0;
        _pooledWidth = 0;
        _pooledHeight = 0;
    }

    private static void Clear(ID2D1DeviceContext context)
    {
        context.Transform = Matrix3x2.Identity;
        context.Clear(background);
    }

    public void HwndResized(int width, int height)
    {
        // Resize 后，离屏共享 Texture 尺寸已经不匹配，必须重建。
        ReleaseThreadDevicePool();
        direct2DWrapper.TargetResized(width, height);
        RenderCurrentView();
    }

    public void Initialize(nint hwnd, int width, int height)
    {
        ReleaseThreadDevicePool();
        direct2DWrapper.SetTarget(hwnd, width, height);
        RenderCurrentView();
    }

    public void ClearData()
    {
        if (direct2DWrapper.Context is not null)
            direct2DWrapper.Context.Transform = Matrix3x2.Identity;

        DrawingElements.Clear();
        direct2DWrapper.Direct2DResourceCache?.ClearCache();

        if (_threadDevicePool != null)
        {
            foreach (var slot in _threadDevicePool)
                slot?.ResourceCache?.ClearCache();
        }

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
        ReleaseThreadDevicePool();
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
        _offsetX = _panStartOffsetX + (x - _panStartX);
        _offsetY = _panStartOffsetY + (y - _panStartY);
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

    private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    private static bool AlmostSame(float a, float b) => Math.Abs(a - b) <= 0.000001f;
}
