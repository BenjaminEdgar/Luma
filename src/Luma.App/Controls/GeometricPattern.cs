using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Luma.App.Controls;

/// <summary>Soft geometric lattice for a living panel backdrop. Stack two instances
/// with different cell sizes / phase speeds for a cheap parallax field.
/// Non-interactive — keep opacity low so content stays primary.</summary>
public sealed class GeometricPattern : Control
{
    public static readonly StyledProperty<double> PhaseProperty =
        AvaloniaProperty.Register<GeometricPattern, double>(nameof(Phase));

    public static readonly StyledProperty<Color> PatternColorProperty =
        AvaloniaProperty.Register<GeometricPattern, Color>(nameof(PatternColor), Color.Parse("#7C4DFF"));

    public static readonly StyledProperty<double> PatternOpacityProperty =
        AvaloniaProperty.Register<GeometricPattern, double>(nameof(PatternOpacity), 0.14);

    public static readonly StyledProperty<double> CellSizeProperty =
        AvaloniaProperty.Register<GeometricPattern, double>(nameof(CellSize), 22);

    /// <summary>Y drift relative to X phase (negative shears the other way for parallax).</summary>
    public static readonly StyledProperty<double> ShearProperty =
        AvaloniaProperty.Register<GeometricPattern, double>(nameof(Shear), 0.55);

    public static readonly StyledProperty<bool> ShowDiagonalsProperty =
        AvaloniaProperty.Register<GeometricPattern, bool>(nameof(ShowDiagonals), true);

    public static readonly StyledProperty<bool> ShowDotsProperty =
        AvaloniaProperty.Register<GeometricPattern, bool>(nameof(ShowDots), true);

    static GeometricPattern()
    {
        AffectsRender<GeometricPattern>(
            PhaseProperty,
            PatternColorProperty,
            PatternOpacityProperty,
            CellSizeProperty,
            ShearProperty,
            ShowDiagonalsProperty,
            ShowDotsProperty,
            BoundsProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<GeometricPattern>(false);
        FocusableProperty.OverrideDefaultValue<GeometricPattern>(false);
    }

    public double Phase
    {
        get => GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public Color PatternColor
    {
        get => GetValue(PatternColorProperty);
        set => SetValue(PatternColorProperty, value);
    }

    public double PatternOpacity
    {
        get => GetValue(PatternOpacityProperty);
        set => SetValue(PatternOpacityProperty, value);
    }

    public double CellSize
    {
        get => GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    public double Shear
    {
        get => GetValue(ShearProperty);
        set => SetValue(ShearProperty, value);
    }

    public bool ShowDiagonals
    {
        get => GetValue(ShowDiagonalsProperty);
        set => SetValue(ShowDiagonalsProperty, value);
    }

    public bool ShowDots
    {
        get => GetValue(ShowDotsProperty);
        set => SetValue(ShowDotsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1) return;

        var step = Math.Max(12, CellSize);
        var phase = Phase % step;
        var ox = phase;
        var oy = phase * Shear;

        // Keep shear offsets tiling-friendly.
        oy %= step;
        if (oy < 0) oy += step;

        var baseColor = PatternColor;
        var alpha = (byte)Math.Clamp((int)(PatternOpacity * 255), 0, 255);
        var soft = Color.FromArgb((byte)Math.Max(8, alpha * 0.55), baseColor.R, baseColor.G, baseColor.B);
        var mid = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
        var softBrush = new SolidColorBrush(soft);
        var midBrush = new SolidColorBrush(mid);
        var linePen = new Pen(softBrush, 0.75);

        if (ShowDiagonals)
        {
            var diagStep = step * 2;
            var diagStart = -h - w;
            var diagPhase = oy % diagStep;
            if (diagPhase < 0) diagPhase += diagStep;
            for (var d = diagStart + diagPhase; d < w + h; d += diagStep)
            {
                context.DrawLine(linePen, new Point(d, 0), new Point(d - h, h));
            }
        }

        if (!ShowDots) return;

        var row = 0;
        for (var y = -step + oy; y < h + step; y += step, row++)
        {
            var rowShift = (row & 1) == 0 ? 0 : step * 0.5;
            for (var x = -step + ox + rowShift; x < w + step; x += step)
            {
                context.DrawEllipse(midBrush, null, new Point(x, y), 1.15, 1.15);
                if (((int)(x / step) + row) % 3 == 0)
                    context.DrawEllipse(null, linePen, new Point(x, y), 3.2, 3.2);
            }
        }
    }
}
