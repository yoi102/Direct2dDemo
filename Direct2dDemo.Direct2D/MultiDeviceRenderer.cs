using Direct2dDemo.Shared.Elements;
using System.Diagnostics;
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
/// 负责 Direct2D 离屏并行绘制和主 Context 合成。
/// 支持“多个独立 Device”和“单 Device + 多个 Context”两种 Worker 拓扑。
/// </summary>
internal sealed class MultiDeviceRenderer : IDisposable
{
    private static readonly Color4 TransparentColor = new(0f, 0f, 0f, 0f);
    private const double MaxTileDuplicationRatio = 1.85;
    private const double MaxTileLoadRatio = 0.75;

    internal const int MaxDeviceCount = 4;
    internal const int DefaultThreshold = 1000;
    internal static int DefaultDeviceCount => Math.Min(MaxDeviceCount, Math.Max(1, Environment.ProcessorCount));

    private readonly Direct2DWrapper _owner;

    private IRenderSlot[]? _slots;
    private int _pooledDeviceCount;
    private bool _enabled;
    private bool _disposed;
    private bool _poolCreationFailed;

    private int _deviceCount = DefaultDeviceCount;
    private int _threshold = DefaultThreshold;
    private MultiThreadPartitionMode _partitionMode = MultiThreadPartitionMode.Auto;
    private MultiThreadDeviceMode _deviceMode = MultiThreadDeviceMode.MultipleDevices;

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

            var newValue = NormalizeDeviceCount(value);
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

    public MultiThreadPartitionMode PartitionMode
    {
        get => _partitionMode;
        set
        {
            ThrowIfDisposed();

            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            if (_partitionMode == value)
                return;

            _partitionMode = value;
            Reset();
        }
    }

    public MultiThreadDeviceMode DeviceMode
    {
        get => _deviceMode;
        set
        {
            ThrowIfDisposed();

            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            if (_deviceMode == value)
                return;

            _deviceMode = value;
            Reset();
        }
    }

    internal static int NormalizeDeviceCount(int value)
    {
        // 每个 Device 都需要一张窗口大小的离屏纹理；继续按 CPU 核数扩张通常只会增加
        // 显存、合成和 GPU 调度成本。1 表示不并行，实际并行限制为 2～4 个 Worker。
        return Math.Clamp(value, 1, MaxDeviceCount);
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

        if (!CanUseParallelRendering(elements.Length))
            return false;

        var maxUsefulTileCount = Math.Max(1, (_owner.Width + 7) / 8);
        var activeDeviceCount = Math.Min(
            Math.Min(NormalizeDeviceCount(_deviceCount), elements.Length),
            maxUsefulTileCount);
        if (activeDeviceCount <= 1)
            return false;

        var renderPlan = CreateRenderPlan(
            elements,
            activeDeviceCount,
            offsetX,
            offsetY,
            scale);

        // 全部元素都在视口外；主 Context 已经清过背景，不需要创建/唤醒 Worker。
        if (renderPlan.WorkItems.Length == 0)
        {
            frameLease = new MultiDeviceFrameLease(0);
            return true;
        }

        try
        {
            EnsureDevicePool(renderPlan);
        }
        catch (Exception ex)
        {
            // 并行绘制是优化路径。驱动不支持所选资源拓扑或资源不足时，本帧退回主 Context，
            // 并在下一次 Reset（尺寸/配置变化）时再尝试创建。
            Debug.WriteLine(ex);
            DisposeSlots();
            _pooledDeviceCount = 0;
            _poolCreationFailed = true;
            return false;
        }

        if (_slots is null || _pooledDeviceCount <= 1)
            return false;

        DrawOnWorkerDevices(renderPlan);
        frameLease = CompositeWorkerBitmapsToMain(mainContext, renderPlan);
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
        _poolCreationFailed = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Reset();
        _disposed = true;
    }

