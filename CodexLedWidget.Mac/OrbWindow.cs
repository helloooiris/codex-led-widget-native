using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CodexLedWidget.Core;

namespace CodexLedWidget.Mac;

public sealed class OrbWindow : Window
{
    private readonly Action expandPanel;
    private readonly QuotaOrbControl quotaOrb = new();
    private Point? mouseDownPoint;
    private PixelPoint windowOrigin;
    private bool wasDragged;

    public OrbWindow(Action expandPanel)
    {
        this.expandPanel = expandPanel;

        Width = 138;
        Height = 138;
        MinWidth = 138;
        MinHeight = 138;
        MaxWidth = 138;
        MaxHeight = 138;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = BuildContent();
    }

    public void Render(DualQuotaMeter meter)
    {
        quotaOrb.Render(meter);
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            Width = 138,
            Height = 138,
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255))
        };
        root.PointerPressed += OrbPointerPressed;
        root.PointerMoved += OrbPointerMoved;
        root.PointerReleased += OrbPointerReleased;

        root.Children.Add(new Ellipse
        {
            Width = 132,
            Height = 132,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
            Stroke = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)),
            StrokeThickness = 1.2
        });

        quotaOrb.Width = 118;
        quotaOrb.Height = 118;
        quotaOrb.HorizontalAlignment = HorizontalAlignment.Center;
        quotaOrb.VerticalAlignment = VerticalAlignment.Center;
        root.Children.Add(quotaOrb);

        return root;
    }

    private void OrbPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            expandPanel();
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        mouseDownPoint = e.GetPosition(this);
        windowOrigin = Position;
        wasDragged = false;
        e.Pointer.Capture(sender as IInputElement);
    }

    private void OrbPointerMoved(object? sender, PointerEventArgs e)
    {
        if (mouseDownPoint is not Point startPoint)
        {
            return;
        }

        Point currentPoint = e.GetPosition(this);
        Vector delta = currentPoint - startPoint;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
        {
            return;
        }

        wasDragged = true;
        Position = new PixelPoint(
            windowOrigin.X + (int)Math.Round(delta.X),
            windowOrigin.Y + (int)Math.Round(delta.Y));
    }

    private void OrbPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool shouldExpandPanel = mouseDownPoint.HasValue && !wasDragged;
        mouseDownPoint = null;
        wasDragged = false;
        e.Pointer.Capture(null);

        if (shouldExpandPanel)
        {
            expandPanel();
        }
    }
}
