namespace Direct2dDemo.Shared.Elements.GeometryElements;

/// <summary>
/// 需要先生成 Geometry，再通过 DrawGeometry / FillGeometry 绘制的元素。
/// 适合缓存、缩放、移动、复杂图形。
/// </summary>
public interface IGeometryElement : IDrawingElement
{
}