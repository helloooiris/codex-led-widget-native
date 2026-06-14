using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CodexLedWidget.Core;
using System.Diagnostics;

namespace CodexLedWidget.Mac;

public sealed class MainWindow : Window
{
    private readonly CodexQuotaClient quotaClient = new();
    private readonly DispatcherTimer refreshTimer;
    private readonly DispatcherTimer panelSignalTimer;
    private readonly ContentControl panelView = new();
    private readonly ContentControl floatingOrbView = new();
    private readonly QuotaOrbControl panelQuotaOrb = new();
    private readonly QuotaOrbControl floatingQuotaOrb = new();
    private readonly TextBlock stateText = new();
    private readonly TextBlock statusText = new();
    private readonly TextBlock primaryLabel = new();
    private readonly TextBlock primaryText = new();
    private readonly TextBlock secondaryLabel = new();
    private readonly TextBlock secondaryText = new();
    private readonly TextBlock planLabel = new();
    private readonly TextBlock planText = new();
    private readonly TextBlock languageButtonText = new();
    private readonly Ellipse trafficLight = new();
    private readonly Ellipse statusDot = new();
    private readonly Button pinButton = new();
    private TrayIcon? trayIcon;
    private OrbWindow? orbWindow = null;
    private bool isTopmost = true;
    private bool isEnglish;
    private QuotaSnapshot? lastSnapshot;
    private WidgetViewMode viewMode = WidgetViewMode.Panel;
    private DualQuotaMeter currentMeter = DualQuotaMeter.FromSnapshot(CreateEmptySnapshot(), "zh-CN");
    private PixelPoint expandedPosition = default;
    private Size expandedSize = default;
    private Point? floatingMouseDownPoint;
    private PixelPoint floatingWindowOrigin;
    private bool floatingWasDragged;
    private DateTime lastPanelSignalWriteUtc = DateTime.MinValue;

    public MainWindow()
    {
        Width = 460;
        Height = 292;
        MinWidth = 320;
        MinHeight = 203;
        Title = "Codex LED Widget";
        Icon = LoadWindowIcon();
        Topmost = isTopmost;
        CanResize = true;
        ShowInTaskbar = true;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = BuildRoot();

        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        refreshTimer.Tick += async (_, _) => await RefreshQuotaAsync();
        panelSignalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        panelSignalTimer.Tick += (_, _) => CheckPanelSignal();

        Opened += async (_, _) =>
        {
            PlaceWindowTopRight();
            CreateTrayIcon();
            InitializePanelSignalCheckpoint();
            refreshTimer.Start();
            panelSignalTimer.Start();
            await RefreshQuotaAsync();
        };
        Closed += (_, _) =>
        {
            refreshTimer.Stop();
            panelSignalTimer.Stop();
            orbWindow?.Close();
            trayIcon?.Dispose();
        };
    }

