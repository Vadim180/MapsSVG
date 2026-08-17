using System.Windows;
using System.Windows.Media;

namespace MapsWPF
{
    /// <summary>
    /// Independent WPF visual for the coordinate and servo HUD.
    /// It is deliberately hosted above Map instead of being drawn by Map.OnRender.
    /// </summary>
    public sealed class MapHudOverlay : FrameworkElement
    {
        public static readonly DependencyProperty MapSourceProperty =
            DependencyProperty.Register(
                nameof(MapSource),
                typeof(Map),
                typeof(MapHudOverlay),
                new PropertyMetadata(null, OnMapSourceChanged));

        public Map MapSource
        {
            get => (Map)GetValue(MapSourceProperty);
            set => SetValue(MapSourceProperty, value);
        }

        public MapHudOverlay()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (MapSource?.ShowCoordinates == true)
            {
                MapSource.DrawCoordinatesOverlay(drawingContext);
            }
        }

        private static void OnMapSourceChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs e)
        {
            var overlay = (MapHudOverlay)dependencyObject;

            if (e.OldValue is Map oldMap)
            {
                oldMap.HudInvalidated -= overlay.Map_HudInvalidated;
            }

            if (e.NewValue is Map newMap)
            {
                newMap.HudInvalidated += overlay.Map_HudInvalidated;
            }

            overlay.InvalidateVisual();
        }

        private void Map_HudInvalidated(object sender, System.EventArgs e)
        {
            InvalidateVisual();
        }
    }
}
