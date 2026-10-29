using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WeatherWidget;

public partial class MainWindow : Window
{
    private static readonly CultureInfo Ko = new("ko-KR");
    private static readonly int[] OpacityChoices = { 30, 45, 55, 70, 85, 100 };
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(2);

    private readonly Settings _settings = Settings.Load();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _refreshTimer = new();
    private DateTime? _lastUpdated;
    private WeatherData? _data;
    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();

        // 바탕화면(Progman)을 소유자로 지정 → Win+D(바탕화면 보기)에도 사라지지 않고 바탕화면 층에 머묾
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero) new WindowInteropHelper(this).Owner = progman;

        ApplyOpacity();
        RestorePosition();
        BuildContextMenu();

        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => await RefreshAsync();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Closed += (_, _) => SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    // ── 바탕화면 층 유지 ──

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // Alt+Tab 목록에서 숨김
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            // 클릭·활성화 등으로 앞으로 올라오려 해도 항상 맨 아래(바탕화면 바로 위)로 고정
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & SWP_NOZORDER) == 0)
            {
                pos.hwndInsertAfter = HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    // ── 위치 ──

    private void RestorePosition()
    {
        double l = _settings.Left, t = _settings.Top;
        bool valid = !double.IsNaN(l) && !double.IsNaN(t) &&
                     l > SystemParameters.VirtualScreenLeft - 200 &&
                     t > SystemParameters.VirtualScreenTop - 50 &&
                     l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                     t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;
        if (!valid)
        {
            // 처음 실행: 주 모니터 오른쪽 위
            var wa = SystemParameters.WorkArea;
            l = wa.Right - Width - 24;
            t = wa.Top + 24;
        }
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = l;
        Top = t;
    }

    private void RootPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OpenDetails();
            return;
        }
        DragMove();
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    // ── 우클릭 메뉴 ──

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();

        var details = new MenuItem { Header = "자세히 보기 (네이버 날씨)", FontWeight = FontWeights.SemiBold };
        details.Click += (_, _) => OpenDetails();
        menu.Items.Add(details);

        var refresh = new MenuItem { Header = "새로고침" };
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        var loc = new MenuItem { Header = "지역 변경…" };
        loc.Click += (_, _) => ChangeLocation();
        menu.Items.Add(loc);

        var opacity = new MenuItem { Header = "배경 진하기" };
        foreach (var v in OpacityChoices)
        {
            var item = new MenuItem { Header = $"{v}%", Tag = v, IsCheckable = true };
            item.Click += (_, _) =>
            {
                _settings.Opacity = v;
                _settings.Save();
                ApplyOpacity();
            };
            opacity.Items.Add(item);
        }
        menu.Items.Add(opacity);

        var daysMenu = new MenuItem { Header = "일별 예보 일수" };
        foreach (var n in new[] { 7, 15 })
        {
            var item = new MenuItem { Header = $"{n}일", Tag = n, IsCheckable = true };
            item.Click += (_, _) =>
            {
                _settings.ForecastDays = n;
                _settings.Save();
                RenderDays();
            };
            daysMenu.Items.Add(item);
        }
        menu.Items.Add(daysMenu);

        var autostart = new MenuItem { Header = "Windows 시작 시 자동 실행", IsCheckable = true };
        autostart.Click += (_, _) =>
        {
            try { AutoStart.Set(autostart.IsChecked); }
            catch (Exception ex) { MessageBox.Show("자동 실행 설정 실패: " + ex.Message, "날씨 위젯"); }
            autostart.IsChecked = AutoStart.IsEnabled;
        };
        menu.Items.Add(autostart);

        menu.Items.Add(new Separator());
        var exit = new MenuItem { Header = "종료" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(exit);

        menu.Opened += (_, _) =>
        {
            autostart.IsChecked = AutoStart.IsEnabled;
            foreach (MenuItem mi in opacity.Items) mi.IsChecked = (int)mi.Tag == _settings.Opacity;
            foreach (MenuItem mi in daysMenu.Items) mi.IsChecked = (int)mi.Tag == _settings.ForecastDays;
        };
        RootPanel.ContextMenu = menu;
    }

    /// <summary>기본 브라우저로 네이버 "[지역명] 날씨" 검색 결과를 연다</summary>
    private void OpenDetails()
    {
        string url = "https://search.naver.com/search.naver?query=" + Uri.EscapeDataString($"{_settings.LocationName} 날씨");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("브라우저를 열 수 없습니다: " + ex.Message, "날씨 위젯"); }
    }

    private void ApplyOpacity()
    {
        byte a = (byte)Math.Round(Math.Clamp(_settings.Opacity, 0, 100) * 2.55);
        RootPanel.Background = new SolidColorBrush(Color.FromArgb(a, 0x10, 0x14, 0x1E));
    }

    private async void ChangeLocation()
    {
        var dlg = new LocationWindow(_settings.LocationName);
        if (dlg.ShowDialog() == true && dlg.Selected is Place p)
        {
            _settings.LocationName = p.Name;
            _settings.Latitude = p.Lat;
            _settings.Longitude = p.Lon;
            _settings.Save();
            await RefreshAsync();
        }
    }

    // ── 시계 ──

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        SecText.Text = now.ToString(":ss");
        DateText.Text = now.ToString("yyyy년 M월 d일 dddd", Ko);
    }

    // ── 날씨 ──

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // 절전 해제 후 네트워크가 붙을 시간을 두고 갱신
        if (e.Mode == PowerModes.Resume)
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(8));
                await RefreshAsync();
            });
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        _refreshTimer.Stop();
        try
        {
            var d = await WeatherService.FetchAsync(_settings.Latitude, _settings.Longitude);
            _data = d;
            _lastUpdated = DateTime.Now;
            Render(d);
            _refreshTimer.Interval = RefreshInterval;
            if (SnapshotPath is not null) SaveSnapshot(SnapshotPath);
        }
        catch
        {
            LocText.Text = _lastUpdated is DateTime t
                ? $"{_settings.LocationName} · {t:HH:mm} 갱신 (갱신 실패, 재시도 중)"
                : $"{_settings.LocationName} · 날씨를 불러오지 못함 (재시도 중)";
            _refreshTimer.Interval = RetryInterval;
        }
        finally
        {
            _refreshing = false;
            _refreshTimer.Start();
        }
    }

    /// <summary>점검용: --snapshot 경로로 실행하면 첫 갱신 후 위젯 모습을 PNG로 저장하고 종료</summary>
    public string? SnapshotPath { get; set; }

    private void SaveSnapshot(string path)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var bg = new System.Windows.Shapes.Rectangle
            {
                Width = ActualWidth, Height = ActualHeight,
                Fill = new LinearGradientBrush(Color.FromRgb(0x2B, 0x58, 0x76), Color.FromRgb(0x4E, 0x43, 0x76), 45)
            };
            bg.Measure(new Size(ActualWidth, ActualHeight));
            bg.Arrange(new Rect(0, 0, ActualWidth, ActualHeight));
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(bg);
            rtb.Render((Visual)Content);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
            Application.Current.Shutdown();
        }, DispatcherPriority.ApplicationIdle);
    }

    private static string Deg(double v) => double.IsNaN(v) ? "-" : $"{Math.Round(v):0}°";

    private void Render(WeatherData d)
    {
        NowIcon.Content = Icons.Create(d.Code, d.IsDay, 58);
        TempText.Text = Deg(d.Temp);
        DescText.Text = WeatherService.Describe(d.Code);
        SubText.Text = $"체감 {Deg(d.Apparent)} · 최고 {Deg(d.TodayMax)} / 최저 {Deg(d.TodayMin)}";
        LocText.Text = $"{_settings.LocationName} · {_lastUpdated:HH:mm} 갱신";

        StatsGrid.Children.Clear();
        StatsGrid.Children.Add(Stat("습도", Value($"{d.Humidity:0}%")));
        StatsGrid.Children.Add(Stat("바람", Value($"{WeatherService.WindDirName(d.WindDir)} {d.WindSpeed:0.0}m/s")));
        StatsGrid.Children.Add(Stat("강수확률", Value($"{d.PrecipProbNow}%")));

        Pm10Badge.Content = d.Pm10 is double pm10 ? Badge(WeatherService.Pm10Grade(pm10), pm10) : Value("-");
        Pm25Badge.Content = d.Pm25 is double pm25 ? Badge(WeatherService.Pm25Grade(pm25), pm25) : Value("-");

        HourGrid.Children.Clear();
        foreach (var h in d.Hours)
            HourGrid.Children.Add(Cell($"{h.Time.Hour}시", Icons.Create(h.Code, h.IsDay, 26), Value(Deg(h.Temp), 14), h.PrecipProb));

        RenderDays();
    }

    private static readonly Brush Dim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Prob = Frozen(Color.FromRgb(0x7C, 0xC4, 0xFF));
    private static readonly Brush Sat = Frozen(Color.FromRgb(0x7C, 0xB6, 0xFF));
    private static readonly Brush Sun = Frozen(Color.FromRgb(0xFF, 0x8A, 0x8A));
    private static readonly Brush BarTrack = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BarFill = FrozenBrush(new LinearGradientBrush(
        Color.FromRgb(0x6E, 0xC1, 0xFF), Color.FromRgb(0xFF, 0xC9, 0x40), 0));

    private static Brush Frozen(Color c) => FrozenBrush(new SolidColorBrush(c));
    private static Brush FrozenBrush(Brush b) { b.Freeze(); return b; }

    /// <summary>일별 예보: 요일·날짜·아이콘·강수확률·최저~최고 막대</summary>
    private void RenderDays()
    {
        DayList.Children.Clear();
        if (_data is null) return;
        var days = _data.Days.Take(_settings.ForecastDays).ToList();
        DaysLabel.Text = $"{days.Count}일 예보";
        if (days.Count == 0) return;

        double lo = days.Where(x => !double.IsNaN(x.Min)).Select(x => x.Min).DefaultIfEmpty(0).Min();
        double hi = days.Where(x => !double.IsNaN(x.Max)).Select(x => x.Max).DefaultIfEmpty(1).Max();
        double span = Math.Max(1, hi - lo);
        const double barWidth = 100;

        foreach (var day in days)
        {
            var g = new Grid { Height = 25 };
            foreach (var w in new[] { 26.0, 36, 24, 34, 28, barWidth + 16, 30 })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

            var dow = day.Date.DayOfWeek;
            Add(g, 0, new TextBlock
            {
                Text = day.Date.ToString("ddd", Ko), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Foreground = dow == DayOfWeek.Saturday ? Sat : dow == DayOfWeek.Sunday ? Sun : Brushes.White
            });
            Add(g, 1, new TextBlock { Text = $"{day.Date.Month}/{day.Date.Day}", FontSize = 11, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
            var icon = Icons.Create(day.Code, true, 20);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            Add(g, 2, icon);
            Add(g, 3, new TextBlock { Text = $"{day.PrecipProb}%", FontSize = 11, Foreground = Prob, VerticalAlignment = VerticalAlignment.Center });
            Add(g, 4, new TextBlock { Text = Deg(day.Min), FontSize = 12, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });

            var bar = new Canvas { Width = barWidth, Height = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            bar.Children.Add(new Border { Width = barWidth, Height = 4, CornerRadius = new CornerRadius(2), Background = BarTrack });
            if (!double.IsNaN(day.Min) && !double.IsNaN(day.Max))
            {
                double x1 = (day.Min - lo) / span * barWidth, x2 = (day.Max - lo) / span * barWidth;
                var fill = new Border { Width = Math.Max(4, x2 - x1), Height = 4, CornerRadius = new CornerRadius(2), Background = BarFill };
                Canvas.SetLeft(fill, x1);
                bar.Children.Add(fill);
            }
            Add(g, 5, bar);
            Add(g, 6, new TextBlock { Text = Deg(day.Max), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            DayList.Children.Add(g);
        }
    }

    private static void Add(Grid g, int col, UIElement e)
    {
        Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    private static TextBlock Value(string text, double size = 12) =>
        new() { Text = text, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center };

    private static FrameworkElement Stat(string label, FrameworkElement value)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) });
        sp.Children.Add(value);
        return sp;
    }

    private static FrameworkElement Badge(int grade, double value) => new Border
    {
        Background = (Brush)new BrushConverter().ConvertFromString(WeatherService.GradeColors[grade])!,
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(7, 1, 7, 2),
        HorizontalAlignment = HorizontalAlignment.Center,
        Child = new TextBlock { Text = $"{WeatherService.GradeNames[grade]} {Math.Round(value):0}", FontSize = 12, FontWeight = FontWeights.SemiBold }
    };

    private static FrameworkElement Cell(string head, FrameworkElement icon, FrameworkElement temp, int prob)
    {
        var sp = new StackPanel();
        sp.Children.Add(Value(head));
        icon.Margin = new Thickness(0, 3, 0, 3);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(icon);
        sp.Children.Add(temp);
        sp.Children.Add(new TextBlock
        {
            Text = $"{prob}%", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0xC4, 0xFF)), Margin = new Thickness(0, 1, 0, 0)
        });
        return sp;
    }

    // ── Win32 ──

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WM_WINDOWPOSCHANGING = 0x46;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}


