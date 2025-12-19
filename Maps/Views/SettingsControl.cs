using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Maps
{
    public partial class SettingsControl : UserControl
    {
        public event EventHandler BackClicked;
        public event EventHandler InfoClicked;
        public event EventHandler RenameClicked;

        public SettingsControl()
        {
            InitializeComponent();
        }

        private void btnInfo_Click(object sender, EventArgs e)
        {
            InfoClicked?.Invoke(this, EventArgs.Empty);
        }

        private void exit_Click(object sender, EventArgs e)
        {
            BackClicked?.Invoke(this, EventArgs.Empty);
        }

        private void btnRename_Click(object sender, EventArgs e)
        {
            RenameClicked?.Invoke(this, EventArgs.Empty);
        }

    }
}