    private bool CanUseParallelRendering(int elementCount)
    {
        return _enabled
            && !_poolCreationFailed
            && elementCount >= _threshold
            && _deviceCount > 1
            && _owner.Width > 0
            && _owner.Height > 0
            && _owner.Context is not null
            && _owner.DwriteFactory is not null
            && (_deviceMode switch
            {
                MultiThreadDeviceMode.MultipleDevices => _owner.D3DDevice is not null,
                MultiThreadDeviceMode.SingleDeviceMultipleContexts =>
                    _owner.Device is not null && _owner.Factory is not null,
                _ => false
            });
    }

    private RenderPlan CreateRenderPlan(
        IDrawingElement[] elements,
        int activeDeviceCount,
        float offsetX,
        float offsetY,
        float scale)
    {
        if (_partitionMode == MultiThreadPartitionMode.ElementChunks)
        {
            return CreateChunkPlan(
                elements,
                activeDeviceCount,
                offsetX,
                offsetY,
                scale);
        }

        var tilePlan = TryCreateTilePlan(
            elements,
            activeDeviceCount,
            offsetX,
            offsetY,
            scale,
            forceTiles: _partitionMode == MultiThreadPartitionMode.Tiles);

        return tilePlan ?? CreateChunkPlan(
            elements,
            activeDeviceCount,
            offsetX,
            offsetY,
            scale);
    }

    private RenderPlan? TryCreateTilePlan(
        IDrawingElement[] elements,
        int activeDeviceCount,
        float offsetX,
        float offsetY,
        float scale,
        bool forceTiles)
    {
        var viewportWidth = _owner.Width;
        var viewportHeight = _owner.Height;
        var buckets = new List<IDrawingElement>[activeDeviceCount];
        var regions = new TileRegion[activeDeviceCount];
        var tileBoundaries = CreateAlignedTileBoundaries(viewportWidth, activeDeviceCount);

        for (var i = 0; i < activeDeviceCount; i++)
        {
            var x = tileBoundaries[i];
            var width = tileBoundaries[i + 1] - x;
            buckets[i] = new List<IDrawingElement>(
                Math.Max(4, elements.Length / activeDeviceCount));
            regions[i] = new TileRegion(x, 0, width, viewportHeight);
        }

        var visibleElementCount = 0;
        var referenceCount = 0;

        foreach (var element in elements)
        {
            var matchedTile = false;

            if (DrawingElementBounds.TryGetScreenBounds(
                    element,
                    offsetX,
                    offsetY,
                    scale,
                    out var bounds))
            {
                for (var tileIndex = 0; tileIndex < activeDeviceCount; tileIndex++)
                {
                    var region = regions[tileIndex];
                    if (!bounds.Intersects(region.X, region.Y, region.Width, region.Height))
                        continue;

                    buckets[tileIndex].Add(element);
                    referenceCount++;
                    matchedTile = true;
                }
            }
            else
            {
                // 未知元素类型不能冒险裁掉，放入所有 tile；重复率过高时会自动退回 chunks。
                for (var tileIndex = 0; tileIndex < activeDeviceCount; tileIndex++)
                {
                    buckets[tileIndex].Add(element);
                    referenceCount++;
                }

                matchedTile = true;
            }

            if (matchedTile)
                visibleElementCount++;
        }

        if (visibleElementCount == 0)
            return RenderPlan.Empty(activeDeviceCount, regions);

        var nonEmptyTileCount = buckets.Count(bucket => bucket.Count > 0);
        var maxTileLoad = buckets.Max(bucket => bucket.Count);
        var duplicationRatio = referenceCount / (double)visibleElementCount;
        var loadRatio = maxTileLoad / (double)visibleElementCount;

        // 大图形/长线跨越过多 tile，或绝大多数工作集中在一个 tile 时，
        // 空间分块不能有效并行，退回按元素 chunks。
        if (!forceTiles &&
            (nonEmptyTileCount <= 1 ||
             duplicationRatio > MaxTileDuplicationRatio ||
             loadRatio > MaxTileLoadRatio))
        {
            return null;
        }

        var slotBatches = new RenderBatch[activeDeviceCount];
        var workItems = new List<RenderBatch>(activeDeviceCount);

        for (var i = 0; i < activeDeviceCount; i++)
        {
            var region = regions[i];
            var tileElements = buckets[i].ToArray();
            var batch = new RenderBatch(
                slotIndex: i,
                elements: tileElements,
                startIndex: 0,
                endIndex: tileElements.Length,
                drawOffsetX: offsetX - region.X,
                drawOffsetY: offsetY - region.Y,
                scale: scale,
                targetWidth: region.Width,
                targetHeight: region.Height,
                compositeX: region.X,
                compositeY: region.Y);

            slotBatches[i] = batch;

            if (batch.ElementCount > 0)
                workItems.Add(batch);
        }

        return new RenderPlan(slotBatches, workItems.ToArray());
    }

