using System;
using System.Collections.Generic;

namespace MapsWPF.Data.Defaults
{
    public static class ReportTemplateDefaults
    {
        public static List<string> CreateStartWorkTemplate()
        {
            return new List<string>
            {
                "Підрозділ: 14 омбр {UnitName}",
                "Екіпаж: “{Position}”",
                "Пілот: “{Pilot}”",
                "Тип засобу: FPV “{DroneBy}”",
                "Район зльоту: ",
                "Частоти: керування 2,6 відео 5900",
                "Висота: 900 - 2500",
                "Час роботи: “{Time}”",
                "Мета польоту: робота по крилам противника",
                "Напрямок польоту: {Direction}",
                "Ціль №: {ShootingTarget}"
            };
        }

        public static List<string> CreateEndWorkTemplate()
        {
            return new List<string>
            {
                "Підрозділ: 14 омбр {UnitName}",
                "Роботу закінчили: “{Time}”",
                "{TargetStatus}"
            };
        }

        public static List<string> CreateReportWorkTemplate()
        {
            return new List<string>
            {
                "{Time} БпЛА-П №1 “{Position}” {UnitName} 14 омбр,",
                "{nearestLocality} кв. ({MGRS_Short})",
                "виявлено БпЛА “{TargetType}” (А - {azimyth}, Д - {range}, В - {height}).",
                "Застосовано FPV дрон-перехоплювач мультироторного типу “{DroneBy}”, денний.",
                "Ціль {TargetStatus}.",
                "{Expenses} {AdditionalInfo} виявлення і супроводження ГРАФІТ. Цілевказівка КП зрдн."
            };
        }

        public static Dictionary<string, List<string>> CreateTemplateMap()
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Report"] = CreateReportWorkTemplate(),
                ["StartWork"] = CreateStartWorkTemplate(),
                ["EndWork"] = CreateEndWorkTemplate()
            };
        }
    }
}