using System;
using System.Windows;
using System.Windows.Input;
using GMap.NET;

namespace MapsWPF.GMapIntegration;

public sealed class GMapWorkAreaEditor : IDisposable
{
    private enum WorkAreaEditMode
    {
        None,
        DrawingNew,
        Moving,
        ResizeLeft,
        ResizeRight,
        ResizeTop,
        ResizeBottom,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight
    }

    private const double MinSizePx = 8.0; // мінімальний розмір у пікселях

    private const double EdgeHitTolerancePx = 8.0;

    private MapsWPF.Map? _map;
    private PositionChanged? _onPositionChangedHandler;

    private bool _isDrawing;
    private Point _startLocalPoint;
    private Point _currentLocalPoint;

    private WorkAreaEditMode _editMode = WorkAreaEditMode.None;
    private PointLatLng _lastMoveGeoPoint;

    public bool IsEditing { get; private set; }

    public RectLatLng? DraftBounds { get; private set; }

    public event Action<RectLatLng?>? DraftBoundsChanged;

    public void AttachTo(MapsWPF.Map map)
    {
        ArgumentNullException.ThrowIfNull(map);

        // Відписуємося від попередньої карти
        Detach();

        _map = map;

        map.PreviewMouseRightButtonDown += Map_PreviewMouseRightButtonDown;
        map.PreviewMouseMove += Map_PreviewMouseMove;
        map.PreviewMouseRightButtonUp += Map_PreviewMouseRightButtonUp;
        map.LostMouseCapture += Map_LostMouseCapture;

        // Правильний делегат
        _onPositionChangedHandler = _ => RefreshVisibleBounds();
        map.OnPositionChanged += _onPositionChangedHandler;

        map.OnMapZoomChanged += RefreshVisibleBounds;
    }