    private static int[] CreateAlignedTileBoundaries(int viewportWidth, int tileCount)
    {
        const int hatchPatternSize = 8;

        var boundaries = new int[tileCount + 1];
        boundaries[0] = 0;
        boundaries[tileCount] = viewportWidth;

        for (var i = 1; i < tileCount; i++)
        {
            var ideal = i * viewportWidth / (double)tileCount;
            var aligned = (int)Math.Round(
                ideal / hatchPatternSize,
                MidpointRounding.AwayFromZero) * hatchPatternSize;

            var minimum = boundaries[i - 1] + hatchPatternSize;
            var maximum = viewportWidth - (tileCount - i);
            boundaries[i] = Math.Clamp(Math.Max(aligned, minimum), boundaries[i - 1] + 1, maximum);
        }

        return boundaries;
    }

    private RenderPlan CreateChunkPlan(
        IDrawingElement[] elements,
        int activeDeviceCount,
        float offsetX,
        float offsetY,
        float scale)
    {
        var chunkSize = (elements.Length + activeDeviceCount - 1) / activeDeviceCount;
        var slotBatches = new RenderBatch[activeDeviceCount];
        var workItems = new List<RenderBatch>(activeDeviceCount);

        for (var deviceIndex = 0; deviceIndex < activeDeviceCount; deviceIndex++)
        {
            var startIndex = deviceIndex * chunkSize;
            var endIndex = Math.Min(startIndex + chunkSize, elements.Length);
            var batch = new RenderBatch(
                slotIndex: deviceIndex,
                elements: elements,
                startIndex: startIndex,
                endIndex: endIndex,
                drawOffsetX: offsetX,
                drawOffsetY: offsetY,
                scale: scale,
                targetWidth: _owner.Width,
                targetHeight: _owner.Height,
                compositeX: 0,
                compositeY: 0);

            slotBatches[deviceIndex] = batch;

            if (batch.ElementCount > 0)
                workItems.Add(batch);
        }

        return new RenderPlan(slotBatches, workItems.ToArray());
    }

    private void EnsureDevicePool(RenderPlan renderPlan)
    {
        var targetDeviceCount = renderPlan.SlotBatches.Length;

        if (IsPoolReusable(renderPlan))
            return;

        Reset();

        if (_owner.DwriteFactory is null || _owner.Context is null)
        {
            return;
        }

        var newSlots = new IRenderSlot[targetDeviceCount];

        try
        {
            if (_deviceMode == MultiThreadDeviceMode.MultipleDevices)
            {
                if (_owner.D3DDevice is null)
                    return;

                using var mainDxgiDevice = _owner.D3DDevice.QueryInterface<IDXGIDevice>();
                using var adapter = mainDxgiDevice.GetAdapter();

                for (var i = 0; i < targetDeviceCount; i++)
                {
                    var batch = renderPlan.SlotBatches[i];
                    newSlots[i] = MultiDeviceSlot.Create(
                        adapter,
                        _owner.D3DDevice,
                        _owner.DwriteFactory,
                        _owner.Context,
                        batch.TargetWidth,
                        batch.TargetHeight);
                }
            }
            else
            {
                if (_owner.Device is null || _owner.Factory is null)
                    return;

                for (var i = 0; i < targetDeviceCount; i++)
                {
                    var batch = renderPlan.SlotBatches[i];
                    newSlots[i] = SharedDeviceContextSlot.Create(
                        _owner.Device,
                        _owner.Factory,
                        _owner.DwriteFactory,
                        batch.TargetWidth,
                        batch.TargetHeight);
                }
            }
        }
        catch
        {
            foreach (var slot in newSlots)
            {
                try { slot?.Dispose(); } catch { }
            }

            throw;
        }

        _slots = newSlots;
        _pooledDeviceCount = targetDeviceCount;
    }

