using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GMap.NET.MapProviders;
using GMap.NET.Projections;
using System.Globalization;
using System.IO;
using System.Text;

#if NETFRAMEWORK
using System.Collections.Concurrent;
#endif

namespace GMap.NET.Internals
{
    /// <summary>
    ///     internal map control core
    /// </summary>
    internal sealed class Core : IDisposable
    {
        internal PointLatLng _position;
        private GPoint _positionPixel;

        internal GPoint RenderOffset;
        internal GPoint CenterTileXYLocation;
        private GPoint _centerTileXYLocationLast;
        private GPoint _dragPoint;

        // _sizeOfMapArea already contains one tile of safety padding. Load one
        // additional ring so ordinary panning stays entirely on prepared tiles.
        private const int AdditionalPrefetchTileMargin = 1;

        private readonly object _tileViewUpdateSync = new object();
        private readonly object _tileViewCommitSync = new object();
        private TileViewUpdateRequest _pendingTileViewUpdate;
        private bool _tileViewUpdateWorkerRunning;
        private int _tileViewGeneration;

        private TaskCompletionSource<bool> _reloadCompletionSource;
        private bool _isReloading = false;

        internal GPoint CompensationOffset;

        internal GPoint MouseDown;
        internal GPoint MouseCurrent;
        internal GPoint MouseLastZoom;
        internal GPoint touchCurrent;

        public MouseWheelZoomType MouseWheelZoomType = MouseWheelZoomType.MousePositionAndCenter;
        public bool MouseWheelZoomEnabled = true;

        public PointLatLng? LastLocationInBounds;
        public bool VirtualSizeEnabled = false;

        private GSize _sizeOfMapArea;
        private GSize _minOfTiles;
        private GSize _maxOfTiles;

        internal GRect TileRect;

        internal GRect TileRectBearing;

        //private GRect _currentRegion;
        internal float Bearing = 0;
        public bool IsRotated = false;

        internal bool FillEmptyTiles = true;

        public TileMatrix Matrix = new TileMatrix();

        internal List<DrawTile> TileDrawingList = new List<DrawTile>();
        internal FastReaderWriterLock TileDrawingListLock = new FastReaderWriterLock();

#if !NETFRAMEWORK
        public readonly Stack<LoadTask> TileLoadQueue = new Stack<LoadTask>();
        private readonly HashSet<LoadTask> _tileLoadsInProgress =
            new HashSet<LoadTask>(new LoadTaskComparer());
        private int _tileLoadBatchId;
        private int _completedTileLoadBatchId;
#endif

        static readonly int GThreadPoolSize = 4;

        DateTime _lastTileLoadStart = DateTime.Now;
        DateTime _lastTileLoadEnd = DateTime.Now;
        internal volatile bool IsStarted;
        int _zoom;

        internal double ScaleX = 1;
        internal double ScaleY = 1;

        internal int MaxZoom = 2;
        internal int MinZoom = 2;
        internal int Width;
        internal int Height;

        internal int PxRes100M; // 100 meters
        internal int PxRes1000M; // 1km  
        internal int PxRes10Km; // 10km
        internal int PxRes100Km; // 100km
        internal int PxRes1000Km; // 1000km
        internal int PxRes5000Km; // 5000km

        /// <summary>
        ///     is user dragging map
        /// </summary>
        public bool IsDragging;

        public Core()
        {
            Provider = EmptyProvider.Instance;
        }

