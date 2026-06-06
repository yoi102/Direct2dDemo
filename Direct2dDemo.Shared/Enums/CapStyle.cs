namespace Direct2dDemo.Shared.Enums;

public enum CapStyle : uint
{
    /// <summary>A cap that does not extend past the last point of the line. Comparable to cap used for objects other than lines.</summary>
    Flat,

    /// <summary>Half of a square that has a length equal to the line thickness.</summary>
    Square,

    /// <summary>A semicircle that has a diameter equal to the line thickness.</summary>
    Round,

    ///// <summary>An isosceles right triangle whose hypotenuse is equal in length to the thickness of the line.</summary>
    //Triangle,
}