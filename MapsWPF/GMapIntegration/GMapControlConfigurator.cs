using System;
using System.Windows.Input;
using System.Windows.Media;
using GMap.NET;
using GMap.NET.WindowsPresentation;

namespace MapsWPF.GMapIntegration;

public sealed class GMapControlConfigurator
{
    public void Configure(GMapControl map)
    {
        ArgumentNullException.ThrowIfNull(map);

        // Вбудований drag вимкнений.
        // Перетягування правою кнопкою тепер виконує GMapBoundedDragController.
        map.CanDragMap = true;
        map.DragButton = MouseButton.Left;

        map.ShowCenter = false;

        map.MouseWheelZoomEnabled = true;

        // Fractional zoom scales raster tiles. Linear sampling is much cheaper
        // than WPF's high-quality resampler while the map is moving and does
        // not affect tiles displayed at their native integer zoom.
        RenderOptions.SetBitmapScalingMode(
            map,
            BitmapScalingMode.LowQuality);

        map.ScaleMode = ScaleModes.Dynamic;
        // Four quarter-steps per zoom level are enough for smooth navigation
        // and avoid ten overlay/tile refresh cycles for one wheel range.
        map.MouseWheelZoomStep = 0.25;

        // Keep only nearby tile levels in the decompressed matrix. The
        // persistent SQLite cache remains available when the user returns.
        map.LevelsKeepInMemory = 2;

        map.MultiTouchEnabled = false;
    }
}
