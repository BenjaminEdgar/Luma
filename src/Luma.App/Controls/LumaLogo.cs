using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Luma.App.Controls;

/// <summary>Luma brand mark — the four-point aurora star, optionally wrapped in the
/// "minimal orbit" ring with its satellite dot (per the brand boards: app icon = star only,
/// lockups = star + orbit). Vector-drawn so it stays crisp from dock size to hero size.
/// Animate <see cref="OrbitAngle"/> (a plain double — safe for keyframes) for a live orbit.</summary>
public sealed class LumaLogo : Control
{
    public static readonly StyledProperty<IBrush?> StarBrushProperty =
        AvaloniaProperty.Register<LumaLogo, IBrush?>(nameof(StarBrush), Brushes.White);

    /// <summary>Ring stroke; falls back to <see cref="StarBrush"/> when unset.</summary>
    public static readonly StyledProperty<IBrush?> OrbitBrushProperty =
        AvaloniaProperty.Register<LumaLogo, IBrush?>(nameof(OrbitBrush));

    /// <summary>Satellite dot fill; falls back to <see cref="OrbitBrush"/> then <see cref="StarBrush"/>.</summary>
    public static readonly StyledProperty<IBrush?> SatelliteBrushProperty =
        AvaloniaProperty.Register<LumaLogo, IBrush?>(nameof(SatelliteBrush));

    public static readonly StyledProperty<bool> ShowOrbitProperty =
        AvaloniaProperty.Register<LumaLogo, bool>(nameof(ShowOrbit), true);

    /// <summary>Degrees; 0 = right, negative = up-right (Y-down coordinates). Brand rest pose is -55.</summary>
    public static readonly StyledProperty<double> OrbitAngleProperty =
        AvaloniaProperty.Register<LumaLogo, double>(nameof(OrbitAngle), -55);

    static LumaLogo()
    {
        AffectsRender<LumaLogo>(
            StarBrushProperty,
            OrbitBrushProperty,
            SatelliteBrushProperty,
            ShowOrbitProperty,
            OrbitAngleProperty,
            BoundsProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<LumaLogo>(false);
        FocusableProperty.OverrideDefaultValue<LumaLogo>(false);
    }

    public IBrush? StarBrush
    {
        get => GetValue(StarBrushProperty);
        set => SetValue(StarBrushProperty, value);
    }

    public IBrush? OrbitBrush
    {
        get => GetValue(OrbitBrushProperty);
        set => SetValue(OrbitBrushProperty, value);
    }

    public IBrush? SatelliteBrush
    {
        get => GetValue(SatelliteBrushProperty);
        set => SetValue(SatelliteBrushProperty, value);
    }

    public bool ShowOrbit
    {
        get => GetValue(ShowOrbitProperty);
        set => SetValue(ShowOrbitProperty, value);
    }

    public double OrbitAngle
    {
        get => GetValue(OrbitAngleProperty);
        set => SetValue(OrbitAngleProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 1) return;

        var cx = Bounds.Width / 2;
        var cy = Bounds.Height / 2;
        var u = size / 100.0; // brand geometry is authored on a 100-unit box
        var star = StarBrush ?? Brushes.White;

        double starRadius;
        if (ShowOrbit)
        {
            var orbit = OrbitBrush ?? star;
            var ringRadius = 44 * u;
            context.DrawEllipse(null, new Pen(orbit, 6.5 * u), new Point(cx, cy), ringRadius, ringRadius);

            var rad = OrbitAngle * Math.PI / 180.0;
            var dot = new Point(cx + Math.Cos(rad) * ringRadius, cy + Math.Sin(rad) * ringRadius);
            context.DrawEllipse(SatelliteBrush ?? orbit, null, dot, 7.5 * u, 7.5 * u);

            starRadius = 27 * u;
        }
        else
        {
            starRadius = 50 * u;
        }

        context.DrawGeometry(star, null, BuildStar(cx, cy, starRadius));
    }

    /// <summary>Concave four-point sparkle: cubic curves bow each edge toward center.</summary>
    private static StreamGeometry BuildStar(double cx, double cy, double r)
    {
        const double a = 0.10, b = 0.433; // control-point pull, tuned to the brand star silhouette
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var top = new Point(cx, cy - r);
        ctx.BeginFigure(top, isFilled: true);
        ctx.CubicBezierTo(new Point(cx + a * r, cy - b * r), new Point(cx + b * r, cy - a * r), new Point(cx + r, cy));
        ctx.CubicBezierTo(new Point(cx + b * r, cy + a * r), new Point(cx + a * r, cy + b * r), new Point(cx, cy + r));
        ctx.CubicBezierTo(new Point(cx - a * r, cy + b * r), new Point(cx - b * r, cy + a * r), new Point(cx - r, cy));
        ctx.CubicBezierTo(new Point(cx - b * r, cy - a * r), new Point(cx - a * r, cy - b * r), top);
        ctx.EndFigure(true);
        return geometry;
    }
}
