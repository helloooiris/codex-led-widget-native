using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CodexLedWidget.Core;

namespace CodexLedWidget.Mac;

internal sealed class QuotaOrbControl : Control
{
    private static readonly IPen ShellPen = new Pen(new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)), 1.35);
    private static readonly IPen InnerPen = new Pen(new SolidColorBrush(Color.FromArgb(34, 18, 52, 78)), 1);
    private static readonly IPen DividerPen = new Pen(new SolidColorBrush(Color.FromArgb(106, 255, 255, 255)), 1);
    private static readonly IPen DividerShadowPen = new Pen(new SolidColorBrush(Color.FromArgb(34, 18, 32, 51)), 1);
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(18, 32, 51));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(82, 98, 115));

    private DualQuotaMeter meter = new(
        new DualQuotaMeterSegment("--", 0, false),
        new DualQuotaMeterSegment("--", 0, false));

    public void Render(DualQuotaMeter nextMeter)
    {
        meter = nextMeter;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double size = Math.Min(Bounds.Width, Bounds.Height);
        Rect orb = new(
            (Bounds.Width - size) / 2 + 3,
            (Bounds.Height - size) / 2 + 3,
            size - 6,
            size - 6);

        context.DrawEllipse(ShellBrush(), ShellPen, orb);
        DrawSegment(context, orb, meter.Left, true);
        DrawSegment(context, orb, meter.Right, false);
        DrawGlassHighlights(context, orb);
        context.DrawEllipse(null, ShellPen, orb);
        context.DrawEllipse(null, InnerPen, orb.Deflate(1.2));
        context.DrawLine(DividerPen, new Point(orb.Center.X, orb.Y + 12), new Point(orb.Center.X, orb.Bottom - 12));
        context.DrawLine(DividerShadowPen, new Point(orb.Center.X + 1, orb.Y + 13), new Point(orb.Center.X + 1, orb.Bottom - 13));
        DrawLabel(context, meter.Left, new Point(orb.X + orb.Width * 0.27, orb.Center.Y));
        DrawLabel(context, meter.Right, new Point(orb.X + orb.Width * 0.73, orb.Center.Y));
    }

    private static void DrawSegment(DrawingContext context, Rect orb, DualQuotaMeterSegment segment, bool isLeft)
    {
        if (!segment.HasData)
        {
            return;
        }

        double halfWidth = orb.Width / 2;
        double fillHeight = Math.Clamp(segment.RemainingPercent / 100.0 * orb.Height, 5, orb.Height);
        Rect clip = new(
            isLeft ? orb.X : orb.X + halfWidth,
            orb.Bottom - fillHeight,
            halfWidth,
            fillHeight);

        using (context.PushClip(clip))
        {
            context.DrawEllipse(isLeft ? PrimaryFillBrush() : SecondaryFillBrush(), null, orb);
            context.DrawEllipse(SegmentVeilBrush(), null, orb.Deflate(9));
        }
    }

    private static void DrawGlassHighlights(DrawingContext context, Rect orb)
    {
        Rect topGlow = new(orb.X + 8, orb.Y + 4, orb.Width - 16, orb.Height * 0.48);
        context.DrawEllipse(TopGlowBrush(), null, topGlow);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(72, 255, 255, 255)), 1.3), orb.Deflate(10));
    }

    private static void DrawLabel(DrawingContext context, DualQuotaMeterSegment segment, Point center)
    {
        DrawCenteredText(context, segment.ShortLabel, new Point(center.X, center.Y - 16), 11, FontWeight.SemiBold, MutedBrush);
        DrawCenteredText(context, segment.PercentText, new Point(center.X, center.Y + 2), 16, FontWeight.Bold, TextBrush);
    }

    private static void DrawCenteredText(
        DrawingContext context,
        string text,
        Point center,
        double fontSize,
        FontWeight weight,
        IBrush brush)
    {
        FormattedText formattedText = new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, weight),
            fontSize,
            brush);
        context.DrawText(formattedText, new Point(center.X - formattedText.Width / 2, center.Y));
    }

    private static IBrush ShellBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.18, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.86, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(218, 252, 255, 255), 0),
                new GradientStop(Color.FromArgb(118, 212, 238, 255), 0.54),
                new GradientStop(Color.FromArgb(162, 252, 255, 255), 1)
            ]
        };
    }

    private static IBrush PrimaryFillBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.02, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.94, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(184, 124, 255, 220), 0),
                new GradientStop(Color.FromArgb(202, 22, 214, 148), 0.48),
                new GradientStop(Color.FromArgb(174, 0, 150, 132), 1)
            ]
        };
    }

    private static IBrush SecondaryFillBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.06, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.96, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(178, 134, 224, 255), 0),
                new GradientStop(Color.FromArgb(204, 45, 128, 255), 0.52),
                new GradientStop(Color.FromArgb(164, 118, 72, 238), 1)
            ]
        };
    }

    private static IBrush SegmentVeilBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.2, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.8, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(78, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(18, 255, 255, 255), 0.56),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
            ]
        };
    }

    private static IBrush TopGlowBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(116, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(42, 255, 255, 255), 0.62),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
            ]
        };
    }
}
