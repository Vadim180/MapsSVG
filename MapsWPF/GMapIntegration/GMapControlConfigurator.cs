using System;
using System.Windows.Input;
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

        map.ScaleMode = ScaleModes.Dynamic;
        map.MouseWheelZoomStep = 0.1;

        map.MultiTouchEnabled = false;
    }
}