    private bool IsPoolReusable(RenderPlan renderPlan)
    {
        if (_slots is null ||
            _pooledDeviceCount != renderPlan.SlotBatches.Length ||
            _slots.Length != renderPlan.SlotBatches.Length)
        {
            return false;
        }

        for (var i = 0; i < _slots.Length; i++)
        {
            var batch = renderPlan.SlotBatches[i];
            if (_slots[i].TargetWidth != batch.TargetWidth ||
                _slots[i].TargetHeight != batch.TargetHeight)
            {
                return false;
            }
        }

        return true;
    }

    private void DrawOnWorkerDevices(RenderPlan renderPlan)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = renderPlan.WorkItems.Length
        };

        var completedFrameFlags = new int[renderPlan.SlotBatches.Length];

        try
        {
            Parallel.For(0, renderPlan.WorkItems.Length, parallelOptions, workIndex =>
            {
                var batch = renderPlan.WorkItems[workIndex];
                _slots![batch.SlotIndex].DrawBatch(batch);
                Volatile.Write(ref completedFrameFlags[batch.SlotIndex], 1);
            });
        }
        catch
        {
            // 部分 Worker 可能已经 ReleaseSync(1) 交给主线程读取。
            // 这里如果不归还 Key 0，下一帧 Worker 会卡在 AcquireSync(0)。
            ReleaseUnreadWorkerFrames(completedFrameFlags, renderPlan.WorkItems);
            throw;
        }
    }

    private void ReleaseUnreadWorkerFrames(
        int[] completedFrameFlags,
        IReadOnlyList<RenderBatch> workItems)
    {
        foreach (var batch in workItems)
        {
            if (Volatile.Read(ref completedFrameFlags[batch.SlotIndex]) == 0)
                continue;

            try { _slots![batch.SlotIndex].ReleaseUnreadFrameToWorker(); } catch { }
        }
    }

    private MultiDeviceFrameLease CompositeWorkerBitmapsToMain(
        ID2D1DeviceContext mainContext,
        RenderPlan renderPlan)
    {
        var frameLease = new MultiDeviceFrameLease(renderPlan.WorkItems.Length);
        var acquiredByMain = new bool[renderPlan.SlotBatches.Length];

        try
        {
            foreach (var batch in renderPlan.WorkItems)
            {
                var slot = _slots![batch.SlotIndex];

                // 等待子 Device 完成本帧写入。子线程 ReleaseSync(1) 后，这里才能读。
                slot.AcquireForMainRead();
                frameLease.Add(slot);
                acquiredByMain[batch.SlotIndex] = true;

                // DrawImage 只是提交命令，真正执行通常在 EndDraw。
                // 所以 frameLease 必须由 Direct2dContext 在 EndDraw 后 Dispose。
                mainContext.DrawImage(
                    slot.MainReadableBitmap,
                    new Vector2(batch.CompositeX, batch.CompositeY),
                    null,
                    Vortice.Direct2D1.InterpolationMode.NearestNeighbor,
                    CompositeMode.SourceOver);
            }

            return frameLease;
        }
        catch
        {
            frameLease.Dispose();

            // 尚未被主 Device Acquire 的 Worker 也已经交出了 Key 1。
            // 若不归还 Key 0，下一帧这些 Worker 会永久等在 AcquireSync(0)。
            foreach (var batch in renderPlan.WorkItems)
            {
                if (acquiredByMain[batch.SlotIndex])
                    continue;

                try { _slots![batch.SlotIndex].ReleaseUnreadFrameToWorker(); } catch { }
            }

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

    private readonly record struct TileRegion(
        int X,
        int Y,
        int Width,
        int Height);

    private sealed class RenderPlan
    {
        public RenderPlan(
            RenderBatch[] slotBatches,
            RenderBatch[] workItems)
        {
            SlotBatches = slotBatches;
            WorkItems = workItems;
        }

        public RenderBatch[] SlotBatches { get; }

        public RenderBatch[] WorkItems { get; }

        public static RenderPlan Empty(int slotCount, TileRegion[] regions)
        {
            var batches = new RenderBatch[slotCount];

            for (var i = 0; i < slotCount; i++)
            {
                var region = regions[i];
                batches[i] = new RenderBatch(
                    slotIndex: i,
                    elements: Array.Empty<IDrawingElement>(),
                    startIndex: 0,
                    endIndex: 0,
                    drawOffsetX: -region.X,
                    drawOffsetY: -region.Y,
                    scale: 1.0f,
                    targetWidth: region.Width,
                    targetHeight: region.Height,
                    compositeX: region.X,
                    compositeY: region.Y);
            }

            return new RenderPlan(batches, Array.Empty<RenderBatch>());
        }
    }

    internal sealed class RenderBatch
    {
        public RenderBatch(
            int slotIndex,
            IDrawingElement[] elements,
            int startIndex,
            int endIndex,
            float drawOffsetX,
            float drawOffsetY,
            float scale,
            int targetWidth,
            int targetHeight,
            int compositeX,
            int compositeY)
        {
            SlotIndex = slotIndex;
            Elements = elements;
            StartIndex = startIndex;
            EndIndex = endIndex;
            DrawOffsetX = drawOffsetX;
            DrawOffsetY = drawOffsetY;
            Scale = scale;
            TargetWidth = Math.Max(1, targetWidth);
            TargetHeight = Math.Max(1, targetHeight);
            CompositeX = compositeX;
            CompositeY = compositeY;
        }

        public int SlotIndex { get; }

        public IDrawingElement[] Elements { get; }

        public int StartIndex { get; }

        public int EndIndex { get; }

        public int ElementCount => Math.Max(0, EndIndex - StartIndex);

        public float DrawOffsetX { get; }

        public float DrawOffsetY { get; }

        public float Scale { get; }

        public int TargetWidth { get; }

        public int TargetHeight { get; }

        public int CompositeX { get; }

        public int CompositeY { get; }
    }

    internal interface IRenderSlot : IDisposable
    {
        ID2D1Bitmap1 MainReadableBitmap { get; }

        int TargetWidth { get; }

        int TargetHeight { get; }

        void DrawBatch(RenderBatch batch);

        void AcquireForMainRead();

        void ReleaseToWorkerWrite();

        void ReleaseUnreadFrameToWorker();

        void ClearResourceCache();
    }

    /// <summary>
    /// 独立 Device Worker。使用共享 D3D11 Texture 和 keyed mutex 跨 Device 合成。
    /// </summary>
    internal sealed class MultiDeviceSlot : IRenderSlot
    {
        private readonly object _useLock = new();
        private readonly ID3D11Device _workerD3DDevice;
        private readonly ID3D11DeviceContext _workerD3DContext;
        private readonly IDXGIDevice _workerDxgiDevice;
        private readonly ID2D1Factory1 _workerD2DFactory;
        private readonly ID2D1Device _workerD2DDevice;
        private readonly ID2D1DeviceContext _workerD2DContext;
        private readonly ID3D11Texture2D _sharedTextureForWorker;
        private readonly IDXGIKeyedMutex _workerMutex;
        private readonly ID2D1Bitmap1 _workerTargetBitmap;
        private readonly ID3D11Texture2D _sharedTextureForMain;
        private readonly IDXGIKeyedMutex _mainMutex;

        private readonly Direct2DResourceCache _resourceCache;

        private MultiDeviceSlot(
            ID3D11Device workerD3DDevice,
            ID3D11DeviceContext workerD3DContext,
            IDXGIDevice workerDxgiDevice,
            ID2D1Factory1 workerD2DFactory,
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
            _workerD2DFactory = workerD2DFactory;
            _workerD2DDevice = workerD2DDevice;
            _workerD2DContext = workerD2DContext;
            _resourceCache = resourceCache;
            _sharedTextureForWorker = sharedTextureForWorker;
            _workerMutex = workerMutex;
            _workerTargetBitmap = workerTargetBitmap;
            _sharedTextureForMain = sharedTextureForMain;
            _mainMutex = mainMutex;
            MainReadableBitmap = mainReadableBitmap;
            TargetWidth = (int)workerTargetBitmap.PixelSize.Width;
            TargetHeight = (int)workerTargetBitmap.PixelSize.Height;
        }

        public ID2D1Bitmap1 MainReadableBitmap { get; }

        public int TargetWidth { get; }

        public int TargetHeight { get; }

        public static MultiDeviceSlot Create(
            IDXGIAdapter adapter,
            ID3D11Device mainD3DDevice,
            IDWriteFactory dwriteFactory,
            ID2D1DeviceContext mainD2DContext,
            int width,
            int height)
        {
            ID3D11Device? workerD3DDevice = null;
            ID3D11DeviceContext? workerD3DContext = null;
            IDXGIDevice? workerDxgiDevice = null;
            ID2D1Factory1? workerD2DFactory = null;
            ID2D1Device? workerD2DDevice = null;
            ID2D1DeviceContext? workerD2DContext = null;
            ID3D11Texture2D? sharedTextureForWorker = null;
            IDXGIKeyedMutex? workerMutex = null;
            ID2D1Bitmap1? workerTargetBitmap = null;
            ID3D11Texture2D? sharedTextureForMain = null;
            IDXGIKeyedMutex? mainMutex = null;
            ID2D1Bitmap1? mainReadableBitmap = null;
            Direct2DResourceCache? resourceCache = null;

            var featureLevels = new[]
            {
                Vortice.Direct3D.FeatureLevel.Level_11_1,
                Vortice.Direct3D.FeatureLevel.Level_11_0,
                Vortice.Direct3D.FeatureLevel.Level_10_1,
                Vortice.Direct3D.FeatureLevel.Level_10_0
            };

            try
            {
                D3D11.D3D11CreateDevice(
                    adapter,
                    DriverType.Unknown,
                    DeviceCreationFlags.BgraSupport,
                    featureLevels,
                    out ID3D11Device createdWorkerD3DDevice,
                    out _,
                    out ID3D11DeviceContext createdWorkerD3DContext).CheckError();

                workerD3DDevice = createdWorkerD3DDevice;
                workerD3DContext = createdWorkerD3DContext;

                sharedTextureForWorker = workerD3DDevice.CreateTexture2D(CreateSharedTextureDescription(width, height));
                workerMutex = sharedTextureForWorker.QueryInterface<IDXGIKeyedMutex>();

                nint sharedHandle;
                using (var dxgiResource = sharedTextureForWorker.QueryInterface<IDXGIResource>())
                {
                    sharedHandle = dxgiResource.SharedHandle;
                }

                workerDxgiDevice = workerD3DDevice.QueryInterface<IDXGIDevice>();
                workerD2DFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(
                    Vortice.Direct2D1.FactoryType.SingleThreaded);
                workerD2DDevice = workerD2DFactory.CreateDevice(workerDxgiDevice);
                workerD2DContext = workerD2DDevice.CreateDeviceContext(DeviceContextOptions.EnableMultithreadedOptimizations);
                workerTargetBitmap = CreateWorkerTargetBitmap(workerD2DContext, sharedTextureForWorker);
                workerD2DContext.Target = workerTargetBitmap;

                // 主 Device 只打开一次共享资源，之后每帧直接复用 MainReadableBitmap。
                sharedTextureForMain = mainD3DDevice.OpenSharedResource<ID3D11Texture2D>(sharedHandle);
                mainMutex = sharedTextureForMain.QueryInterface<IDXGIKeyedMutex>();
                mainReadableBitmap = CreateMainReadableBitmap(mainD2DContext, sharedTextureForMain);

                resourceCache = new Direct2DResourceCache(workerD2DFactory, dwriteFactory, workerD2DContext);

                return new MultiDeviceSlot(
                    workerD3DDevice,
                    workerD3DContext,
                    workerDxgiDevice,
                    workerD2DFactory,
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
            catch
            {
                try { resourceCache?.ClearCache(); } catch { }
                try { if (workerD2DContext is not null) workerD2DContext.Target = null; } catch { }

                try { mainReadableBitmap?.Dispose(); } catch { }
                try { mainMutex?.Dispose(); } catch { }
                try { sharedTextureForMain?.Dispose(); } catch { }

                try { workerTargetBitmap?.Dispose(); } catch { }
                try { workerMutex?.Dispose(); } catch { }
                try { sharedTextureForWorker?.Dispose(); } catch { }

                try { workerD2DContext?.Dispose(); } catch { }
                try { workerD2DDevice?.Dispose(); } catch { }
                try { workerD2DFactory?.Dispose(); } catch { }
                try { workerDxgiDevice?.Dispose(); } catch { }

                try { workerD3DContext?.ClearState(); } catch { }
                try { workerD3DContext?.Dispose(); } catch { }
                try { workerD3DDevice?.Dispose(); } catch { }

                throw;
            }
        }

        public void DrawBatch(RenderBatch batch)
        {
            lock (_useLock)
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

                    for (var i = batch.StartIndex; i < batch.EndIndex; i++)
                    {
                        batch.Elements[i].Draw(
                            _resourceCache,
                            _workerD2DContext,
                            batch.DrawOffsetX,
                            batch.DrawOffsetY,
                            batch.Scale);
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
            lock (_useLock)
            {
                _resourceCache.ClearCache();
            }
        }

        public void Dispose()
        {
            lock (_useLock)
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
                _workerD2DFactory.Dispose();
                _workerDxgiDevice.Dispose();

                try { _workerD3DContext.ClearState(); } catch { }
                _workerD3DContext.Dispose();
                _workerD3DDevice.Dispose();
            }
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

    /// <summary>
    /// 共享主 D2D Device 的 Worker。每个 Slot 只拥有独立 DeviceContext、目标位图和资源缓存；
    /// 目标位图与主 Context 属于同一资源域，因此不需要共享 Handle 或 keyed mutex。
    /// </summary>
    internal sealed class SharedDeviceContextSlot : IRenderSlot
    {
        private readonly object _useLock = new();
        private readonly ID2D1DeviceContext _workerD2DContext;
        private readonly ID2D1Bitmap1 _workerTargetBitmap;
        private readonly Direct2DResourceCache _resourceCache;
        private int _frameReadyForMain;
        private bool _disposed;

        private SharedDeviceContextSlot(
            ID2D1DeviceContext workerD2DContext,
            ID2D1Bitmap1 workerTargetBitmap,
            Direct2DResourceCache resourceCache)
        {
            _workerD2DContext = workerD2DContext;
            _workerTargetBitmap = workerTargetBitmap;
            _resourceCache = resourceCache;
            TargetWidth = (int)workerTargetBitmap.PixelSize.Width;
            TargetHeight = (int)workerTargetBitmap.PixelSize.Height;
        }

        public ID2D1Bitmap1 MainReadableBitmap => _workerTargetBitmap;

        public int TargetWidth { get; }

        public int TargetHeight { get; }

        public static SharedDeviceContextSlot Create(
            ID2D1Device sharedDevice,
            ID2D1Factory1 sharedFactory,
            IDWriteFactory dwriteFactory,
            int width,
            int height)
        {
            ID2D1DeviceContext? workerContext = null;
            ID2D1Bitmap1? workerTarget = null;
            Direct2DResourceCache? resourceCache = null;

            try
            {
                workerContext = sharedDevice.CreateDeviceContext(
                    DeviceContextOptions.EnableMultithreadedOptimizations);

                var bitmapProperties = new BitmapProperties1
                {
                    PixelFormat = new PixelFormat(
                        Format.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Premultiplied),
                    DpiX = 96.0f,
                    DpiY = 96.0f,
                    BitmapOptions = BitmapOptions.Target
                };

                workerTarget = workerContext.CreateBitmap(
                    new SizeI(Math.Max(1, width), Math.Max(1, height)),
                    nint.Zero,
                    0,
                    bitmapProperties);

                resourceCache = new Direct2DResourceCache(
                    sharedFactory,
                    dwriteFactory,
                    workerContext);

                return new SharedDeviceContextSlot(
                    workerContext,
                    workerTarget,
                    resourceCache);
            }
            catch
            {
                try { resourceCache?.ClearCache(); } catch { }
                try { if (workerContext is not null) workerContext.Target = null; } catch { }
                try { workerTarget?.Dispose(); } catch { }
                try { workerContext?.Dispose(); } catch { }
                throw;
            }
        }

        public void DrawBatch(RenderBatch batch)
        {
            lock (_useLock)
            {
                ThrowIfDisposed();

                if (Volatile.Read(ref _frameReadyForMain) != 0)
                    throw new InvalidOperationException("The previous shared-device frame has not been released.");

                var drawBegun = false;
                var frameReady = false;

                try
                {
                    // 目标位图在 Worker 绘制期间绑定；EndDraw 后解除绑定，主 Context 才把它作为输入图像。
                    _workerD2DContext.Target = _workerTargetBitmap;
                    _workerD2DContext.BeginDraw();
                    drawBegun = true;

                    _workerD2DContext.Transform = Matrix3x2.Identity;
                    _workerD2DContext.Clear(TransparentColor);

                    for (var i = batch.StartIndex; i < batch.EndIndex; i++)
                    {
                        batch.Elements[i].Draw(
                            _resourceCache,
                            _workerD2DContext,
                            batch.DrawOffsetX,
                            batch.DrawOffsetY,
                            batch.Scale);
                    }

                    _workerD2DContext.EndDraw();
                    drawBegun = false;
                    _workerD2DContext.Target = null;

                    Volatile.Write(ref _frameReadyForMain, 1);
                    frameReady = true;
                }
                finally
                {
                    if (drawBegun)
                    {
                        try { _workerD2DContext.EndDraw(); } catch { }
                    }

                    try { _workerD2DContext.Target = null; } catch { }

                    if (!frameReady)
                        Volatile.Write(ref _frameReadyForMain, 0);
                }
            }
        }

        public void AcquireForMainRead()
        {
            ThrowIfDisposed();

            if (Volatile.Read(ref _frameReadyForMain) == 0)
                throw new InvalidOperationException("The shared-device worker frame is not ready.");
        }

        public void ReleaseToWorkerWrite()
        {
            Volatile.Write(ref _frameReadyForMain, 0);
        }

        public void ReleaseUnreadFrameToWorker()
        {
            Volatile.Write(ref _frameReadyForMain, 0);
        }

        public void ClearResourceCache()
        {
            lock (_useLock)
            {
                ThrowIfDisposed();
                _resourceCache.ClearCache();
            }
        }

        public void Dispose()
        {
            lock (_useLock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                Volatile.Write(ref _frameReadyForMain, 0);

                try { _resourceCache.ClearCache(); } catch { }
                try { _workerD2DContext.Target = null; } catch { }
                _workerTargetBitmap.Dispose();
                _workerD2DContext.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SharedDeviceContextSlot));
        }
    }
}

/// <summary>
/// 主 Context 已经提交合成的 Worker Slot 集合。
/// 必须在主 Context.EndDraw() 之后释放，避免下一帧 Worker 提前覆盖离屏位图。
/// </summary>
internal sealed class MultiDeviceFrameLease : IDisposable
{
    private readonly MultiDeviceRenderer.IRenderSlot[] _slots;
    private int _count;
    private bool _disposed;

    public MultiDeviceFrameLease(int capacity)
    {
        _slots = new MultiDeviceRenderer.IRenderSlot[Math.Max(0, capacity)];
    }

    public void Add(MultiDeviceRenderer.IRenderSlot slot)
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
                // 归还给 Worker，下一帧才允许继续写入离屏目标。
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
