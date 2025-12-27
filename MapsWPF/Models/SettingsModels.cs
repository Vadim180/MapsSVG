using System;
using System.IO;
using Newtonsoft.Json;
using System.Drawing;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MapsWPF.Models
{
    public class MapStartSettings
    {
        public double Lat { get; set; } = 50.4501;
        public double Lng { get; set; } = 30.52001953125;
        public int Zoom { get; set; } = 11;
        
        // Map Provider
        public string MapProviderName { get; set; } = "GoogleHybridMap";
        
        // Go GroupBox Saved State
        public string GoGeo { get; set; } = "Kyiv";
        
        // Target Point (Red Marker) - Nullable if not set
        public double? TargetLat { get; set; }
        public double? TargetLng { get; set; }

        // Cached Target Info
        public double? CachedTargetLat { get; set; }
        public double? CachedTargetLng { get; set; }
        public string LastTargetLocationName { get; set; }
        public string LastTargetAddress { get; set; }

        // UI Settings
        public bool ShowGrid { get; set; } = false;
        public bool ShowCoordinates { get; set; } = false;

        // Expander States
        public bool IsCoordinatesExpanded { get; set; } = true;
        public bool IsGmapExpanded { get; set; } = true;
        public bool IsCacheExpanded { get; set; } = true;
        public bool IsGoExpanded { get; set; } = true;
        public bool IsRayExpanded { get; set; } = true;
        public bool IsTargetExpanded { get; set; } = true;
        public bool IsMapLimitsExpanded { get; set; } = true;

        // Map Limits
        public bool IsMapLimitsEnabled { get; set; } = false;
        public double? LimitTopLeftLat { get; set; }
        public double? LimitTopLeftLng { get; set; }
        public double? LimitBottomRightLat { get; set; }
        public double? LimitBottomRightLng { get; set; }

        // Window State
        public double? WindowTop { get; set; }
        public double? WindowLeft { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public int WindowState { get; set; } = 0; // 0=Normal, 1=Minimized, 2=Maximized
    }

    public class AttackSettings : INotifyPropertyChanged
    {
        // Changed from X/Y (screen pixels) to Lat/Lng (Geo)
        private double _lat;
        private double _lng;
        private bool _isSet = false;

        private float _angle;
        private double _rayLength = 5000; // Meters
        private float _sectorWidth = 30f;
        private float _rotateStep = 0.5f;
        private float _rotateShiftStep = 0.2f;

        public double Lat
        {
            get => _lat;
            set { if (_lat != value) { _lat = value; OnPropertyChanged(); } }
        }

        public double Lng
        {
            get => _lng;
            set { if (_lng != value) { _lng = value; OnPropertyChanged(); } }
        }
        
        public bool IsSet
        {
            get => _isSet;
            set { if (_isSet != value) { _isSet = value; OnPropertyChanged(); } }
        }

        public float Angle
        {
            get => _angle;
            set { if (_angle != value) { _angle = value; OnPropertyChanged(); } }
        }

        public double RayLength
        {
            get => _rayLength;
            set { if (_rayLength != value) { _rayLength = value; OnPropertyChanged(); } }
        }

        public float SectorWidth
        {
            get => _sectorWidth;
            set { if (_sectorWidth != value) { _sectorWidth = value; OnPropertyChanged(); } }
        }

        public float RotateStep
        {
            get => _rotateStep;
            set { if (_rotateStep != value) { _rotateStep = value; OnPropertyChanged(); } }
        }

        public float RotateShiftStep
        {
            get => _rotateShiftStep;
            set { if (_rotateShiftStep != value) { _rotateShiftStep = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
