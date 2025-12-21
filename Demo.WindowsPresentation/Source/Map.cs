using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GMap.NET.WindowsPresentation;

namespace Demo.WindowsPresentation
{
    /// <summary>
    ///     The custom map of GMapControl
    /// </summary>
    public class Map : GMapControl
    {
        public long ElapsedMilliseconds;

#if DEBUG

        private int _counter;
        readonly Typeface _tf = new Typeface("GenericSansSerif");
        readonly FlowDirection fd = new FlowDirection();
        public static readonly Stopwatch _stopwatch = new Stopwatch();

        /// <summary>
        ///     any custom drawing here
        /// </summary>
        /// <param name="drawingContext"></param>
        protected override void OnRender(DrawingContext drawingContext)
        {
            _stopwatch.Reset();
            _stopwatch.Start();

            base.OnRender(drawingContext);
            _stopwatch.Stop();

            var text = new FormattedText(
                "Render: " + _stopwatch.ElapsedMilliseconds +
                "ms",
                CultureInfo.InvariantCulture,
                fd,
                _tf,
                12,
                Brushes.Red);

            // Правий нижній кут карти з відступом 10 пікселів
            drawingContext.DrawText(text, new Point(ActualWidth - text.Width - 10, ActualHeight - text.Height - 10));
        }
#endif
    }
}
