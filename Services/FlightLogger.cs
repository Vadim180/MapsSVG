using Maps.Models;
using Newtonsoft.Json;
using System.Text;

namespace Maps.Services;

public class FlightLogger
{
    public Dictionary<string, List<FlightLogEntry>> FlightsByPilot { get; set; } = new();
    private readonly string logFilePath;

    public FlightLogger()
    {
        // Тільки встановлюємо шлях, НЕ завантажуємо дані
        logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Maps", "flight_log.json");
    }

    // Додаємо окремий метод для ініціалізації
    public void Initialize()
    {
        LoadData();
    }

    public void AddFlight(string pilot, string dron, string position, int distance, DateTime endTime)
    {
        if (!FlightsByPilot.ContainsKey(pilot))
        {
            FlightsByPilot[pilot] = new List<FlightLogEntry>();
        }

        var lastFlight = FlightsByPilot[pilot].LastOrDefault();

        if (lastFlight != null && lastFlight.EndTime == endTime)
        {
            // Оновлюємо останній запис
            lastFlight.Dron = dron;
            lastFlight.Position = position;
            lastFlight.Distance = distance;
        }
        else
        {
            // Додаємо новий запис
            FlightsByPilot[pilot].Add(new FlightLogEntry
            {
                Pilot = pilot,
                Dron = dron,
                Position = position,
                EndTime = endTime,
                Distance = distance
            });
        }

        SaveData(); // оновлюємо файл
    }


    public void SaveData()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
            File.WriteAllText(logFilePath, JsonConvert.SerializeObject(FlightsByPilot, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Помилка збереження логів: {ex.Message}");
        }
    }

    public void LoadData()
    {
        try
        {
            // ТІЛЬКИ перевіряємо локальну папку
            if (File.Exists(logFilePath))
            {
                var json = File.ReadAllText(logFilePath);
                var loadedData = JsonConvert.DeserializeObject<Dictionary<string, List<FlightLogEntry>>>(json);
                FlightsByPilot = loadedData ?? new Dictionary<string, List<FlightLogEntry>>();
            }
            else
            {
                // Якщо файлу немає - просто порожній словник
                FlightsByPilot = new Dictionary<string, List<FlightLogEntry>>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Помилка завантаження логів: {ex.Message}");
            FlightsByPilot = new Dictionary<string, List<FlightLogEntry>>();
        }
    }

    public string GetStatistics()
    {
        if (FlightsByPilot.Count == 0)
            return "Польотів ще не було.";

        var sb = new StringBuilder();
        DateTime? previousDate = null;

        foreach (var kvp in FlightsByPilot)
        {
            sb.AppendLine($"Пілот: {kvp.Key}");

            foreach (var flight in kvp.Value)
            {
                if (previousDate.HasValue && previousDate.Value.Date != flight.EndTime.Date)
                {
                    sb.AppendLine(); // Додаємо вільну строку при зміні дня
                }

                sb.AppendLine($" - {flight.EndTime:dd.MM.yyyy HH:mm}, Позиція: {flight.Position}, Дрон: {flight.Dron}, Відстань: {flight.Distance} м");

                previousDate = flight.EndTime; // Оновлюємо дату
            }

            sb.AppendLine(); // Додаємо порожній рядок між пілотами
        }

        return sb.ToString();
    }
}
