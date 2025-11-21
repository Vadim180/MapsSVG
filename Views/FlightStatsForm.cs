using System;
using System.Windows.Forms;

namespace Maps
{
    public partial class FlightStatsForm : Form
    {
        public FlightStatsForm(string statsText)
        {
            InitializeComponent();
            textBoxStats.Text = statsText;
        }
    }
}
