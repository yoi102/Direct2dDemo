namespace Direct2dDemo.Direct2D;

/// <summary>
/// 多 Device 渲染任务的划分方式。
/// </summary>
public enum MultiThreadPartitionMode
{
    /// <summary>
    /// 根据空间重复率和各 tile 负载，在 Tiles 与 ElementChunks 之间自动选择。
    /// </summary>
    Auto,

    /// <summary>
    /// 按元素顺序切成连续区间；适合长线、大图形和高度重叠的场景。
    /// </summary>
    ElementChunks,

    /// <summary>
    /// 按屏幕空间切成互不重叠的 tile；适合局部、分散的小图形。
    /// </summary>
    Tiles
}
