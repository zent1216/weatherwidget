using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace WeatherWidget;

public record HourItem(DateTime Time, double Temp, int Code, bool IsDay, int PrecipProb);
public record DayItem(DateTime Date, double Max, double Min, int Code, int PrecipProb);

public class WeatherData
{
    public double Temp, Apparent, Humidity, WindSpeed, WindDir;
    public int Code;
    public bool IsDay;
    public double TodayMax, TodayMin;
    public int PrecipProbNow;
    public double? Pm10, Pm25;
    public List<HourItem> Hours = new();
    public List<DayItem> Days = new();
}

public record Place(string Name, string Detail, double Lat, double Lon);

public static class WeatherService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // Nominatim 이용 정책상 User-Agent 필수
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WeatherWidget/1.0 (Windows desktop gadget)");
        return c;
    }

    public static async Task<WeatherData> FetchAsync(double lat, double lon)
    {
        string ll = $"latitude={lat.ToString(Inv)}&longitude={lon.ToString(Inv)}";
        string fUrl = "https://api.open-meteo.com/v1/forecast?" + ll +
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m,is_day" +
            "&hourly=temperature_2m,weather_code,precipitation_probability,is_day" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
            "&timezone=auto&forecast_days=16&wind_speed_unit=ms";
        string aUrl = "https://air-quality-api.open-meteo.com/v1/air-quality?" + ll + "&current=pm10,pm2_5&timezone=auto";

        var fTask = Http.GetStringAsync(fUrl);
        var aTask = Http.GetStringAsync(aUrl);

        var d = new WeatherData();
        using (var doc = JsonDocument.Parse(await fTask))
        {
            var root = doc.RootElement;
            var cur = root.GetProperty("current");
            d.Temp = cur.GetProperty("temperature_2m").GetDouble();
            d.Apparent = cur.GetProperty("apparent_temperature").GetDouble();
            d.Humidity = cur.GetProperty("relative_humidity_2m").GetDouble();
            d.Code = cur.GetProperty("weather_code").GetInt32();
            d.WindSpeed = cur.GetProperty("wind_speed_10m").GetDouble();
            d.WindDir = cur.GetProperty("wind_direction_10m").GetDouble();
            d.IsDay = cur.GetProperty("is_day").GetInt32() == 1;
            var curTime = DateTime.Parse(cur.GetProperty("time").GetString()!, Inv);

            var h = root.GetProperty("hourly");
            var hTime = h.GetProperty("time");
            var hTemp = h.GetProperty("temperature_2m");
            var hCode = h.GetProperty("weather_code");
            var hProb = h.GetProperty("precipitation_probability");
            var hDay = h.GetProperty("is_day");
            var nowHour = new DateTime(curTime.Year, curTime.Month, curTime.Day, curTime.Hour, 0, 0);
            for (int i = 0; i < hTime.GetArrayLength(); i++)
            {
                var t = DateTime.Parse(hTime[i].GetString()!, Inv);
                if (t == nowHour) d.PrecipProbNow = IntOr(hProb[i]);
                // 2시간 간격 5칸
                if (t > nowHour && (t - nowHour).TotalHours % 2 == 0 && d.Hours.Count < 5)
                    d.Hours.Add(new HourItem(t, DblOr(hTemp[i]), IntOr(hCode[i]), IntOr(hDay[i]) == 1, IntOr(hProb[i])));
            }

            var dl = root.GetProperty("daily");
            var dTime = dl.GetProperty("time");
            for (int i = 0; i < dTime.GetArrayLength(); i++)
            {
                var item = new DayItem(DateTime.Parse(dTime[i].GetString()!, Inv),
                    DblOr(dl.GetProperty("temperature_2m_max")[i]), DblOr(dl.GetProperty("temperature_2m_min")[i]),
                    IntOr(dl.GetProperty("weather_code")[i]), IntOr(dl.GetProperty("precipitation_probability_max")[i]));
                if (i == 0) { d.TodayMax = item.Max; d.TodayMin = item.Min; }
                else d.Days.Add(item);
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(await aTask);
            var cur = doc.RootElement.GetProperty("current");
            if (cur.GetProperty("pm10").ValueKind == JsonValueKind.Number) d.Pm10 = cur.GetProperty("pm10").GetDouble();
            if (cur.GetProperty("pm2_5").ValueKind == JsonValueKind.Number) d.Pm25 = cur.GetProperty("pm2_5").GetDouble();
        }
        catch { /* 대기질은 없어도 표시 */ }

        return d;
    }

    /// <summary>
    /// 지역 검색. Open-Meteo 지오코딩은 한글 시/군/구 검색이 약해서
    /// ("서울", "해운대구" 등 결과 없음) OpenStreetMap Nominatim 결과를 함께 합친다.
    /// </summary>
    public static async Task<List<Place>> SearchAsync(string query)
    {
        query = query.Trim();
        var variants = new List<string> { query };
        if (ContainsHangul(query) && !query.EndsWith("시"))
            variants.AddRange(new[] { query + "시", query + "특별시", query + "광역시" });

        var omTasks = variants.Select(v => SearchOpenMeteoAsync(v)).ToList();
        var nomTask = SearchNominatimAsync(query);

        var om = new List<(Place p, long pop)>();
        foreach (var t in omTasks)
        {
            try { om.AddRange(await t); } catch { }
        }
        List<Place> nom;
        try { nom = await nomTask; } catch { nom = new(); }

        var result = new List<Place>();
        foreach (var p in om.OrderByDescending(x => x.pop).Select(x => x.p).Concat(nom))
        {
            if (result.Any(r => Math.Abs(r.Lat - p.Lat) < 0.03 && Math.Abs(r.Lon - p.Lon) < 0.03)) continue;
            result.Add(p);
            if (result.Count >= 12) break;
        }
        return result;
    }

    private static async Task<List<(Place, long)>> SearchOpenMeteoAsync(string q)
    {
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&language=ko&format=json&name=" + Uri.EscapeDataString(q);
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var list = new List<(Place, long)>();
        if (!doc.RootElement.TryGetProperty("results", out var arr)) return list;
        foreach (var r in arr.EnumerateArray())
        {
            string name = Str(r, "name");
            var parts = new[] { Str(r, "admin2"), Str(r, "admin1"), Str(r, "country") }
                .Where(s => s != "" && s != name).Distinct();
            long pop = r.TryGetProperty("population", out var pp) && pp.ValueKind == JsonValueKind.Number ? pp.GetInt64() : 0;
            list.Add((new Place(name, string.Join(", ", parts), r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble()), pop));
        }
        return list;
    }

    private static async Task<List<Place>> SearchNominatimAsync(string q)
    {
        string url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=8&accept-language=ko&featureType=settlement&q=" + Uri.EscapeDataString(q);
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var list = new List<Place>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            string name = Str(r, "name");
            string display = Str(r, "display_name");
            var parts = display.Split(',').Select(s => s.Trim())
                .Where(s => s != "" && s != name && !s.All(char.IsDigit)).Take(3);
            if (name == "") name = display.Split(',')[0];
            list.Add(new Place(name, string.Join(", ", parts),
                double.Parse(Str(r, "lat"), Inv), double.Parse(Str(r, "lon"), Inv)));
        }
        return list;
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    private static int IntOr(JsonElement e) => e.ValueKind == JsonValueKind.Number ? (int)Math.Round(e.GetDouble()) : 0;
    private static double DblOr(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.NaN;
    private static bool ContainsHangul(string s) => s.Any(c => c >= 0xAC00 && c <= 0xD7A3);

    // ── 표시용 변환 ──

    public static string Describe(int code) => code switch
    {
        0 => "맑음",
        1 => "대체로 맑음",
        2 => "구름 조금",
        3 => "흐림",
        45 or 48 => "안개",
        51 or 53 or 55 => "이슬비",
        56 or 57 => "어는 이슬비",
        61 => "약한 비",
        63 => "비",
        65 => "강한 비",
        66 or 67 => "어는 비",
        71 => "약한 눈",
        73 => "눈",
        75 => "많은 눈",
        77 => "싸락눈",
        80 or 81 => "소나기",
        82 => "강한 소나기",
        85 or 86 => "소낙눈",
        95 => "뇌우",
        96 or 99 => "우박 동반 뇌우",
        _ => "-"
    };

    public static string WindDirName(double deg)
    {
        string[] names = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };
        return names[(int)Math.Round(((deg % 360) + 360) % 360 / 45.0) % 8];
    }

    /// <summary>환경부 기준 미세먼지 등급 (0 좋음 ~ 3 매우나쁨)</summary>
    public static int Pm10Grade(double v) => v <= 30 ? 0 : v <= 80 ? 1 : v <= 150 ? 2 : 3;
    public static int Pm25Grade(double v) => v <= 15 ? 0 : v <= 35 ? 1 : v <= 75 ? 2 : 3;
    public static readonly string[] GradeNames = { "좋음", "보통", "나쁨", "매우나쁨" };
    public static readonly string[] GradeColors = { "#2E9CFF", "#3CB371", "#F0A030", "#E04848" };
}
