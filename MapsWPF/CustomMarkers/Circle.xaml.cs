using System.Windows.Controls;
using GMap.NET;

namespace MapsWPF.CustomMarkers
{
    /// <summary>
    ///     Interaction logic for Circle.xaml
    /// </summary>
    public partial class Circle : UserControl
    {
        public Circle()
        {
            InitializeComponent();
        }

        public PointLatLng Center;
        public PointLatLng Bound;
    }
}
