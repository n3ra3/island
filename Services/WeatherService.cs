using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland;

public record HourItem(string Label, int Code, double Temp, bool Night = false);
public record DayItem(string Label, int Code, double Min, double Max);

/// <summary>Погода с open-meteo.com (бесплатно, без ключа). Обновление раз в 30 минут.</summary>
public sealed class WeatherService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly CultureInfo Ru = new("ru-RU");

    public double? Temp { get; private set; }
    public int Code { get; private set; }
    public bool Night { get; private set; }
    public double Wind { get; private set; }
    public int Humidity { get; private set; }
    public List<HourItem> Hours { get; private set; } = new();
    public List<DayItem> Days { get; private set; } = new();
    public bool Loading { get; private set; }
    public bool Failed { get; private set; }
    public event Action? Changed;

    public async Task RefreshAsync()
    {
        if (Loading) return;
        Loading = true; Failed = false;
        Changed?.Invoke();
        try
        {
            var s = Hub.Settings;
            var lat = s.Lat.ToString(CultureInfo.InvariantCulture);
            var lon = s.Lon.ToString(CultureInfo.InvariantCulture);
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                "&current=temperature_2m,weather_code,wind_speed_10m,relative_humidity_2m,is_day" +
                "&hourly=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                "&timezone=auto&forecast_days=7&wind_speed_unit=ms";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var root = doc.RootElement;

            var cur = root.GetProperty("current");
            Temp = cur.GetProperty("temperature_2m").GetDouble();
            Code = cur.GetProperty("weather_code").GetInt32();
            Wind = cur.GetProperty("wind_speed_10m").GetDouble();
            Humidity = (int)Math.Round(cur.GetProperty("relative_humidity_2m").GetDouble());
            Night = cur.TryGetProperty("is_day", out var isDay) && isDay.GetInt32() == 0;
            var nowHour = cur.GetProperty("time").GetString()![..13] + ":00";

            var h = root.GetProperty("hourly");
            var times = h.GetProperty("time");
            var ht = h.GetProperty("temperature_2m");
            var hc = h.GetProperty("weather_code");
            var hd = h.GetProperty("is_day");
            int start = 0;
            for (int i = 0; i < times.GetArrayLength(); i++)
                if (string.CompareOrdinal(times[i].GetString(), nowHour) >= 0) { start = i; break; }
            var hours = new List<HourItem>();
            for (int i = start; i < Math.Min(start + 7, times.GetArrayLength()); i++)
            {
                var label = i == start ? "Сейчас" : times[i].GetString()!.Substring(11, 2);
                hours.Add(new HourItem(label, hc[i].GetInt32(), ht[i].GetDouble(), hd[i].GetInt32() == 0));
            }
            Hours = hours;

            var d = root.GetProperty("daily");
            var dt = d.GetProperty("time");
            var days = new List<DayItem>();
            for (int i = 0; i < Math.Min(6, dt.GetArrayLength()); i++)
            {
                var date = DateTime.ParseExact(dt[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var name = i == 0 ? "Сегодня" : Ru.TextInfo.ToTitleCase(date.ToString("ddd", Ru));
                days.Add(new DayItem(name, d.GetProperty("weather_code")[i].GetInt32(),
                    d.GetProperty("temperature_2m_min")[i].GetDouble(), d.GetProperty("temperature_2m_max")[i].GetDouble()));
            }
            Days = days;
        }
        catch { Failed = true; }
        Loading = false;
        Changed?.Invoke();
    }

    public static async Task<(string Name, double Lat, double Lon)?> FindCity(string q)
    {
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(q)}&count=1&language=ru&format=json";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var r) || r.GetArrayLength() == 0) return null;
            var c = r[0];
            return (c.GetProperty("name").GetString()!, c.GetProperty("latitude").GetDouble(), c.GetProperty("longitude").GetDouble());
        }
        catch { return null; }
    }

    public static string IconKey(int code) => code switch
    {
        0 => "i.sun",
        45 or 48 => "i.fog",
        (>= 51 and <= 67) or (>= 80 and <= 82) => "i.rain",
        (>= 71 and <= 77) or 85 or 86 => "i.snow",
        >= 95 => "i.storm",
        _ => "i.cloud",
    };

    public static string Describe(int code, bool night = false) => code switch
    {
        0 => night ? "Ясная ночь" : "Ясно",
        1 => "Преимущественно ясно",
        2 => "Переменная облачность",
        3 => "Пасмурно",
        45 or 48 => "Туман",
        >= 51 and <= 57 => "Морось",
        (>= 61 and <= 67) or (>= 80 and <= 82) => "Дождь",
        (>= 71 and <= 77) or 85 or 86 => "Снег",
        >= 95 => "Гроза",
        _ => "",
    };
}
