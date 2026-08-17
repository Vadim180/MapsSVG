using System.Collections.Generic;

namespace MapsWPF.Data.Defaults
{
    public static class ReportSelectionDefaults
    {
        public static Dictionary<string, List<string>> CreatePositionPilots()
        {
            return new Dictionary<string, List<string>>
            {
                ["DETROIT"] = new List<string>
                {
                    "GREENDAY та DREENDAY",
                    "Zibert"
                },

                ["Форпост"] = new List<string>
                {
                    "Zibert",
                    "Валькірія"
                }
            };
        }

        public static Dictionary<string, List<string>> CreateDronesByPosition()
        {
            return new Dictionary<string, List<string>>
            {
                ["DETROIT"] = new List<string>
                {
                    "Дикі шершні",
                    "Русоріз"
                },

                ["Форпост"] = new List<string>
                {
                    "P1SUN",
                    "Тарас-П"
                }
            };
        }

        public static List<string> CreateTargets()
        {
            return new List<string>
            {
                "Молнія",
                "Орлан",
                "Шахед",
                "Зала",
                "Скат",
                "Гербера",
                "Герань-2"
            };
        }
    }
}