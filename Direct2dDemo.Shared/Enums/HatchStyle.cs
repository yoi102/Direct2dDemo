namespace Direct2dDemo.Shared.Enums;

public enum HatchStyle : uint
{
    /// <summary>A 45-degree upward, left-to-right hatch</summary>
    BackwardDiagonal = 3,

    /// <summary>Horizontal and vertical cross-hatch</summary>
    Cross = 4,

    /// <summary>45-degree crosshatch</summary>
    DiagCross = 5,

    /// <summary>A 45-degree downward, left-to-right hatch</summary>
    ForwardDiagonal = 2,

    /// <summary>Horizontal hatch</summary>
    Horizontal = 0,

    /// <summary>Vertical hatch</summary>
    Vertical = 1
}