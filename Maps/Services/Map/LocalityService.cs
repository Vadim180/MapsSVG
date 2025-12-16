using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Newtonsoft.Json;

namespace Maps.Services.Map
{
    public class LocalityService
    {
        public Dictionary<string, List<PointF>> Localities { get; private set; } = new();
        private readonly string path;

        public LocalityService(string? filePath = null)
        {
            path = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "localities.json");
            Load();
        }

        public bool Load()
        {
            try
            {
                if (!File.Exists(path))
                {
                    // Немає файлу — залишаємо порожній словник
                    Localities = new Dictionary<string, List<PointF>>();
                    return false;
                }

                var json = File.ReadAllText(path);
                var raw = JsonConvert.DeserializeObject<Dictionary<string, List<List<float>>>>(json);

                var result = new Dictionary<string, List<PointF>>();
                foreach (var kv in raw)
                {
                    var pts = new List<PointF>();
                    foreach (var arr in kv.Value)
                    {
                        if (arr.Count >= 2)
                        {
                            pts.Add(new PointF(arr[0], arr[1]));
                        }
                    }
                    result[kv.Key] = pts;
                }

                Localities = result;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не вдалося завантажити localities.json: {ex.Message}");
                Localities = new Dictionary<string, List<PointF>>();
                return false;
            }
        }
    }
}
