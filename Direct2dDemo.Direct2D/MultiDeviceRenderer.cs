using Direct2dDemo.Shared.Elements;
using System.Numerics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Direct2dDemo.Direct2D;

/// <summary>
/// 负责多 D3D/D2D Device 的离屏并行绘制和主 Device 合成。
/// Direct2dContext 只需要决定“要不要用多 Device”，不用关心共享 Texture、KeyedMutex、Device Pool 等细节。
/// </summary>
internal sealed class MultiDeviceRenderer : IDisposable
{
    private static readonly Color4 TransparentColor = new(0f, 0f, 0f, 0f);

    private readonly Direct2DWrapper _owner;

    private ThreadDeviceSlot[]? _slots;
    private int _pooledDeviceCount;
    private int _pooledWidth;
    private int _pooledHeight;
    private bool _enabled;
    private bool _disposed;

    private int _deviceCount = Math.Min(4, Math.Max(1, Environment.ProcessorCount));
    private int _threshold = 1000;

    public MultiDeviceRenderer(Direct2DWrapper owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            ThrowIfDisposed();

            if (_enabled == value)
                return;

            _enabled = value;

            if (!value)
                Reset();
        }
    }

    public int DeviceCount
    {
        get => _deviceCount;
        set
        {
            ThrowIfDisposed();

            var newValue = Math.Clamp(value, 1, Math.Max(1, Environment.ProcessorCount));
            if (_deviceCount == newValue)
                return;

            _deviceCount = newValue;
            Reset();
        }
    }

    public int Threshold
    {
        get => _threshold;
        set => _threshold = Math.Max(0, value);
    }

    public bool TryDraw(
        IDrawingElement[] elements,
        ID2D1DeviceContext mainContext,
        float offsetX,
        float offsetY,
        float scale,
        out MultiDeviceFrameLease? frameLease)
    {
        ThrowIfDisposed();
        frameLease = null;

        if (!CanUseMultiDevice(elements.Length))
            return false;

        EnsureDevicePool();

        if (_slots is null || _pooledDeviceCount <= 1)
            return false;

        var activeDeviceCount = Math.Min(_pooledDeviceCount, elements.Length);
        if (activeDeviceCount <= 1)
            return false;

        DrawOnWorkerDevices(elements, activeDeviceCount, offsetX, offsetY, scale);
        frameLease = CompositeWorkerBitmapsToMain(mainContext, activeDeviceCount);
        return true;
    }

    public void ClearResourceCaches()
    {
        if (_slots is null)
            return;

        foreach (var slot in _slots)
            slot?.ClearResourceCache();
    }

    public void Reset()
    {
        DisposeSlots();
        _pooledDeviceCount = 0;
        _pooledWidth = 0;
        _pooledHeight = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Reset();
        _disposed = true;
    }

    private bool CanUseMultiDevice(int elementCount)
    {
        return _enabled
            && elementCount >= _threshold
            && _deviceCount > 1
            && _owner.Width > 0
            && _owner.Height > 0
            && _owner.Context is not null
            && _owner.Factory is not null
            && _owner.D3DDevice is not null
            && _owner.DwriteFactory is not null;
    }

    private void EnsureDevicePool()
    {
        var targetDeviceCount = Math.Clamp(_deviceCount, 1, Math.Max(1, Environment.ProcessorCount));

        if (IsPoolReusable(targetDeviceCount))
            return;

        Reset();

        if (_owner.Factory is null ||
            _owner.D3DDevice is null ||
            _owner.DwriteFactory is null ||
            _owner.Context is null)
        {
            return;
        }

        _slots = new ThreadDeviceSlot[targetDeviceCount];
        _pooledDeviceCount = targetDeviceCount;
        _pooledWidth = _owner.Width;
        _pooledHeight = _owner.Height;

        using var mainDxgiDevice = _owner.D3DDevice.QueryInterface<IDXGIDevice>();
        using var adapter = mainDxgiDevice.GetAdapter();

        for (var i = 0; i < targetDeviceCount; i++)
        {
            _slots[i] = ThreadDeviceSlot.Create(
                adapter,
                _owner.Factory,
                _owner.D3DDevice,
                _owner.DwriteFactory,
                _owner.Context,
                _owner.Width,
                _owner.Height);
        }
    }

    private bool IsPoolReusable(int targetDeviceCount)
    {
        return _slots != null
            && _pooledDeviceCount == targetDeviceCount
            && _pooledWidth == _owner.Width
            && _pooledHeight == _owner.Height;
    }

    private void DrawOnWorkerDevices(
        IDrawingElement[] elements,
        int activeDeviceCount,
        float offsetX,
        float offsetY,
        float scale)
    {
        var chunkSize = (elements.Length + activeDeviceCount - 1) / activeDeviceCount;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = activeDeviceCount
        };

        var completedFrameFlags = new int[activeDeviceCount];

        try
        {
            Parallel.For(0, activeDeviceCount, parallelOptions, deviceIndex =>
            {
                var startIndex = deviceIndex * chunkSize;
                var endIndex = Math.Min(startIndex + chunkSize, elements.Length);

                if (startIndex >= endIndex)
                    return;

                _slots![deviceIndex].DrawChunk(elements, startIndex, endIndex, offsetX, offsetY, scale);
                completedFrameFlags[deviceIndex] = 1;
            });
        }
        catch
        {
            // 部分 Worker 可能已经 ReleaseSync(1) 交给主线程读取。
            // 这里如果不归还 Key 0，下一帧 Worker 会卡在 AcquireSync(0)。
            ReleaseUnreadWorkerFrames(completedFrameFlags, activeDeviceCount);
            throw;
        }
    }

    private void ReleaseUnreadWorkerFrames(int[] completedFrameFlags, int activeDeviceCount)
    {
        for (var i = 0; i < activeDeviceCount; i++)
        {
            if (completedFrameFlags[i] == 0)
                continue;

            try { _slots![i].ReleaseUnreadFrameToWorker(); } catch { }
        }
    }

    private MultiDeviceFrameLease CompositeWorkerBitmapsToMain(
        ID2D1DeviceContext mainContext,
        int activeDeviceCount)
    {
        var frameLease = new MultiDeviceFrameLease(activeDeviceCount);

        try
        {
            for (var i = 0; i < activeDeviceCount; i++)
            {
                var slot = _slots![i];

                // 等待子 Device 完成本帧写入。子线程 ReleaseSync(1) 后，这里才能读。
                slot.AcquireForMainRead();
                frameLease.Add(slot);

                // DrawImage 只是提交命令，真正执行通常在 EndDraw。
                // 所以 frameLease 必须由 Direct2dContext 在 EndDraw 后 Dispose。
                mainContext.DrawImage(slot.MainReadableBitmap);
            }

            return frameLease;
        }
        catch
        {
            frameLease.Dispose();
            throw;
        }
    }

    private void DisposeSlots()
    {
        if (_slots is null)
            return;

        foreach (var slot in _slots)
            slot?.Dispose();

        _slots = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MultiDeviceRenderer));
    }

    internal sealed class ThreadDeviceSlot : IDisposable
    {
        private readonly ID3D11Device _workerD3DDevice;
        private readonly ID3D11DeviceContext _workerD3DContext;
        private readonly IDXGIDevice _workerDxgiDevice;
        private readonly ID2D1Device _workerD2DDevice;
        private readonly ID2D1DeviceContext _workerD2DContext;
        private readonly ID3D11Texture2D _sharedTextureForWorker;
        private readonly IDXGIKeyedMutex _workerMutex;
        private readonly ID2D1Bitmap1 _workerTargetBitmap;
        private readonly ID3D11Texture2D _sharedTextureForMain;
        private readonly IDXGIKeyedMutex _mainMutex;

        private readonly Direct2DResourceCache _resourceCache;

        private ThreadDeviceSlot(
            ID3D11Device workerD3DDevice,
            ID3D11DeviceContext workerD3DContext,
            IDXGIDevice workerDxgiDevice,
            ID2D1Device workerD2DDevice,
            ID2D1DeviceContext workerD2DContext,
            Direct2DResourceCache resourceCache,
            ID3D11Texture2D sharedTextureForWorker,
            IDXGIKeyedMutex workerMutex,
            ID2D1Bitmap1 workerTargetBitmap,
            ID3D11Texture2D sharedTextureForMain,
            IDXGIKeyedMutex mainMutex,
            ID2D1Bitmap1 mainReadableBitmap)
        {
            _workerD3DDevice = workerD3DDevice;
            _workerD3DContext = workerD3DContext;
            _workerDxgiDevice = workerDxgiDevice;
            _workerD2DDevice = workerD2DDevice;
            _workerD2DContext = workerD2DContext;
            _resourceCache = resourceCache;
            _sharedTextureForWorker = sharedTextureForWorker;
            _workerMutex = workerMutex;
            _workerTargetBitmap = workerTargetBitmap;
            _sharedTextureForMain = sharedTextureForMain;
            _mainMutex = mainMutex;
            MainReadableBitmap = mainReadableBitmap;
        }

        public ID2D1Bitmap1 MainReadableBitmap { get; }

        public static ThreadDeviceSlot Create(
            IDXGIAdapter adapter,
            ID2D1Factory1 d2dFactory,
            ID3D11Device mainD3DDevice,
            IDWriteFactory dwriteFactory,
            ID2D1DeviceContext mainD2DContext,
            int width,
            int height)
        {
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

            var sharedTextureForWorker = workerD3DDevice.CreateTexture2D(CreateSharedTextureDescription(width, height));
            var workerMutex = sharedTextureForWorker.QueryInterface<IDXGIKeyedMutex>();

            using var dxgiResource = sharedTextureForWorker.QueryInterface<IDXGIResource>();
            var sharedHandle = dxgiResource.SharedHandle;

            var workerDxgiDevice = workerD3DDevice.QueryInterface<IDXGIDevice>();
            var workerD2DDevice = d2dFactory.CreateDevice(workerDxgiDevice);
            var workerD2DContext = workerD2DDevice.CreateDeviceContext(DeviceContextOptions.EnableMultithreadedOptimizations);
            var workerTargetBitmap = CreateWorkerTargetBitmap(workerD2DContext, sharedTextureForWorker);
            workerD2DContext.Target = workerTargetBitmap;

            // 主 Device 只打开一次共享资源，之后每帧直接复用 MainReadableBitmap。
            var sharedTextureForMain = mainD3DDevice.OpenSharedResource<ID3D11Texture2D>(sharedHandle);
            var mainMutex = sharedTextureForMain.QueryInterface<IDXGIKeyedMutex>();
            var mainReadableBitmap = CreateMainReadableBitmap(mainD2DContext, sharedTextureForMain);

            var resourceCache = new Direct2DResourceCache(d2dFactory, dwriteFactory, workerD2DContext);

            return new ThreadDeviceSlot(
                workerD3DDevice,
                workerD3DContext,
                workerDxgiDevice,
                workerD2DDevice,
                workerD2DContext,
                resourceCache,
                sharedTextureForWorker,
                workerMutex,
                workerTargetBitmap,
                sharedTextureForMain,
                mainMutex,
                mainReadableBitmap);
        }

        public void DrawChunk(
            IDrawingElement[] elements,
            int startIndex,
            int endIndex,
            float offsetX,
            float offsetY,
            float scale)
        {
            var mutexAcquired = false;
            var drawBegun = false;
            var frameReadyForMain = false;

            try
            {
                // Key 0：子线程可写。
                _workerMutex.AcquireSync(0, int.MaxValue);
                mutexAcquired = true;

                _workerD2DContext.BeginDraw();
                drawBegun = true;

                _workerD2DContext.Transform = Matrix3x2.Identity;
                _workerD2DContext.Clear(TransparentColor);

                for (var i = startIndex; i < endIndex; i++)
                {
                    elements[i].Draw(_resourceCache, _workerD2DContext, offsetX, offsetY, scale);
                }

                _workerD2DContext.EndDraw();
                drawBegun = false;
                frameReadyForMain = true;
            }
            finally
            {
                if (drawBegun)
                {
                    try { _workerD2DContext.EndDraw(); } catch { }
                }

                if (mutexAcquired)
                {
                    // 成功绘制才交给主线程读；失败时仍归还 Key 0，避免下一帧 Worker 死锁。
                    _workerMutex.ReleaseSync(frameReadyForMain ? 1u : 0u);
                }
            }
        }

        public void AcquireForMainRead()
        {
            _mainMutex.AcquireSync(1, int.MaxValue);
        }

        public void ReleaseToWorkerWrite()
        {
            _mainMutex.ReleaseSync(0);
        }

        public void ReleaseUnreadFrameToWorker()
        {
            // Worker 已经交出 Key 1，但主合成还没开始就发生异常时使用。
            _mainMutex.AcquireSync(1, 0);
            _mainMutex.ReleaseSync(0);
        }

        public void ClearResourceCache()
        {
            _resourceCache.ClearCache();
        }

        public void Dispose()
        {
            try { _resourceCache.ClearCache(); } catch { }
            try { _workerD2DContext.Target = null; } catch { }

            MainReadableBitmap.Dispose();
            _mainMutex.Dispose();
            _sharedTextureForMain.Dispose();

            _workerTargetBitmap.Dispose();
            _workerMutex.Dispose();
            _sharedTextureForWorker.Dispose();

            _workerD2DContext.Dispose();
            _workerD2DDevice.Dispose();
            _workerDxgiDevice.Dispose();

            try { _workerD3DContext.ClearState(); } catch { }
            _workerD3DContext.Dispose();
            _workerD3DDevice.Dispose();
        }

        private static Texture2DDescription CreateSharedTextureDescription(int width, int height)
        {
            return new Texture2DDescription
            {
                Width = (uint)Math.Max(1, width),
                Height = (uint)Math.Max(1, height),
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.SharedKeyedMutex
            };
        }

        private static ID2D1Bitmap1 CreateWorkerTargetBitmap(
            ID2D1DeviceContext workerContext,
            ID3D11Texture2D sharedTexture)
        {
            using var workerSurface = sharedTexture.QueryInterface<IDXGISurface>();

            var bitmapProperties = new BitmapProperties1
            {
                PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                DpiX = 96.0f,
                DpiY = 96.0f,
                BitmapOptions = BitmapOptions.Target
            };

            return workerContext.CreateBitmapFromDxgiSurface(workerSurface, bitmapProperties);
        }

        private static ID2D1Bitmap1 CreateMainReadableBitmap(
            ID2D1DeviceContext mainContext,
            ID3D11Texture2D sharedTexture)
        {
            using var mainSurface = sharedTexture.QueryInterface<IDXGISurface>();

            var bitmapProperties = new BitmapProperties1
            {
                PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                DpiX = 96.0f,
                DpiY = 96.0f,
                BitmapOptions = BitmapOptions.None
            };

            return mainContext.CreateBitmapFromDxgiSurface(mainSurface, bitmapProperties);
        }
    }
}

/// <summary>
/// 主 Device 已经获得读权限的 Worker Slot 集合。
/// 必须在主 Context.EndDraw() 之后释放，否则 DrawImage 还没真正执行就可能被 Worker 下一帧覆盖。
/// </summary>
internal sealed class MultiDeviceFrameLease : IDisposable
{
    private readonly MultiDeviceRenderer.ThreadDeviceSlot[] _slots;
    private int _count;
    private bool _disposed;

    public MultiDeviceFrameLease(int capacity)
    {
        _slots = new MultiDeviceRenderer.ThreadDeviceSlot[Math.Max(0, capacity)];
    }

    public void Add(MultiDeviceRenderer.ThreadDeviceSlot slot)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MultiDeviceFrameLease));

        _slots[_count++] = slot;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        for (var i = 0; i < _count; i++)
        {
            try
            {
                // Key 0：归还给子线程，下一帧继续写。
                _slots[i].ReleaseToWorkerWrite();
            }
            catch
            {
                // 不在释放阶段抛出，避免 EndDraw 异常时导致后续资源无法释放。
            }
        }

        _disposed = true;
    }
}
