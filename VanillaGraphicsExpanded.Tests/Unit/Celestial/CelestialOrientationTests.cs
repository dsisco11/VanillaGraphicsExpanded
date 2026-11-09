using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests.Unit.Celestial;

/// <summary>Checks the calendar adapter against independently authored axis and phase examples.</summary>
public sealed class CelestialOrientationTests
{
    #region Public API
    /// <summary>Every admitted latitude produces a proper rotation and the installed tilted pole.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(1)]
    public void CalendarRotationIsOrthonormalAndRetainsLatitudePole(double latitude)
    {
        // Arrange: use each admitted latitude as the calendar adapter input.
        // Act: build the rotation and transform its basis axes.
        Matrix4x4 rotation=CelestialOrientation.Create(21.3,.6,latitude);
        Vector3 x=Vector3.TransformNormal(Vector3.UnitX,rotation),y=Vector3.TransformNormal(Vector3.UnitY,rotation),z=Vector3.TransformNormal(Vector3.UnitZ,rotation);
        // Assert: retain an orthonormal, proper rotation and the expected tilted pole.
        Assert.InRange(Math.Abs(rotation.GetDeterminant()-1),0,.000001f);
        Assert.InRange(Math.Abs(x.LengthSquared()-1),0,.000001f);
        Assert.InRange(Math.Abs(Vector3.Dot(x,y)),0,.000001f);
        Assert.InRange(Math.Abs(Vector3.Dot(y,z)),0,.000001f);
        float tilt=MathF.Acos(MathF.Cos((float)latitude*MathF.PI/2-.40910518f));
        Assert.InRange(Vector3.Distance(y,new(0,-MathF.Sin(tilt),MathF.Cos(tilt))),0,.000001f);
    }
    /// <summary>Daily-minus-year phase rotates a quarter turn and remains periodic across negative dates.</summary>
    [Fact]
    public void DailyMinusYearPhaseControlsRotation()
    {
        // Arrange: compare the origin phase with a quarter-turn daily-minus-year offset.
        // Act: create both calendar orientations and compute the independently rotated axis.
        Matrix4x4 origin=CelestialOrientation.Create(0,0,0),quarter=CelestialOrientation.Create(.5,.25,0);
        Vector3 pole=Vector3.TransformNormal(Vector3.UnitY,origin);
        Vector3 expected=Vector3.Transform(Vector3.UnitX,Quaternion.CreateFromAxisAngle(pole,MathF.PI/2));
        // Assert: the phase rotates by a quarter turn and wraps periodically for negative dates.
        Assert.InRange(Vector3.Distance(expected,Vector3.TransformNormal(Vector3.UnitX,quarter)),0,.000001f);
        Assert.Equal(quarter,CelestialOrientation.Create(-.5,.25,0));
    }
    /// <summary>Malformed calendar values cannot publish a nonfinite celestial frame.</summary>
    [Fact]
    public void InvalidCalendarInputsRejectPublication()
    {
        // Arrange: choose malformed day, year, and latitude values.
        // Act: submit each malformed calendar input.
        // Assert: invalid values fail before a frame is published.
        Assert.Throws<ArgumentException>(()=>CelestialOrientation.Create(double.NaN,0,0));
        Assert.Throws<ArgumentException>(()=>CelestialOrientation.Create(0,double.PositiveInfinity,0));
        Assert.Throws<ArgumentException>(()=>CelestialOrientation.Create(0,0,1.01));
    }
    #endregion
}
