using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Maps.Views
{
    public partial class LocalityControl : UserControl
    {
        public event Action<string>? LocalitySelected;
        public event EventHandler BackClicked;

        public LocalityControl()
        {
            InitializeComponent();
        }

        public void SetLocalities(IEnumerable<string> names)
        {
            listBoxLocalities.Items.Clear();
            listBoxLocalities.Items.AddRange(names.ToArray());
        }

        private void listBoxLocalities_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBoxLocalities.SelectedItem is string name)
            {
                LocalitySelected?.Invoke(name);
            }
        }

        private void exit_Click(object sender, EventArgs e)
        {
            BackClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
