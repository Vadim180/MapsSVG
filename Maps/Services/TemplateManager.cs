using Maps.Models;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Maps.Services
{
     public class TemplateManager
    {
        //знищено - не знищено
        public Dictionary<string, List<string>> Position_Point { get; set; } = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> DroneByPosition { get; set; } = new Dictionary<string, List<string>>();

        public List<string> LocalCiti { get; set; } = new List<string>();
        public List<string> StartWorkShablon { get; set; } = new List<string>();
        public List<string> EndWorkShablon { get; set; } = new List<string>();
        public List<string> ReportWorkShablon { get; set; } = new List<string>();
        public List<string> TargetTypeShablon { get; set; } = new List<string>();
        public string CustomUnit { get; set; } = "зрдн";
        public string LaunchArea { get; set; } = "Купянськ";


        // Кастомний шаблон для бойової доповіді
        public List<string> CustomReportWorkShablon { get; set; }

        public void InitializeData()
        {
            Position_Point = new Dictionary<string, List<string>>
            {
                ["ФОРПОСТ"] = ["GREENDAY", "GREENDAY, Kasper"],
                ["ДЕТРОЙТ"] = ["GREENDAY", "Kasper", "Volt"]
            };

            DroneByPosition = new Dictionary<string, List<string>>
            {
                ["ФОРПОСТ"] = ["BARABASH MAX FLY", "BARABASH 10", "BLINK 8", "F7", "СПОРТИВНИЙ ПОВІТРЯНИЙ РОБОТ", "PILUM 10"],
                ["ДЕТРОЙТ"] = ["Дикі шершні '10'", "Rusoriz '10'"]
            }; 

            LocalCiti = new List<string>
            {
                "Купянськ", "Подоли", "Соболівка", "Курилівка", "Осиново", "Петропавлівка", "Голубівка",
                "Садове", "Благодатівка", "Московка", "Кіндрашівка", "Калинове", "Синьківка", "Радьківка",
            };

            StartWorkShablon = new List<string>
                    {
                        "Підрозділ: 14 омбр {UnitName}",
                        "Екіпаж: “{Position}”",
                        "Пілот: “{Pilot}”",
                        "Тип засобу: FPV “{DroneBy}”",
                        "Район зльоту: {LaunchArea}",
                        "Частоти: керування 2,6 відео 5900",
                        "Висота: 900 - 2500",
                        "Час роботи: “{Time}”",
                        "Мета польоту: робота по крилам противника",
                        "Напрямок польоту: {LocalCiti}",
                        "Ціль №: {ShootingTarget}"
                    };

            EndWorkShablon = new List<string>
                    {
                        "Підрозділ: 14 омбр {UnitName}",
                        "Роботу закінчили: “{Time}”",
                        "{TargetStatus}"
                    };

            ReportWorkShablon = new List<string>
                    {
                        "{Time} БпЛА-П №1 “{Position}” {UnitName} 14 омбр,",
                        "{nearestLocality} кв. ({MGRS_Short})",
                        "виявлено БпЛА “{TargetType}” (А - {azimyth}, Д - {range}, В - {height}).",
                        "Застосовано FPV дрон-перехоплювач мультироторного типу “{DroneBy}”, денний.",
                        "Ціль {TargetStatus}.",
                        "{Expenses} {AdditionalInfo} виявлення і супроводження DELTA-ВЕЖА, Цілевказівка КП зрдн."
                    };

            TargetTypeShablon =
            [
                "Молнія 2", "Зала", "Куб", "Орлан", "Ланцет", "Суперкам"
            ];

            // Якщо потрібно, ініціалізуйте порожній список
            if (CustomReportWorkShablon == null)
            {
                CustomReportWorkShablon = new List<string>();
            }
        }

        public void LoadAllData()
        {
            // Спочатку ініціалізуємо програмні дані
            InitializeData();

            // Завантажуємо користувацькі дані
            var userDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Maps", "UserOverrides.json");

            TryLoadOverrides(userDataPath);
        }

        public bool TryLoadOverrides(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                if (string.IsNullOrEmpty(path)) return false;

                var json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json)) return false;

                var ovr = JsonConvert.DeserializeObject<ShablonOverrides>(json);
                if (ovr == null) return false;

                if (!string.IsNullOrEmpty(ovr.CustomUnit))
                {
                    CustomUnit = ovr.CustomUnit;
                }

                // Завантажуємо кастомний шаблон
                if (ovr.CustomReportWorkShablon != null && ovr.CustomReportWorkShablon.Count > 0)
                {
                    CustomReportWorkShablon = ovr.CustomReportWorkShablon;
                }

                // Додайте перевірки на null
                Position_Point = Position_Point ?? new Dictionary<string, List<string>>();
                DroneByPosition = DroneByPosition ?? new Dictionary<string, List<string>>();
                ovr.Position_Point = ovr.Position_Point ?? new Dictionary<string, List<string>>();
                ovr.DroneByPosition = ovr.DroneByPosition ?? new Dictionary<string, List<string>>();

                // Оновлюємо позиції
                if (ovr.Position_Point != null)
                {
                    foreach (var item in ovr.Position_Point)
                    {
                        if (Position_Point.ContainsKey(item.Key))
                        {
                            Position_Point[item.Key] = item.Value;
                        }
                        else
                        {
                            Position_Point.Add(item.Key, item.Value);
                        }
                    }
                }

                // Оновлюємо дрони
                if (ovr.DroneByPosition != null)
                {
                    foreach (var item in ovr.DroneByPosition)
                    {
                        if (DroneByPosition.ContainsKey(item.Key))
                        {
                            DroneByPosition[item.Key] = item.Value;
                        }
                        else
                        {
                            DroneByPosition.Add(item.Key, item.Value);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(ovr.LaunchArea))
                {
                    LaunchArea = ovr.LaunchArea;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка завантаження користувацьких даних: {ex.Message}");
                return false;
            }
        }

        public void SaveOverrides(string path)
        {
            var obj = new ShablonOverrides
            {
                Position_Point = Position_Point,
                DroneByPosition = DroneByPosition,
                CustomUnit = CustomUnit,
                CustomReportWorkShablon = CustomReportWorkShablon, // Зберігаємо кастомний шаблон
                LaunchArea = LaunchArea // <-- нове поле

            };

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(path, json);
        }

        public void SaveCustomUnit(string unitName)
        {
            if (!string.IsNullOrEmpty(unitName))
            {
                CustomUnit = unitName.Trim();
                SaveShablon(); // Зберігаємо одразу
            }
        }

        public void SaveLaunchArea(string launchArea)
        {
            if (string.IsNullOrWhiteSpace(launchArea)) return;

            LaunchArea = launchArea.Trim();

            SaveShablon();
        }

        // Метод для збереження кастомного шаблону
        public void SaveCustomReportTemplate(List<string> template)
        {
            // Фільтруємо порожні рядки
            CustomReportWorkShablon = template
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();

            SaveShablon();

            // Відлагоджувальна інформація
            MessageBox.Show(($"Збережено шаблон з {CustomReportWorkShablon.Count} рядків"));
        }

        public List<string> ReportWorkShablonActual
        {
            get
            {
                // Якщо кастомний шаблон існує, не null і не порожній - використовуємо його
                if (CustomReportWorkShablon != null && CustomReportWorkShablon.Any(line => !string.IsNullOrWhiteSpace(line)))
                {
                    return CustomReportWorkShablon.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
                }
                // Інакше - програмний шаблон
                return ReportWorkShablon;
            }
        }

        // Метод для скидання до програмного шаблону
        public void ResetReportTemplate()
        {
            CustomReportWorkShablon = null;
            SaveShablon();
        }

        public void SaveShablon()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Maps", "UserOverrides.json");
            SaveOverrides(path);
        }

        //public void CreateEmptyJsonFile(string filePath)
        //{
        //    InitializeData();
        //    var shablonData = new
        //    {
        //        Position_Point,
        //        DroneByPosition,
        //        StartWorkShablon,
        //        EndWorkShablon,
        //        ReportWorkShablon
        //    };
        //File.WriteAllText(filePath, JsonConvert.SerializeObject(shablonData, Formatting.Indented));


        //}

        //public ShablonManager DeserializePosition(string filePath)
        //{
        //    if (File.Exists(filePath))
        //    {
        //        try
        //        {
        //            var jsonData = File.ReadAllText(filePath);
        //            var shablonData = JsonConvert.DeserializeObject<ShablonManager>(jsonData);

        //            if (shablonData == null || shablonData.Position_Point == null || shablonData.StartWorkShablon == null)
        //            {
        //                throw new InvalidDataException("Структура JSON не відповідає очікуваному формату.");
        //            }

        //            return shablonData;
        //        }
        //        catch (JsonException)
        //        {
        //            //CreateEmptyJsonFile(filePath);
        //            MessageBox.Show($"JSON-файл створено за адресою: {filePath}");
        //            throw new InvalidDataException("Помилка під час парсингу JSON даних.");
        //        }
        //    }
        //    else
        //    {
        //        throw new FileNotFoundException("JSON-файл не знайдено.");
        //    }
        //}
    }
}