using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using GMap.NET.Internals;
using GMap.NET.MapProviders;
using GMap.NET.Projections;
using System.IO;
using System.Text;

namespace GMap.NET.WindowsPresentation
{
    /// <summary>
    ///     GMap.NET control for Windows Presentation
    /// </summary>
    public partial class GMapControl : ItemsControl, Interface, IDisposable
    {
        private const double ZoomOutLimitEpsilon = 0.01;
        private bool _isUpdatingPositionFromCore;

        private bool _isClampingVisibleBounds;

        private bool _disposed = false;
                
        private bool _leftStuckBeforeZoom;
        private bool _rightStuckBeforeZoom;
        private bool _topStuckBeforeZoom;
        private bool _bottomStuckBeforeZoom;

        public bool IsViewportResizeBoundsCorrectionSuspended { get; set; }

        /// <summary>
        ///     type of map
        /// </summary>
        [Browsable(false)]
        public GMapProvider MapProvider
        {
            get { return GetValue(MapProviderProperty) as GMapProvider; }
            set { SetValue(MapProviderProperty, value); }
        }

        public Point MapPoint
        {
            get { return (Point)GetValue(MapPointProperty); }
            set { SetValue(MapPointProperty, value); }
        }

        public static readonly DependencyProperty CenterPositionProperty = DependencyProperty.Register("CenterPosition",
            typeof(PointLatLng),
            typeof(GMapControl),
            new UIPropertyMetadata(new PointLatLng(),
                CenterPositionPropertyChanged));

        // Using a DependencyProperty as the backing store for point.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty MapPointProperty = DependencyProperty.Register("MapPoint",
            typeof(Point),
            typeof(GMapControl),
            new PropertyMetadata(new Point(),
                OnMapPointPropertyChanged));

        private static void OnMapPointPropertyChanged(DependencyObject source,
            DependencyPropertyChangedEventArgs e)
        {
            var temp = (Point)e.NewValue;
            (source as GMapControl).Position = new PointLatLng(temp.X, temp.Y);
        }

        public static readonly DependencyProperty MapProviderProperty = DependencyProperty.Register("MapProvider",
            typeof(GMapProvider),
            typeof(GMapControl),
            new UIPropertyMetadata(EmptyProvider.Instance,
                MapProviderPropertyChanged));


        public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register("Zoom",
            typeof(double),
            typeof(GMapControl),
            new UIPropertyMetadata(0.0,
                ZoomPropertyChanged,
                OnCoerceZoom));

        /// <summary>
        ///     The multi touch enabled property
        /// </summary>
        public static readonly DependencyProperty MultiTouchEnabledProperty = DependencyProperty.Register(
            "MultiTouchEnabled",
            typeof(bool),
            typeof(GMapControl),
            new PropertyMetadata(false, OnMultiTouchEnabledChanged));

        /// <summary>
        ///     The touch enabled property
        /// </summary>
        public static readonly DependencyProperty TouchEnabledProperty = DependencyProperty.Register("TouchEnabled",
            typeof(bool),
            typeof(GMapControl),
            new PropertyMetadata(false));


        /// <summary>
        ///     map zoom
        /// </summary>
        [Category("GMap.NET")]
        public double Zoom
        {
            get { return (double)GetValue(ZoomProperty); }
            set { SetValue(ZoomProperty, value); }
        }

        [Category("GMap.NET")]
        public PointLatLng CenterPosition
        {
            get { return (PointLatLng)GetValue(CenterPositionProperty); }
            set { SetValue(CenterPositionProperty, value); }
        }

        /// <summary>
        ///     Specifies, if a floating map scale is displayed using a
        ///     stretched, or a narrowed map.
        ///     If <code>ScaleMode</code> is <code>ScaleDown</code>,
        ///     then a scale of 12.3 is displayed using a map zoom level of 13
        ///     resized to the lower level. If the parameter is <code>ScaleUp</code> ,
        ///     then the same scale is displayed using a zoom level of 12 with an
        ///     enlarged scale. If the value is <code>Dynamic</code>, then until a
        ///     remainder of 0.25 <code>ScaleUp</code> is applied, for bigger
        ///     remainders <code>ScaleDown</code>.
        /// </summary>
        [Category("GMap.NET")]
        [Description("map scale type")]
        public ScaleModes ScaleMode
        {
            get { return _scaleMode; }
            set
            {
                if (_scaleMode == value)
                {
                    return;
                }

                _scaleMode = value;

                ReapplyCurrentZoomTransform();

                InvalidateVisual(true);
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether [multi touch enabled].
        /// </summary>
        /// <value><c>true</c> if [multi touch enabled]; otherwise, <c>false</c>.</value>
        [Category("GMap.NET")]
        [Description("Enable pinch map zoom")]
        public bool MultiTouchEnabled
        {
            get { return (bool)GetValue(MultiTouchEnabledProperty); }
            set { SetValue(MultiTouchEnabledProperty, value); }
        }

        private static object OnCoerceZoom(DependencyObject o, object value)
        {
            var map = o as GMapControl;

            if (map != null)
            {
                double result = (double)value;

                if (result > map.MaxZoom)
                    result = map.MaxZoom;

                if (result < map.MinZoom)
                    result = map.MinZoom;

                return result;
            }
            else
            {
                return value;
            }
        }

        /// <summary>
        ///     Centers the position property changed.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="e">The <see cref="DependencyPropertyChangedEventArgs" /> instance containing the event data.</param>
        private static void CenterPositionPropertyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            var gmapControl = obj as GMapControl;

            if (gmapControl != null && e.NewValue is PointLatLng)
            {
                gmapControl.CenterPosition = gmapControl.Position = (PointLatLng)e.NewValue;
            }
        }

        private static void MapProviderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var map = (GMapControl)d;

            if (map != null && e.NewValue != null)
            {
                Debug.WriteLine("MapType: " + e.OldValue + " -> " + e.NewValue);

                var viewarea = map.SelectedArea;

                if (viewarea != RectLatLng.Empty)
                {
                    map.Position = new PointLatLng(viewarea.Lat - viewarea.HeightLat / 2,
                        viewarea.Lng + viewarea.WidthLng / 2);
                }
                else
                {
                    viewarea = map.ViewArea;
                }

                map._core.Provider = e.NewValue as GMapProvider;

                map._copyright = null;

                if (!string.IsNullOrEmpty(map._core.Provider.Copyright))
                {
                    map._copyright = new FormattedText(map._core.Provider.Copyright,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        new Typeface("GenericSansSerif"),
                        9,
                        Brushes.Navy);
                }

                if (map._core.IsStarted && map._core.ZoomToArea)
                {
                    // restore zoomrect as close as possible
                    if (viewarea != RectLatLng.Empty && viewarea != map.ViewArea)
                    {
                        int bestZoom = map._core.GetMaxZoomToFitRect(viewarea);

                        if (bestZoom > 0 && map.Zoom != bestZoom)
                            map.Zoom = bestZoom;
                    }
                }
            }
        }

        private static void ZoomPropertyChanged(GMapControl mapControl, double value, double oldValue,
            ZoomMode zoomMode)
        {
            if (mapControl != null && mapControl.MapProvider.Projection != null)
            {
                double remainder = value % 1;

                if (mapControl.ScaleMode != ScaleModes.Integer && remainder != 0 && mapControl.ActualWidth > 0)
                {
                    bool scaleDown;

                    switch (mapControl.ScaleMode)
                    {
                        case ScaleModes.ScaleDown:
                            scaleDown = true;
                            break;

                        case ScaleModes.Dynamic:
                            scaleDown = remainder > 0.25;
                            break;

                        default:
                            scaleDown = false;
                            break;
                    }

                    if (scaleDown)
                        remainder--;

                    double scaleValue = Math.Pow(2d, remainder);
                    {
                        if (mapControl.MapScaleTransform == null)
                        {
                            mapControl.MapScaleTransform = mapControl._lastScaleTransform;
                        }

                        if (zoomMode == ZoomMode.XY || zoomMode == ZoomMode.X)
                        {
                            mapControl.MapScaleTransform.ScaleX = scaleValue;
                            mapControl._core.ScaleX = 1 / scaleValue;
                            mapControl.MapScaleTransform.CenterX = mapControl.ActualWidth / 2;
                        }

                        if (zoomMode == ZoomMode.XY || zoomMode == ZoomMode.Y)
                        {
                            mapControl.MapScaleTransform.ScaleY = scaleValue;
                            mapControl._core.ScaleY = 1 / scaleValue;
                            mapControl.MapScaleTransform.CenterY = mapControl.ActualHeight / 2;
                        }
                    }

                    mapControl._isApplyingZoom = true;
                    try
                    {
                        mapControl._core.Zoom = Convert.ToInt32(scaleDown ? Math.Ceiling(value) : value - remainder);
                    }
                    finally
                    {
                        mapControl._isApplyingZoom = false;
                    }
                }
                else
                {
                    mapControl.MapScaleTransform = null;

                    if (zoomMode == ZoomMode.XY || zoomMode == ZoomMode.X)
                        mapControl._core.ScaleX = 1;

                    if (zoomMode == ZoomMode.XY || zoomMode == ZoomMode.Y)
                        mapControl._core.ScaleY = 1;

                    mapControl._isApplyingZoom = true;
                    try
                    {
                        mapControl._core.Zoom = (int)Math.Floor(value);
                    }
                    finally
                    {
                        mapControl._isApplyingZoom = false;
                    }
                }

                if (mapControl.IsLoaded)
                {
                    mapControl.ForceUpdateOverlays();
                    mapControl.InvalidateVisual(true);
                }
            }
        }

