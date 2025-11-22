using Maps.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Maps
{
    class CalibrationData
    {
        public List<ReferencePoint> Points { get; set; } = new();
        public int MapWidth { get; set; }
        public int MapHeight { get; set; }
    }

}