    private Control BuildRoot()
    {
        Grid root = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255))
        };

        panelView.Content = BuildPanel();
        floatingOrbView.Content = BuildFloatingOrb();
        floatingOrbView.IsVisible = false;
        root.Children.Add(panelView);
        root.Children.Add(floatingOrbView);
        return root;
    }

    private Control BuildPanel()
    {
        Viewbox viewbox = new() { Stretch = Stretch.Uniform };
        Grid panelRoot = new()
        {
            Width = 440,
            Height = 272,
            Clip = new RectangleGeometry(new Rect(0, 0, 440, 272), 18, 18)
        };
        panelRoot.PointerPressed += PanelRoot_PointerPressed;

        panelRoot.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(18),
            BorderBrush = new SolidColorBrush(Color.FromArgb(31, 40, 55, 71)),
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(Color.FromArgb(250, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(241, 234, 245, 255), 1)
                ]
            }
        });
        panelRoot.Children.Add(BlurCircle(170, "#667BE0BD", HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(-50, 0, 0, -70)));
        panelRoot.Children.Add(BlurCircle(170, "#558EC5FF", HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -64, -54, 0)));
        panelRoot.Children.Add(BuildPanelContent());
        viewbox.Child = panelRoot;
        return viewbox;
    }

    private Control BuildPanelContent()
    {
        Grid content = new() { Margin = new Thickness(14) };
        content.RowDefinitions.Add(new RowDefinition(new GridLength(54)));
        content.RowDefinitions.Add(new RowDefinition(new GridLength(166)));
        content.RowDefinitions.Add(new RowDefinition(new GridLength(24)));

        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetRow(header, 0);
        content.Children.Add(header);

        StackPanel titleBlock = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 4, 0, 0)
        };
        trafficLight.Width = 17;
        trafficLight.Height = 17;
        trafficLight.Margin = new Thickness(0, 7, 10, 0);
        titleBlock.Children.Add(trafficLight);
        titleBlock.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "Codex 额度",
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    Foreground = TextBrush()
                },
                stateText
            }
        });
        stateText.Margin = new Thickness(0, 2, 0, 0);
        stateText.FontSize = 12;
        stateText.Foreground = MutedBrush();
        header.Children.Add(titleBlock);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(actions, 1);
        actions.Children.Add(IconButton("●", "收起为悬浮球", (_, _) => CollapseToFloatingOrb()));
        Button languageButton = IconButton("", "Language", (_, _) => ToggleLanguage());
        languageButtonText.Text = "EN";
        languageButtonText.FontSize = 12;
        languageButtonText.FontWeight = FontWeight.SemiBold;
        languageButton.Content = languageButtonText;
        languageButton.Width = 34;
        actions.Children.Add(languageButton);
        pinButton.Content = "PIN";
        pinButton.Width = 38;
        ApplyButtonChrome(pinButton);
        pinButton.Click += (_, _) => ToggleTopmost();
        actions.Children.Add(pinButton);
        actions.Children.Add(IconButton("↻", "刷新额度", async (_, _) => await RefreshQuotaAsync()));
        actions.Children.Add(IconButton("−", "隐藏", (_, _) => Hide()));
        actions.Children.Add(IconButton("×", "退出", (_, _) => Shutdown()));
        header.Children.Add(actions);

        Grid body = new();
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(126)));
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetRow(body, 1);
        content.Children.Add(body);

        panelQuotaOrb.Width = 118;
        panelQuotaOrb.Height = 118;
        panelQuotaOrb.HorizontalAlignment = HorizontalAlignment.Left;
        panelQuotaOrb.VerticalAlignment = VerticalAlignment.Top;
        panelQuotaOrb.Margin = new Thickness(0, 23, 0, 0);
        body.Children.Add(panelQuotaOrb);

        StackPanel cards = new() { Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(cards, 1);
        cards.Children.Add(QuotaCard(primaryLabel, primaryText, new Thickness(0)));
        cards.Children.Add(QuotaCard(secondaryLabel, secondaryText, new Thickness(0, 7, 0, 0)));
        cards.Children.Add(QuotaCard(planLabel, planText, new Thickness(0, 7, 0, 0)));
        SetQuotaTextPlaceholders();
        body.Children.Add(cards);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(footer, 2);
        statusDot.Width = 8;
        statusDot.Height = 8;
        statusDot.Margin = new Thickness(4, 0, 10, 0);
        statusText.FontSize = 12;
        statusText.VerticalAlignment = VerticalAlignment.Center;
        statusText.Foreground = MutedBrush();
        footer.Children.Add(statusDot);
        footer.Children.Add(statusText);
        content.Children.Add(footer);

        return content;
    }

    private Control BuildFloatingOrb()
    {
        Viewbox viewbox = new() { Stretch = Stretch.Uniform };
        Grid orbRoot = new()
        {
            Width = 138,
            Height = 138,
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255))
        };
        orbRoot.PointerPressed += FloatingOrb_PointerPressed;
        orbRoot.PointerMoved += FloatingOrb_PointerMoved;
        orbRoot.PointerReleased += FloatingOrb_PointerReleased;

        orbRoot.Children.Add(new Ellipse
        {
            Width = 132,
            Height = 132,
            Fill = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
            Stroke = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)),
            StrokeThickness = 1.2
        });
        floatingQuotaOrb.Width = 118;
        floatingQuotaOrb.Height = 118;
        floatingQuotaOrb.HorizontalAlignment = HorizontalAlignment.Center;
        floatingQuotaOrb.VerticalAlignment = VerticalAlignment.Center;
        orbRoot.Children.Add(floatingQuotaOrb);

        viewbox.Child = orbRoot;
        return viewbox;
    }

    private async Task RefreshQuotaAsync()
    {
        SetLoading();

        try
        {
            lastSnapshot = await quotaClient.GetQuotaAsync();
            RenderQuota(lastSnapshot);
        }
        catch (Exception ex)
        {
            stateText.Text = isEnglish ? "Error" : "读取失败";
            statusText.Text = ex.Message;
            RenderMeter(DualQuotaMeter.FromSnapshot(CreateEmptySnapshot(), CultureName));
            SetStateBrush(Color.FromRgb(205, 92, 92));
        }
    }

    private void SetLoading()
    {
        SetQuotaTextPlaceholders();
        stateText.Text = isEnglish ? "Reading" : "读取中";
        statusText.Text = isEnglish ? "Reading Codex quota..." : "正在读取 Codex 额度...";
        SetStateBrush(Color.FromRgb(59, 130, 246));
    }

    private void RenderQuota(QuotaSnapshot snapshot)
    {
        QuotaSnapshot displaySnapshot = snapshot;
        int remaining = displaySnapshot.RemainingPercent ?? 0;
        RenderMeter(DualQuotaMeter.FromSnapshot(displaySnapshot, CultureName));
        primaryLabel.Text = isEnglish ? "5h window" : "5小时窗口";
        secondaryLabel.Text = isEnglish ? "7d window" : "7天窗口";
        planLabel.Text = isEnglish ? "Plan" : "计划";
        primaryText.Text = QuotaTextFormatter.FormatWindow(displaySnapshot.Primary, CultureName);
        secondaryText.Text = QuotaTextFormatter.FormatWindow(displaySnapshot.Secondary, CultureName);
        planText.Text = QuotaTextFormatter.FormatPlan(displaySnapshot.PlanType);
        stateText.Text = remaining <= 0 ? (isEnglish ? "Empty" : "耗尽") : remaining < 10 ? (isEnglish ? "Low" : "偏低") : (isEnglish ? "Ready" : "可用");
        statusText.Text = $"{(isEnglish ? "Updated" : "已更新")} {DateTime.Now:HH:mm}";
        SetStateBrush(remaining <= 0 ? Color.FromRgb(205, 92, 92) : remaining < 10 ? Color.FromRgb(241, 183, 47) : Color.FromRgb(24, 182, 115));
    }

    private void SetQuotaTextPlaceholders()
    {
        primaryLabel.Text = isEnglish ? "5h window" : "5小时窗口";
        secondaryLabel.Text = isEnglish ? "7d window" : "7天窗口";
        planLabel.Text = isEnglish ? "Plan" : "计划";
        primaryText.Text = "--";
        secondaryText.Text = "--";
        planText.Text = "--";
    }

    private void RenderMeter(DualQuotaMeter meter)
    {
        currentMeter = meter;
        panelQuotaOrb.Render(meter);
        floatingQuotaOrb.Render(meter);
        orbWindow?.Render(meter);
    }

    private void ToggleLanguage()
    {
        isEnglish = !isEnglish;
        languageButtonText.Text = isEnglish ? "中" : "EN";

        if (lastSnapshot is not null)
        {
            RenderQuota(lastSnapshot);
        }
    }

    private void ToggleTopmost()
    {
        isTopmost = !isTopmost;
        Topmost = isTopmost;
        if (orbWindow is not null)
        {
            orbWindow.Topmost = isTopmost;
        }

        pinButton.Opacity = isTopmost ? 1 : 0.6;
        UpdateTrayMenu();
    }

    private void CollapseToFloatingOrb()
    {
        InitializePanelSignalCheckpoint();
        StartOrbHelper();
        Close();
    }

    private void HideToMenuBar()
    {
        ShowFloatingOrbUnavailable();
        UpdateTrayMenu();
    }

    private void ShowFloatingOrbUnavailable()
    {
        statusText.Text = isEnglish
            ? "Floating orb is temporarily disabled on macOS."
            : "macOS 版暂时禁用了悬浮球。";
        SetStateBrush(Color.FromRgb(241, 183, 47));
    }

    private void StartOrbHelper()
    {
        string helperPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "Helpers",
            "CodexLedOrb"));

        if (!File.Exists(helperPath))
        {
            statusText.Text = isEnglish
                ? "Floating orb helper was not found."
                : "未找到悬浮球组件。";
            SetStateBrush(Color.FromRgb(241, 183, 47));
            return;
        }

        TerminateOrbHelpers();
        Process.Start(new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = false
        });
    }

    private static void TerminateOrbHelpers()
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "/usr/bin/pkill",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("CodexLedOrb");
            using Process? process = Process.Start(startInfo);
            process?.WaitForExit(800);
        }
        catch
        {
        }
    }

    private void ExpandPanel()
    {
        if (viewMode == WidgetViewMode.Panel)
        {
            Show();
            Activate();
            TerminateOrbHelpers();
            UpdateTrayMenu();
            return;
        }

        WidgetWindowLayout layout = WidgetLayout.ForMode(WidgetViewMode.Panel);
        viewMode = WidgetViewMode.Panel;
        orbWindow?.Hide();
        MinWidth = layout.MinWidth;
        MinHeight = layout.MinHeight;
        Position = expandedPosition;
        Width = expandedSize.Width > 0 ? expandedSize.Width : layout.Width;
        Height = expandedSize.Height > 0 ? expandedSize.Height : layout.Height;
        Show();
        Activate();
        TerminateOrbHelpers();
        UpdateTrayMenu();
    }

    private void InitializePanelSignalCheckpoint()
    {
        string path = PanelSignalPath();
        lastPanelSignalWriteUtc = File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    private void CheckPanelSignal()
    {
        string path = PanelSignalPath();
        if (!File.Exists(path))
        {
            return;
        }

        DateTime writeUtc = File.GetLastWriteTimeUtc(path);
        if (writeUtc <= lastPanelSignalWriteUtc)
        {
            return;
        }

        lastPanelSignalWriteUtc = writeUtc;
        ExpandPanel();
    }

    private static string PanelSignalPath()
    {
        string appSupport = OperatingSystem.IsMacOS()
            ? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(appSupport, "Codex LED Widget", "show-panel.signal");
    }

    private void CreateTrayIcon()
    {
        trayIcon = new TrayIcon
        {
            Icon = LoadWindowIcon(),
            ToolTipText = "Codex LED Widget",
            IsVisible = true,
            Menu = BuildTrayMenu()
        };
    }

    private NativeMenu BuildTrayMenu()
    {
        NativeMenu menu = new();
        menu.Items.Add(MenuItem(isEnglish ? "Show/Hide" : "显示/隐藏", ToggleWindow));
        menu.Items.Add(MenuItem(viewMode == WidgetViewMode.Panel ? (isEnglish ? "Collapse orb" : "收起悬浮球") : (isEnglish ? "Expand panel" : "展开面板"), ToggleWidgetMode));
        menu.Items.Add(MenuItem(isEnglish ? "Refresh quota" : "刷新额度", async () => await RefreshQuotaAsync()));
        menu.Items.Add(MenuItem(isTopmost ? (isEnglish ? "Unpin" : "取消置顶") : (isEnglish ? "Pin on top" : "置顶"), ToggleTopmost));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(MenuItem(isEnglish ? "Exit" : "退出", Shutdown));
        return menu;
    }

    private void UpdateTrayMenu()
    {
        if (trayIcon is not null)
        {
            trayIcon.Menu = BuildTrayMenu();
        }
    }

    private static NativeMenuItem MenuItem(string header, Action action)
    {
        return new NativeMenuItem(header) { Command = new DelegateCommand(action) };
    }

    private void ToggleWidgetMode()
    {
        if (viewMode == WidgetViewMode.Panel)
        {
            CollapseToFloatingOrb();
            return;
        }

        ExpandPanel();
    }

    private void ToggleWindow()
    {
        if (viewMode == WidgetViewMode.FloatingOrb && orbWindow is not null)
        {
            if (orbWindow.IsVisible)
            {
                orbWindow.Hide();
                return;
            }

            orbWindow.Show();
            return;
        }

        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
        Activate();
    }

    private void PanelRoot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button)
        {
            BeginMoveDrag(e);
        }
    }

    private void FloatingOrb_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            ExpandPanel();
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        floatingMouseDownPoint = e.GetPosition(this);
        floatingWindowOrigin = Position;
        floatingWasDragged = false;
        e.Pointer.Capture(sender as IInputElement);
    }

    private void FloatingOrb_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (floatingMouseDownPoint is not Point startPoint)
        {
            return;
        }

        Point currentPoint = e.GetPosition(this);
        Vector delta = currentPoint - startPoint;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
        {
            return;
        }

        floatingWasDragged = true;
        Position = new PixelPoint(
            floatingWindowOrigin.X + (int)Math.Round(delta.X),
            floatingWindowOrigin.Y + (int)Math.Round(delta.Y));
    }

    private void FloatingOrb_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool shouldExpandPanel = floatingMouseDownPoint.HasValue && !floatingWasDragged;
        floatingMouseDownPoint = null;
        floatingWasDragged = false;
        e.Pointer.Capture(null);

        if (shouldExpandPanel)
        {
            ExpandPanel();
        }
    }

    private void PlaceWindowTopRight()
    {
        Screen? screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        PixelRect area = screen.WorkingArea;
        Position = new PixelPoint(area.Right - (int)Width - 24, area.Y + 24);
    }

    private string CultureName => isEnglish ? "en-US" : "zh-CN";

    private static QuotaSnapshot CreateEmptySnapshot()
    {
        return new QuotaSnapshot(
            LimitId: "codex",
            LimitName: "Codex",
            PlanType: "unknown",
            Primary: null,
            Secondary: null,
            FetchedAt: DateTimeOffset.Now);
    }

    private void SetStateBrush(Color color)
    {
        SolidColorBrush brush = new(color);
        trafficLight.Fill = brush;
        statusDot.Fill = brush;
    }

    private static WindowIcon? LoadWindowIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://CodexLedWidget.Mac/Assets/App.ico")));
        }
        catch
        {
            return null;
        }
    }

    private static Button IconButton(string content, string tooltip, EventHandler<RoutedEventArgs> onClick)
    {
        Button button = new()
        {
            Content = content
        };
        ToolTip.SetTip(button, tooltip);
        ApplyButtonChrome(button);
        button.Click += onClick;
        return button;
    }

    private static void ApplyButtonChrome(Button button)
    {
        button.Width = 28;
        button.Height = 28;
        button.Margin = new Thickness(3, 0, 0, 0);
        button.Padding = new Thickness(0);
        button.FontSize = 13;
        button.FontWeight = FontWeight.SemiBold;
        button.Foreground = TextBrush();
        button.Background = new SolidColorBrush(Color.FromArgb(136, 255, 255, 255));
        button.BorderBrush = new SolidColorBrush(Color.FromArgb(31, 40, 55, 71));
        button.BorderThickness = new Thickness(1);
    }

    private static Border QuotaCard(TextBlock label, TextBlock value, Thickness margin)
    {
        label.FontSize = 11;
        label.Foreground = MutedBrush();
        value.Margin = new Thickness(0, 4, 0, 0);
        value.FontSize = 13;
        value.FontWeight = FontWeight.Bold;
        value.Foreground = TextBrush();
        value.TextWrapping = TextWrapping.NoWrap;
        value.TextTrimming = TextTrimming.CharacterEllipsis;

        return new Border
        {
            Height = 50,
            Padding = new Thickness(10, 7),
            Margin = margin,
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(26, 40, 55, 71)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(184, 255, 255, 255)),
            Child = new StackPanel
            {
                Children = { label, value }
            }
        };
    }

    private static Ellipse BlurCircle(double size, string color, HorizontalAlignment horizontal, VerticalAlignment vertical, Thickness margin)
    {
        return new Ellipse
        {
            Width = size,
            Height = size,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Margin = margin,
            Fill = new SolidColorBrush(Color.Parse(color))
        };
    }

    private static IBrush TextBrush()
    {
        return new SolidColorBrush(Color.FromRgb(18, 32, 51));
    }

    private static IBrush MutedBrush()
    {
        return new SolidColorBrush(Color.FromRgb(82, 98, 115));
    }

    private static void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
