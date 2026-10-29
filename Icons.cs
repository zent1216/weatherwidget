using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WeatherWidget;

/// <summary>24x24 좌표계 벡터 날씨 아이콘 (WPF는 컬러 이모지를 못 그려서 직접 그림)</summary>
public static class Icons
{
    private const string Cloud = "M7,19 L17.5,19 A4,4 0 0 0 17.5,11 A5.5,5.5 0 0 0 6.9,12.2 A3.4,3.4 0 0 0 7,19 Z";
    private const string SunRays = "M12,2.5 L12,5 M12,19 L12,21.5 M2.5,12 L5,12 M19,12 L21.5,12 M5.3,5.3 L7.1,7.1 M16.9,16.9 L18.7,18.7 M5.3,18.7 L7.1,16.9 M16.9,7.1 L18.7,5.3";
    private const string SmallRays = "M9,1.8 L9,3.6 M1.8,9 L3.6,9 M3.9,3.9 L5.2,5.2 M14.1,3.9 L12.8,5.2";
    private const string Moon = "M14,3 A9,9 0 1 0 21,15.5 A7,7 0 0 1 14,3 Z";
    private const string SmallMoon = "M9.5,2 A6,6 0 1 0 15,10 A4.7,4.7 0 0 1 9.5,2 Z";

    private static readonly Brush Sun = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x40)));
    private static readonly Brush MoonC = Freeze(new SolidColorBrush(Color.FromRgb(0xF3, 0xE7, 0xA8)));
    private static readonly Brush CloudLight = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF5)));
    private static readonly Brush CloudMid = Freeze(new SolidColorBrush(Color.FromRgb(0xD5, 0xDE, 0xE8)));
    private static readonly Brush CloudDark = Freeze(new SolidColorBrush(Color.FromRgb(0xC3, 0xCE, 0xDA)));
    private static readonly Brush Rain = Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0xB4, 0xFF)));
    private static readonly Brush Fog = Freeze(new SolidColorBrush(Color.FromRgb(0xB8, 0xC4, 0xD0)));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    public static FrameworkElement Create(int code, bool isDay, double size)
    {
        var c = new Canvas { Width = 24, Height = 24 };
        switch (code)
        {
            case 0:
                if (isDay) { c.Children.Add(Circle(12, 12, 5, Sun)); c.Children.Add(Stroke(SunRays, Sun, 1.8)); }
                else c.Children.Add(Fill(Moon, MoonC));
                break;
            case 1:
            case 2:
                if (isDay) { c.Children.Add(Circle(9, 9, 4, Sun)); c.Children.Add(Stroke(SmallRays, Sun, 1.6)); }
                else c.Children.Add(Fill(SmallMoon, MoonC));
                c.Children.Add(Fill(Cloud, CloudLight, 0.92, 1.5, 1.5));
                break;
            case 3:
                c.Children.Add(Fill(Cloud, CloudMid));
                break;
            case 45:
            case 48:
                c.Children.Add(Stroke("M4,8 L20,8 M6,12 L18,12 M4,16 L20,16", Fog, 2));
                break;
            case 71: case 73: case 75: case 77: case 85: case 86:
                c.Children.Add(Fill(Cloud, CloudDark, 1, 0, -3));
                c.Children.Add(Circle(8, 20, 1.3, Brushes.White));
                c.Children.Add(Circle(12, 21.5, 1.3, Brushes.White));
                c.Children.Add(Circle(16, 20, 1.3, Brushes.White));
                break;
            case 95: case 96: case 99:
                c.Children.Add(Fill(Cloud, CloudDark, 1, 0, -3));
                c.Children.Add(Fill("M12.5,15 L9.5,19.5 L12,19.5 L10.5,23 L15,18 L12.5,18 L14,15 Z", Sun));
                break;
            default: // 비/이슬비/소나기
                if (code >= 51)
                {
                    c.Children.Add(Fill(Cloud, CloudDark, 1, 0, -3));
                    c.Children.Add(Stroke("M8,18.5 L7,21.5 M12,18.5 L11,21.5 M16,18.5 L15,21.5", Rain, 1.8));
                }
                else c.Children.Add(Fill(Cloud, CloudMid));
                break;
        }
        return new Viewbox { Width = size, Height = size, Child = c };
    }

    private static Path Fill(string data, Brush b, double scale = 1, double dx = 0, double dy = 0)
    {
        var p = new Path { Data = Geometry.Parse(data), Fill = b };
        if (scale != 1 || dx != 0 || dy != 0)
        {
            var g = new TransformGroup();
            g.Children.Add(new ScaleTransform(scale, scale));
            g.Children.Add(new TranslateTransform(dx, dy));
            p.RenderTransform = g;
        }
        return p;
    }

    private static Path Stroke(string data, Brush b, double w) => new()
    {
        Data = Geometry.Parse(data), Stroke = b, StrokeThickness = w,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
    };

    private static Ellipse Circle(double cx, double cy, double r, Brush b)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = b };
        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        return e;
    }
}
