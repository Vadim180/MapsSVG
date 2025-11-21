using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Maps
{
    public class ShablonOverrides
    {
        public Dictionary<string, List<string>>? Position_Point { get; set; }
        public Dictionary<string, List<string>>? Pilot {  get; set; }
        public Dictionary<string, List<string>>? DroneByPosition { get; set; }
        public string? CustomUnit { get; set; }
        public List<string> CustomReportWorkShablon { get; set; }
    }

}