        /// <summary>
        ///     Zooms the property changed.
        /// </summary>
        /// <param name="d">The d.</param>
        /// <param name="e">The <see cref="DependencyPropertyChangedEventArgs" /> instance containing the event data.</param>
        private static void ZoomPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ZoomPropertyChanged((GMapControl)d, (double)e.NewValue, (double)e.OldValue, ZoomMode.XY);
        }

        /// <summary>
        ///     Handles the <see cref="E:MultiTouchEnabledChanged" /> event.
        /// </summary>
        /// <param name="d">The d.</param>
        /// <param name="e">The <see cref="DependencyPropertyChangedEventArgs" /> instance containing the event data.</param>
        private static void OnMultiTouchEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var mapControl = (GMapControl)d;
            mapControl.MultiTouchEnabled = (bool)e.NewValue;
            mapControl.IsManipulationEnabled = (bool)e.NewValue;
        }

        /// <summary>
        ///     is touch control enabled
        /// </summary>
        /// <value><c>true</c> if [touch enabled]; otherwise, <c>false</c>.</value>
        [Obsolete("Touch Enabled is deprecated, please use MultiTouchEnabled")]
        public bool TouchEnabled
        {
            get { return (bool)GetValue(TouchEnabledProperty); }
            set { SetValue(TouchEnabledProperty, value); }
        }

        readonly ScaleTransform _lastScaleTransform = new ScaleTransform();

        private ScaleModes _scaleMode = ScaleModes.Integer;

        readonly Core _core = new Core();
        private bool _isApplyingZoom;
        private bool _dragFrameInvalidationPending;

        private readonly List<DrawTile> _tileDrawingSnapshot = new();
        private readonly List<TileRenderImage> _tileRenderImages = new();
        private readonly List<TileRenderCell> _tileRenderCells = new();
        private readonly Dictionary<Rect, RectangleGeometry>
            _tileClipGeometryCache = new();

        private readonly struct TileRenderImage
        {
            public TileRenderImage(
                ImageSource image,
                Rect drawRect,
                Rect clipRect,
                bool requiresClip,
                bool drawSelectionFill)
            {
                Image = image;
                DrawRect = drawRect;
                ClipRect = clipRect;
                RequiresClip = requiresClip;
                DrawSelectionFill = drawSelectionFill;
            }

            public ImageSource Image { get; }
            public Rect DrawRect { get; }
            public Rect ClipRect { get; }
            public bool RequiresClip { get; }
            public bool DrawSelectionFill { get; }
        }

        private readonly struct TileRenderCell
        {
            public TileRenderCell(
                Rect rect,
                bool found,
                Exception failure)
            {
                Rect = rect;
                Found = found;
                Failure = failure;
            }

            public Rect Rect { get; }
            public bool Found { get; }
            public Exception Failure { get; }
        }

        PointLatLng _selectionStart;
        PointLatLng _selectionEnd;
        readonly Typeface _tileTypeface = new Typeface("Arial");
        bool _showTileGridLines;

        private FormattedText _copyright;

        /// <summary>
        ///     enables filling empty tiles using lower level images
        /// </summary>
        [Browsable(false)]
        public bool FillEmptyTiles
        {
            get { return _core.FillEmptyTiles; }
            set { _core.FillEmptyTiles = value; }
        }

        /// <summary>
        ///     max zoom
        /// </summary>
        [Category("GMap.NET")]
        [Description("maximum zoom level of map")]
        public int MaxZoom
        {
            get { return _core.MaxZoom; }
            set { _core.MaxZoom = value; }
        }

        /// <summary>
        ///     min zoom
        /// </summary>
        [Category("GMap.NET")]
        [Description("minimum zoom level of map")]
        public int MinZoom
        {
            get { return _core.MinZoom; }
            set { _core.MinZoom = value; }
        }

        /// <summary>
        ///     pen for empty tile borders
        /// </summary>
        public Pen EmptyTileBorders = new Pen(Brushes.White, 1.0);

        /// <summary>
        ///     pen for Selection
        /// </summary>
        public Pen SelectionPen = new Pen(Brushes.Blue, 2.0);

        /// <summary>
        ///     background of selected area
        /// </summary>
        public Brush SelectedAreaFill =
            new SolidColorBrush(Color.FromArgb(33, Colors.RoyalBlue.R, Colors.RoyalBlue.G, Colors.RoyalBlue.B));

        /// <summary>
        ///     pen for empty tile background
        /// </summary>
        public Brush EmptyTileBrush = Brushes.Navy;

        /// <summary>
        ///     text on empty tiles
        /// </summary>        
        public FormattedText EmptyTileText =
            new FormattedText("We are sorry, but we don't\nhave imagery at this zoom\n     level for this region.",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                16,
                Brushes.Blue);

        /// <summary>
        ///     enable map zoom on mouse wheel
        /// </summary>
        [Category("GMap.NET")]
        [Description("enable map zoom on mouse wheel")]
        public bool MouseWheelZoomEnabled
        {
            get { return _core.MouseWheelZoomEnabled; }
            set { _core.MouseWheelZoomEnabled = value; }
        }


        private double _mouseWheelZoomStep = 0.5;

        /// <summary>
        /// Zoom step for one mouse wheel notch.
        /// 1.0 keeps the original behavior.
        /// 0.5 / 0.25 make zoom smoother.
        /// </summary>
        [Category("GMap.NET")]
        [Description("zoom step for one mouse wheel notch")]
        public double MouseWheelZoomStep
        {
            get { return _mouseWheelZoomStep; }
            set { _mouseWheelZoomStep = Math.Max(0.05, value); }
        }

        /// <summary>
        ///     map dragg button
        /// </summary>
        [Category("GMap.NET")] public MouseButton DragButton = MouseButton.Left; // changed: drag map with left mouse button

        /// <summary>
        ///     use circle for selection
        /// </summary>
        public bool SelectionUseCircle = false;

        /// <summary>
        ///     shows tile gridlines
        /// </summary>
        [Category("GMap.NET")]
        public bool ShowTileGridLines
        {
            get { return _showTileGridLines; }
            set
            {
                _showTileGridLines = value;
                InvalidateVisual();
            }
        }

        /// <summary>
        ///     retry count to get tile
        /// </summary>
        [Browsable(false)]
        public int RetryLoadTile
        {
            get { return _core.RetryLoadTile; }
            set { _core.RetryLoadTile = value; }
        }

        /// <summary>
        ///     how many levels of tiles are staying decompresed in memory
        /// </summary>
        [Browsable(false)]
        public int LevelsKeepInMemory
        {
            get { return _core.LevelsKeepInMemory; }
            set { _core.LevelsKeepInMemory = value; }
        }

        /// <summary>
        ///     current selected area in map
        /// </summary>
        private RectLatLng _selectedArea;

        [Browsable(false)]
        public RectLatLng SelectedArea
        {
            get { return _selectedArea; }
            set
            {
                _selectedArea = value;
                InvalidateVisual();
            }
        }

        /// <summary>
        ///     map boundaries
        /// </summary>
        private RectLatLng? _boundsOfMap = null;

        public RectLatLng? BoundsOfMap
        {
            get { return _boundsOfMap; }
            set
            {
                _boundsOfMap = value;

            }
        }

        /// <summary>
        ///     occurs when mouse selection is changed
        /// </summary>
        public event SelectionChange OnSelectionChange;

        private static readonly DependencyPropertyKey MarkersKey
    = DependencyProperty.RegisterReadOnly("Markers",
        typeof(ObservableCollection<GMapMarker>),
        typeof(GMapControl),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.None)); // ← без callback

        public static readonly DependencyProperty MarkersProperty = MarkersKey.DependencyProperty;

        /// <summary>
        ///     List of markers
        /// </summary>
        public ObservableCollection<GMapMarker> Markers
        {
            get { return (ObservableCollection<GMapMarker>)GetValue(MarkersProperty); }
            private set { SetValue(MarkersKey, value); }
        }
          
        /// <summary>
        ///     current markers overlay offset
        /// </summary>
        internal readonly TranslateTransform MapTranslateTransform = new TranslateTransform();

        internal readonly TranslateTransform MapOverlayTranslateTransform = new TranslateTransform();

        internal ScaleTransform MapScaleTransform = new ScaleTransform();

        protected bool DesignModeInConstruct
        {
            get { return DesignerProperties.GetIsInDesignMode(this); }
        }

        Canvas _mapCanvas;

        /// <summary>
        ///     markers overlay
        /// </summary>
        internal Canvas MapCanvas
        {
            get
            {
                if (_mapCanvas == null)
                {
                    if (VisualChildrenCount > 0)
                    {
                        var border = VisualTreeHelper.GetChild(this, 0) as Border;
                        var items = border.Child as ItemsPresenter;
                        var target = VisualTreeHelper.GetChild(items, 0);
                        _mapCanvas = target as Canvas;

                        // Markers/routes use coordinates already projected by
                        // FromLatLngToLocal. During fractional zoom their pan
                        // offset must be scaled exactly like the tile layer.
                        _mapCanvas.RenderTransform = MapOverlayTranslateTransform;
                    }
                }

                return _mapCanvas;
            }
        }

        public GMaps Manager
        {
            get { return GMaps.Instance; }
        }

        static DataTemplate _dataTemplateInstance;
        static ItemsPanelTemplate _itemsPanelTemplateInstance;
        static Style _styleInstance;

        public GMapControl()
        {
            if (!DesignModeInConstruct)
            {
                #region -- templates --

                #region -- xaml --

                //  <ItemsControl Name="figures">
                //    <ItemsControl.ItemTemplate>
                //        <DataTemplate>
                //            <ContentPresenter Content="{Binding Path=Shape}" />
                //        </DataTemplate>
                //    </ItemsControl.ItemTemplate>
                //    <ItemsControl.ItemsPanel>
                //        <ItemsPanelTemplate>
                //            <Canvas />
                //        </ItemsPanelTemplate>
                //    </ItemsControl.ItemsPanel>
                //    <ItemsControl.ItemContainerStyle>
                //        <Style>
                //            <Setter Property="Canvas.Left" Value="{Binding Path=LocalPositionX}"/>
                //            <Setter Property="Canvas.Top" Value="{Binding Path=LocalPositionY}"/>
                //        </Style>
                //    </ItemsControl.ItemContainerStyle>
                //</ItemsControl> 

                #endregion

                if (_dataTemplateInstance == null)
                {
                    _dataTemplateInstance = new DataTemplate(typeof(GMapMarker));
                    {
                        var fef = new FrameworkElementFactory(typeof(ContentPresenter));
                        fef.SetBinding(ContentPresenter.ContentProperty, new Binding("Shape"));
                        _dataTemplateInstance.VisualTree = fef;
                    }
                }

                ItemTemplate = _dataTemplateInstance;

                if (_itemsPanelTemplateInstance == null)
                {
                    var factoryPanel = new FrameworkElementFactory(typeof(Canvas));
                    {
                        factoryPanel.SetValue(Panel.IsItemsHostProperty, true);

                        _itemsPanelTemplateInstance = new ItemsPanelTemplate();
                        {
                            _itemsPanelTemplateInstance.VisualTree = factoryPanel;
                        }
                    }
                }

                ItemsPanel = _itemsPanelTemplateInstance;

                if (_styleInstance == null)
                {
                    _styleInstance = new Style();
                    {
                        _styleInstance.Setters.Add(new Setter(Canvas.LeftProperty, new Binding("LocalPositionX")));
                        _styleInstance.Setters.Add(new Setter(Canvas.TopProperty, new Binding("LocalPositionY")));
                        _styleInstance.Setters.Add(new Setter(Panel.ZIndexProperty, new Binding("ZIndex")));
                    }
                }

                ItemContainerStyle = _styleInstance;

                #endregion

                Markers = new ObservableCollection<GMapMarker>();

                ClipToBounds = true;
                SnapsToDevicePixels = true;

                _core.SystemType = "WindowsPresentation";

                _core.RenderMode = RenderMode.WPF;

                _core.OnMapZoomChanged += CoreOnMapZoomChanged;
                _core.OnCurrentPositionChanged += CoreOnCurrentPositionChanged;
                Loaded += GMapControl_Loaded;
                Dispatcher.ShutdownStarted += Dispatcher_ShutdownStarted;
                SizeChanged += GMapControl_SizeChanged;

                // by default its internal property, feel free to use your own
                if (ItemsSource == null)
                    ItemsSource = Markers;

                _core.Zoom = (int)(double)ZoomProperty.DefaultMetadata.DefaultValue;
            }
        }

        private void CoreOnCurrentPositionChanged(PointLatLng pointLatLng)
        {
            if (_isUpdatingPositionFromCore) return; // додатковий захист

            _isUpdatingPositionFromCore = true;
            try
            {
                Position = pointLatLng;
            }
            finally
            {
                _isUpdatingPositionFromCore = false;
            }
        }

        private void CoreOnMapZoomChanged()
        {
            // ZoomPropertyChanged performs the one required regeneration after
            // changing the scale. Keep this handler for zoom changes initiated
            // internally by GMap.NET, but avoid the duplicate regeneration for
            // the normal property-change path.
            if (!_isApplyingZoom)
            {
                ForceUpdateOverlays();
            }
        }

        static GMapControl()
        {
            GMapImageProxy.Enable();
            GMaps.Instance.SQLitePing();
        }

        void InvalidatorEngage(object sender, ProgressChangedEventArgs e)
        {
            base.InvalidateVisual();
        }

        /// <summary>
        ///     enque built-in thread safe invalidation
        /// </summary>
        public new void InvalidateVisual()
        {
            if (_core.Refresh != null)
                _core.Refresh.Set();
        }

        /// <summary>
        ///     Invalidates the rendering of the element, and forces a complete new layout
        ///     pass. System.Windows.UIElement.OnRender(System.Windows.Media.DrawingContext)
        ///     is called after the layout cycle is completed. If not forced enques built-in thread safe invalidation
        /// </summary>
        /// <param name="forced"></param>
        public void InvalidateVisual(bool forced)
        {
            if (forced)
            {
                lock (_core.InvalidationLock)
                {
                    _core.LastInvalidation = DateTime.Now;
                }

                base.InvalidateVisual();
            }
            else
            {
                InvalidateVisual();
            }
        }

        protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            base.OnItemsChanged(e);

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                ForceUpdateOverlays(e.NewItems);
            }
            else
            {
                InvalidateVisual();
            }
        }

        public void InitializeForBackgroundRendering(int width, int height)
        {
            Width = width;
            Height = height;

            if (!_core.IsStarted)
            {
                if (_lazyEvents)
                {
                    _lazyEvents = false;

                    if (_lazySetZoomToFitRect.HasValue)
                    {
                        SetZoomToFitRect(_lazySetZoomToFitRect.Value);
                        _lazySetZoomToFitRect = null;
                    }
                }

                _core.OnMapOpen();
                ForceUpdateOverlays();
            }

            _core.OnMapSizeChanged(width, height);

            if (_core.IsStarted)
            {
                if (IsRotated)
                {
                    UpdateRotationMatrix();
                }

                ForceUpdateOverlays();
            }

            UpdateLayout();
        }

        /// <summary>
        ///     inits core system
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void GMapControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_core.IsStarted)
            {
                if (_lazyEvents)
                {
                    _lazyEvents = false;

                    if (_lazySetZoomToFitRect.HasValue)
                    {
                        SetZoomToFitRect(_lazySetZoomToFitRect.Value);
                        _lazySetZoomToFitRect = null;
                    }
                }

                _core.OnMapOpen().ProgressChanged += InvalidatorEngage;

                ReapplyCurrentZoomTransform();

                ForceUpdateOverlays();

                if (Application.Current != null)
                {
                    _loadedApp = Application.Current;

                    _loadedApp.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,
                        new Action(delegate ()
                            {
                                _loadedApp.SessionEnding += Current_SessionEnding;
                            }
                        ));
                }
            }
        }

        Application _loadedApp;

        void Current_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            GMaps.Instance.CancelTileCaching();
        }

        void Dispatcher_ShutdownStarted(object sender, EventArgs e)
        {
            Dispose();
        }

        /// <summary>
        ///     recalculates size
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// 
        private bool _isApplyingBoundsAfterSizeChanged;

        void GMapControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var constraint = e.NewSize;

            _core.OnMapSizeChanged(
                (int)constraint.Width,
                (int)constraint.Height
            );

            if (MapScaleTransform != null)
            {
                MapScaleTransform.CenterX = constraint.Width / 2.0;
                MapScaleTransform.CenterY = constraint.Height / 2.0;
            }

            ReapplyCurrentZoomTransform();

            if (_core.IsStarted)
            {
                if (IsRotated)
                {
                    UpdateRotationMatrix();
                }

                ForceUpdateOverlays();

                if (!IsViewportResizeBoundsCorrectionSuspended &&
      BoundsOfMap.HasValue &&
      !BoundsOfMap.Value.IsEmpty)
                {
                    ApplyBoundsAfterViewportSizeChanged();
                }
            }
        }

        private void ApplyBoundsAfterViewportSizeChanged()
        {
            if (_isApplyingBoundsAfterSizeChanged)
            {
                return;
            }

            if (!_core.IsStarted ||
                !BoundsOfMap.HasValue ||
                BoundsOfMap.Value.IsEmpty)
            {
                return;
            }

            _isApplyingBoundsAfterSizeChanged = true;

            try
            {
                var bounds = BoundsOfMap.Value;

                FitZoomToBoundsIfNeeded(bounds);

                ForceUpdateOverlays();
                InvalidateVisual(true);

                ClampVisibleBoundsToAllowedBounds();

                ForceUpdateOverlays();
                InvalidateVisual(true);
            }
            finally
            {
                _isApplyingBoundsAfterSizeChanged = false;
            }
        }

        void ForceUpdateOverlays()
        {
            ForceUpdateOverlays(ItemsSource);
        }

        /// <summary>
        ///     regenerates shape of route
        /// </summary>
        public virtual void RegenerateShape(IShapable s)
        {
            var marker = s as GMapMarker;

            if (s.Points != null && s.Points.Count > 1)
            {
                marker.Position = s.Points[0];
                var localPath = new List<Point>(s.Points.Count);
                var offset = FromLatLngToLocal(s.Points[0]);

                foreach (var i in s.Points)
                {
                    var p = FromLatLngToLocal(i);
                    localPath.Add(new Point(p.X - offset.X, p.Y - offset.Y));
                }

                // RegenerateShape replaces Data on an existing Path. Creating a
                // temporary blur effect here only allocates an object which is
                // immediately discarded and makes zoom heavier.
                var shape = s.CreatePath(localPath, false);

                if (marker.Shape is System.Windows.Shapes.Path)
                {
                    (marker.Shape as System.Windows.Shapes.Path).Data = shape.Data;
                }
                else
                {
                    marker.Shape = shape;
                }
            }
            else
            {
                marker.Shape = null;
            }
        }

        void ForceUpdateOverlays(System.Collections.IEnumerable items)
        {
            using (Dispatcher.DisableProcessing())
            {
                UpdateMarkersOffset();

                foreach (GMapMarker i in items)
                {
                    if (i != null)
                    {
                        i.ForceUpdateLocalPosition(this);

                        if (i is IShapable)
                            RegenerateShape(i as IShapable);
                    }
                }
            }

            InvalidateVisual();
        }

        /// <summary>
        ///     updates markers overlay offset
        /// </summary>
        void UpdateMarkersOffset()
        {
            if (MapCanvas != null)
            {
                if (MapScaleTransform != null)
                {
                    var tp = MapScaleTransform.Transform(new Point(_core.RenderOffset.X,
                        _core.RenderOffset.Y));
                    MapOverlayTranslateTransform.X = tp.X;
                    MapOverlayTranslateTransform.Y = tp.Y;

                    // map is scaled already
                    MapTranslateTransform.X = _core.RenderOffset.X;
                    MapTranslateTransform.Y = _core.RenderOffset.Y;
                }
                else
                {
                    MapTranslateTransform.X = _core.RenderOffset.X;
                    MapTranslateTransform.Y = _core.RenderOffset.Y;

                    MapOverlayTranslateTransform.X = MapTranslateTransform.X;
                    MapOverlayTranslateTransform.Y = MapTranslateTransform.Y;
                }
            }
        }

        private void RequestDragFrame()
        {
            if (_disposed || _dragFrameInvalidationPending)
            {
                return;
            }

            _dragFrameInvalidationPending = true;
            CompositionTarget.Rendering += CompositionTargetOnDragRendering;
        }

        private void CompositionTargetOnDragRendering(
            object sender,
            EventArgs e)
        {
            CompositionTarget.Rendering -= CompositionTargetOnDragRendering;
            _dragFrameInvalidationPending = false;

            if (!_disposed)
            {
                InvalidateVisual(true);
            }
        }

        private void UpdateOverlaysAfterMapOffset()
        {
            if (IsRotated)
            {
                ForceUpdateOverlays();
            }
            else
            {
                UpdateMarkersOffset();
            }
        }

        public Brush EmptyMapBackground = Brushes.WhiteSmoke;

        /// <summary>
        ///     render map in WPF
        /// </summary>
        /// <param name="g"></param>
        void DrawMap(DrawingContext g)
        {
            if (MapProvider == EmptyProvider.Instance || MapProvider == null)
            {
                return;
            }

            // Copy the prepared window under a short lock. The background
            // prefetch worker can then publish a newer window without waiting
            // for WPF to finish drawing every image.
            _tileDrawingSnapshot.Clear();
            _core.TileDrawingListLock.AcquireReaderLock();
            try
            {
                _tileDrawingSnapshot.AddRange(_core.TileDrawingList);
            }
            finally
            {
                _core.TileDrawingListLock.ReleaseReaderLock();
            }

            var matrix = _core.Matrix;
            if (matrix == null)
            {
                return;
            }

            _tileRenderImages.Clear();
            _tileRenderCells.Clear();

            // Snapshot frozen ImageSource references under the matrix lock and
            // release it before issuing WPF draw commands. Tile loaders no
            // longer contend with the UI for the duration of the whole frame.
            matrix.EnterReadLock();
            try
            {
                foreach (var tilePoint in _tileDrawingSnapshot)
                {
                    var tileRect = new Rect(
                        tilePoint.PosPixel.X - _core.CompensationOffset.X,
                        tilePoint.PosPixel.Y - _core.CompensationOffset.Y,
                        _core.TileRect.Width,
                        _core.TileRect.Height);

                    if (!IsTileVisible(tileRect))
                    {
                        continue;
                    }

                    var imageRect = new Rect(
                        tileRect.X + 0.6,
                        tileRect.Y + 0.6,
                        tileRect.Width + 0.6,
                        tileRect.Height + 0.6);
                    var found = false;
                    var tile = matrix.GetTileWithNoLock(
                        _core.Zoom,
                        tilePoint.PosXY);

                    if (tile.NotEmpty)
                    {
                        foreach (GMapImage image in tile.Overlays)
                        {
                            var imageSource = image?.Img;
                            if (imageSource == null)
                            {
                                continue;
                            }

                            found = true;

                            if (!image.IsParent)
                            {
                                _tileRenderImages.Add(new TileRenderImage(
                                    imageSource,
                                    imageRect,
                                    Rect.Empty,
                                    requiresClip: false,
                                    drawSelectionFill: false));
                                continue;
                            }

                            var parentImageRect = new Rect(
                                tileRect.X - tileRect.Width * image.Xoff + 0.6,
                                tileRect.Y - tileRect.Height * image.Yoff + 0.6,
                                tileRect.Width * image.Ix + 0.6,
                                tileRect.Height * image.Ix + 0.6);

                            _tileRenderImages.Add(new TileRenderImage(
                                imageSource,
                                parentImageRect,
                                imageRect,
                                requiresClip: true,
                                drawSelectionFill: false));
                        }
                    }
                    else if (FillEmptyTiles &&
                             MapProvider.Projection is MercatorProjection)
                    {
                        var parentTile = Tile.Empty;
                        long parentScale = 0;

                        for (var zoomOffset = 1;
                             !parentTile.NotEmpty &&
                             zoomOffset < _core.Zoom &&
                             zoomOffset <= LevelsKeepInMemory;
                             zoomOffset++)
                        {
                            parentScale = 1L << zoomOffset;
                            parentTile = matrix.GetTileWithNoLock(
                                _core.Zoom - zoomOffset,
                                new GPoint(
                                    tilePoint.PosXY.X / parentScale,
                                    tilePoint.PosXY.Y / parentScale));
                        }

                        if (parentTile.NotEmpty)
                        {
                            var xOffset = Math.Abs(
                                tilePoint.PosXY.X -
                                parentTile.Pos.X * parentScale);
                            var yOffset = Math.Abs(
                                tilePoint.PosXY.Y -
                                parentTile.Pos.Y * parentScale);
                            var parentImageRect = new Rect(
                                tileRect.X - tileRect.Width * xOffset + 0.6,
                                tileRect.Y - tileRect.Height * yOffset + 0.6,
                                tileRect.Width * parentScale + 0.6,
                                tileRect.Height * parentScale + 0.6);

                            foreach (GMapImage image in parentTile.Overlays)
                            {
                                var imageSource = image?.Img;
                                if (imageSource != null && !image.IsParent)
                                {
                                    found = true;
                                    _tileRenderImages.Add(
                                        new TileRenderImage(
                                            imageSource,
                                            parentImageRect,
                                            imageRect,
                                            requiresClip: true,
                                            drawSelectionFill: true));
                                }
                            }
                        }
                    }

                    Exception failure = null;
                    if (!found && _core.FailedLoads != null)
                    {
                        lock (_core.FailedLoads)
                        {
                            _core.FailedLoads.TryGetValue(
                                new LoadTask(tilePoint.PosXY, _core.Zoom),
                                out failure);
                        }
                    }

                    _tileRenderCells.Add(new TileRenderCell(
                        tileRect,
                        found,
                        failure));
                }
            }
            finally
            {
                matrix.LeaveReadLock();
            }

            foreach (var image in _tileRenderImages)
            {
                if (image.RequiresClip)
                {
                    var clip = GetTileClipGeometry(image.ClipRect);
                    g.PushClip(clip);
                    g.DrawImage(image.Image, image.DrawRect);

                    if (image.DrawSelectionFill)
                    {
                        g.DrawRectangle(
                            SelectedAreaFill,
                            null,
                            image.ClipRect);
                    }

                    g.Pop();
                }
                else
                {
                    g.DrawImage(image.Image, image.DrawRect);
                }
            }

            foreach (var cell in _tileRenderCells)
            {
                if (!cell.Found && cell.Failure != null)
                {
                    g.DrawRectangle(
                        EmptyTileBrush,
                        EmptyTileBorders,
                        cell.Rect);

                    var tileText = new FormattedText(
                        "Exception: " + cell.Failure.Message,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        _tileTypeface,
                        14,
                        Brushes.Red);

                    tileText.MaxTextWidth = cell.Rect.Width - 11;

                    g.DrawText(
                        tileText,
                        new Point(cell.Rect.X + 11, cell.Rect.Y + 11));
                    g.DrawText(
                        EmptyTileText,
                        new Point(
                            cell.Rect.X + cell.Rect.Width / 2 -
                            EmptyTileText.Width / 2,
                            cell.Rect.Y + cell.Rect.Height / 2 -
                            EmptyTileText.Height / 2));
                }

                if (ShowTileGridLines)
                {
                    g.DrawRectangle(
                        null,
                        EmptyTileBorders,
                        cell.Rect);
                }
            }
        }

        private bool IsTileVisible(Rect tileRect)
        {
            if (IsRotated)
            {
                return true;
            }

            var screenRect = tileRect;
            screenRect.Offset(
                MapTranslateTransform.X,
                MapTranslateTransform.Y);

            if (MapScaleTransform != null)
            {
                screenRect = MapScaleTransform.TransformBounds(screenRect);
            }

            return screenRect.IntersectsWith(new Rect(
                0,
                0,
                Math.Max(0, ActualWidth),
                Math.Max(0, ActualHeight)));
        }

        private RectangleGeometry GetTileClipGeometry(Rect clipRect)
        {
            if (_tileClipGeometryCache.TryGetValue(
                    clipRect,
                    out var geometry))
            {
                return geometry;
            }

            if (_tileClipGeometryCache.Count >= 512)
            {
                _tileClipGeometryCache.Clear();
            }

            geometry = new RectangleGeometry(clipRect);
            geometry.Freeze();
            _tileClipGeometryCache[clipRect] = geometry;
            return geometry;
        }

        /// <summary>
        ///     gets image of the current view
        /// </summary>
        /// <returns></returns>
        public ImageSource ToImageSource()
        {
            FrameworkElement obj = this;

            // Save current canvas transform
            var transform = obj.LayoutTransform;
            obj.LayoutTransform = null;

            // fix margin offset as well
            var margin = obj.Margin;
            obj.Margin = new Thickness(0,
                0,
                margin.Right - margin.Left,
                margin.Bottom - margin.Top);

            // Get the size of canvas
            var size = new Size(obj.ActualWidth, obj.ActualHeight);

            // force control to Update
            obj.Measure(size);
            obj.Arrange(new Rect(size));

            var bmp = new RenderTargetBitmap(
                (int)size.Width,
                (int)size.Height,
                96,
                96,
                PixelFormats.Pbgra32);

            bmp.Render(obj);

            if (bmp.CanFreeze)
            {
                bmp.Freeze();
            }

            // return values as they were before
            obj.LayoutTransform = transform;
            obj.Margin = margin;

            return bmp;
        }

        /// <summary>
        ///     sets zoom to max to fit rect
        /// </summary>
        /// <param name="rect">area</param>
        /// <returns></returns>
        public bool SetZoomToFitRect(RectLatLng rect)
        {
            if (_lazyEvents)
            {
                _lazySetZoomToFitRect = rect;
            }
            else
            {
                int maxZoom = _core.GetMaxZoomToFitRect(rect);
                if (maxZoom > 0)
                {
                    var center =
                        new PointLatLng(rect.Lat - rect.HeightLat / 2, rect.Lng + rect.WidthLng / 2);
                    Position = center;

                    if (maxZoom > MaxZoom)
                    {
                        maxZoom = MaxZoom;
                    }

                    if (_core.Zoom != maxZoom)
                    {
                        Zoom = maxZoom;
                    }

                    return true;
                }
            }

            return false;
        }

        RectLatLng? _lazySetZoomToFitRect;
        bool _lazyEvents = true;

        /// <summary>
        ///     sets to max zoom to fit all markers and centers them in map
        /// </summary>
        /// <param name="zIndex">z index or null to check all</param>
        /// <returns></returns>
        public bool ZoomAndCenterMarkers(int? zIndex)
        {
            var rect = GetRectOfAllMarkers(zIndex);
            if (rect.HasValue)
            {
                return SetZoomToFitRect(rect.Value);
            }

            return false;
        }

        /// <summary>
        ///     gets rectangle with all objects inside
        /// </summary>
        /// <param name="zIndex">z index or null to check all</param>
        /// <returns></returns>
        public RectLatLng? GetRectOfAllMarkers(int? zIndex)
        {
            RectLatLng? ret = null;

            double left = double.MaxValue;
            double top = double.MinValue;
            double right = double.MinValue;
            double bottom = double.MaxValue;
            IEnumerable<GMapMarker> overlays;

            overlays = zIndex.HasValue
                ? ItemsSource.Cast<GMapMarker>().Where(p => p != null && p.ZIndex == zIndex)
                : ItemsSource.Cast<GMapMarker>();

            if (overlays != null)
            {
                foreach (var m in overlays)
                {
                    if (m.Shape != null && m.Shape.Visibility == Visibility.Visible)
                    {
                        // left
                        if (m.Position.Lng < left)
                        {
                            left = m.Position.Lng;
                        }

                        // top
                        if (m.Position.Lat > top)
                        {
                            top = m.Position.Lat;
                        }

                        // right
                        if (m.Position.Lng > right)
                        {
                            right = m.Position.Lng;
                        }

                        // bottom
                        if (m.Position.Lat < bottom)
                        {
                            bottom = m.Position.Lat;
                        }
                    }
                }
            }

            if (left != double.MaxValue && right != double.MinValue && top != double.MinValue &&
                bottom != double.MaxValue)
            {
                ret = RectLatLng.FromLTRB(left, top, right, bottom);
            }

            return ret;
        }

        /// <summary>
        ///     offset position in pixels
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <summary>
        /// Виконує безпосереднє піксельне зміщення карти без перевірки BoundsOfMap.
        ///
        /// Цей метод використовується внутрішніми корекціями viewport.
        /// Він не повинен викликати ClampVisibleBoundsToAllowedBounds(),
        /// інакше виникне рекурсія:
        /// clamp → offset → clamp → offset.
        /// </summary>
        private void ApplyRawOffset(int x, int y)
        {
            if (!IsLoaded)
            {
                return;
            }

            if (IsRotated)
            {
                var angleRad = Bearing * Math.PI / 180.0;
                var cos = Math.Cos(angleRad);
                var sin = Math.Sin(angleRad);

                var dx = x * cos - y * sin;
                var dy = x * sin + y * cos;

                _core.DragOffset(
                    new GPoint(
                        (long)dx,
                        (long)dy
                    )
                );

                ForceUpdateOverlays();
                return;
            }

            _core.DragOffset(
                new GPoint(
                    x,
                    y
                )
            );

            UpdateMarkersOffset();
            InvalidateVisual(true);
        }

        /// <summary>
        /// Публічно зміщує карту в пікселях.
        ///
        /// Після звичайного програмного зміщення, наприклад через WASD,
        /// фактичний viewport повертається всередину BoundsOfMap.
        /// </summary>
        public void Offset(int x, int y)
        {
            if (!IsLoaded)
            {
                return;
            }

            ApplyRawOffset(x, y);

            if (BoundsOfMap.HasValue &&
                !BoundsOfMap.Value.IsEmpty)
            {
                ClampVisibleBoundsToAllowedBounds();
            }
        }

        readonly RotateTransform _rotationMatrix = new RotateTransform();
        GeneralTransform _rotationMatrixInvert = new RotateTransform();

        /// <summary>
        ///     updates rotation matrix
        /// </summary>
        void UpdateRotationMatrix()
        {
            var center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);

            _rotationMatrix.Angle = -Bearing;
            _rotationMatrix.CenterY = center.Y;
            _rotationMatrix.CenterX = center.X;

            _rotationMatrixInvert = _rotationMatrix.Inverse;
        }

        /// <summary>
        ///     returs true if map bearing is not zero
        /// </summary>
        public bool IsRotated
        {
            get { return _core.IsRotated; }
        }

        /// <summary>
        ///     bearing for rotation of the map
        /// </summary>
        [Category("GMap.NET")]
        public float Bearing
        {
            get { return _core.Bearing; }
            set
            {
                if (_core.Bearing != value)
                {
                    bool resize = _core.Bearing == 0;
                    _core.Bearing = value;

                    UpdateRotationMatrix();

                    if (value != 0 && value % 360 != 0)
                    {
                        _core.IsRotated = true;

                        if (_core.TileRectBearing.Size == _core.TileRect.Size)
                        {
                            _core.TileRectBearing = _core.TileRect;
                            _core.TileRectBearing.Inflate(1, 1);
                        }
                    }
                    else
                    {
                        _core.IsRotated = false;
                        _core.TileRectBearing = _core.TileRect;
                    }

                    if (resize)
                    {
                        _core.OnMapSizeChanged((int)ActualWidth, (int)ActualHeight);
                    }

                    if (_core.IsStarted)
                    {
                        ForceUpdateOverlays();
                    }
                }
            }
        }

        /// <summary>
        ///     apply transformation if in rotation mode
        /// </summary>
        Point ApplyRotationInversion(double x, double y)
        {
            var ret = new Point(x, y);

            if (IsRotated)
            {
                ret = _rotationMatrixInvert.Transform(ret);
            }

            return ret;
        }

        #region UserControl Events

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (!_core.IsStarted)
                return;

            drawingContext.DrawRectangle(EmptyMapBackground, null, new Rect(RenderSize));

            if (IsRotated)
            {
                drawingContext.PushTransform(_rotationMatrix);

                if (MapScaleTransform != null)
                {
                    drawingContext.PushTransform(MapScaleTransform);
                    drawingContext.PushTransform(MapTranslateTransform);
                    {
                        DrawMap(drawingContext);
                    }
                    drawingContext.Pop();
                    drawingContext.Pop();
                }
                else
                {
                    drawingContext.PushTransform(MapTranslateTransform);
                    {
                        DrawMap(drawingContext);
                    }
                    drawingContext.Pop();
                }

                drawingContext.Pop();
            }
            else
            {
                if (MapScaleTransform != null)
                {
                    drawingContext.PushTransform(MapScaleTransform);
                    drawingContext.PushTransform(MapTranslateTransform);
                    {
                        DrawMap(drawingContext);
                    }
                    drawingContext.Pop();
                    drawingContext.Pop();
                }
                else
                {
                    drawingContext.PushTransform(MapTranslateTransform);
                    {
                        DrawMap(drawingContext);
                    }
                    drawingContext.Pop();
                }
            }

            // selection
            if (!SelectedArea.IsEmpty)
            {
                var p1 = FromLatLngToLocal(SelectedArea.LocationTopLeft);
                var p2 = FromLatLngToLocal(SelectedArea.LocationRightBottom);

                long x1 = p1.X;
                long y1 = p1.Y;
                long x2 = p2.X;
                long y2 = p2.Y;

                if (SelectionUseCircle)
                {
                    drawingContext.DrawEllipse(SelectedAreaFill,
                        SelectionPen,
                        new Point(x1 + (x2 - x1) / 2, y1 + (y2 - y1) / 2),
                        (x2 - x1) / 2,
                        (y2 - y1) / 2);
                }
                else
                {
                    drawingContext.DrawRoundedRectangle(SelectedAreaFill,
                        SelectionPen,
                        new Rect(x1, y1, x2 - x1, y2 - y1),
                        5,
                        5);
                }
            }

            if (ShowCenter)
            {
                drawingContext.DrawLine(CenterCrossPen,
                    new Point(ActualWidth / 2 - 5, ActualHeight / 2),
                    new Point(ActualWidth / 2 + 5, ActualHeight / 2));
                drawingContext.DrawLine(CenterCrossPen,
                    new Point(ActualWidth / 2, ActualHeight / 2 - 5),
                    new Point(ActualWidth / 2, ActualHeight / 2 + 5));
            }

            if (_renderHelperLine)
            {
                var p = Mouse.GetPosition(this);

                drawingContext.DrawLine(HelperLinePen, new Point(p.X, 0), new Point(p.X, ActualHeight));
                drawingContext.DrawLine(HelperLinePen, new Point(0, p.Y), new Point(ActualWidth, p.Y));
            }

            #region -- copyright --

            if (_copyright != null)
            {
                drawingContext.DrawText(_copyright, new Point(5, ActualHeight - _copyright.Height - 5));
            }

            #endregion

            base.OnRender(drawingContext);
        }

        public Pen CenterCrossPen = new Pen(Brushes.Red, 1);
        public bool ShowCenter = true;

        HelperLineOptions _helperLineOption = HelperLineOptions.DontShow;

        /// <summary>
        ///     draw lines at the mouse pointer position
        /// </summary>
        [Browsable(false)]
        public HelperLineOptions HelperLineOption
        {
            get { return _helperLineOption; }
            set
            {
                _helperLineOption = value;
                _renderHelperLine = _helperLineOption == HelperLineOptions.ShowAlways;
                if (_core.IsStarted)
                {
                    InvalidateVisual();
                }
            }
        }

        public Pen HelperLinePen = new Pen(Brushes.Blue, 1);
        bool _renderHelperLine;

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);

            if (HelperLineOption == HelperLineOptions.ShowOnModifierKey)
            {
                _renderHelperLine = !(e.IsUp && (e.Key == Key.LeftShift || e.SystemKey == Key.LeftAlt));
                if (!_renderHelperLine)
                {
                    InvalidateVisual();
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (HelperLineOption == HelperLineOptions.ShowOnModifierKey)
            {
                _renderHelperLine = e.IsDown && (e.Key == Key.LeftShift || e.SystemKey == Key.LeftAlt);
                if (_renderHelperLine)
                {
                    InvalidateVisual();
                }
            }
        }

        private void PositionChanged(DependencyPropertyChangedEventArgs e)
        {
            if (_isUpdatingPositionFromCore) return;

            _isUpdatingPositionFromCore = true;
            try
            {
                _core.Position = Position;
                if (_core.IsStarted)
                {
                    // Position changes only translate already projected
                    // markers. Their geometry changes when zoom or rotation
                    // changes, not on every pan position update.
                    UpdateMarkersOffset();
                    InvalidateVisual();
                }
            }
            finally
            {
                _isUpdatingPositionFromCore = false;
            }
        }

        /// <summary>
        ///     Reverses MouseWheel zooming direction
        /// </summary>
        public static readonly DependencyProperty InvertedMouseWheelZoomingProperty = DependencyProperty.Register(
            "InvertedMouseWheelZooming",
            typeof(bool),
            typeof(GMapControl),
            new PropertyMetadata(false));

        public bool InvertedMouseWheelZooming
        {
            get { return (bool)GetValue(InvertedMouseWheelZoomingProperty); }
            set { SetValue(InvertedMouseWheelZoomingProperty, value); }
        }

        private double NormalizeZoom(double zoom)
        {
            var clampedZoom = Math.Max(
                MinZoom,
                Math.Min(MaxZoom, zoom)
            );

            return Math.Round(
                clampedZoom,
                2,
                MidpointRounding.AwayFromZero
            );
        }

        private double GetZoomOutTarget()
        {
            var requestedZoom = GetPreviousZoomGridValue();

            if (!BoundsOfMap.HasValue ||
                BoundsOfMap.Value.IsEmpty)
            {
                return requestedZoom;
            }

            var minimumAllowedZoom = GetMinimumAllowedZoomForCurrentView(
                BoundsOfMap.Value
            );

            var debugRequestedZoom = requestedZoom;
            var debugMinimumAllowedZoom = minimumAllowedZoom;
            var debugCurrentZoom = Zoom;

            double targetZoom;

            if (requestedZoom < minimumAllowedZoom)
            {
                var normalizedMinimumZoom = NormalizeBoundsZoom(minimumAllowedZoom);

                // Важливо:
                // wheel-down не має права збільшувати zoom.
                // Якщо мінімальний дозволений zoom вже більший або майже дорівнює поточному,
                // значить ми вже на межі — нічого не робимо.
                if (normalizedMinimumZoom >= Zoom - ZoomOutLimitEpsilon)
                {
                    targetZoom = Zoom;
                }
                else
                {
                    targetZoom = normalizedMinimumZoom;
                }
            }
            else
            {
                targetZoom = requestedZoom;
            }

            return targetZoom;
        }

        private double GetZoomInTarget()
        {
            return GetNextZoomGridValue();
        }

        // Обчислює реальний мінімальний zoom, потрібний для вміщення
        // поточного viewport у робочу область.
        //
        // Важливо: результат тут не обмежується MaxZoom.
        // Це дозволяє виявити ситуацію, коли користувацький MaxZoom
        // занадто малий і bounds фізично неможливо виконати.
        private double CalculateRequiredZoomForBounds(RectLatLng bounds)
        {
            if (bounds.IsEmpty ||
                bounds.WidthLng <= 0 ||
                bounds.HeightLat <= 0)
            {
                return MinZoom;
            }

            var visibleBounds = GetVisibleBoundsFromScreen();

            if (visibleBounds.IsEmpty ||
                visibleBounds.WidthLng <= 0 ||
                visibleBounds.HeightLat <= 0)
            {
                return MinZoom;
            }

            var widthRatio = visibleBounds.WidthLng / bounds.WidthLng;
            var heightRatio = visibleBounds.HeightLat / bounds.HeightLat;

            var requiredRatio = Math.Max(widthRatio, heightRatio);

            if (requiredRatio <= 0)
            {
                return MinZoom;
            }

            var requiredZoom = Zoom + Math.Log(requiredRatio, 2.0);

            return Math.Max(MinZoom, requiredZoom);
        }

        private double GetMinimumAllowedZoomForCurrentView(RectLatLng bounds)
        {
            var requiredZoom = CalculateRequiredZoomForBounds(bounds);

            return NormalizeBoundsZoom(requiredZoom);
        }

        // Повертає найменший цілий MaxZoom, за якого поточний viewport
        // може повністю поміститися всередині BoundsOfMap.
        public int GetMinimumCompatibleMaxZoom()
        {
            if (!BoundsOfMap.HasValue ||
                BoundsOfMap.Value.IsEmpty ||
                ActualWidth <= 0 ||
                ActualHeight <= 0)
            {
                return MinZoom;
            }

            var requiredZoom = CalculateRequiredZoomForBounds(BoundsOfMap.Value);

            if (double.IsNaN(requiredZoom) ||
                double.IsInfinity(requiredZoom))
            {
                return MinZoom;
            }

            return Math.Max(
                MinZoom,
                (int)Math.Ceiling(requiredZoom - 0.000000001)
            );
        }

        private RectLatLng GetVisibleBoundsFromScreen()
        {
            if (ActualWidth <= 0 || ActualHeight <= 0)
            {
                return RectLatLng.Empty;
            }

            var width = Math.Max(1, (int)ActualWidth - 1);
            var height = Math.Max(1, (int)ActualHeight - 1);

            var topLeft = FromLocalToLatLng(0, 0);
            var topRight = FromLocalToLatLng(width, 0);
            var bottomLeft = FromLocalToLatLng(0, height);
            var bottomRight = FromLocalToLatLng(width, height);

            var top = Math.Max(
                Math.Max(topLeft.Lat, topRight.Lat),
                Math.Max(bottomLeft.Lat, bottomRight.Lat)
            );

            var bottom = Math.Min(
                Math.Min(topLeft.Lat, topRight.Lat),
                Math.Min(bottomLeft.Lat, bottomRight.Lat)
            );

            var left = Math.Min(
                Math.Min(topLeft.Lng, topRight.Lng),
                Math.Min(bottomLeft.Lng, bottomRight.Lng)
            );

            var right = Math.Max(
                Math.Max(topLeft.Lng, topRight.Lng),
                Math.Max(bottomLeft.Lng, bottomRight.Lng)
            );

            return RectLatLng.FromLTRB(
                left,
                top,
                right,
                bottom
            );
        }

        private PointLatLng GetVisibleCenterFromScreen()
        {
            var visible = GetVisibleBoundsFromScreen();

            if (visible.IsEmpty ||
                visible.WidthLng <= 0 ||
                visible.HeightLat <= 0)
            {
                return Position;
            }

            return new PointLatLng(
                visible.Bottom + visible.HeightLat / 2.0,
                visible.Left + visible.WidthLng / 2.0
            );
        }

        private void ClampVisibleBoundsToAllowedBounds()
        {
            if (_isClampingVisibleBounds)
            {
                return;
            }

            _isClampingVisibleBounds = true;

            try
            {
                if (!BoundsOfMap.HasValue ||
                BoundsOfMap.Value.IsEmpty ||
                ActualWidth <= 0 ||
                ActualHeight <= 0)
                {
                    return;
                }

                var bounds = BoundsOfMap.Value;

                for (var pass = 0; pass < 4; pass++)
                {
                    var visibleBounds = GetVisibleBoundsFromScreen();

                    if (visibleBounds.IsEmpty ||
                        visibleBounds.WidthLng <= 0 ||
                        visibleBounds.HeightLat <= 0)
                    {
                        return;
                    }

                    var lngPerPixel = visibleBounds.WidthLng / ActualWidth;
                    var latPerPixel = visibleBounds.HeightLat / ActualHeight;

                    if (lngPerPixel <= 0 ||
                        latPerPixel <= 0)
                    {
                        return;
                    }

                    var offsetX = 0.0;
                    var offsetY = 0.0;

                    var visibleCenterLng = visibleBounds.Left + visibleBounds.WidthLng / 2.0;
                    var visibleCenterLat = visibleBounds.Bottom + visibleBounds.HeightLat / 2.0;

                    var boundsCenterLng = bounds.Left + bounds.WidthLng / 2.0;
                    var boundsCenterLat = bounds.Bottom + bounds.HeightLat / 2.0;

                    if (visibleBounds.WidthLng >= bounds.WidthLng)
                    {
                        offsetX = (visibleCenterLng - boundsCenterLng) / lngPerPixel;
                    }
                    else if (visibleBounds.Left < bounds.Left)
                    {
                        offsetX = -(bounds.Left - visibleBounds.Left) / lngPerPixel;
                    }
                    else if (visibleBounds.Right > bounds.Right)
                    {
                        offsetX = (visibleBounds.Right - bounds.Right) / lngPerPixel;
                    }

                    if (visibleBounds.HeightLat >= bounds.HeightLat)
                    {
                        offsetY = (boundsCenterLat - visibleCenterLat) / latPerPixel;
                    }
                    else if (visibleBounds.Top > bounds.Top)
                    {
                        offsetY = -(visibleBounds.Top - bounds.Top) / latPerPixel;
                    }
                    else if (visibleBounds.Bottom < bounds.Bottom)
                    {
                        offsetY = (bounds.Bottom - visibleBounds.Bottom) / latPerPixel;
                    }

                    var roundedOffsetX = (int)Math.Round(offsetX);
                    var roundedOffsetY = (int)Math.Round(offsetY);

                    if (roundedOffsetX == 0 &&
                        roundedOffsetY == 0)
                    {
                        return;
                    }

                    ApplyExternalOffsetAndRebaseDrag(
                        roundedOffsetX,
                        roundedOffsetY
                    );

                    UpdateOverlaysAfterMapOffset();
                    InvalidateVisual(true);
                }
            }
            finally
            {
                _isClampingVisibleBounds = false;
            }
        }

        private void FitZoomToBoundsIfNeeded(RectLatLng bounds)
        {
            if (bounds.IsEmpty)
            {
                return;
            }

            var minimumAllowedZoom = GetMinimumAllowedZoomForCurrentView(bounds);

            if (minimumAllowedZoom > Zoom + 0.0001)
            {
                //Zoom = NormalizeZoom(minimumAllowedZoom);
                Zoom = NormalizeBoundsZoom(minimumAllowedZoom);
            }
        }

        private double GetNextZoomGridValue()
        {
            var step = MouseWheelZoomStep;

            if (step <= 0)
            {
                return NormalizeZoom(Zoom + 1.0);
            }

            var nextZoom = Math.Ceiling(
                (Zoom + 0.000001) / step
            ) * step;

            if (Math.Abs(nextZoom - Zoom) < 0.0001)
            {
                nextZoom += step;
            }

            return NormalizeZoom(nextZoom);
        }

        private double GetPreviousZoomGridValue()
        {
            var step = MouseWheelZoomStep;

            if (step <= 0)
            {
                return NormalizeZoom(Zoom - 1.0);
            }

            var previousZoom = Math.Floor(
                (Zoom - 0.000001) / step
            ) * step;

            if (Math.Abs(previousZoom - Zoom) < 0.0001)
            {
                previousZoom -= step;
            }

            return NormalizeZoom(previousZoom);
        }

        private void CaptureStuckBounds()
        {
            _leftStuckBeforeZoom = false;
            _rightStuckBeforeZoom = false;
            _topStuckBeforeZoom = false;
            _bottomStuckBeforeZoom = false;

            if (!BoundsOfMap.HasValue ||
                BoundsOfMap.Value.IsEmpty ||
                ActualWidth <= 0 ||
                ActualHeight <= 0)
            {
                return;
            }

            var visible = GetVisibleBoundsFromScreen();

            if (visible.IsEmpty ||
                visible.WidthLng <= 0 ||
                visible.HeightLat <= 0)
            {
                return;
            }

            var bounds = BoundsOfMap.Value;

            var lngTolerance = visible.WidthLng / ActualWidth * 4.0;
            var latTolerance = visible.HeightLat / ActualHeight * 4.0;

            _leftStuckBeforeZoom = visible.Left <= bounds.Left + lngTolerance;
            _rightStuckBeforeZoom = visible.Right >= bounds.Right - lngTolerance;

            _topStuckBeforeZoom = visible.Top >= bounds.Top - latTolerance;
            _bottomStuckBeforeZoom = visible.Bottom <= bounds.Bottom + latTolerance;
        }

        public void ApplyBoundsOfMapToViewport()
        {
            ApplyBoundsAfterViewportSizeChanged();
        }

        private double NormalizeBoundsZoom(double zoom)
        {
            var clampedZoom = Math.Max(
                MinZoom,
                Math.Min(MaxZoom, zoom)
            );

            return Math.Ceiling(clampedZoom * 10000.0) / 10000.0;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            if (!MouseWheelZoomEnabled || _core.IsDragging)
            {
                return;
            }

            var wheelDelta = InvertedMouseWheelZooming
                ? -e.Delta
                : e.Delta;

            var isZoomIn = wheelDelta > 0;
            var isZoomOut = wheelDelta < 0;

            if (!isZoomIn && !isZoomOut)
            {
                e.Handled = true;
                return;
            }

            var zoomBefore = Zoom;

            CaptureStuckBounds();

            var hasStuckBounds =
                isZoomOut &&
                (_leftStuckBeforeZoom ||
                 _rightStuckBeforeZoom ||
                 _topStuckBeforeZoom ||
                 _bottomStuckBeforeZoom);

            var targetZoom = isZoomIn
                ? GetZoomInTarget()
                : GetZoomOutTarget();

            var isBoundsLimitedZoomOut = false;

            if (isZoomOut &&
                BoundsOfMap.HasValue &&
                !BoundsOfMap.Value.IsEmpty)
            {
                var minimumAllowedZoom = GetMinimumAllowedZoomForCurrentView(
                    BoundsOfMap.Value
                );

                isBoundsLimitedZoomOut = targetZoom <= minimumAllowedZoom + 0.01;
            }

            var mousePos = e.GetPosition(this);

            if (Math.Abs(targetZoom - Zoom) < 0.0001)
            {
                if (hasStuckBounds)
                {
                    CorrectStuckBoundsByPixelOffset();
                    ClampVisibleBoundsToAllowedBounds();
                }
                else if (isBoundsLimitedZoomOut)
                {
                    ClampVisibleBoundsToAllowedBounds();
                }

                e.Handled = true;
                return;
            }

            var hasActiveBounds =
     BoundsOfMap.HasValue &&
     !BoundsOfMap.Value.IsEmpty;

            // Без робочої області опорною точкою має бути Position.
            // При активній робочій області залишаємо фактичний центр viewport,
            // оскільки його положення могло бути скориговане через RenderOffset.
            var positionBaseBeforeZoom = hasActiveBounds
                ? GetVisibleCenterFromScreen()
                : Position;

            var anchorBeforeZoom = FromLocalToLatLng(
                (int)mousePos.X,
                (int)mousePos.Y
            );

            Zoom = targetZoom;

            if (hasStuckBounds)
            {
                CorrectStuckBoundsByPixelOffset();

                e.Handled = true;
                return;
            }

            if (isBoundsLimitedZoomOut)
            {
                ClampVisibleBoundsToAllowedBounds();
                e.Handled = true;
                return;
            }

            var anchorAfterZoom = FromLocalToLatLng(
                (int)mousePos.X,
                (int)mousePos.Y
            );

            var desiredPosition = new PointLatLng(
     positionBaseBeforeZoom.Lat +
         (anchorBeforeZoom.Lat - anchorAfterZoom.Lat),

     positionBaseBeforeZoom.Lng +
         (anchorBeforeZoom.Lng - anchorAfterZoom.Lng)
 );

            Position = desiredPosition;

            if (hasActiveBounds)
            {
                ClampVisibleBoundsToAllowedBounds();
            }

            e.Handled = true;
        }

        // У WPF GMap дробовий zoom реалізується через MapScaleTransform.
        // Внутрішній Core.BoundsOfMap працює з цілим zoom Core та RenderOffset,
        // тому може блокувати коректне зміщення карти або неправильно визначати
        // фактичну видиму область.
        //
        // Через це обмеження робочої області контролюється на рівні GMapControl
        // за реальною видимою областю екрана. Під час програмної корекції
        // Core.BoundsOfMap тимчасово вимикається, щоб Core не заблокував Offset.
        private void ApplyExternalOffsetAndRebaseDrag(int x, int y)
        {
            var wasCoreDragging = _core.IsDragging;
            var mouseCurrent = _core.MouseCurrent;

            ApplyRawOffset(x, y);

            if (wasCoreDragging)
            {
                //_core.RebaseDragAfterExternalOffset(mouseCurrent);
                _core.RebaseDragAfterExternalOffset(mouseCurrent);

            }
        }

        private void CorrectStuckBoundsByPixelOffset()
        {

            if (!BoundsOfMap.HasValue ||
                BoundsOfMap.Value.IsEmpty ||
                ActualWidth <= 0 ||
                ActualHeight <= 0)
            {
                return;
            }

            var bounds = BoundsOfMap.Value;

            for (var pass = 0; pass < 5; pass++)
            {
                var visible = GetVisibleBoundsFromScreen();

                if (visible.IsEmpty ||
                    visible.WidthLng <= 0 ||
                    visible.HeightLat <= 0)
                {
                    return;
                }

                var lngPerPixel = visible.WidthLng / ActualWidth;
                var latPerPixel = visible.HeightLat / ActualHeight;

                if (lngPerPixel <= 0 ||
                    latPerPixel <= 0)
                {
                    return;
                }

                var offsetX = 0.0;
                var offsetY = 0.0;

                if (_leftStuckBeforeZoom)
                {
                    // Тримаємо Visible.Left рівно біля Bounds.Left
                    offsetX = -(bounds.Left - visible.Left) / lngPerPixel;
                }
                else if (_rightStuckBeforeZoom)
                {
                    // Тримаємо Visible.Right рівно біля Bounds.Right
                    offsetX = (visible.Right - bounds.Right) / lngPerPixel;
                }

                if (_topStuckBeforeZoom)
                {
                    // Тримаємо Visible.Top рівно біля Bounds.Top
                    offsetY = -(visible.Top - bounds.Top) / latPerPixel;
                }
                else if (_bottomStuckBeforeZoom)
                {
                    // Тримаємо Visible.Bottom рівно біля Bounds.Bottom
                    offsetY = (bounds.Bottom - visible.Bottom) / latPerPixel;
                }

                var roundedOffsetX = (int)Math.Round(offsetX);
                var roundedOffsetY = (int)Math.Round(offsetY);

                if (Math.Abs(roundedOffsetX) < 1 &&
                    Math.Abs(roundedOffsetY) < 1)
                {
                    return;
                }

                ApplyExternalOffsetAndRebaseDrag(
    roundedOffsetX,
    roundedOffsetY
);

                UpdateOverlaysAfterMapOffset();
                InvalidateVisual(true);

            }
        }

        bool _isSelected;

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            if (CanDragMap && e.ChangedButton == DragButton)
            {
                var p = e.GetPosition(this);

                if (MapScaleTransform != null)
                {
                    p = MapScaleTransform.Inverse.Transform(p);
                }

                p = ApplyRotationInversion(p.X, p.Y);

                _core.MouseDown.X = (int)p.X;
                _core.MouseDown.Y = (int)p.Y;

                InvalidateVisual();
            }
            else
            {
                if (!_isSelected)
                {
                    var p = e.GetPosition(this);
                    _isSelected = true;
                    SelectedArea = RectLatLng.Empty;
                    _selectionEnd = PointLatLng.Empty;
                    _selectionStart = FromLocalToLatLng((int)p.X, (int)p.Y);
                }
            }
        }

        private void StopMouseDragState(int timestamp)
        {
            _onMouseUpTimestamp = timestamp & Int32.MaxValue;

            if (IsDragging)
            {
                IsDragging = false;
                Cursor = _cursorBefore;
            }

            if (Mouse.Captured == this)
            {
                Mouse.Capture(null);
            }

            if (_core.IsDragging)
            {
                _core.EndDrag();
            }

            _core.MouseDown = GPoint.Empty;
            _core.MouseCurrent = GPoint.Empty;
        }

        private bool IsDragButtonPressed(MouseEventArgs e)
        {
            if (DragButton == MouseButton.Left)
            {
                return e.LeftButton == MouseButtonState.Pressed;
            }

            if (DragButton == MouseButton.Right)
            {
                return e.RightButton == MouseButtonState.Pressed;
            }

            if (DragButton == MouseButton.Middle)
            {
                return e.MiddleButton == MouseButtonState.Pressed;
            }

            if (DragButton == MouseButton.XButton1)
            {
                return e.XButton1 == MouseButtonState.Pressed;
            }

            if (DragButton == MouseButton.XButton2)
            {
                return e.XButton2 == MouseButtonState.Pressed;
            }

            return false;
        }

        int _onMouseUpTimestamp;

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);

            if (_isSelected)
            {
                _isSelected = false;
            }

            if (e.ChangedButton == DragButton)
            {
                StopMouseDragState(e.Timestamp);

                if (BoundsOfMap.HasValue &&
                    !BoundsOfMap.Value.IsEmpty)
                {
                    ClampVisibleBoundsToAllowedBounds();
                }

                e.Handled = true;
                return;
            }

            if (_core.IsDragging)
            {
                if (IsDragging)
                {
                    _onMouseUpTimestamp = e.Timestamp & Int32.MaxValue;
                    IsDragging = false;
                    Cursor = _cursorBefore;
                    Mouse.Capture(null);
                }

                _core.EndDrag();

                if (BoundsOfMap.HasValue &&
                    !BoundsOfMap.Value.IsEmpty)
                {
                    ClampVisibleBoundsToAllowedBounds();
                }
            }
            else
            {
                if (e.ChangedButton == DragButton)
                {
                    _core.MouseDown = GPoint.Empty;
                }

                if (!_selectionEnd.IsEmpty && !_selectionStart.IsEmpty)
                {
                    bool zoomtofit = false;

                    if (!SelectedArea.IsEmpty && Keyboard.Modifiers == ModifierKeys.Shift)
                    {
                        zoomtofit = SetZoomToFitRect(SelectedArea);
                    }

                    OnSelectionChange?.Invoke(SelectedArea, zoomtofit);
                }
                else
                {
                    InvalidateVisual();
                }
            }
        }

        Cursor _cursorBefore = Cursors.Arrow;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if ((e.Timestamp & Int32.MaxValue) - _onMouseUpTimestamp < 55)
            {
                return;
            }

            if ((IsDragging || _core.IsDragging) &&
    !IsDragButtonPressed(e))
            {
                StopMouseDragState(e.Timestamp);
                return;
            }

            if (!_core.IsDragging && !_core.MouseDown.IsEmpty)
            {
                var p = e.GetPosition(this);

                if (MapScaleTransform != null)
                {
                    p = MapScaleTransform.Inverse.Transform(p);
                }

                p = ApplyRotationInversion(p.X, p.Y);

                if (IsDragButtonPressed(e)) // ← використовуємо єдиний helper
                {
                    if (Math.Abs(p.X - _core.MouseDown.X) * 2 >= SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(p.Y - _core.MouseDown.Y) * 2 >= SystemParameters.MinimumVerticalDragDistance)
                    {
                        _core.BeginDrag(_core.MouseDown);
                    }
                }
            }

            if (_core.IsDragging)
            {
                if (!IsDragging)
                {
                    IsDragging = true;
                    _cursorBefore = Cursor;
                    Cursor = Cursors.SizeAll;
                    Mouse.Capture(this);
                }

                var p = e.GetPosition(this);

                if (MapScaleTransform != null)
                {
                    p = MapScaleTransform.Inverse.Transform(p);
                }

                p = ApplyRotationInversion(p.X, p.Y);

                _core.MouseCurrent.X = (int)p.X;
                _core.MouseCurrent.Y = (int)p.Y;

                _core.Drag(_core.MouseCurrent);

                if (BoundsOfMap.HasValue &&
                    !BoundsOfMap.Value.IsEmpty)
                {
                    ClampVisibleBoundsToAllowedBounds();
                }

                if (IsRotated)
                {
                    ForceUpdateOverlays();
                }
                else
                {
                    UpdateMarkersOffset();
                }

                RequestDragFrame();
            }
            else
            {
                if (_isSelected &&
                    !_selectionStart.IsEmpty &&
                    (Keyboard.Modifiers == ModifierKeys.Shift ||
                     Keyboard.Modifiers == ModifierKeys.Alt ||
                     DisableAltForSelection))
                {
                    var p = e.GetPosition(this);
                    _selectionEnd = FromLocalToLatLng((int)p.X, (int)p.Y);

                    var p1 = _selectionStart;
                    var p2 = _selectionEnd;

                    var x1 = Math.Min(p1.Lng, p2.Lng);
                    var y1 = Math.Max(p1.Lat, p2.Lat);
                    var x2 = Math.Max(p1.Lng, p2.Lng);
                    var y2 = Math.Min(p1.Lat, p2.Lat);

                    SelectedArea = new RectLatLng(
                        y1,
                        x1,
                        x2 - x1,
                        y1 - y2
                    );
                }

                if (_renderHelperLine)
                {
                    InvalidateVisual(true);
                }
            }
        }

        /// <summary>
        ///     if true, selects area just by holding mouse and moving
        /// </summary>
        public bool DisableAltForSelection = false;

        protected override void OnStylusDown(StylusDownEventArgs e)
        {
            base.OnStylusDown(e);

            if (TouchEnabled && CanDragMap && !e.InAir)
            {
                var p = e.GetPosition(this);

                if (MapScaleTransform != null)
                {
                    p = MapScaleTransform.Inverse.Transform(p);
                }

                p = ApplyRotationInversion(p.X, p.Y);

                _core.MouseDown.X = (int)p.X;
                _core.MouseDown.Y = (int)p.Y;

                InvalidateVisual();
            }
        }

        protected override void OnStylusUp(StylusEventArgs e)
        {
            base.OnStylusUp(e);

            if (TouchEnabled)
            {
                if (_isSelected)
                {
                    _isSelected = false;
                }

                if (_core.IsDragging)
                {
                    if (IsDragging)
                    {
                        _onMouseUpTimestamp = e.Timestamp & Int32.MaxValue;
                        IsDragging = false;
                        Cursor = _cursorBefore;
                        Mouse.Capture(null);
                    }

                    _core.EndDrag();

                    if (BoundsOfMap.HasValue && !BoundsOfMap.Value.IsEmpty)
                    {
                        ClampVisibleBoundsToAllowedBounds();
                    }
                }
                else
                {
                    _core.MouseDown = GPoint.Empty;
                    InvalidateVisual();
                }
            }
        }

        private void ReapplyCurrentZoomTransform()
        {
            if (MapProvider == null ||
                MapProvider.Projection == null ||
                ActualWidth <= 0 ||
                ActualHeight <= 0)
            {
                return;
            }

            ZoomPropertyChanged(
                this,
                Zoom,
                Zoom,
                ZoomMode.XY
            );
        }

        protected override void OnStylusMove(StylusEventArgs e)
        {
            base.OnStylusMove(e);

            if (TouchEnabled)
            {
                // wpf generates to many events if mouse is over some visual
                // and OnMouseUp is fired, wtf, anyway...
                // http://greatmaps.codeplex.com/workitem/16013
                if ((e.Timestamp & Int32.MaxValue) - _onMouseUpTimestamp < 55)
                {
                    return;
                }

                if (!_core.IsDragging && !_core.MouseDown.IsEmpty)
                {
                    var p = e.GetPosition(this);

                    if (MapScaleTransform != null)
                    {
                        p = MapScaleTransform.Inverse.Transform(p);
                    }

                    p = ApplyRotationInversion(p.X, p.Y);

                    // cursor has moved beyond drag tolerance
                    if (Math.Abs(p.X - _core.MouseDown.X) * 2 >= SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(p.Y - _core.MouseDown.Y) * 2 >= SystemParameters.MinimumVerticalDragDistance)
                    {
                        _core.BeginDrag(_core.MouseDown);
                    }
                }

                if (_core.IsDragging)
                {
                    if (!IsDragging)
                    {
                        IsDragging = true;
                        _cursorBefore = Cursor;
                        Cursor = Cursors.SizeAll;
                        Mouse.Capture(this);
                    }

                    if (BoundsOfMap.HasValue && !BoundsOfMap.Value.Contains(Position))
                    {
                        // ...
                    }
                    else
                    {
                        var p = e.GetPosition(this);

                        if (MapScaleTransform != null)
                        {
                            p = MapScaleTransform.Inverse.Transform(p);
                        }

                        p = ApplyRotationInversion(p.X, p.Y);

                        _core.MouseCurrent.X = (int)p.X;
                        _core.MouseCurrent.Y = (int)p.Y;
                        {
                            _core.Drag(_core.MouseCurrent);
                        }

                        if (IsRotated)
                        {
                            ForceUpdateOverlays();
                        }
                        else
                        {
                            UpdateMarkersOffset();
                        }
                    }

                    InvalidateVisual();
                }
            }
        }

        /// <summary>
        ///     Called when the <see cref="E:System.Windows.UIElement.ManipulationDelta" /> event occurs.
        /// </summary>
        /// <param name="e">The data for the event.</param>
        protected override void OnManipulationDelta(ManipulationDeltaEventArgs e)
        {
            if (!MultiTouchEnabled) return;

            base.OnManipulationDelta(e);

            if (MultiTouchEnabled && !TouchEnabled)
            {
                var touchPoints = e.Manipulators.ToArray();
                var element = e.Source as FrameworkElement;

                if (element != null)
                {
                    var delta = e.DeltaManipulation;

                    if (touchPoints.Length == 1)
                    {
                        SingleTouchPanMap(new Point(delta.Translation.X, delta.Translation.Y));
                    }
                    else if (touchPoints.Length >= 2)
                    {
                        var centerOfTouchPoints = e.ManipulationOrigin;
                    }

                    e.Handled = true;
                }
            }
        }

        protected override void OnManipulationStarted(ManipulationStartedEventArgs e)
        {
            if (!MultiTouchEnabled) return;

            base.OnManipulationStarted(e);

            if (MultiTouchEnabled && !TouchEnabled)
            {
                _core.MouseDown.X = 0;
                _core.MouseDown.Y = 0;
                _core.touchCurrent.X = 0;
                _core.touchCurrent.Y = 0;
            }
        }

        /// <summary>
        ///     Singles the touch pan map.
        /// </summary>
        /// <param name="deltaPoint">The delta point.</param>
        protected virtual void SingleTouchPanMap(Point deltaPoint)
        {
            if (!MultiTouchEnabled) return;

                deltaPoint = ApplyRotationInversion(deltaPoint.X, deltaPoint.Y);

                _core.touchCurrent.X += (int)deltaPoint.X;
                _core.touchCurrent.Y += (int)deltaPoint.Y;

                if (!_core.IsDragging)
                {
                    // cursor has moved beyond drag tolerance
                    if (Math.Abs(deltaPoint.X - _core.MouseDown.X) * 2 >=
                        SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(deltaPoint.Y - _core.MouseDown.Y) * 2 >= SystemParameters.MinimumVerticalDragDistance)
                    {
                        _core.BeginDrag(_core.MouseDown);
                    }
                }
                else if (_core.IsDragging)
                {
                    if (!IsDragging)
                    {
                        IsDragging = true;
                        _cursorBefore = Cursor;
                        Cursor = Cursors.SizeAll;
                        Mouse.Capture(this);
                    }

                    if (BoundsOfMap.HasValue && !BoundsOfMap.Value.Contains(Position))
                    {
                        // ...
                    }
                    else
                    {
                        _core.Drag(_core.touchCurrent);

                        if (IsRotated)
                        {
                            ForceUpdateOverlays();
                        }
                        else
                        {
                            UpdateMarkersOffset();
                        }
                    }

                    InvalidateVisual();

                }            
        }

        /// <summary>
        ///     Called when the <see cref="E:System.Windows.UIElement.ManipulationCompleted" /> event occurs.
        /// </summary>
        /// <param name="e">The data for the event.</param>
        protected override void OnManipulationCompleted(ManipulationCompletedEventArgs e)
        {
            if (!MultiTouchEnabled) return;

            base.OnManipulationCompleted(e);

            if (MultiTouchEnabled && !TouchEnabled)
            {
                var touchPoints = e.Manipulators.ToArray();
                if (true)
                {
                    // add bool to starting for single touch vs multi touch
                    if (_isSelected)
                    {
                        _isSelected = false;
                    }

                    if (_core.IsDragging)
                    {
                        if (IsDragging)
                        {
                            _onMouseUpTimestamp = e.Timestamp & Int32.MaxValue;
                            IsDragging = false;
                            Cursor = _cursorBefore;
                            Mouse.Capture(null);
                        }

                        _core.EndDrag();

                        if (BoundsOfMap.HasValue && !BoundsOfMap.Value.Contains(Position))
                        {
                            if (_core.LastLocationInBounds.HasValue)
                            {
                                Position = _core.LastLocationInBounds.Value;
                            }
                        }
                    }
                    else
                    {
                        _core.MouseDown = GPoint.Empty;
                        InvalidateVisual();
                    }
                }
            }
        }

        int _change;
        private Dictionary<int, Point> movingPoints = new Dictionary<int, Point>();

        private double calcdist(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));
        }

        protected override void OnTouchDown(TouchEventArgs e)
        {
            if (!MultiTouchEnabled) return;

            base.OnTouchDown(e);

            if (MultiTouchEnabled)
            {
                var touchpoint = e.GetTouchPoint(this);
                var point = new Point();
                point.X = touchpoint.Position.X;
                point.Y = touchpoint.Position.Y;
                movingPoints[e.TouchDevice.Id] = point;

                if (movingPoints.Count == 0)
                {
                    if (MapScaleTransform != null)
                    {
                        point = MapScaleTransform.Inverse.Transform(point);
                    }

                    point = ApplyRotationInversion(point.X, point.Y);
                    _core.MouseDown.X = (int)point.X;
                    _core.MouseDown.Y = (int)point.Y;
                    InvalidateVisual();
                }
            }
        }

        protected override void OnTouchMove(TouchEventArgs e)
        {
            if (!MultiTouchEnabled) return;

            base.OnTouchMove(e);

            if (MultiTouchEnabled)
            {
                if (movingPoints.Count == 1)
                {
                    if (movingPoints.Keys.Contains(e.TouchDevice.Id))
                    {
                        if ((e.Timestamp & Int32.MaxValue) - _onMouseUpTimestamp < 55)
                        {
                            return;
                        }

                        if (!_core.IsDragging)
                        {
                            var touchpoint = e.GetTouchPoint(this);
                            var p = new Point();
                            p.X = touchpoint.Position.X;
                            p.Y = touchpoint.Position.Y;

                            if (MapScaleTransform != null)
                            {
                                p = MapScaleTransform.Inverse.Transform(p);
                            }

                            p = ApplyRotationInversion(p.X, p.Y);

                            // cursor has moved beyond drag tolerance
                            if (Math.Abs(p.X - movingPoints[e.TouchDevice.Id].X) * 2 >=
                                SystemParameters.MinimumHorizontalDragDistance ||
                                Math.Abs(p.Y - movingPoints[e.TouchDevice.Id].Y) * 2 >=
                                SystemParameters.MinimumVerticalDragDistance)
                            {
                                var gp = new GPoint();
                                gp.X = (int)movingPoints[e.TouchDevice.Id].X;
                                gp.Y = (int)movingPoints[e.TouchDevice.Id].Y;
                                _core.BeginDrag(gp);
                            }
                        }

                        if (_core.IsDragging)
                        {
                            if (!IsDragging)
                            {
                                IsDragging = true;
                            _cursorBefore = Cursor;
                                Cursor = Cursors.SizeAll;
                                Mouse.Capture(this);
                            }

                            if (BoundsOfMap.HasValue && !BoundsOfMap.Value.Contains(Position))
                            {
                                // ...
                            }
                            else
                            {
                                var touchpoint = e.GetTouchPoint(this);
                                var p = new Point();
                                p.X = touchpoint.Position.X;
                                p.Y = touchpoint.Position.Y;

                                if (MapScaleTransform != null)
                                {
                                    p = MapScaleTransform.Inverse.Transform(p);
                                }

                                p = ApplyRotationInversion(p.X, p.Y);

                                _core.MouseCurrent.X = (int)p.X;
                                _core.MouseCurrent.Y = (int)p.Y;
                                {
                                    _core.Drag(_core.MouseCurrent);
                                }

                                if (IsRotated)
                                {
                                    ForceUpdateOverlays();
                                }
                                else
                                {
                                    UpdateMarkersOffset();
                                }
                            }

                            InvalidateVisual();
                        }
                    }
                }
                else if (movingPoints.Count == 2)
                {
                    if (movingPoints.Keys.Contains(e.TouchDevice.Id))
                    {
                        var point1 = new Point();
                        var point2 = new Point();
                        double nowdistance = 0;
                        double predistance = 0;
                        int count = 0;

                        foreach (var item in movingPoints)
                        {
                            if (count == 0)
                                point1 = item.Value;
                            else
                                point2 = item.Value;
                            count++;
                        }

                        predistance = calcdist(point1.X, point1.Y, point2.X, point2.Y);
                        var touchpoint = e.GetTouchPoint(this);
                        var npoint = new Point();
                        npoint.X = touchpoint.Position.X;
                        npoint.Y = touchpoint.Position.Y;

                        if (movingPoints[e.TouchDevice.Id] == point1)
                        {
                            nowdistance = calcdist(npoint.X, npoint.Y, point2.X, point2.Y);
                        }
                        else
                        {
                            nowdistance = calcdist(npoint.X, npoint.Y, point1.X, point1.Y);
                        }

                        //movingPoints[e.TouchDevice.Id] = npoint;
                        if (_change <= 2)
                        {
                            if (nowdistance - predistance > 10)
                            {
                                Zoom += 0.5;
                                _change++;
                            }
                            else if (nowdistance - predistance < -10)
                            {
                                Zoom -= 0.5;
                                _change++;
                            }
                        }
                    }
                }
            }
        }

        protected override void OnTouchUp(TouchEventArgs e)
        {
            if (!MultiTouchEnabled) return;

            base.OnTouchUp(e);

            if (MultiTouchEnabled && !TouchEnabled)
            {
                _change = 0;
                movingPoints.Remove(e.TouchDevice.Id);

                if (true) // add bool to starting for single touch vs multi touch
                {
                    if (_isSelected)
                    {
                        _isSelected = false;
                    }

                    if (_core.IsDragging)
                    {
                        if (IsDragging)
                        {
                            _onMouseUpTimestamp = e.Timestamp & Int32.MaxValue;
                            IsDragging = false;
                            Cursor = _cursorBefore;
                            Mouse.Capture(null);
                        }

                        _core.EndDrag();

                        if (BoundsOfMap.HasValue && !BoundsOfMap.Value.Contains(Position))
                        {
                            if (_core.LastLocationInBounds.HasValue)
                            {
                                Position = _core.LastLocationInBounds.Value;
                            }
                        }
                    }
                    else
                    {
                        _core.MouseDown = GPoint.Empty;
                        InvalidateVisual();
                    }
                }
            }
        }

        #endregion

        #region IGControl Members

        /// <summary>
        ///     Call it to empty tile cache & reload tiles
        /// </summary>
        public void ReloadMap()
        {
            _core.ReloadMap();
        }

        public Task ReloadMapAsync()
        {
            return _core.ReloadMapAsync();
        }

        /// <summary>
        ///     sets position using geocoder
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public GeoCoderStatusCode SetPositionByKeywords(string keys)
        {
            var status = GeoCoderStatusCode.UNKNOWN_ERROR;

            var gp = MapProvider as GeocodingProvider;
            if (gp == null)
            {
                gp = GMapProviders.OpenStreetMap as GeocodingProvider;
            }

            if (gp != null)
            {
                var pt = gp.GetPoint(keys, out status);
                if (status == GeoCoderStatusCode.OK && pt.HasValue)
                {
                    Position = pt.Value;
                }
            }

            return status;
        }

        /// <summary>
        ///     gets position using geocoder
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public PointLatLng GetPositionByKeywords(string keys)
        {
            var status = GeoCoderStatusCode.UNKNOWN_ERROR;

            var gp = MapProvider as GeocodingProvider;
            if (gp == null)
            {
                gp = GMapProviders.OpenStreetMap as GeocodingProvider;
            }

            if (gp != null)
            {
                var pt = gp.GetPoint(keys, out status);
                if (status == GeoCoderStatusCode.OK && pt.HasValue)
                {
                    return pt.Value;
                }
            }

            return new PointLatLng();
        }

        public PointLatLng FromLocalToLatLng(int x, int y)
        {
            if (MapScaleTransform != null)
            {
                var tp = MapScaleTransform.Inverse.Transform(new Point(x, y));
                x = (int)tp.X;
                y = (int)tp.Y;
            }

            if (IsRotated)
            {
                var f = _rotationMatrixInvert.Transform(new Point(x, y));

                x = (int)f.X;
                y = (int)f.Y;
            }

            return _core.FromLocalToLatLng(x, y);
        }

        public GPoint FromLatLngToLocal(PointLatLng point)
        {
            var ret = _core.FromLatLngToLocal(point);

            if (MapScaleTransform != null)
            {
                var tp = MapScaleTransform.Transform(new Point(ret.X, ret.Y));
                ret.X = (int)tp.X;
                ret.Y = (int)tp.Y;
            }

            if (IsRotated)
            {
                var f = _rotationMatrix.Transform(new Point(ret.X, ret.Y));

                ret.X = (int)f.X;
                ret.Y = (int)f.Y;
            }

            return ret;
        }

        public bool ShowExportDialog()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog();
            {
                dlg.CheckPathExists = true;
                dlg.CheckFileExists = false;
                dlg.AddExtension = true;
                dlg.DefaultExt = "gmdb";
                dlg.ValidateNames = true;
                dlg.Title = "GMap.NET: Export map to db, if file exsist only new data will be added";
                dlg.FileName = "DataExp";
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                dlg.Filter = "GMap.NET DB files (*.gmdb)|*.gmdb";
                dlg.FilterIndex = 1;
                dlg.RestoreDirectory = true;

                if (dlg.ShowDialog() == true)
                {
                    bool ok = GMaps.Instance.ExportToGMDB(dlg.FileName);
                    if (ok)
                    {
                        MessageBox.Show("Complete!", "GMap.NET", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("  Failed!", "GMap.NET", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    return ok;
                }
            }

            return false;
        }

        public bool ShowImportDialog()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            {
                dlg.CheckPathExists = true;
                dlg.CheckFileExists = false;
                dlg.AddExtension = true;
                dlg.DefaultExt = "gmdb";
                dlg.ValidateNames = true;
                dlg.Title = "GMap.NET: Import to db, only new data will be added";
                dlg.FileName = "DataImport";
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                dlg.Filter = "GMap.NET DB files (*.gmdb)|*.gmdb";
                dlg.FilterIndex = 1;
                dlg.RestoreDirectory = true;

                if (dlg.ShowDialog() == true)
                {
                    Cursor = Cursors.Wait;

                    bool ok = GMaps.Instance.ImportFromGMDB(dlg.FileName);
                    if (ok)
                    {
                        MessageBox.Show("Complete!", "GMap.NET", MessageBoxButton.OK, MessageBoxImage.Information);
                        ReloadMap();
                    }
                    else
                    {
                        MessageBox.Show("  Failed!", "GMap.NET", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    Cursor = Cursors.Arrow;

                    return ok;
                }
            }

            return false;
        }

        public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
            "Position",
            typeof(PointLatLng),
            typeof(GMapControl),
            new FrameworkPropertyMetadata(default(PointLatLng),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                PositionChangedCallBack));

        /// <summary>
        ///     Current coordinates of the map center
        /// </summary>
        [Browsable(false)]
        public PointLatLng Position
        {
            get { return (PointLatLng)GetValue(PositionProperty); }
            set { SetValue(PositionProperty, value); }
        }

        private static void PositionChangedCallBack(DependencyObject source,
            DependencyPropertyChangedEventArgs e)
        {
            ((GMapControl)source).PositionChanged(e);
        }

        [Browsable(false)]
        public GPoint PositionPixel
        {
            get { return _core.PositionPixel; }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Browsable(false)]
        public string CacheLocation
        {
            get { return CacheLocator.Location; }
            set { CacheLocator.Location = value; }
        }

        [Browsable(false)] public bool IsDragging { get; private set; }

        [Browsable(false)]
        public RectLatLng ViewArea
        {
            get
            {
                if (!IsRotated)
                {
                    return _core.ViewArea;
                }
                else if (_core.Provider.Projection != null)
                {
                    var p = FromLocalToLatLng(0, 0);
                    var p2 = FromLocalToLatLng((int)Width, (int)Height);

                    return RectLatLng.FromLTRB(p.Lng, p.Lat, p2.Lng, p2.Lat);
                }

                return RectLatLng.Empty;
            }
        }

        [Category("GMap.NET")]
        public bool CanDragMap
        {
            get { return _core.CanDragMap; }
            set { _core.CanDragMap = value; }
        }

        public RenderMode RenderMode
        {
            get { return RenderMode.WPF; }
        }

        #endregion

        #region IGControl event Members

        public event PositionChanged OnPositionChanged
        {
            add { _core.OnCurrentPositionChanged += value; }
            remove { _core.OnCurrentPositionChanged -= value; }
        }

        public event TileLoadComplete OnTileLoadComplete
        {
            add { _core.OnTileLoadComplete += value; }
            remove { _core.OnTileLoadComplete -= value; }
        }

        public event TileLoadStart OnTileLoadStart
        {
            add { _core.OnTileLoadStart += value; }
            remove { _core.OnTileLoadStart -= value; }
        }

        public event MapDrag OnMapDrag
        {
            add { _core.OnMapDrag += value; }
            remove { _core.OnMapDrag -= value; }
        }

        public event MapZoomChanged OnMapZoomChanged
        {
            add { _core.OnMapZoomChanged += value; }
            remove { _core.OnMapZoomChanged -= value; }
        }

        /// <summary>
        ///     occures on map type changed
        /// </summary>
        public event MapTypeChanged OnMapTypeChanged
        {
            add { _core.OnMapTypeChanged += value; }
            remove { _core.OnMapTypeChanged -= value; }
        }

        /// <summary>
        ///     occurs on empty tile displayed
        /// </summary>
        public event EmptyTileError OnEmptyTileError
        {
            add { _core.OnEmptyTileError += value; }
            remove { _core.OnEmptyTileError -= value; }
        }

        #endregion

        #region IDisposable Members

        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_dragFrameInvalidationPending)
            {
                CompositionTarget.Rendering -=
                    CompositionTargetOnDragRendering;
                _dragFrameInvalidationPending = false;
            }

            _tileClipGeometryCache.Clear();

            // Відписуємося від усіх подій control (незалежно від стану Core)
            _core.OnMapZoomChanged -= CoreOnMapZoomChanged;
            _core.OnCurrentPositionChanged -= CoreOnCurrentPositionChanged;
            Loaded -= GMapControl_Loaded;
            Dispatcher.ShutdownStarted -= Dispatcher_ShutdownStarted;
            SizeChanged -= GMapControl_SizeChanged;

            if (_loadedApp != null)
            {
                _loadedApp.SessionEnding -= Current_SessionEnding;
                _loadedApp = null;
            }

            // Якщо Core запущено, закриваємо його
            if (_core.IsStarted)
            {
                _core.OnMapClose();
            }
        }

        #endregion
    }

    public enum HelperLineOptions
    {
        DontShow = 0,
        ShowAlways = 1,
        ShowOnModifierKey = 2
    }

    public enum ScaleModes
    {
        /// <summary>
        ///     no scaling
        /// </summary>
        Integer,

        /// <summary>
        ///     scales to fractional level using a stretched tiles, any issues -> http://greatmaps.codeplex.com/workitem/16046
        /// </summary>
        ScaleUp,

        /// <summary>
        ///     scales to fractional level using a narrowed tiles, any issues -> http://greatmaps.codeplex.com/workitem/16046
        /// </summary>
        ScaleDown,

        /// <summary>
        ///     scales to fractional level using a combination both stretched and narrowed tiles, any issues ->
        ///     http://greatmaps.codeplex.com/workitem/16046
        /// </summary>
        Dynamic
    }

    /// <summary>
    ///     Enum ZoomMode
    /// </summary>
    public enum ZoomMode
    {
        /// <summary>
        ///     Only update X coordinates
        /// </summary>
        X,

        /// <summary>
        ///     Only update Y coordinates
        /// </summary>
        Y,

        /// <summary>
        ///     Updates both the X and Y coordinates
        /// </summary>
        XY
    }

    public delegate void SelectionChange(RectLatLng selection, bool zoomToFit);
}
