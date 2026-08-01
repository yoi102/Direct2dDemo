namespace Direct2dDemo.Direct2D;

/// <summary>
/// Direct2D 并行绘制时 Worker 使用的设备拓扑。
/// </summary>
public enum MultiThreadDeviceMode
{
    /// <summary>
    /// 每个 Worker 使用独立的 D3D11 Device、D2D Device 和 D2D DeviceContext，
    /// 通过共享纹理和 keyed mutex 把结果交给主 Device 合成。
    /// </summary>
    MultipleDevices,

    /// <summary>
    /// 所有 Worker 共享主 D2D Device，每个 Worker 使用独立的 D2D DeviceContext
    /// 和同一资源域内的离屏目标位图。
    /// </summary>
    SingleDeviceMultipleContexts
}