        /// <summary>
        ///     map zoom
        /// </summary>
        public int Zoom
        {
            get
            {
                return _zoom;
            }
            set
            {
                if (_zoom != value && !IsDragging)
                {
                    _zoom = value;

                    _minOfTiles = Provider.Projection.GetTileMatrixMinXY(value);
                    _maxOfTiles = Provider.Projection.GetTileMatrixMaxXY(value);

                    _positionPixel = Provider.Projection.FromLatLngToPixel(Position, value);

                    if (IsStarted)
                    {
                        CancelAsyncTasks();

                        Matrix.ClearLevelsBelove(_zoom - LevelsKeepInMemory);
                        Matrix.ClearLevelsAbove(_zoom + LevelsKeepInMemory);

                        lock (FailedLoads)
                        {
                            FailedLoads.Clear();
                            _raiseEmptyTileError = true;
                        }

                        GoToCurrentPositionOnZoom();
                        UpdateBounds();

                        if (OnMapZoomChanged != null)
                        {
                            OnMapZoomChanged();
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     current marker position in pixel coordinates
        /// </summary>
        public GPoint PositionPixel
        {
            get
            {
                return _positionPixel;
            }
        }

        /// <summary>
        ///     current marker position
        /// </summary>
        public PointLatLng Position
        {
            get { return _position; }
            set
            {
                if (value == _position)
                    return;

                _position = value;
                _positionPixel = Provider.Projection.FromLatLngToPixel(value, Zoom);

                if (IsStarted)
                {
                    if (!IsDragging)
                        GoToCurrentPosition();

                    OnCurrentPositionChanged?.Invoke(_position);
                }
            }
        }

        private GMapProvider _provider;

        public GMapProvider Provider
        {
            get
            {
                return _provider;
            }
            set
            {
                if (_provider == null || !_provider.Equals(value))
                {
                    bool diffProjection = _provider == null || _provider.Projection != value.Projection;

                    _provider = value;

                    if (!_provider.IsInitialized)
                    {
                        _provider.IsInitialized = true;
                        _provider.OnInitialized();
                    }

                    if (_provider.Projection != null && diffProjection)
                    {
                        TileRect = new GRect(GPoint.Empty, Provider.Projection.TileSize);
                        TileRectBearing = TileRect;
                        if (IsRotated)
                        {
                            TileRectBearing.Inflate(1, 1);
                        }

                        _minOfTiles = Provider.Projection.GetTileMatrixMinXY(Zoom);
                        _maxOfTiles = Provider.Projection.GetTileMatrixMaxXY(Zoom);
                        _positionPixel = Provider.Projection.FromLatLngToPixel(Position, Zoom);
                    }

                    if (IsStarted)
                    {
                        CancelAsyncTasks();
                        if (diffProjection)
                        {
                            OnMapSizeChanged(Width, Height);
                        }

                        ReloadMap();

                        if (MinZoom < _provider.MinZoom)
                        {
                            MinZoom = _provider.MinZoom;
                        }

                        //if(provider.MaxZoom.HasValue && maxZoom > provider.MaxZoom)
                        //{
                        //   maxZoom = provider.MaxZoom.Value;
                        //}

                        ZoomToArea = true;

                        if (_provider.Area.HasValue && !_provider.Area.Value.Contains(Position))
                        {
                            SetZoomToFitRect(_provider.Area.Value);
                            ZoomToArea = false;
                        }

                        if (OnMapTypeChanged != null)
                        {
                            OnMapTypeChanged(value);
                        }
                    }
                }
            }
        }

        internal bool ZoomToArea = true;

        /// <summary>
        ///     sets zoom to max to fit rect
        /// </summary>
        /// <param name="rect"></param>
        /// <returns></returns>
        public bool SetZoomToFitRect(RectLatLng rect)
        {
            int mmaxZoom = GetMaxZoomToFitRect(rect);
            if (mmaxZoom > 0)
            {
                var center = new PointLatLng(rect.Lat - rect.HeightLat / 2, rect.Lng + rect.WidthLng / 2);
                Position = center;

                if (mmaxZoom > MaxZoom)
                {
                    mmaxZoom = MaxZoom;
                }

                if (Zoom != mmaxZoom)
                {
                    Zoom = mmaxZoom;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        ///     is polygons enabled
        /// </summary>
        public bool PolygonsEnabled = true;

        /// <summary>
        ///     is routes enabled
        /// </summary>
        public bool RoutesEnabled = true;

        /// <summary>
        ///     is markers enabled
        /// </summary>
        public bool MarkersEnabled = true;

        /// <summary>
        ///     can user drag map
        /// </summary>
        public bool CanDragMap = true;

        /// <summary>
        ///     retry count to get tile
        /// </summary>
        public int RetryLoadTile = 0;

        /// <summary>
        ///     how many levels of tiles are staying decompressed in memory
        /// </summary>
        public int LevelsKeepInMemory = 5;

        /// <summary>
        ///     map render mode
        /// </summary>
        public RenderMode RenderMode = RenderMode.GDI_PLUS;

        /// <summary>
        ///     occurs when current position is changed
        /// </summary>
        public event PositionChanged OnCurrentPositionChanged;

        /// <summary>
        ///     occurs when tile set load is complete
        /// </summary>
        public event TileLoadComplete OnTileLoadComplete;

        /// <summary>
        ///     occurs when tile set is starting to load
        /// </summary>
        public event TileLoadStart OnTileLoadStart;

        /// <summary>
        ///     occurs on empty tile displayed
        /// </summary>
        public event EmptyTileError OnEmptyTileError;

        /// <summary>
        ///     occurs on map drag
        /// </summary>
        public event MapDrag OnMapDrag;

        /// <summary>
        ///     occurs on map zoom changed
        /// </summary>
        public event MapZoomChanged OnMapZoomChanged;

        /// <summary>
        ///     occurs on map type changed
        /// </summary>
        public event MapTypeChanged OnMapTypeChanged;

        readonly List<Thread> _gThreadPool = new List<Thread>();

        // should be only one pool for multiply controls, any ideas how to fix?
        //static readonly List<Thread> GThreadPool = new List<Thread>();
        // windows forms or wpf
        internal string SystemType;

        internal static int Instances;

        BackgroundWorker _invalidator;

        public BackgroundWorker OnMapOpen()
        {
            if (!IsStarted)
            {
                int x = Interlocked.Increment(ref Instances);
                Debug.WriteLine("OnMapOpen: " + x);

                IsStarted = true;

                if (x == 1)
                {
                    GMaps.Instance.NoMapInstances = false;
                }

                GoToCurrentPosition();

                _invalidator = new BackgroundWorker();
                _invalidator.WorkerSupportsCancellation = true;
                _invalidator.WorkerReportsProgress = true;
                _invalidator.DoWork += InvalidatorWatch;
                _invalidator.RunWorkerAsync();

                //if(x == 1)
                //{
                // first control shown
                //}
            }

            return _invalidator;
        }

        public void OnMapClose()
        {
            Dispose();
        }

        internal readonly object InvalidationLock = new object();
        internal DateTime LastInvalidation = DateTime.Now;

        void InvalidatorWatch(object sender, DoWorkEventArgs e)
        {
            var worker = (BackgroundWorker)sender;

            var minimumInterval = TimeSpan.FromMilliseconds(111);
            var waitMilliseconds = (int)minimumInterval.TotalMilliseconds;

            var invalidatePending = false;

            while (!worker.CancellationPending)
            {
                var refresh = Refresh;

                if (refresh == null)
                {
                    break;
                }

                try
                {
                    if (refresh.WaitOne(waitMilliseconds))
                    {
                        invalidatePending = true;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (!invalidatePending)
                {
                    continue;
                }

                var now = DateTime.Now;

                lock (InvalidationLock)
                {
                    if (now - LastInvalidation < minimumInterval)
                    {
                        continue;
                    }

                    LastInvalidation = now;
                }

                invalidatePending = false;
                worker.ReportProgress(1);
            }
        }

        public void UpdateCenterTileXYLocation()
        {
            var center = FromLocalToLatLng(Width / 2, Height / 2);
            var centerPixel = Provider.Projection.FromLatLngToPixel(center, Zoom);
            CenterTileXYLocation = Provider.Projection.FromPixelToTileXY(centerPixel);
        }

        internal int VWidth = 800;
        internal int VHeight = 400;

        public void OnMapSizeChanged(int width, int height)
        {
            Width = width;
            Height = height;

            if (IsRotated)
            {
                int diag = (int)Math.Round(
                    Math.Sqrt(Width * Width + Height * Height) / Provider.Projection.TileSize.Width,
                    MidpointRounding.AwayFromZero);
                _sizeOfMapArea.Width = 1 + diag / 2;
                _sizeOfMapArea.Height = 1 + diag / 2;
            }
            else
            {
                _sizeOfMapArea.Width = 1 + Width / Provider.Projection.TileSize.Width / 2;
                _sizeOfMapArea.Height = 1 + Height / Provider.Projection.TileSize.Height / 2;
            }

            Debug.WriteLine("OnMapSizeChanged, w: " + width + ", h: " + height + ", size: " + _sizeOfMapArea);

            if (IsStarted)
            {
                UpdateBounds();
                GoToCurrentPosition();
            }
        }

        /// <summary>
        ///     gets current map view top/left coordinate, width in Lng, height in Lat
        /// </summary>
        /// <returns></returns>
        public RectLatLng ViewArea
        {
            get
            {
                if (Provider.Projection != null)
                {
                    var p = FromLocalToLatLng(0, 0);
                    var p2 = FromLocalToLatLng(Width, Height);

                    return RectLatLng.FromLTRB(p.Lng, p.Lat, p2.Lng, p2.Lat);
                }

                return RectLatLng.Empty;
            }
        }

        /// <summary>
        ///     gets lat/lng from local control coordinates
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public PointLatLng FromLocalToLatLng(long x, long y)
        {
            var p = new GPoint(x, y);
            p.OffsetNegative(RenderOffset);
            p.Offset(CompensationOffset);

            return Provider.Projection.FromPixelToLatLng(p, Zoom);
        }

        /// <summary>
        ///     return local coordinates from lat/lng
        /// </summary>
        /// <param name="latlng"></param>
        /// <returns></returns>
        public GPoint FromLatLngToLocal(PointLatLng latlng)
        {
            var pLocal = Provider.Projection.FromLatLngToPixel(latlng, Zoom);
            pLocal.Offset(RenderOffset);
            pLocal.OffsetNegative(CompensationOffset);
            return pLocal;
        }

        /// <summary>
        ///     gets max zoom level to fit rectangle
        /// </summary>
        /// <param name="rect"></param>
        /// <returns></returns>
        public int GetMaxZoomToFitRect(RectLatLng rect)
        {
            int zoom = MinZoom;

            if (rect.HeightLat == 0 || rect.WidthLng == 0)
            {
                zoom = MaxZoom / 2;
            }
            else
            {
                for (int i = zoom; i <= MaxZoom; i++)
                {
                    var p1 = Provider.Projection.FromLatLngToPixel(rect.LocationTopLeft, i);
                    var p2 = Provider.Projection.FromLatLngToPixel(rect.LocationRightBottom, i);

                    if (p2.X - p1.X <= Width + 10 && p2.Y - p1.Y <= Height + 10)
                    {
                        zoom = i;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return zoom;
        }

        public void RebaseDragAfterExternalOffset(GPoint currentMouse)
        {
            MouseCurrent = currentMouse;

            _dragPoint.X = currentMouse.X - RenderOffset.X;
            _dragPoint.Y = currentMouse.Y - RenderOffset.Y;

            IsDragging = true;
        }

        /// <summary>
        ///     initiates map dragging
        /// </summary>
        /// <param name="pt"></param>
        public void BeginDrag(GPoint pt)
        {
            _dragPoint.X = pt.X - RenderOffset.X;
            _dragPoint.Y = pt.Y - RenderOffset.Y;
            IsDragging = true;
        }

        /// <summary>
        ///     ends map dragging
        /// </summary>
        public void EndDrag()
        {
            IsDragging = false;
            MouseDown = GPoint.Empty;

            Refresh.Set();
        }

        /// <summary>
        ///     reloads map
        /// </summary>
        public void ReloadMap()
        {
            if (IsStarted)
            {
                Debug.WriteLine("------------------");

                _okZoom = 0;
                _skipOverZoom = 0;

                CancelAsyncTasks();

                Matrix.ClearAllLevels();

                lock (FailedLoads)
                {
                    FailedLoads.Clear();
                    _raiseEmptyTileError = true;
                }

                Refresh.Set();

                UpdateBounds();
            }
            else
            {
                throw new Exception("Please, do not call ReloadMap before form is loaded, it's useless");
            }
        }

        public Task ReloadMapAsync()
        {
            if (_isReloading)
                return _reloadCompletionSource.Task;

            ReloadMap();
            _isReloading = true;
            _reloadCompletionSource = new TaskCompletionSource<bool>();
            return _reloadCompletionSource.Task;
        }

        /// <summary>
        ///     moves current position into map center
        /// </summary>
        public void GoToCurrentPosition()
        {
            CompensationOffset = _positionPixel; // TODO: fix

            // reset stuff
            RenderOffset = GPoint.Empty;
            _dragPoint = GPoint.Empty;

            //var dd = new GPoint(-(CurrentPositionGPixel.X - Width / 2), -(CurrentPositionGPixel.Y - Height / 2));
            //dd.Offset(compensationOffset);

            var d = new GPoint(Width / 2, Height / 2);

            Drag(d);
        }

        public bool MouseWheelZooming = false;

        /// <summary>
        ///     moves current position into map center
        /// </summary>
        internal void GoToCurrentPositionOnZoom()
        {
            CompensationOffset = _positionPixel; // TODO: fix

            // reset stuff
            RenderOffset = GPoint.Empty;
            _dragPoint = GPoint.Empty;

            // goto location and centering
            if (MouseWheelZooming)
            {
                if (MouseWheelZoomType != MouseWheelZoomType.MousePositionWithoutCenter)
                {
                    var pt = new GPoint(-(_positionPixel.X - Width / 2), -(_positionPixel.Y - Height / 2));
                    pt.Offset(CompensationOffset);
                    RenderOffset.X = pt.X - _dragPoint.X;
                    RenderOffset.Y = pt.Y - _dragPoint.Y;
                }
                else // without centering
                {
                    RenderOffset.X = -_positionPixel.X - _dragPoint.X;
                    RenderOffset.Y = -_positionPixel.Y - _dragPoint.Y;
                    RenderOffset.Offset(MouseLastZoom);
                    RenderOffset.Offset(CompensationOffset);
                }
            }
            else // use current map center
            {
                MouseLastZoom = GPoint.Empty;

                var pt = new GPoint(-(_positionPixel.X - Width / 2), -(_positionPixel.Y - Height / 2));
                pt.Offset(CompensationOffset);
                RenderOffset.X = pt.X - _dragPoint.X;
                RenderOffset.Y = pt.Y - _dragPoint.Y;
            }

            UpdateCenterTileXYLocation();
        }

        /// <summary>
        ///     darg map by offset in pixels
        /// </summary>
        /// <param name="offset"></param>
        public void DragOffset(GPoint offset)
        {
            var newOffset = RenderOffset;
            newOffset.Offset(offset);

            RenderOffset = newOffset;

            UpdateCenterTileXYLocation();

            if (CenterTileXYLocation != _centerTileXYLocationLast)
            {
                _centerTileXYLocationLast = CenterTileXYLocation;
                QueueBoundsUpdate();
            }

            {
                LastLocationInBounds = Position;
                IsDragging = true;
                try
                {
                    Position = FromLocalToLatLng(Width / 2, Height / 2);
                }
                finally
                {
                    IsDragging = false;
                }
            }

            if (OnMapDrag != null)
            {
                OnMapDrag();
            }
        }

        /// <summary>
        ///     drag map
        /// </summary>
        /// <param name="pt"></param>
        public void Drag(GPoint pt)
        {
            var newOffsetX = pt.X - _dragPoint.X;
            var newOffsetY = pt.Y - _dragPoint.Y;

            RenderOffset.X = newOffsetX;
            RenderOffset.Y = newOffsetY;

            UpdateCenterTileXYLocation();

            if (CenterTileXYLocation != _centerTileXYLocationLast)
            {
                _centerTileXYLocationLast = CenterTileXYLocation;

                // Mouse input must never wait for tile-list or queue work.
                // During a drag, coalesce requests on a worker and keep moving
                // the already prepared tile window. Programmatic positioning
                // still updates synchronously so callers see a ready viewport.
                if (!IsDragging)
                {
                    UpdateBounds();
                }
                else
                {
                    QueueBoundsUpdate();
                }
            }

            if (IsDragging)
            {
                LastLocationInBounds = Position;
                Position = FromLocalToLatLng(Width / 2, Height / 2);

                if (OnMapDrag != null)
                {
                    OnMapDrag();
                }
            }
        }

        /// <summary>
        ///     cancels tile loaders and bounds checker
        /// </summary>
        public void CancelAsyncTasks()
        {
            Interlocked.Increment(ref _tileViewGeneration);

            lock (_tileViewUpdateSync)
            {
                _pendingTileViewUpdate = null;
            }

            if (IsStarted)
            {
#if NETFRAMEWORK
                //TODO: clear loading
#else
                Monitor.Enter(TileLoadQueue);
                try
                {
                    TileLoadQueue.Clear();
                }
                finally
                {
                    Monitor.Exit(TileLoadQueue);
                }
#endif
            }
        }

        bool _raiseEmptyTileError;

        internal Dictionary<LoadTask, Exception> FailedLoads =
            new Dictionary<LoadTask, Exception>(new LoadTaskComparer());

        internal static readonly int WaitForTileLoadThreadTimeout = 5 * 1000 * 60; // 5 min.

        volatile int _okZoom;
        volatile int _skipOverZoom;

#if NETFRAMEWORK
        static readonly BlockingCollection<LoadTask> TileLoadQueue4 =
            new BlockingCollection<LoadTask>(new ConcurrentStack<LoadTask>());

        static List<Task> _tileLoadQueue4Tasks;
        static int _loadWaitCount;
        void AddLoadTask(LoadTask t)
        {
            if (_tileLoadQueue4Tasks == null)
            {
                lock (TileLoadQueue4)
                {
                    if (_tileLoadQueue4Tasks == null)
                    {
                        _tileLoadQueue4Tasks = new List<Task>();

                        while (_tileLoadQueue4Tasks.Count < GThreadPoolSize)
                        {
                            Debug.WriteLine("creating ProcessLoadTask: " + _tileLoadQueue4Tasks.Count);

                            _tileLoadQueue4Tasks.Add(Task.Factory.StartNew(delegate()
                                {
                                    string ctid = "ProcessLoadTask[" + Thread.CurrentThread.ManagedThreadId + "]";
                                    Thread.CurrentThread.Name = ctid;

                                    Debug.WriteLine(ctid + ": started");
                                    do
                                    {
                                        if (TileLoadQueue4.Count == 0)
                                        {
                                            Debug.WriteLine(ctid + ": ready");

                                            if (Interlocked.Increment(ref _loadWaitCount) >= GThreadPoolSize)
                                            {
                                                Interlocked.Exchange(ref _loadWaitCount, 0);
                                                OnLoadComplete(ctid);
                                            }
                                        }

                                        ProcessLoadTask(TileLoadQueue4.Take(), ctid);
                                    } while (!TileLoadQueue4.IsAddingCompleted);

                                    Debug.WriteLine(ctid + ": exit");
                                },
                                TaskCreationOptions.LongRunning));
                        }
                    }
                }
            }

            TileLoadQueue4.Add(t);
        }
#else
        void TileLoadThread()
        {
            LoadTask? task = null;
            bool stop = false;

            var ct = Thread.CurrentThread;
            string ctid = "Thread[" + ct.ManagedThreadId + "]";
            while (!stop && IsStarted)
            {
                task = null;

                Monitor.Enter(TileLoadQueue);
                try
                {
                    while (TileLoadQueue.Count == 0)
                    {
                        if (!IsStarted || false == Monitor.Wait(TileLoadQueue, WaitForTileLoadThreadTimeout, false) || !IsStarted)
                        {
                            stop = true;
                            break;
                        }
                    }

                    if (IsStarted && !stop && TileLoadQueue.Count > 0)
                    {
                        task = TileLoadQueue.Pop();
                        _tileLoadsInProgress.Add(task.Value);
                    }
                }
                finally
                {
                    Monitor.Exit(TileLoadQueue);
                }

                if (task.HasValue)
                {
                    try
                    {
                        if (IsStarted)
                        {
                            ProcessLoadTask(task.Value, ctid);
                        }
                    }
                    finally
                    {
                        var completedBatchId = 0;

                        Monitor.Enter(TileLoadQueue);
                        try
                        {
                            _tileLoadsInProgress.Remove(task.Value);

                            if (TileLoadQueue.Count == 0 &&
                                _tileLoadsInProgress.Count == 0 &&
                                _completedTileLoadBatchId !=
                                _tileLoadBatchId)
                            {
                                _completedTileLoadBatchId =
                                    _tileLoadBatchId;
                                completedBatchId = _tileLoadBatchId;
                            }
                        }
                        finally
                        {
                            Monitor.Exit(TileLoadQueue);
                        }

                        if (completedBatchId > 0 &&
                            IsStarted &&
                            IsTileLoadBatchStillComplete(completedBatchId))
                        {
                            OnLoadComplete(ctid);
                        }
                    }
                }
            }

            Monitor.Enter(TileLoadQueue);
            try
            {
                Debug.WriteLine("Quit - " + ct.Name);
                lock (_gThreadPool)
                {
                    _gThreadPool.Remove(ct);
                }
            }
            finally
            {
                Monitor.Exit(TileLoadQueue);
            }
        }
#endif

        static void ProcessLoadTask(LoadTask task, string ctid)
        {
            try
            {
                #region -- execute --

                var matrix = task.Core.Matrix;
                if (matrix == null)
                {
                    return;
                }

                var m = task.Core.Matrix.GetTileWithReadLock(task.Zoom, task.Pos);
                if (!m.NotEmpty)
                {
                    var t = new Tile(task.Zoom, task.Pos);

                    foreach (var tl in task.Core._provider.Overlays)
                    {
                        int retry = 0;
                        do
                        {
                            PureImage img = null;
                            Exception ex = null;

                            if (task.Zoom >= task.Core._provider.MinZoom &&
                                (!task.Core._provider.MaxZoom.HasValue || task.Zoom <= task.Core._provider.MaxZoom))
                            {
                                if (task.Core._skipOverZoom == 0 || task.Zoom <= task.Core._skipOverZoom)
                                {
                                    // tile number inversion(BottomLeft -> TopLeft)
                                    if (tl.InvertedAxisY)
                                    {
                                        img = GMaps.Instance.GetImageFrom(tl,
                                            new GPoint(task.Pos.X, task.Core._maxOfTiles.Height - task.Pos.Y),
                                            task.Zoom,
                                            out ex);
                                    }
                                    else // ok
                                    {
                                        img = GMaps.Instance.GetImageFrom(tl, task.Pos, task.Zoom, out ex);
                                    }
                                }
                            }

                            if (img != null && ex == null)
                            {
                                if (task.Core._okZoom < task.Zoom)
                                {
                                    task.Core._okZoom = task.Zoom;
                                    task.Core._skipOverZoom = 0;
                                    Debug.WriteLine("skipOverZoom disabled, okZoom: " + task.Core._okZoom);
                                }
                            }
                            else if (ex != null)
                            {
                                if (task.Core._skipOverZoom != task.Core._okZoom && task.Zoom > task.Core._okZoom)
                                {
                                    if (ex.Message.Contains("(404) Not Found"))
                                    {
                                        task.Core._skipOverZoom = task.Core._okZoom;
                                        Debug.WriteLine("skipOverZoom enabled: " + task.Core._skipOverZoom);
                                    }
                                }
                            }

                            // check for parent tiles if not found
                            if (img == null && task.Core._okZoom > 0 && task.Core.FillEmptyTiles &&
                                task.Core.Provider.Projection is MercatorProjection)
                            {
                                int zoomOffset = task.Zoom > task.Core._okZoom ? task.Zoom - task.Core._okZoom : 1;
                                long ix = 0;
                                var parentTile = GPoint.Empty;

                                while (img == null && zoomOffset < task.Zoom)
                                {
                                    ix = (long)Math.Pow(2, zoomOffset);
                                    parentTile = new GPoint(task.Pos.X / ix, task.Pos.Y / ix);
                                    img = GMaps.Instance.GetImageFrom(tl, parentTile, task.Zoom - zoomOffset++, out ex);
                                }

                                if (img != null)
                                {
                                    // offsets in quadrant
                                    long xOff = Math.Abs(task.Pos.X - parentTile.X * ix);
                                    long yOff = Math.Abs(task.Pos.Y - parentTile.Y * ix);

                                    img.IsParent = true;
                                    img.Ix = ix;
                                    img.Xoff = xOff;
                                    img.Yoff = yOff;

                                    // wpf
                                    //var geometry = new RectangleGeometry(new Rect(Core.tileRect.X + 0.6, Core.tileRect.Y + 0.6, Core.tileRect.Width + 0.6, Core.tileRect.Height + 0.6));
                                    //var parentImgRect = new Rect(Core.tileRect.X - Core.tileRect.Width * Xoff + 0.6, Core.tileRect.Y - Core.tileRect.Height * Yoff + 0.6, Core.tileRect.Width * Ix + 0.6, Core.tileRect.Height * Ix + 0.6);

                                    // gdi+
                                    //System.Drawing.Rectangle dst = new System.Drawing.Rectangle((int)Core.tileRect.X, (int)Core.tileRect.Y, (int)Core.tileRect.Width, (int)Core.tileRect.Height);
                                    //System.Drawing.RectangleF srcRect = new System.Drawing.RectangleF((float)(Xoff * (img.Img.Width / Ix)), (float)(Yoff * (img.Img.Height / Ix)), (img.Img.Width / Ix), (img.Img.Height / Ix));
                                }
                            }

                            if (img != null)
                            {
                                {
                                    t.AddOverlay(img);
                                }
                                break;
                            }
                            else
                            {
                                if (ex != null && task.Core.FailedLoads != null)
                                {
                                    lock (task.Core.FailedLoads)
                                    {
                                        if (!task.Core.FailedLoads.ContainsKey(task))
                                        {
                                            task.Core.FailedLoads.Add(task, ex);

                                            if (task.Core.OnEmptyTileError != null)
                                            {
                                                if (!task.Core._raiseEmptyTileError)
                                                {
                                                    task.Core._raiseEmptyTileError = true;
                                                    task.Core.OnEmptyTileError(task.Zoom, task.Pos);
                                                }
                                            }
                                        }
                                    }
                                }

                                if (task.Core.RetryLoadTile > 0)
                                {
                                    {
                                        Thread.Sleep(1111);
                                    }
                                }
                            }
                        } while (++retry < task.Core.RetryLoadTile);
                    }

                    if (t.HasAnyOverlays && task.Core.IsStarted)
                    {
                        task.Core.Matrix.SetTile(t);
                    }
                    else
                    {
                        t.Dispose();
                    }
                }

                #endregion
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ctid + " - ProcessLoadTask: " + ex.ToString());
            }
            finally
            {
                if (task.Core.Refresh != null)
                {
                    task.Core.Refresh.Set();
                }
            }
        }

        void OnLoadComplete(string ctid)
        {
            _lastTileLoadEnd = DateTime.Now;
            long lastTileLoadTimeMs = (long)(_lastTileLoadEnd - _lastTileLoadStart).TotalMilliseconds;

            #region -- clear stuff--

            lock (_tileViewCommitSync)
            {
                if (IsStarted && Matrix != null)
                {
                    GMaps.Instance.MemoryCache.RemoveOverload();

                    TileDrawingListLock.AcquireReaderLock();
                    try
                    {
                        Matrix.ClearLevelAndPointsNotIn(
                            Zoom,
                            TileDrawingList);
                    }
                    finally
                    {
                        TileDrawingListLock.ReleaseReaderLock();
                    }
                }
            }

            #endregion

            UpdateGroundResolution();
#if UseGC
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
#endif
            if (OnTileLoadComplete != null)
            {
                OnTileLoadComplete(lastTileLoadTimeMs);
            }

            if (_isReloading)
            {
                _isReloading = false;
                _reloadCompletionSource?.TrySetResult(true);
                _reloadCompletionSource = null;
            }
        }

        public AutoResetEvent Refresh = new AutoResetEvent(false);

        public bool UpdatingBounds;

        private sealed class TileViewUpdateRequest
        {
            public int Generation;
            public int Zoom;
            public GPoint CenterTile;
            public GSize MapArea;
            public GSize MinimumTile;
            public GSize MaximumTile;
            public long TileWidth;
            public long TileHeight;
            public double ScaleX;
            public double ScaleY;
            public GMapProvider Provider;
        }

        /// <summary>
        ///     updates map bounds
        /// </summary>
        void UpdateBounds()
        {
            if (!IsStarted || Provider.Equals(EmptyProvider.Instance))
            {
                return;
            }

            ApplyTileViewUpdate(CreateTileViewUpdateRequest());
        }

        /// <summary>
        /// Queues the latest viewport while the user is panning. Only one
        /// worker is active; newer requests replace work which has not started.
        /// </summary>
        private void QueueBoundsUpdate()
        {
            if (!IsStarted || Provider.Equals(EmptyProvider.Instance))
            {
                return;
            }

            var request = CreateTileViewUpdateRequest();

            lock (_tileViewUpdateSync)
            {
                _pendingTileViewUpdate = request;

                if (_tileViewUpdateWorkerRunning)
                {
                    return;
                }

                _tileViewUpdateWorkerRunning = true;
            }

            ThreadPool.QueueUserWorkItem(_ => ProcessQueuedTileViewUpdates());
        }

        private void ProcessQueuedTileViewUpdates()
        {
            while (true)
            {
                TileViewUpdateRequest request;

                lock (_tileViewUpdateSync)
                {
                    request = _pendingTileViewUpdate;
                    _pendingTileViewUpdate = null;

                    if (request == null)
                    {
                        _tileViewUpdateWorkerRunning = false;
                        return;
                    }
                }

                ApplyTileViewUpdate(request);
            }
        }

        private TileViewUpdateRequest CreateTileViewUpdateRequest()
        {
            return new TileViewUpdateRequest
            {
                Generation = Interlocked.Increment(ref _tileViewGeneration),
                Zoom = Zoom,
                CenterTile = CenterTileXYLocation,
                MapArea = _sizeOfMapArea,
                MinimumTile = _minOfTiles,
                MaximumTile = _maxOfTiles,
                TileWidth = TileRect.Width,
                TileHeight = TileRect.Height,
                ScaleX = ScaleX,
                ScaleY = ScaleY,
                Provider = Provider
            };
        }

        private bool IsTileViewUpdateCurrent(TileViewUpdateRequest request)
        {
            return request != null &&
                   request.Generation == Volatile.Read(ref _tileViewGeneration) &&
                   IsStarted &&
                   request.Zoom == Zoom &&
                   ReferenceEquals(request.Provider, Provider);
        }

        private void ApplyTileViewUpdate(TileViewUpdateRequest request)
        {
            if (!IsTileViewUpdateCurrent(request))
            {
                return;
            }

            UpdatingBounds = true;

            try
            {
                var tiles = TilePrefetchPlanner.Build(
                    request.CenterTile,
                    request.MapArea,
                    request.MinimumTile,
                    request.MaximumTile,
                    request.TileWidth,
                    request.TileHeight,
                    request.ScaleX,
                    request.ScaleY,
                    AdditionalPrefetchTileMargin,
                    GMaps.Instance.ShuffleTilesOnLoad);
                var raiseTileLoadStart = false;
                var wakeTileLoaders = false;
                var completedCanceledBatchId = 0;

                lock (_tileViewCommitSync)
                {
                    if (!IsTileViewUpdateCurrent(request) || Matrix == null)
                    {
                        return;
                    }

#if !NETFRAMEWORK
                    var missingTasks = new HashSet<LoadTask>(
                        new LoadTaskComparer());

                    Matrix.EnterReadLock();
                    try
                    {
                        foreach (var tile in tiles)
                        {
                            if (!Matrix.GetTileWithNoLock(
                                    request.Zoom,
                                    tile.PosXY).NotEmpty)
                            {
                                missingTasks.Add(new LoadTask(
                                    tile.PosXY,
                                    request.Zoom,
                                    this));
                            }
                        }
                    }
                    finally
                    {
                        Matrix.LeaveReadLock();
                    }

                    if (!IsTileViewUpdateCurrent(request))
                    {
                        return;
                    }
#endif

                    TileDrawingListLock.AcquireWriterLock();
                    try
                    {
                        TileDrawingList.Clear();
                        TileDrawingList.AddRange(tiles);
                    }
                    finally
                    {
                        TileDrawingListLock.ReleaseWriterLock();
                    }

#if NETFRAMEWORK
                    foreach (var tile in tiles)
                    {
                        AddLoadTask(new LoadTask(
                            tile.PosXY,
                            request.Zoom,
                            this));
                    }

                    raiseTileLoadStart = tiles.Count > 0;
#else
                    Monitor.Enter(TileLoadQueue);
                    try
                    {
                        var wasLoading = TileLoadQueue.Count > 0 ||
                                         _tileLoadsInProgress.Count > 0;
                        var queuedTasks = new HashSet<LoadTask>(
                            _tileLoadsInProgress,
                            new LoadTaskComparer());

                        // Rebuild from the newest prepared window. This drops
                        // stale pending requests but leaves active I/O alone.
                        TileLoadQueue.Clear();

                        foreach (var tile in tiles)
                        {
                            var task = new LoadTask(
                                tile.PosXY,
                                request.Zoom,
                                this);

                            if (missingTasks.Contains(task) &&
                                queuedTasks.Add(task))
                            {
                                TileLoadQueue.Push(task);
                            }
                        }

                        if (TileLoadQueue.Count > 0)
                        {
                            _tileLoadBatchId++;
                            raiseTileLoadStart = !wasLoading;
                            wakeTileLoaders = true;
                        }
                        else if (wasLoading &&
                                 _tileLoadsInProgress.Count == 0 &&
                                 _completedTileLoadBatchId !=
                                 _tileLoadBatchId)
                        {
                            _completedTileLoadBatchId = _tileLoadBatchId;
                            completedCanceledBatchId = _tileLoadBatchId;
                        }
                    }
                    finally
                    {
                        Monitor.Exit(TileLoadQueue);
                    }
#endif
                }

                if (raiseTileLoadStart)
                {
                    _lastTileLoadStart = DateTime.Now;
                    OnTileLoadStart?.Invoke();
                }

#if !NETFRAMEWORK
                EnsureTileLoaderThreads();

                if (wakeTileLoaders)
                {
                    Monitor.Enter(TileLoadQueue);
                    try
                    {
                        Monitor.PulseAll(TileLoadQueue);
                    }
                    finally
                    {
                        Monitor.Exit(TileLoadQueue);
                    }
                }
#endif

                UpdateGroundResolution();
                Refresh?.Set();

#if !NETFRAMEWORK
                if (completedCanceledBatchId > 0 &&
                    IsTileLoadBatchStillComplete(
                        completedCanceledBatchId))
                {
                    OnLoadComplete("TileViewUpdate");
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateBounds] ERROR: {ex}");
            }
            finally
            {
                UpdatingBounds = false;
            }
        }

#if !NETFRAMEWORK
        private void EnsureTileLoaderThreads()
        {
            lock (_gThreadPool)
            {
                while (IsStarted && _gThreadPool.Count < GThreadPoolSize)
                {
                    var thread = new Thread(TileLoadThread)
                    {
                        Name = "TileLoader: " + _gThreadPool.Count,
                        IsBackground = true,
                        Priority = ThreadPriority.BelowNormal
                    };

                    _gThreadPool.Add(thread);
                    thread.Start();
                }
            }
        }

        private bool IsTileLoadBatchStillComplete(int batchId)
        {
            Monitor.Enter(TileLoadQueue);
            try
            {
                return batchId == _tileLoadBatchId &&
                       batchId == _completedTileLoadBatchId &&
                       TileLoadQueue.Count == 0 &&
                       _tileLoadsInProgress.Count == 0;
            }
            finally
            {
                Monitor.Exit(TileLoadQueue);
            }
        }
#endif

        /// <summary>
        ///     updates ground resolution info
        /// </summary>
        void UpdateGroundResolution()
        {
            double rez = Provider.Projection.GetGroundResolution(Zoom, Position.Lat);
            PxRes100M = (int)(100.0 / rez); // 100 meters
            PxRes1000M = (int)(1000.0 / rez); // 1km  
            PxRes10Km = (int)(10000.0 / rez); // 10km
            PxRes100Km = (int)(100000.0 / rez); // 100km
            PxRes1000Km = (int)(1000000.0 / rez); // 1000km
            PxRes5000Km = (int)(5000000.0 / rez); // 5000km
        }

        #region IDisposable Members

        ~Core()
        {
            Dispose(false);
        }

        void Dispose(bool disposing)
        {
            if (IsStarted)
            {
                CancelAsyncTasks();
                IsStarted = false;

                if (_invalidator != null)
                {
                    _invalidator.CancelAsync();
                    _invalidator.DoWork -= InvalidatorWatch;
                    _invalidator.Dispose();
                    _invalidator = null;
                }

                if (Refresh != null)
                {
                    Refresh.Set();
                    Refresh.Close();
                    Refresh = null;
                }

                int x = Interlocked.Decrement(ref Instances);
                Debug.WriteLine("OnMapClose: " + x);

                lock (_tileViewCommitSync)
                {
                    if (Matrix != null)
                    {
                        Matrix.Dispose();
                        Matrix = null;
                    }
                }

                if (FailedLoads != null)
                {
                    lock (FailedLoads)
                    {
                        FailedLoads.Clear();
                        _raiseEmptyTileError = false;
                    }

                    FailedLoads = null;
                }

                TileDrawingListLock.AcquireWriterLock();
                try
                {
                    TileDrawingList.Clear();
                }
                finally
                {
                    TileDrawingListLock.ReleaseWriterLock();
                }

#if NETFRAMEWORK
                //TODO: maybe
#else
                // cancel waiting loaders
                Monitor.Enter(TileLoadQueue);
                try
                {
                    Monitor.PulseAll(TileLoadQueue);
                }
                finally
                {
                    Monitor.Exit(TileLoadQueue);
                }
#endif

                if (TileDrawingListLock != null)
                {
                    TileDrawingListLock.Dispose();
                    TileDrawingListLock = null;
                    TileDrawingList = null;
                }

                if (x == 0)
                {
#if DEBUG
                    GMaps.Instance.CancelTileCaching();
#endif
                    GMaps.Instance.NoMapInstances = true;
                    GMaps.Instance.WaitForCache.Set();
                    if (disposing)
                    {
                        GMaps.Instance.MemoryCache.Clear();
                    }
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    /// <summary>
    /// Builds the immutable tile window used for drawing and background
    /// prefetch. Kept separate from Core so window sizing and load order can
    /// be verified without starting network or WPF infrastructure.
    /// </summary>
    internal static class TilePrefetchPlanner
    {
        internal static List<DrawTile> Build(
            GPoint centerTile,
            GSize mapArea,
            GSize minimumTile,
            GSize maximumTile,
            long tileWidth,
            long tileHeight,
            double scaleX,
            double scaleY,
            int additionalMargin,
            bool shuffle)
        {
            additionalMargin = Math.Max(0, additionalMargin);

            var minimumX = (long)Math.Floor(-mapArea.Width * scaleX) -
                           additionalMargin;
            var maximumX = (long)Math.Ceiling(mapArea.Width * scaleX) +
                           additionalMargin;
            var minimumY = (long)Math.Floor(-mapArea.Height * scaleY) -
                           additionalMargin;
            var maximumY = (long)Math.Ceiling(mapArea.Height * scaleY) +
                           additionalMargin;

            var width = Math.Max(0L, maximumX - minimumX + 1);
            var height = Math.Max(0L, maximumY - minimumY + 1);
            var capacity = (int)Math.Min(int.MaxValue, width * height);
            var tiles = new List<DrawTile>(capacity);

            for (var xOffset = minimumX; xOffset <= maximumX; xOffset++)
            {
                for (var yOffset = minimumY; yOffset <= maximumY; yOffset++)
                {
                    var position = centerTile;
                    position.X += xOffset;
                    position.Y += yOffset;

                    if (position.X < minimumTile.Width ||
                        position.Y < minimumTile.Height ||
                        position.X > maximumTile.Width ||
                        position.Y > maximumTile.Height)
                    {
                        continue;
                    }

                    tiles.Add(new DrawTile
                    {
                        PosXY = position,
                        PosPixel = new GPoint(
                            position.X * tileWidth,
                            position.Y * tileHeight),
                        DistanceSqr =
                            (centerTile.X - position.X) *
                            (centerTile.X - position.X) +
                            (centerTile.Y - position.Y) *
                            (centerTile.Y - position.Y)
                    });
                }
            }

            if (shuffle)
            {
                Stuff.Shuffle(tiles);
            }
            else
            {
                // DrawTile sorts farthest first. Pushing in this order leaves
                // the nearest, visible tiles on top of the loader stack.
                tiles.Sort();
            }

            return tiles;
        }
    }
}