    private void Map_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!IsEditing)
        {
            return;
        }

        _isDrawing = false;
        _editMode = WorkAreaEditMode.None;

        if (_map != null)
        {
            _map.Cursor = Cursors.Cross;
        }
    }

    public void StartEdit()
    {
        var map = GetMap();

        IsEditing = true;
        _isDrawing = false;
        _editMode = WorkAreaEditMode.None;

        DraftBounds = null;

        ClearPreview();

        DraftBoundsChanged?.Invoke(DraftBounds);

        map.Cursor = Cursors.Cross;
    }

    public void LoadDraftBounds(RectLatLng bounds)
    {
        DraftBounds = bounds;

        DrawBounds(bounds);
        DraftBoundsChanged?.Invoke(DraftBounds);
    }

    public void CancelEdit()
    {
        FinishEditing(clearDraft: true);
    }

    public void CompleteEdit()
    {
        FinishEditing(clearDraft: true);
    }

    public void ClearPreview()
    {
        if (_map == null)
        {
            return;
        }

        _map.IsWorkAreaEditVisible = false;
        _map.WorkAreaEditBounds = null;
        _map.InvalidateVisual(true);
    }

    private void Map_PreviewMouseRightButtonDown(
    object sender,
    MouseButtonEventArgs e)
    {
        if (!IsEditing ||
            e.ChangedButton != MouseButton.Right)
        {
            return;
        }

        var map = GetMap();
        var localPoint = e.GetPosition(map);

        var hitMode = HitTestLocalPoint(localPoint);

        if (hitMode != WorkAreaEditMode.None)
        {
            _editMode = hitMode;
            _isDrawing = false;

            if (_editMode == WorkAreaEditMode.Moving)
            {
                _lastMoveGeoPoint = map.FromLocalToLatLng(
                    (int)localPoint.X,
                    (int)localPoint.Y
                );
            }

            Mouse.Capture(map);

            e.Handled = true;
            return;
        }

        _editMode = WorkAreaEditMode.DrawingNew;
        _isDrawing = true;

        DraftBounds = null;
        ClearPreview();

        _startLocalPoint = localPoint;
        _currentLocalPoint = localPoint;

        Mouse.Capture(map);

        UpdatePreviewFromLocalPoints(
            _startLocalPoint,
            _currentLocalPoint
        );

        e.Handled = true;
    }

    private void Map_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsEditing)
        {
            return;
        }

        var map = GetMap();
        var localPoint = e.GetPosition(map);

        // Якщо ПКМ уже відпущена, але стан жесту ще активний,
        // примусово завершуємо поточне малювання, переміщення або resize.
        if ((_isDrawing ||
             IsResizeMode(_editMode) ||
             _editMode == WorkAreaEditMode.Moving) &&
            e.RightButton != MouseButtonState.Pressed)
        {
            _isDrawing = false;
            _editMode = WorkAreaEditMode.None;

            if (Mouse.Captured == map)
            {
                Mouse.Capture(null);
            }

            UpdateCursor(localPoint);

            e.Handled = true;
            return;
        }

        if (_editMode == WorkAreaEditMode.Moving &&
            e.RightButton == MouseButtonState.Pressed)
        {
            MoveDraftBounds(localPoint);

            e.Handled = true;
            return;
        }

        if (IsResizeMode(_editMode) &&
            e.RightButton == MouseButtonState.Pressed)
        {
            ResizeDraftBounds(localPoint);

            e.Handled = true;
            return;
        }

        if (_editMode == WorkAreaEditMode.DrawingNew &&
            _isDrawing &&
            e.RightButton == MouseButtonState.Pressed)
        {
            _currentLocalPoint = localPoint;

            UpdatePreviewFromLocalPoints(
                _startLocalPoint,
                _currentLocalPoint
            );

            e.Handled = true;
            return;
        }

        // Без натиснутих кнопок визначаємо, чи курсор перебуває
        // над краєм, кутом або всередині прямокутника.
        UpdateCursor(localPoint);
    }

    private void Map_PreviewMouseRightButtonUp(object sender,MouseButtonEventArgs e)
    {
        if (!IsEditing ||
            e.ChangedButton != MouseButton.Right)
        {
            return;
        }

        var map = GetMap();
        var localPoint = e.GetPosition(map);

        if (_editMode == WorkAreaEditMode.Moving ||
            IsResizeMode(_editMode))
        {
            _editMode = WorkAreaEditMode.None;

            if (Mouse.Captured == map)
            {
                Mouse.Capture(null);
            }

            UpdateCursor(localPoint);

            e.Handled = true;
            return;
        }

        if (_editMode == WorkAreaEditMode.DrawingNew && _isDrawing)
        {
            _isDrawing = false;
            _editMode = WorkAreaEditMode.None;

            _currentLocalPoint = localPoint;

            UpdatePreviewFromLocalPoints(
                _startLocalPoint,
                _currentLocalPoint
            );

            Mouse.Capture(null);

            UpdateCursor(localPoint);

            e.Handled = true;
        }
    }

    private void UpdatePreviewFromLocalPoints(Point start, Point end)
    {
        var map = GetMap();

        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);

        var width = right - left;
        var height = bottom - top;

        if (width < MinSizePx || height < MinSizePx)
        {
            return;
        }

        var topLeft = map.FromLocalToLatLng(
            (int)left,
            (int)top
        );

        var bottomRight = map.FromLocalToLatLng(
            (int)right,
            (int)bottom
        );

        var bounds = CreateBounds(
            topLeft,
            bottomRight
        );

        DraftBounds = bounds;

        DrawBounds(bounds);
        DraftBoundsChanged?.Invoke(DraftBounds);
    }

    private void MoveDraftBounds(Point currentLocalPoint)
    {
        if (!DraftBounds.HasValue)
        {
            return;
        }

        var map = GetMap();

        var currentGeoPoint = map.FromLocalToLatLng(
            (int)currentLocalPoint.X,
            (int)currentLocalPoint.Y
        );

        var deltaLat = currentGeoPoint.Lat - _lastMoveGeoPoint.Lat;
        var deltaLng = currentGeoPoint.Lng - _lastMoveGeoPoint.Lng;

        if (Math.Abs(deltaLat) < 0.000000001 &&
            Math.Abs(deltaLng) < 0.000000001)
        {
            return;
        }

        var bounds = DraftBounds.Value;

        var movedBounds = RectLatLng.FromLTRB(
            bounds.Left + deltaLng,
            bounds.Top + deltaLat,
            bounds.Right + deltaLng,
            bounds.Bottom + deltaLat
        );

        DraftBounds = movedBounds;
        _lastMoveGeoPoint = currentGeoPoint;

        DrawBounds(movedBounds);
        DraftBoundsChanged?.Invoke(DraftBounds);
    }

    private void ResizeDraftBounds(Point currentLocalPoint)
    {
        if (!DraftBounds.HasValue)
        {
            return;
        }

        var map = GetMap();

        var currentGeoPoint = map.FromLocalToLatLng(
            (int)currentLocalPoint.X,
            (int)currentLocalPoint.Y
        );

        var bounds = DraftBounds.Value;

        var left = bounds.Left;
        var right = bounds.Right;
        var top = bounds.Top;
        var bottom = bounds.Bottom;

        switch (_editMode)
        {
            case WorkAreaEditMode.ResizeLeft:
                left = currentGeoPoint.Lng;
                break;

            case WorkAreaEditMode.ResizeRight:
                right = currentGeoPoint.Lng;
                break;

            case WorkAreaEditMode.ResizeTop:
                top = currentGeoPoint.Lat;
                break;

            case WorkAreaEditMode.ResizeBottom:
                bottom = currentGeoPoint.Lat;
                break;

            case WorkAreaEditMode.ResizeTopLeft:
                top = currentGeoPoint.Lat;
                left = currentGeoPoint.Lng;
                break;

            case WorkAreaEditMode.ResizeTopRight:
                top = currentGeoPoint.Lat;
                right = currentGeoPoint.Lng;
                break;

            case WorkAreaEditMode.ResizeBottomLeft:
                bottom = currentGeoPoint.Lat;
                left = currentGeoPoint.Lng;
                break;

            case WorkAreaEditMode.ResizeBottomRight:
                bottom = currentGeoPoint.Lat;
                right = currentGeoPoint.Lng;
                break;
        }

        var resizedBounds = RectLatLng.FromLTRB(
            Math.Min(left, right),
            Math.Max(top, bottom),
            Math.Max(left, right),
            Math.Min(top, bottom)
        );

        DraftBounds = resizedBounds;

        DrawBounds(resizedBounds);
        DraftBoundsChanged?.Invoke(DraftBounds);
    }

    private void RefreshVisibleBounds()
    {
        if (IsEditing && DraftBounds.HasValue)
        {
            DrawBounds(DraftBounds.Value);
        }
    }

    private void DrawBounds(RectLatLng bounds)
    {
        var map = GetMap();

        map.WorkAreaEditBounds = bounds;
        map.IsWorkAreaEditVisible = true;
        map.IsWorkAreaEditFillVisible = IsEditing;

        map.InvalidateVisual(true);
    }

    private WorkAreaEditMode HitTestLocalPoint(Point localPoint)
    {
        if (!DraftBounds.HasValue)
        {
            return WorkAreaEditMode.None;
        }

        var rect = GetLocalBoundsRect(DraftBounds.Value);

        var isNearLeft = Math.Abs(localPoint.X - rect.Left) <= EdgeHitTolerancePx;
        var isNearRight = Math.Abs(localPoint.X - rect.Right) <= EdgeHitTolerancePx;
        var isNearTop = Math.Abs(localPoint.Y - rect.Top) <= EdgeHitTolerancePx;
        var isNearBottom = Math.Abs(localPoint.Y - rect.Bottom) <= EdgeHitTolerancePx;

        var isWithinHorizontalRange =
            localPoint.X >= rect.Left - EdgeHitTolerancePx &&
            localPoint.X <= rect.Right + EdgeHitTolerancePx;

        var isWithinVerticalRange =
            localPoint.Y >= rect.Top - EdgeHitTolerancePx &&
            localPoint.Y <= rect.Bottom + EdgeHitTolerancePx;

        if (isNearLeft && isNearTop)
        {
            return WorkAreaEditMode.ResizeTopLeft;
        }

        if (isNearRight && isNearTop)
        {
            return WorkAreaEditMode.ResizeTopRight;
        }

        if (isNearLeft && isNearBottom)
        {
            return WorkAreaEditMode.ResizeBottomLeft;
        }

        if (isNearRight && isNearBottom)
        {
            return WorkAreaEditMode.ResizeBottomRight;
        }

        if (isNearLeft && isWithinVerticalRange)
        {
            return WorkAreaEditMode.ResizeLeft;
        }

        if (isNearRight && isWithinVerticalRange)
        {
            return WorkAreaEditMode.ResizeRight;
        }

        if (isNearTop && isWithinHorizontalRange)
        {
            return WorkAreaEditMode.ResizeTop;
        }

        if (isNearBottom && isWithinHorizontalRange)
        {
            return WorkAreaEditMode.ResizeBottom;
        }

        if (rect.Contains(localPoint))
        {
            return WorkAreaEditMode.Moving;
        }

        return WorkAreaEditMode.None;
    }

    private Rect GetLocalBoundsRect(RectLatLng bounds)
    {
        var map = GetMap();

        // Перетворюємо всі чотири кути для коректного обліку повороту
        var corners = new[]
        {
        map.FromLatLngToLocal(new PointLatLng(bounds.Top, bounds.Left)),
        map.FromLatLngToLocal(new PointLatLng(bounds.Top, bounds.Right)),
        map.FromLatLngToLocal(new PointLatLng(bounds.Bottom, bounds.Right)),
        map.FromLatLngToLocal(new PointLatLng(bounds.Bottom, bounds.Left))
    };

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var p in corners)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private void FinishEditing(bool clearDraft)
    {
        var map = GetMap();

        IsEditing = false;
        _isDrawing = false;
        _editMode = WorkAreaEditMode.None;

        if (Mouse.Captured == map)
        {
            Mouse.Capture(null);
        }

        if (clearDraft)
        {
            DraftBounds = null;
            ClearPreview();
        }

        map.Cursor = Cursors.Arrow;

        DraftBoundsChanged?.Invoke(DraftBounds);
    }

    private void UpdateCursor(Point localPoint)
    {
        var map = GetMap();

        if (!IsEditing)
        {
            map.Cursor = Cursors.Arrow;
            return;
        }

        var hitMode = HitTestLocalPoint(localPoint);

        map.Cursor = hitMode switch
        {
            WorkAreaEditMode.Moving => Cursors.SizeAll,

            WorkAreaEditMode.ResizeLeft => Cursors.SizeWE,
            WorkAreaEditMode.ResizeRight => Cursors.SizeWE,

            WorkAreaEditMode.ResizeTop => Cursors.SizeNS,
            WorkAreaEditMode.ResizeBottom => Cursors.SizeNS,

            WorkAreaEditMode.ResizeTopLeft => Cursors.SizeNWSE,
            WorkAreaEditMode.ResizeBottomRight => Cursors.SizeNWSE,

            WorkAreaEditMode.ResizeTopRight => Cursors.SizeNESW,
            WorkAreaEditMode.ResizeBottomLeft => Cursors.SizeNESW,

            _ => Cursors.Cross
        };
    }

    private static bool IsResizeMode(WorkAreaEditMode mode)
    {
        return mode == WorkAreaEditMode.ResizeLeft ||
               mode == WorkAreaEditMode.ResizeRight ||
               mode == WorkAreaEditMode.ResizeTop ||
               mode == WorkAreaEditMode.ResizeBottom ||
               mode == WorkAreaEditMode.ResizeTopLeft ||
               mode == WorkAreaEditMode.ResizeTopRight ||
               mode == WorkAreaEditMode.ResizeBottomLeft ||
               mode == WorkAreaEditMode.ResizeBottomRight;
    }

    private static RectLatLng CreateBounds(PointLatLng first, PointLatLng second)
    {
        var topLat = Math.Max(first.Lat, second.Lat);
        var bottomLat = Math.Min(first.Lat, second.Lat);
        var leftLng = Math.Min(first.Lng, second.Lng);
        var rightLng = Math.Max(first.Lng, second.Lng);

        return RectLatLng.FromLTRB(
            leftLng,
            topLat,
            rightLng,
            bottomLat
        );
    }

    private MapsWPF.Map GetMap()
    {
        return _map ?? throw new InvalidOperationException(
            "GMapWorkAreaEditor is not attached to a Map."
        );
    }

    public void Detach()
    {
        if (_map == null)
            return;

        _map.PreviewMouseRightButtonDown -= Map_PreviewMouseRightButtonDown;
        _map.PreviewMouseRightButtonUp -= Map_PreviewMouseRightButtonUp;
        _map.PreviewMouseMove -= Map_PreviewMouseMove;
        _map.LostMouseCapture -= Map_LostMouseCapture;

        if (_onPositionChangedHandler != null)
            _map.OnPositionChanged -= _onPositionChangedHandler;

        _map.OnMapZoomChanged -= RefreshVisibleBounds;

        _map = null;
        _onPositionChangedHandler = null;
    }

    public void Dispose()
    {
        Detach();
        GC.SuppressFinalize(this);
    }

}
