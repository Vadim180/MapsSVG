using System.Collections.Generic;
using System.Drawing;
using GMap.NET;

namespace MapsWPF.Services
{
    public interface ISelectionProvider
    {
        string GetSelectedPosition();
        string GetSelectedPilot();
        string GetSelectedDrone();
        string GetShootingTarget();
        string GetHeightText();
        int GetSelectedRange();
        bool TryGetUTM(out PointF utm);
        string FormatShortMGRSFromUTM(PointF utm);
        bool TryGetClickedLatLng(out PointLatLng latlng);
        string FormatShortMGRSFromLatLng(PointLatLng latlng);
        string FindClosestLocalityFromLatLng(PointLatLng latlng);
        string GetTargetType();
        string GetAndSetTime();
        string GetCurrentTimeString();
        bool IsTargetDestroyed();
        bool IsTargetBoardLost();
        PointF? GetClickedPoint();
        PointF GetAttackPoint();
        string GetAzimuthText();
        IEnumerable<string> GetSelectedFlyDirections();
    }
}