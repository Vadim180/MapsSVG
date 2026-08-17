using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GMap.NET;
using GMap.NET.WindowsPresentation;

namespace MapsWPF.GMapIntegration;

public sealed class GMapTargetMarkerManager
{
    private GMapControl? _map;

    public GMapMarker? CurrentMarker { get; private set; }

    public event EventHandler? MarkerDeleted;

    public void AttachTo(GMapControl map)
    {
        ArgumentNullException.ThrowIfNull(map);

        _map = map;
    }

    public GMapMarker SetMarker(PointLatLng position)
    {
        var map = GetMap();

        RemoveCurrentMarkerFromMap();

        var marker = new GMapMarker(position)
        {
            Shape = CreateMarkerShape(),
            Offset = new Point(-5, -5),
            ZIndex = int.MaxValue
        };

        CurrentMarker = marker;
        map.Markers.Add(marker);

        return marker;
    }

    public void ShowMarker()
    {
        if (_map == null || CurrentMarker == null)
        {
            return;
        }

        if (!_map.Markers.Contains(CurrentMarker))
        {
            _map.Markers.Add(CurrentMarker);
        }
    }

    public void HideMarker()
    {
        if (_map == null || CurrentMarker == null)
        {
            return;
        }

        RemoveCurrentMarkerFromMap();
    }

    public void DeleteMarker()
    {
        if (CurrentMarker == null)
        {
            return;
        }

        RemoveCurrentMarkerFromMap();
        CurrentMarker = null;

        MarkerDeleted?.Invoke(this, EventArgs.Empty);
    }

    private Shape CreateMarkerShape()
    {
        var markerShape = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.Red,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsHitTestVisible = true
        };

        markerShape.MouseRightButtonDown += Marker_MouseRightButtonDown;

        return markerShape;
    }

    private void Marker_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        DeleteMarker();
        e.Handled = true;
    }

    private void RemoveCurrentMarkerFromMap()
    {
        if (_map == null || CurrentMarker == null)
        {
            return;
        }

        _map.Markers.Remove(CurrentMarker);
    }

    private GMapControl GetMap()
    {
        return _map ?? throw new InvalidOperationException(
            "GMapTargetMarkerManager is not attached to a GMapControl."
        );
    }
}