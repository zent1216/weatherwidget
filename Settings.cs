using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WeatherWidget;

public class Settings
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public string LocationName { get; set; } = "서울";
    public double Latitude { get; set; } = 37.5667;
    public double Longitude { get; set; } = 126.9783;
    /// <summary>배경 진하기 (%)</summary>
    public int Opacity { get; set; } = 55;
    /// <summary>일별 예보 표시 일수 (7 또는 15)</summary>
    public int ForecastDays { get; set; } = 7;

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WeatherWidget");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WeatherWidget";

    private static string ExePath => Environment.ProcessPath ?? "";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string v &&
                   v.Trim('"').Equals(ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enable)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) k.SetValue(ValueName, $"\"{ExePath}\"");
        else k.DeleteValue(ValueName, false);
    }
}

