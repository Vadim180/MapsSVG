using Accord.Math;
using CoordinateSharp;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using Maps.Models;
using Maps.Services;
using Maps.Views;
using Maps.Controllers;
using Microsoft.VisualBasic.Logging;
using Newtonsoft.Json;
using ProjNet.CoordinateSystems;
using Svg;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;


namespace Maps
{
    public partial class Maps : Form, Controllers.IReportContext, Controllers.IInputContext
    {
        private RectLatLng _allowedArea;   // область, в межах якої живе карта
        private bool _isAdjustingPosition;   // <-- додай це
        private Point _lastMouse;

        private GMap.NET.WindowsForms.GMapControl? mapControl;

        // Абстракція провайдера карти (поступова міграція на IMapProvider)
        private Services.Map.GMapProvider? _gmapProvider;
        private Services.Map.SvgMapProvider? _svgProvider;
        private Controllers.MapController? _mapController;
        private Controllers.InputController? _inputController;

        // Report controller (handles report generation and button logic)
        private Controllers.ReportController? _reportController;

        private bool _dragging;
        private double _minZoomForAllowed = 0;
        private bool _isAdjustingZoom = false;

        private static readonly Random random = new Random();
        private List<ReferencePoint> referencePoints = new(); // Список точок для зберігання координат
        // Афінні коефіцієнти тепер керуються через CoordinateConverter
        private Control[] previousControls; // Зберігає елементи з panelOptions перед "Налаштуванням"

        private Stack<Control> panelHistory = new Stack<Control>();
        private Control previousControl = null;

        // Перехідна залежність: CoordinateConverter обробляє всі перетворення
        private Services.Map.CoordinateConverter? coordinateConverter = null;

        // Сервіс для завантаження даних населених пунктів
        private Services.Map.LocalityService? localityService = null;

        private Dictionary<string, List<PointF>> _scaledPointsCache = new();  //для кешування назв населених пунктів

        //захист від декількох потоків якщо викликається з різних місць
        private readonly object _invalidateLock = new object();
        private volatile bool _isInvalidating = false;            //це теж


        private HashSet<string> highlightedLocalities = new();

        private float _scale = 1.0f;                        //основний масштаб карти
        private Size _originalImageSize;                    //Оригінальний розмір мапи
        private Bitmap? cachedBitmap = new Bitmap(1000,1000);                      //Закешоване зображення використовується для малювання на PictureBox


        private PointF _imageOffset = new PointF(0, 0);     //зміщення зображення відносно PictureBox
        private Point _panStart;                            //точка, з якої почалося переміщення
        private PointF? clickedPointMarker = null;          // Точка, в якій було натиснуто мишкою для вимірювання відстані
        private bool _isPanning = false;                    //чи виконується зараз переміщення



        private int currentCornerIndex = 0; // Стан: яка точка зараз вводиться (4)
        private bool isCollectingCorners = false; // Прапорець, чи активний режим збору
        private PointF[] calibrationMarkers = new PointF[4];

        private PointF? _courseStartPoint = null;    // коли добавив ці 2 строчки і їх потім використовував, то перестала запускатися програма 
        private PointF? _courseEndPoint = null;      //
        private RectangleF expectedCornerRegion;

        private bool isRotateLeftPressed = false;
        private bool isRotateRightPressed = false;
        private bool _isSingleLineFormat = false;           // false - з перенесенням, true - в один рядок
        private bool isMeasuringPixel = false;              // вимірювання пікселів, 
        private bool isStart_and_Work = false;                     // Перевіряє чи був час між початком та кінцем робортти
        private bool isScaleMarkersVisible = false;         // Перевіряє чи підсвічувати 2 точки для встановлення масштабу між ними
        private bool isAttakPointSet = false;                 // Дає можливість встановити точку атаки
        private bool _saved;
        private bool _isReportTemplate = false;              //Перевіряє чи натиснуто бойова робота
        private bool IsMainTabActive => panelMainView?.Visible == true;   // Повертає true, коли показана панель з головним екраном (panelMainView.Visible == true).

        private float _mapMinUtmX, _mapMaxUtmX, _mapMinUtmY, _mapMaxUtmY;    //поля для зберігання меж карти

        private bool _isInteracting = false;     // Прапорець "йде взаємодія" (пан/зум/drag), щоб в Paint знижувати якість для швидкості

        private readonly Stopwatch _fpsSw = Stopwatch.StartNew(); // FPS-тротлінг перемальовування (~60 FPS)
        private const int MinFrameMs = 16;



        private System.Windows.Forms.Timer? timer;

        // Модель зони атаки
        private Models.AttackZone attackZone = new Models.AttackZone
        {
            AttackPoint = PointF.Empty,
            Angle = 0f,
            RayLength = 2500f,
            SectorRadius = 2500f,
            SectorWidth = 30f
        };

        float labelScale_serva_attak = 10f; // кут для сервоприводу
        private int selectedrange_tmp = 0;
        private int? attackCourse = null; // null означає, що курс не встановлено
        private float? pixelsToMeters = null; //null означає, що калібрування відстані не встановлено


        // Змінні для зберігання контролів
        private UserControl currentControl;
        //private readonly MainControl mainControl = new MainControl(); // Ваш основний контрол
        private readonly SettingsControl settingsControl = new SettingsControl(); // Контрол налаштувань

        //координати
        //private int X = 941;
        //private int Y = 823;


        private readonly ShablonManager Shablon = new ShablonManager();
        private readonly HashSet<string> LocalCities = new(StringComparer.CurrentCultureIgnoreCase);
        private DateTime time_Start;
        private string timeString = "";  // час закінчення польоту
        private string azimyth_Combat = "0";

        private readonly string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Maps", "MapTest.svg"); //шлях до мапи

        // Startup fallback lat/lon (user-specified)
        private readonly GMap.NET.PointLatLng StartupLatLng = new GMap.NET.PointLatLng(49.707398, 37.570155); // requested startup center

        private readonly string filePath_shablon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shablon.json");
        private readonly string calibrationFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        private readonly string calibrationFilePath;
        private readonly string attackPointPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings", "attack_point.json");
        private readonly string userSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings", "userSettings.json");
        private FlightLogger flightLogger;
        private readonly string scaleFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings", "scale.json");

        string overridesPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Maps", "UserOverrides.json");

        private bool shown = false;
        private void Maps_KeyDown(object sender, KeyEventArgs e)// стрілки в вкл состояніі
        {
            // Debug shortcut: Ctrl+Shift+S -> set a sample attack point at map center
            if (e.Control && e.Shift && e.KeyCode == Keys.S)
            {
                SetSampleAttackPoint();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            try
            {
                _inputController?.OnKeyDown(sender, e);
            }
            catch { }
        }

        private void Maps_KeyUp(object sender, KeyEventArgs e)// стрілки в викл состояніі
        {
            try
            {
                _inputController?.OnKeyUp(sender, e);
            }
            catch { }
        }

        // === ХЕЛПЕРИ ДЛЯ СТРІЛОК ===
        private void Control_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.A || e.KeyCode == Keys.D || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                e.IsInputKey = true; // Запобігає використанню клавіш A, D та стрілок для навігації фокусом
        }

        // Метод MarkArrowsAsInput залишається без змін
        private void MarkArrowsAsInput(Control root)
        {
            root.PreviewKeyDown -= Control_PreviewKeyDown;
            root.PreviewKeyDown += Control_PreviewKeyDown;

            foreach (Control c in root.Controls)
                MarkArrowsAsInput(c); // Рекурсивно застосовується до всіх вкладених елементів
        }


        public Maps()
        {
            InitializeComponent();

            this.FormClosing += Maps_FormClosing;

            this.WindowState = FormWindowState.Maximized;

            this.KeyPreview = true;

            calibrationFilePath = Path.Combine(calibrationFolderPath, "calibration.json");

            Directory.CreateDirectory(Path.GetDirectoryName(overridesPath)!);

            // або користувацькі, або дефолти
            Shablon.LoadAllData(); // дефолти з коду

            if (!Directory.Exists(calibrationFolderPath))
            {
                Directory.CreateDirectory(calibrationFolderPath);
            }

            if (!File.Exists(filePath))
            {
                MessageBox.Show("Файл не знайдено: " + filePath, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // Подвійна буферизація, щоб не миготіло і не фрізило
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();  

            // Увімкнути DoubleBuffered для pictureBox1 через рефлексію
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(pictureBox1, true, null);

            // Коректне масштабування на різних DPI
            this.AutoScaleMode = AutoScaleMode.Dpi;

            ResizePanels();

            CreateEmptyJsonFile();
            GenerateButtons(Shablon.LocalCiti);

            long memoryUsage = GC.GetTotalMemory(false);
            Console.WriteLine($"Пам'ять: {memoryUsage / 1024 / 1024} MB");

            // стрілки завжди підуть у KeyDown/KeyUp форми, навіть коли фокус на кнопках/комбобоксах
            MarkArrowsAsInput(panelMainView);
            MarkArrowsAsInput(pictureBox1);

            // Ініціалізуємо сервіси
            localityService = new Services.Map.LocalityService();

            timer.Enabled = false;

            // стартовий фокус на карті
            //this.Shown += (_, __) => { pictureBox1.Select(); pictureBox1.Focus(); shown = true; };
            this.Shown += (_, __) =>
            {
                pictureBox1.Select();
                pictureBox1.Focus();
                UpdateLabelPosition();
                BeginInvoke(new Action(UpdateLabelPosition)); // ще раз після лейауту
            };

        }
        private int frameCount = 0; // Додаємо змінну для підрахунку кадрів
          
        
        private void Gmap_Paint(object? sender, PaintEventArgs e)
        {

            Graphics g = e.Graphics;
            PrepareGraphics(g);
            DrawBaseMap(g);

            if (attackZone.AttackPoint != PointF.Empty)
            {
                DrawAttackZone(g);
                DrawClickedPoint(g);
                DrawCourseLine(g);
            }

            DrawCalibrationMarkers(g);
            DrawCornerHints(g);
            DrawScaleMeasurementMarkers(g);
            DrawHighlightedLocalities(g);
            DrawLocalityLabels(g);
        }


        // Обробка Кліка Мишки
        private void Gmap_MouseDown(object sender, MouseEventArgs e)
        {
            Console.WriteLine($"Gmap_MouseDown Button={e.Button} Modifiers={Control.ModifierKeys} isAttakPointSet={isAttakPointSet} Location={e.Location}");

            // If we are awaiting attack point and user clicks CTRL+Right — set the attack point on GMap
            if (e.Button == MouseButtons.Right && Control.ModifierKeys.HasFlag(Keys.Control) && isAttakPointSet)
            {
                if (mapControl == null) return;

                // Coordinates are in client pixels of the map control
                var newAttackPoint = new PointF(e.X, e.Y);
                attackZone.AttackPoint = newAttackPoint;

                var data = new AttackPointData
                {
                    X = newAttackPoint.X,
                    Y = newAttackPoint.Y,
                    MapWidth = mapControl.Width,
                    MapHeight = mapControl.Height
                };

                var dir = Path.GetDirectoryName(attackPointPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(attackPointPath, JsonConvert.SerializeObject(data, Formatting.Indented));

                isAttakPointSet = false;
                Console.WriteLine($"GMap: saved attack point at {newAttackPoint} (isAttakPointSet={isAttakPointSet})");
                if (label2 != null) label2.Text = $"GMap: attack point set {newAttackPoint}";
                mapControl.Invalidate();
                MessageBox.Show("Точку атаки збережено на GMap.", "Інформація");
                return;
            }

            if (e.Button != MouseButtons.Left || mapControl == null) return;
            _dragging = true;
            _lastMouse = e.Location;
        }

        private void Gmap_MouseMove(object sender, MouseEventArgs e)
        {
            
            if (!_dragging || mapControl == null || _allowedArea.IsEmpty)
                return;

            int dx = e.X - _lastMouse.X;
            int dy = e.Y - _lastMouse.Y;
            if (dx == 0 && dy == 0)
                return;

            var view = mapControl.ViewArea;
            if (view.IsEmpty || mapControl.Width <= 0 || mapControl.Height <= 0)
                return;

            // градуси на 1 піксель у поточному зумі
            double latPerPixel = view.HeightLat / mapControl.Height;
            double lngPerPixel = view.WidthLng / mapControl.Width;

            // Рахуємо новий центр від руху миші
            // (ці знаки зазвичай “правильні” для відчуття як у Google Maps)
            double targetLat = mapControl.Position.Lat + dy * latPerPixel;
            double targetLng = mapControl.Position.Lng - dx * lngPerPixel;

            // Тепер найважливіше:
            // тримаємо ВИДИМУ ОБЛАСТЬ (view) всередині allowed
            // Для цього обмежуємо центр так, щоб краї view не вилізали.

            double halfLat = view.HeightLat / 2.0;
            double halfLng = view.WidthLng / 2.0;

            double minCenterLat = _allowedArea.Bottom + halfLat;
            double maxCenterLat = _allowedArea.Top - halfLat;

            double minCenterLng = _allowedArea.Left + halfLng;
            double maxCenterLng = _allowedArea.Right - halfLng;

            // Якщо view більший за allowed — "влізти" неможливо.
            // Тоді просто не даємо перетягувати (карта стоїть).
            if (minCenterLat > maxCenterLat || minCenterLng > maxCenterLng)
                return;

            // Clamp центру
            double clampedLat = Math.Max(minCenterLat, Math.Min(maxCenterLat, targetLat));
            double clampedLng = Math.Max(minCenterLng, Math.Min(maxCenterLng, targetLng));

            // Якщо вперлись — clamped == поточній позиції → карта не рухається далі
            if (clampedLat != mapControl.Position.Lat || clampedLng != mapControl.Position.Lng)
            {
                mapControl.Position = new PointLatLng(clampedLat, clampedLng);
            }

            _lastMouse = e.Location;
        }

        private void Gmap_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                _dragging = false;
        }
               
        private void SafeInvalidate(RectangleF bounds)
        {
            // Швидка перевірка без блокування
            if (_isInvalidating) return;

            lock (_invalidateLock)
            {
                if (_isInvalidating) return;

                if (_fpsSw.ElapsedMilliseconds >= MinFrameMs)
                {
                    _isInvalidating = true;

                    try
                    {
                        // Потокобезпечна перевірка
                        if (pictureBox1 != null && !pictureBox1.IsDisposed)
                        {
                            frameCount++; // Збільшуємо лічильник кадрів
                            if (_svgProvider != null)
                        {
                            _svgProvider.Refresh();
                        }
                        else
                        {
                            pictureBox1.Invalidate(Rectangle.Round(bounds));
                        }
                        }
                    }
                    finally
                    {
                        _isInvalidating = false;
                        _fpsSw.Restart();
                    }
                }
            }
        }

        private void BtnHome_Click(object sender, EventArgs e)
        {
            panelSettingsView.Visible = false;
            panelMainView.Visible = true;
            btnHome.Enabled = false;
            btnHome.BackColor = Color.LightGray;
            btnSettingsScreen.Enabled = true;
            btnSettingsScreen.BackColor = SystemColors.Control;
            //pictureBox1.Focus();                                // щоб стрілки одразу працювали
            if (timer.Enabled) timer.Stop();                    // на всяк випадок
            //panelSettings.Visible = false;
        }

        private void BtnSettingsScreen_Click(object sender, EventArgs e)
        {
            panelMainView.Visible = false;
            panelSettingsView.Visible = true;
            btnHome.Enabled = true;
            btnHome.BackColor = SystemColors.Control;
            btnSettingsScreen.Enabled = false;
            btnSettingsScreen.BackColor = Color.LightGray;
            var settingsControl = CreateSettingsControl();
            ConfigureSettingsEvents(settingsControl);
            panelSettingsView.Controls.Add(settingsControl);
            if (timer.Enabled) timer.Stop(); // при виході з головної — стоп
        }

        private void EnsureViewAreaInsideAllowed()
        {
            // Restrictions on view area have been removed — do nothing.
            return;
        }

        private void BtnShowGMap_Click(object sender, EventArgs e)
        {           
            try
            {
                if (panelMap == null)
                {
                    MessageBox.Show("panelMap не знайдена!", "Помилка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (_gmapProvider == null)
                {
                    // Ініціалізуємо наш провайдер, він сам створить внутрішній GMapControl та додасть його в panelMap
                    _gmapProvider = new Services.Map.GMapProvider();
                    _gmapProvider.Initialize(panelMap);
                    // Apply requested zoom limits and initial position
                    try {
                        _gmapProvider.SetMinZoom(10);
                        _gmapProvider.SetMaxZoom(25);
                        _gmapProvider.SetZoomLimitEnabled(true);
                        _gmapProvider.SetPosition(StartupLatLng.Lat, StartupLatLng.Lng);
                    } catch { }
                    mapControl = _gmapProvider.Control; // зворотна сумісність з існуючим кодом

                    // Ensure any legacy overlay click marker is cleared when switching to GMap
                    clickedPointMarker = null;

                    // Attempt to set initial map center from calibration / SVG map center to avoid open-ocean start
                    bool positionedFromCalibration = false;
                    try
                    {
                        // Only use calibration if converter is present and marked calibrated and we have 4 reference points
                        if (coordinateConverter != null && coordinateConverter.IsCalibrated && referencePoints.Count >= 4 && _originalImageSize != Size.Empty)
                        {
                            // Prefer attack point if set, otherwise use image center
                            PointF pixelCenter = attackZone.AttackPoint != PointF.Empty ? attackZone.AttackPoint : new PointF(_originalImageSize.Width / 2f, _originalImageSize.Height / 2f);
                            var utmCenter = coordinateConverter.PixelToUTM(pixelCenter);
                            if (utmCenter != PointF.Empty)
                            {
                                if (coordinateConverter.TryUTMToLatLng(utmCenter, out double lat, out double lon))
                                {
                                    // sanity check latitude/longitude ranges
                                    if (!double.IsNaN(lat) && !double.IsNaN(lon) && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180)
                                    {
                                                        Console.WriteLine($"Maps: centering map from calibration to {lat},{lon}");
                                        _gmapProvider.SetPosition(lat, lon);
                                        positionedFromCalibration = true;
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    // Additional safety: schedule a short one-shot timer to re-apply startup center
                    // This handles initialization races where the control may change center after our initial SetPosition
                    try
                    {
                        var startupReapplyTimer = new System.Windows.Forms.Timer();
                        startupReapplyTimer.Interval = 500; // ms
                        startupReapplyTimer.Tick += (s, ev) =>
                        {
                            try
                            {
                                Console.WriteLine("Maps: startup timer tick — re-applying startup position");
                                _gmapProvider.SetPosition(StartupLatLng.Lat, StartupLatLng.Lng);
                            }
                            catch { }
                            finally
                            {
                                try { startupReapplyTimer.Stop(); startupReapplyTimer.Dispose(); } catch { }
                            }
                        };
                        startupReapplyTimer.Start();
                    }
                    catch { }

                    // If we couldn't determine a sensible center from calibration, use configured startup fallback
                    if (!positionedFromCalibration)
                    {
                        try
                        {
                            Console.WriteLine($"Maps: centering map at startup fallback {StartupLatLng.Lat},{StartupLatLng.Lng}");
                            _gmapProvider.SetPosition(StartupLatLng.Lat, StartupLatLng.Lng);
                        }
                        catch (Exception ex) { Console.WriteLine("Maps: failed to set startup position: " + ex.Message); }
                    }

                    // Підключаємо події провайдера до існуючих обробників (малі адаптації)
                    _gmapProvider.OnPositionChanged += p => Gmap_OnPositionChanged(new PointLatLng(p.X, p.Y));

                    // One-shot: re-apply startup position on first zoom-change (handles cases where control overrides position on init)
                    try
                    {
                        if (_gmapProvider.Control != null)
                        {
                            void Handler()
                            {
                                try
                                {
                                    Console.WriteLine("Maps: OnMapZoomChanged fired — re-applying startup position");
                                    _gmapProvider.SetPosition(StartupLatLng.Lat, StartupLatLng.Lng);
                                }
                                catch { }
                                finally
                                {
                                    try { _gmapProvider.Control.OnMapZoomChanged -= Handler; } catch { }
                                }
                            }

                            _gmapProvider.Control.OnMapZoomChanged += Handler;
                        }
                    }
                    catch { }
                    // Клік на GMap (Lat/Lng) -> перетворимо в пікселі контролу та викликаємо старий HandleClickAction
                    _gmapProvider.OnClick += latlng =>
                    {
                        try
                        {
                            var screen = _gmapProvider.GeoToScreen(latlng);
                            HandleGMapClick(screen);
                            try { _gmapProvider.Refresh(); } catch { }
                        }
                        catch { }
                    };

                    // Test overlays: додамо дебажні оверлеї (тестовий маркер та дублювання позиції на поверхню карти)
                    if (_gmapProvider.Control != null)
                    {
                        var posOv = new Services.Map.PositionOverlay(_gmapProvider.Control);
                        _gmapProvider.AddOverlay(posOv);

                        // Overlay for attack zone on GMap
                        _gmapProvider.AddOverlay(new Services.Map.GMapAttackZoneOverlay(() => attackZone));

                        // Marker overlay (calibration / clicked point / scale markers) wrapped for GMap
                        var markerOverlay = new Rendering.Overlays.MarkerOverlay(
                            () => clickedPointMarker,
                            () => calibrationMarkers,
                            () => currentCornerIndex,
                            () => isScaleMarkersVisible,
                            () => referencePoints
                        );

                        // For GMap we render using identity mapping because coordinates are in client pixels
                        _gmapProvider.AddOverlay(new Services.Map.RenderingOverlayAdapter(markerOverlay, p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))));

                        // Додатково лог для перевірки подій позиції
                        _gmapProvider.OnPositionChanged += p => System.Diagnostics.Debug.WriteLine($"Provider.OnPositionChanged: {p.X:F6},{p.Y:F6}");
                    }
                }

                if (pictureBox1 != null)
                {
                    pictureBox1.Visible = false;
                    pictureBox1.SendToBack();
                }

                // Раніше тут встановлювалась 'allowed area' та блокування прокрутки карти.
                // Видаляємо будь-які обмеження переміщення карти/маркерів — тепер користувач може вільно панити/зумити.
                // (Колишні виклики SetBounds / SetLockToAllowedArea видалені)

                // Початкова позиція: немає обмежень — використовуємо (0,0) або існуючу позицію карти
                double centerLat = 0.0;
                double centerLng = 0.0;

                // Налаштування карти
                mapControl.MapProvider = GMap.NET.MapProviders.GMapProviders.GoogleMap;
                GMaps.Instance.Mode = AccessMode.ServerAndCache;

                // ВАЖЛИВО: НЕ використовуємо BoundsOfMap - він конфліктує з нашим обробником
                // mapControl.BoundsOfMap = _allowedArea; // ← ЗАКОМЕНТУЙТЕ ЦЕ

                // Початкова позиція
                mapControl.Position = new GMap.NET.PointLatLng(centerLat, centerLng);
                //mapControl.MinZoom = 3;
                //mapControl.MaxZoom = 20;
                mapControl.Zoom = 11;

                //_lastValidPosition = mapControl.Position;
                                                
                mapControl.MouseDown -= Gmap_MouseDown;
                mapControl.MouseDown += Gmap_MouseDown;

                mapControl.MouseMove -= Gmap_MouseMove;
                mapControl.MouseMove += Gmap_MouseMove;
                
                mapControl.MouseUp -= Gmap_MouseUp;
                mapControl.MouseUp += Gmap_MouseUp;

                mapControl.OnMapZoomChanged -= Map_OnMapZoomChanged;
                mapControl.OnMapZoomChanged += Map_OnMapZoomChanged;

                // ВАЖЛИВО: перерахувати min zoom треба коли контрол уже має розмір.
                // Найпростіше — через BeginInvoke:
                mapControl.BeginInvoke(new Action(() =>
                {
                    // Disabled: previously recalculated min-zoom and enforced view area inside allowed bounds.
                    // Movement/zoom restrictions have been removed to allow free panning/zooming.
                }));

                // 7. Додамо оверлей для маркерів (опційно)
                if (mapControl.Overlays.Count == 0)
                {
                    var overlay = new GMap.NET.WindowsForms.GMapOverlay("markers");
                    mapControl.Overlays.Add(overlay);
                }

                mapControl.Visible = true;
                mapControl.BringToFront();
                mapControl.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка відкриття GMap: {ex.Message}", "Помилка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }

        private void RecalculateMinZoomForAllowed()
        {
            // Movement/zoom restrictions are intentionally disabled.
            // Previously this method calculated a conservative minimum zoom that kept the view inside an allowed area;
            // that behaviour has been removed per user request.
            _minZoomForAllowed = 0;
            if (mapControl != null)
            {
                try { mapControl.MinZoom = 0; } catch { }
            }
        }

        private void Map_OnMapZoomChanged()
        {
            if (mapControl == null || _allowedArea.IsEmpty) return;
            if (_isAdjustingZoom) return;

            // 1) Не даємо зум-аутись нижче мінімального
            if (_minZoomForAllowed > 0 && mapControl.Zoom < _minZoomForAllowed)
            {
                _isAdjustingZoom = true;
                try
                {
                    // Preserve current center to avoid sudden recentering to library defaults
                    var prev = mapControl.Position;
                    mapControl.Zoom = _minZoomForAllowed;
                    try { mapControl.Position = prev; } catch { }
                }
                finally { _isAdjustingZoom = false; }
            }

            // 2) І на всяк випадок підтискаємо позицію, щоб ViewArea була всередині allowed
            EnsureViewAreaInsideAllowed();
        }

        private void Gmap_OnPositionChanged(PointLatLng point)
        {
            if (_isAdjustingPosition) return;
            // Recalculate side-panel distance when the map center moves (for GMap mode)
            UpdateDistanceFromCenterToMarker();
        }

        private void UpdateDistanceFromCenterToMarker()
        {
            try
            {
                // GMap provider mode: compute geodesic distance between control center and clicked marker (if present)
                if (_gmapProvider != null && _gmapProvider.Control != null)
                {
                    var gprov = _gmapProvider as Services.Map.GMapProvider;
                    if (gprov != null)
                    {
                        var marker = gprov.GetClickedMarkerLatLng();
                        if (marker.HasValue)
                        {
                            var center = _gmapProvider.Control.Position;
                            double meters = HaversineDistanceMeters(center.Lat, center.Lng, marker.Value.Lat, marker.Value.Lng);
                            selectedrange_tmp = (int)(MathF.Round((float)meters / 100f) * 100f);
                            UpdateDistanceDisplay(selectedrange_tmp);
                            return;
                        }
                    }
                }

                // SVG / pictureBox mode: distance from view center pixel to chosen overlay marker (if present)
                if (clickedPointMarker.HasValue)
                {
                    // Center of view in screen coordinates
                    var centerScreen = new Point(panelMap.Width / 2, panelMap.Height / 2);
                    var centerMap = ScreenToMapCoordinates(centerScreen);
                    float dx = clickedPointMarker.Value.X - centerMap.X;
                    float dy = clickedPointMarker.Value.Y - centerMap.Y;
                    float distanceToAttack = MathF.Sqrt(dx * dx + dy * dy);
                    if (pixelsToMeters != null && pixelsToMeters > 0f)
                    {
                        float distanceInMeters = distanceToAttack * pixelsToMeters.GetValueOrDefault();
                        selectedrange_tmp = (int)(MathF.Round(distanceInMeters / 100f) * 100f);
                        UpdateDistanceDisplay(selectedrange_tmp);
                    }
                }
            }
            catch { }
        }

        private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000.0; // Earth radius in meters
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }



        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                _inputController?.Timer_Tick(sender, e);
            }
            catch { }
        }


        /// <summary>
        /// Конвертує SVG одиниці виміру в пікселі
        /// </summary>
        /// <param name="unit">SVG одиниця виміру</param>
        /// <returns>Значення в пікселях</returns>
        private float ConvertToPixels(Svg.SvgUnit unit)
        {
            // Стандартні коефіцієнти конвертації в пікселі (96 DPI)
            const float DPI = 96f;
            const float PT_TO_PX = DPI / 72f;      // 1 pt = 1/72 inch
            const float PC_TO_PX = DPI / 6f;       // 1 pc = 1/6 inch  
            const float MM_TO_PX = DPI / 25.4f;    // 1 inch = 25.4 mm
            const float CM_TO_PX = DPI / 2.54f;    // 1 inch = 2.54 cm
            const float IN_TO_PX = DPI;            // 1 inch = 96 px (at 96 DPI)

            switch (unit.Type)
            {
                case Svg.SvgUnitType.Pixel:
                    return unit.Value;

                case Svg.SvgUnitType.Point:
                    return unit.Value * PT_TO_PX;

                case Svg.SvgUnitType.Pica:
                    return unit.Value * PC_TO_PX;

                case Svg.SvgUnitType.Millimeter:
                    return unit.Value * MM_TO_PX;

                case Svg.SvgUnitType.Centimeter:
                    return unit.Value * CM_TO_PX;

                case Svg.SvgUnitType.Inch:
                    return unit.Value * IN_TO_PX;

                case Svg.SvgUnitType.Percentage:
                    // Для відсотків повертаємо значення як є (буде оброблено окремо)
                    Console.WriteLine($"Попередження: розмір SVG заданий у відсотках ({unit.Value}%), використовуємо як пікселі");
                    return unit.Value;

                case Svg.SvgUnitType.Em:
                case Svg.SvgUnitType.Ex:
                    // Для em/ex використовуємо значення як є (залежить від шрифту)
                    Console.WriteLine($"Попередження: розмір SVG заданий у відносних одиницях ({unit.Type}), використовуємо як пікселі");
                    return unit.Value;

                case Svg.SvgUnitType.User:
                default:
                    // Користувацькі одиниці зазвичай = пікселі
                    return unit.Value;
            }
        }

        private void LoadSvg()
        {            
            // 1) Відкриваємо SVG
            Svg.SvgDocument doc;
            try
            {
                doc = Svg.SvgDocument.Open(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка при відкритті SVG: {ex.Message}", "Помилка",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 2) Визначаємо "натуральний" розмір з документа (зберігаємо СПІВВІДНОШЕННЯ СТОРІН)
            //    Пробуємо Width/Height, якщо їх немає — беремо viewBox
            int natW = 0, natH = 0;

            try
            {
                // Якщо в SVG явно задані width/height - конвертуємо в пікселі
                if (doc.Width != null && doc.Height != null && doc.Width.Value > 0 && doc.Height.Value > 0)
                {
                    // Конвертуємо одиниці виміру в пікселі
                    natW = (int)Math.Round(ConvertToPixels(doc.Width));
                    natH = (int)Math.Round(ConvertToPixels(doc.Height));
                    
                    Console.WriteLine($"SVG розміри: {doc.Width.Type} {doc.Width.Value} x {doc.Height.Type} {doc.Height.Value} → {natW}x{natH} px");
                }
                // Інакше — беремо з viewBox (viewBox завжди в користувацьких одиницях, які зазвичай = пікселі)
                else if (doc.ViewBox.Width > 0 && doc.ViewBox.Height > 0)
                {
                    natW = (int)Math.Round(doc.ViewBox.Width);
                    natH = (int)Math.Round(doc.ViewBox.Height);
                    
                    Console.WriteLine($"SVG viewBox: {natW}x{natH} (користувацькі одиниці)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка обробки розмірів SVG: {ex.Message}");
                // fallback нижче
            }

            // Fallback: якщо SVG не дав коректний розмір — беремо розмір панелі (але це рідко)
            if (natW <= 0 || natH <= 0)
            {
                natW = Math.Max(panelMap.ClientSize.Width, 1);
                natH = Math.Max(panelMap.ClientSize.Height, 1);
            }

            // 3) Обмежуємо розмір без спотворення (щоб не створювати гігантські бітмапи)
            const int MAX_DIM = 8000; // можеш змінити під свою машину
            float scaleClamp = Math.Min(1f, Math.Min((float)MAX_DIM / natW, (float)MAX_DIM / natH));

            // Якщо viewBox дуже малий (нативно 300×200 тощо), можна трохи підмасштабити під панель,
            // але ТІЛЬКИ рівномірно (один і той самий scale по X і Y), щоб не спотворити.
            // Плавний апскейл до поточної панелі:
            int targetW = natW;
            int targetH = natH;

            // Прагнемо, щоб карта виглядала різко на твоїй панелі, але зберегла aspect ratio:
            if (panelMap.ClientSize.Width > 0 && panelMap.ClientSize.Height > 0)
            {
                float scaleToPanelW = (float)panelMap.ClientSize.Width / natW;
                float scaleToPanelH = (float)panelMap.ClientSize.Height / natH;
                // Візьмемо помірний масштаб (наприклад, середній), але потім все одно затиснемо MAX_DIM
                float suggested = Math.Max(1f, Math.Min(scaleToPanelW, scaleToPanelH));
                float finalScale = Math.Min(suggested, scaleClamp);

                targetW = Math.Max(1, (int)Math.Round(natW * finalScale));
                targetH = Math.Max(1, (int)Math.Round(natH * finalScale));
            }
            else
            {
                // якщо панель ще не ініціалізована — просто кламп до MAX_DIM
                targetW = Math.Max(1, (int)Math.Round(natW * scaleClamp));
                targetH = Math.Max(1, (int)Math.Round(natH * scaleClamp));
            }

            // 4) Рендеримо у БІТМАПУ з тим самим aspect ratio
            Bitmap? newBmp = null;
            try
            {
                newBmp = doc.Draw(targetW, targetH);
                if (newBmp == null)
                {
                    MessageBox.Show("Не вдалося створити растрове зображення з SVG.", "Помилка",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 5) Безпечна заміна (swap) + оновлення стану
                var old = cachedBitmap;
                old?.Dispose(); // Звільняємо стару бітмапу
                
                cachedBitmap = newBmp;
                newBmp = null;

                _originalImageSize = cachedBitmap.Size;
                
                // Очищуємо кеш масштабованих точок для населених пунктів
                _scaledPointsCache.Clear();

                Console.WriteLine($"SVG завантажено: {_originalImageSize.Width}x{_originalImageSize.Height} px");

                // важливо: НІЯКОГО pictureBox1.Image = ...
                FitMapToScreen();   // Перераховуємо масштаб для нових розмірів
                Console.WriteLine($"Масштаб після FitMapToScreen: {_scale:F4}");
                
                CenterImage();      // Центруємо нову карту
                Console.WriteLine($"Зміщення після CenterImage: ({_imageOffset.X:F1}, {_imageOffset.Y:F1})");
                
                pictureBox1.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка рендера SVG: {ex.Message}", "Помилка",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                newBmp?.Dispose();
            }
            Console.WriteLine($"SVG розміри: {doc.Width.Value} {doc.Width.Type} x {doc.Height.Value} {doc.Height.Type}");
            Console.WriteLine($"ViewBox: {doc.ViewBox.Width} x {doc.ViewBox.Height}");
            Console.WriteLine($"Розраховані розміри: {natW} x {natH}");
            Console.WriteLine($"Розмір панелі: {panelMap.ClientSize.Width} x {panelMap.ClientSize.Height}");
        }

        private void SaveCalibrationData()
        {
            if (cachedBitmap == null)
            {
                MessageBox.Show("Спочатку завантажте карту!", "Помилка",
                               MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var data = new CalibrationData
                {
                    Points = referencePoints,
                    MapWidth = cachedBitmap.Width,
                    MapHeight = cachedBitmap.Height,
                };

                File.WriteAllText(calibrationFilePath,
                    JsonConvert.SerializeObject(data, Formatting.Indented));

                MessageBox.Show("Калібрування збережено!", "Успіх",
                               MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка збереження: {ex.Message}", "Помилка",
                               MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            RebuildInverse(); // ← Оновлюємо обернену матрицю
        }

        private void Maps_Load(object sender, EventArgs e)
        {
            //attackPoint = ScreenToMapCoordinates(new Point(X, Y));

            try
            {
                Console.WriteLine("=== Початок завантаження мапи ===");
                LoadSvg();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка при завантаженні карти: " + ex.Message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (cachedBitmap != null)
            {
                _originalImageSize = cachedBitmap.Size;

                // Обчислюємо масштаб
                float scaleX = (float)panelMap.Width / _originalImageSize.Width;
                float scaleY = (float)panelMap.Height / _originalImageSize.Height;
                _scale = Math.Max(scaleX, scaleY);
                
                Console.WriteLine($"Maps_Load: Panel {panelMap.Width}x{panelMap.Height}, Image {_originalImageSize.Width}x{_originalImageSize.Height}, Scale={_scale:F4}");

                LoadCalibrationData();

                flightLogger = new FlightLogger();  // створюємо екземпляр
                flightLogger.Initialize();

                CenterImage();
                InvalidateMap();

                LoadAttackPoint();

                        // Ініціалізуємо тимчасовий SVG-провайдер (адаптер)
                    try
                    {
                        _svgProvider = new Services.Map.SvgMapProvider();
                    _svgProvider.CoordinateConverter = coordinateConverter;
                    _svgProvider.SetBitmap(cachedBitmap);

                    // Ініціалізуємо MapController (керуватиме провайдером)
                    _mapController = new Controllers.MapController(coordinateConverter);
                    _mapController.SetProvider(_svgProvider, panelMap);

                    // Input controller (keyboard & rotation handling)
                    _inputController = new Controllers.InputController(this);
                    // Set rotation speed and timer interval for smooth updates
                    try { _inputController.SetRotationSpeedDegPerSec(90.0); } catch { }
                    try { if (timer != null) timer.Interval = 16; } catch { }

                    // Report controller
                    _reportController = new Controllers.ReportController(this);

                    // Ховаємо оригінальний pictureBox (поступова міграція)
                    if (pictureBox1 != null)
                    {
                        pictureBox1.Visible = false;
                    }

                    // Підписуємо обробники позиції/кліку через контролер
                    _mapController.OnPositionChanged += utm =>
                    {
                        if (labelCoordinates == null) return;
                        if (coordinateConverter == null || referencePoints.Count < 4)
                        {
                            labelCoordinates.Text = "Відкалібруйте карту";
                        }
                        else
                        {
                            string mgrs = coordinateConverter.FormatShortMGRSFromUTM(utm);
                            labelCoordinates.Text = $"MGRS: {mgrs}";
                        }
                    };

                    _mapController.OnClick += utm =>
                    {
                        if (coordinateConverter == null) return;
                        var pixel = coordinateConverter.UTMToPixel(utm);
                        // Еквівалент ScreenToMapCoordinates^-1 : pixel -> screen
                        var screen = new Point((int)Math.Round(pixel.X * _scale + _imageOffset.X), (int)Math.Round(pixel.Y * _scale + _imageOffset.Y));
                        HandleClickAction(screen);

                        // Оновлюємо провайдер (щоб маркер/зони перемалювалися)
                        try { _mapController?.Refresh(); } catch { }
                    };

                    // Прив'язуємо внутрішні події pictureBox до існуючих обробників карти у Maps
                    if (_svgProvider.Control != null)
                    {
                        _svgProvider.Control.MouseDown += pictureBox1_MouseDown;
                        _svgProvider.Control.MouseMove += pictureBox1_MouseMove;
                        _svgProvider.Control.MouseUp += pictureBox1_MouseUp;
                    }

                    // Додаємо SVG-оверлеї: зона атаки, маркер кліка, та підписи населених пунктів
                    _mapController.AddProviderOverlay(new Services.Map.SvgAttackZoneOverlay(() => attackZone));
                    _mapController.AddProviderOverlay(new Services.Map.SvgClickMarkerOverlay(() => clickedPointMarker));
                    _mapController.AddProviderOverlay(
                        new Services.Map.SvgLocalitiesOverlay(
                            () => LocalityCoordinates,
                            coordinateConverter,
                            () => _originalImageSize,
                            () => Size.Empty,
                            () => _scale
                        )
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Помилка ініціалізації SvgMapProvider: {ex.Message}");
                }
            }

            ConfigureSettingsEvents(settingsControl);
            LoadScaleValue();
            LoadJsonData();     // Кнопки локацій та шаблони
            FillTargetTypeCombo(); //Завантаження списку цілей по яким вилітати
            LoadUserSettings(); // Завантаження останнього вибору (пілот, дрон і т.д.)
        }

        // Обробники кнопок
        //private void btnSettings_Click(object sender, EventArgs e) => ShowSettingsTab();
        //private void btnMain_Click(object sender, EventArgs e) => ShowMainTab();

        private void LoadCalibrationData()
        {
            if (!File.Exists(calibrationFilePath))
                return;

            try
            {
                var json = File.ReadAllText(calibrationFilePath);
                var data = JsonConvert.DeserializeObject<CalibrationData>(json);

                if (data == null || data.Points.Count != 4)
                    return;

                referencePoints = data.Points;

                // Якщо в файлі були збережені розміри — масштабуємо у поточний розмір бітмапи
                if (cachedBitmap != null && data.MapWidth > 0 && data.MapHeight > 0 &&
                    (data.MapWidth != cachedBitmap.Width || data.MapHeight != cachedBitmap.Height))
                {
                    float sx = cachedBitmap.Width / (float)data.MapWidth;
                    float sy = cachedBitmap.Height / (float)data.MapHeight;

                    Console.WriteLine($"Масштабування калібрувальних точок: {data.MapWidth}x{data.MapHeight} → {cachedBitmap.Width}x{cachedBitmap.Height}, sx={sx:F3}, sy={sy:F3}");

                    referencePoints = referencePoints.Select(p => new ReferencePoint
                    {
                        Pixel = new PointF(p.Pixel.X * sx, p.Pixel.Y * sy),
                        Easting = p.Easting,
                        Northing = p.Northing
                    }).ToList();
                }

                // Обчислюємо коефіцієнти після масштабування
                var pixels = referencePoints.Select(p => p.Pixel).ToList();
                var eastings = referencePoints.Select(p => new PointF((float)p.Easting, 0)).ToList();
                var northings = referencePoints.Select(p => new PointF((float)p.Northing, 0)).ToList();

                var eastingCoeffsLocal = (coordinateConverter ??= new Services.Map.CoordinateConverter()).SolveAffineTransform(pixels, eastings);
                var northingCoeffsLocal = coordinateConverter.SolveAffineTransform(pixels, northings);

                // Встановлюємо їх у сервіс CoordinateConverter
                coordinateConverter.SetCoefficients(eastingCoeffsLocal, northingCoeffsLocal);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка при завантаженні калібрування: " + ex.Message,
                                "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // Синхронізуємо стан конвертера (якщо він існує)
            coordinateConverter?.RebuildInverse(); // оновлюємо внутрішній інверсний стан
        }

        private void RebuildInverse()
        {
            // Делегуємо в CoordinateConverter якщо він існує
            if (coordinateConverter != null)
            {
                coordinateConverter.RebuildInverse();
                return;
            }

            // Для зворотної сумісності залишаємо пусту реалізацію
        }

        private void LoadAttackPoint()
        {
            if (!File.Exists(attackPointPath) || cachedBitmap == null)
                return;

            try
            {
                var json = File.ReadAllText(attackPointPath);
                var data = JsonConvert.DeserializeObject<AttackPointData>(json);
                if (data == null) return;

                var loadedPoint = new PointF(data.X, data.Y);
                attackZone.AttackPoint = loadedPoint;

                int srcW = data.MapWidth;
                int srcH = data.MapHeight;

                // Міграція: якщо у файлі точки розмірів немає
                if ((srcW <= 0 || srcH <= 0) && File.Exists(calibrationFilePath))
                {
                    try
                    {
                        var calJson = File.ReadAllText(calibrationFilePath);
                        var cal = JsonConvert.DeserializeObject<CalibrationData>(calJson);
                        if (cal != null && cal.MapWidth > 0 && cal.MapHeight > 0)
                        {
                            srcW = cal.MapWidth;
                            srcH = cal.MapHeight;
                        }
                    }
                    catch
                    {
                        // Мовчки ігноруємо помилки калібрування
                    }
                }

                // Масштабування тільки якщо є вихідні розміри
                if (srcW > 0 && srcH > 0 &&
                    (srcW != cachedBitmap.Width || srcH != cachedBitmap.Height))
                {
                    float sx = cachedBitmap.Width / (float)srcW;
                    float sy = cachedBitmap.Height / (float)srcH;
                    loadedPoint = new PointF(loadedPoint.X * sx, loadedPoint.Y * sy);
                }

                // Синхронізуємо модель атаки
                attackZone.AttackPoint = loadedPoint;

                isAttakPointSet = false;
                pictureBox1.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка при завантаженні точки атаки: " + ex.Message,
                                "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateLabelPosition()
        {
            labelScale.Location = new Point(
                pictureBox1.ClientSize.Width - labelScale.Width - 100,
                10
            );

            labelCoordinates.Location = new Point(
                pictureBox1.ClientSize.Width - labelCoordinates.Width - 10,
                labelScale.Bottom + 5
            );

            labelScale_serva.Location = new Point(
                pictureBox1.ClientSize.Width - labelScale_serva.Width - 40,
                labelCoordinates.Bottom + 5
            );
        }



        private void Maps_Resize(object sender, EventArgs e)
        {
            if (cachedBitmap == null) return;

            // Викликаємо метод, щоб зберігати 20% ширини для panelOptions
            ResizePanels();

            // Оновлення масштабу та позиції зображення
            UpdateScale();
            ConstrainImagePosition();

            // Очищаємо кеш при зміні розміру вікна
            _scaledPointsCache.Clear();

            pictureBox1.Invalidate();
            //UpdateLabelPosition(); // Оновлення позиції labelScale
        }

        private void ResizePanels()
        {
            int panelOptionsWidth = (int)(this.ClientSize.Width * 0.2); // 20% ширини вікна
            panelOptions.Width = panelOptionsWidth;

            int newPanelMapWidth = this.ClientSize.Width - panelOptionsWidth;
            int newPanelMapHeight = this.ClientSize.Height;
            panelMap.Size = new Size(newPanelMapWidth, newPanelMapHeight);
        }

        private void UpdateScale()
        {
            // Перерахунок нового масштабу
            float scaleX = (float)panelMap.Width / _originalImageSize.Width;
            float scaleY = (float)panelMap.Height / _originalImageSize.Height;
            _scale = Math.Max(scaleX, scaleY);
        }


        private void Maps_MouseWheel(object sender, MouseEventArgs e)//контроль колесеком мишки
        {
            if (!panelMap.ClientRectangle.Contains(panelMap.PointToClient(Cursor.Position)))
            {
                return; // Вихід, якщо мишка НЕ над картою
            }

            float oldScale = _scale; // Запам’ятовуємо поточний масштаб
            float zoomFactor = (e.Delta > 0) ? 1.1f : 0.9f; // Масштабування в більшу чи меншу сторону
            _scale *= zoomFactor;

            float minScaleX = (float)panelMap.Width / _originalImageSize.Width;
            float minScaleY = (float)panelMap.Height / _originalImageSize.Height;
            float minScale = Math.Max(minScaleX, minScaleY);

            if (_scale < minScale) _scale = minScale; // Мінімальний масштаб, щоб карта не зникла

            float maxScale = 3.0f; // Максимальний масштаб (5× від початкового)
            if (_scale < minScale) _scale = minScale;
            else if (_scale > maxScale) _scale = maxScale;

            // Розраховуємо нове зміщення з урахуванням позицію курсора
            float relativeMouseX = (e.X - _imageOffset.X) / oldScale;
            float relativeMouseY = (e.Y - _imageOffset.Y) / oldScale;

            _imageOffset.X = e.X - (relativeMouseX * _scale);
            _imageOffset.Y = e.Y - (relativeMouseY * _scale);

            ConstrainImagePosition();
            pictureBox1.Invalidate();

        }
        private PointF ScreenToMapCoordinates(Point screenPoint)// вираховування координат для точки
        {
            return new PointF(
                (screenPoint.X - _imageOffset.X) / _scale,
                (screenPoint.Y - _imageOffset.Y) / _scale
            );
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning && e.Button == MouseButtons.Left && cachedBitmap != null)
            {
                _isInteracting = true;

                float deltaX = e.X - _panStart.X;
                float deltaY = e.Y - _panStart.Y;

                float factor = 0.8f;
                _imageOffset.X += deltaX * factor;
                _imageOffset.Y += deltaY * factor;

                _panStart = e.Location;
                ConstrainImagePosition();
                pictureBox1.Invalidate();
                SafeInvalidate(GetAttackZoneBounds());

                // Recompute distance between current view center and any placed marker (SVG mode)
                UpdateDistanceFromCenterToMarker();
            }
            if (_courseStartPoint.HasValue && (Control.MouseButtons & MouseButtons.Right) != 0)   //<< != 0 >> можна замінити на == MouseButtons.Right
            {
                _courseEndPoint = ScreenToMapCoordinates(e.Location); // Конвертуємо координати
                InvalidateMap(); // Оновлення графіки для малювання лінії
            }

            // Отримати координати в пікселях карти
            PointF pixel = ScreenToMapCoordinates(e.Location);

            // Якщо ще не відкалібровано — показати повідомлення
            if (labelCoordinates != null)
            {
                if (coordinateConverter == null || referencePoints.Count < 4)
                {
                    labelCoordinates.Text = "Відкалібруйте карту";
                }
                else
                {
                    PointF mapPoint = ScreenToMapCoordinates(e.Location);
                    PointF utm = coordinateConverter.PixelToUTM(mapPoint);

                    if (utm == PointF.Empty)
                    {
                        labelCoordinates.Text = "UTM недоступне";
                    }
                    else
                    {
                        string mgrs = coordinateConverter.FormatShortMGRSFromUTM(utm);
                        labelCoordinates.Text = $"MGRS: {mgrs}";
                    }
                }

            }

        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            _isInteracting = false;

            if (e.Button != MouseButtons.Right) return;

            _courseEndPoint = ScreenToMapCoordinates(e.Location);

            if (!_courseStartPoint.HasValue || !_courseEndPoint.HasValue) return;

            if (IsClickAction(_courseStartPoint.Value, _courseEndPoint.Value))
                HandleClickAction(e.Location);
            else
                HandleCourseMeasurement(_courseStartPoint.Value, _courseEndPoint.Value);

            InvalidateMap(); // Оновити карту
        }

        private bool IsClickAction(PointF start, PointF end)
        {
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            return distance < 10f;
        }

        private void HandleClickAction(Point location)
        {
            // Конвертуємо координати кліку в координати карти
            clickedPointMarker = ScreenToMapCoordinates(location);

            if (!clickedPointMarker.HasValue) return;

            // Завантажуємо масштабний коефіцієнт (пікселі->метри)
            LoadScaleValue();

            // Перевіряємо, чи карта відкалібрована
            if (pixelsToMeters is null || pixelsToMeters <= 0f)
            {
                MessageBox.Show("Карта ще не відкалібрована! Виконайте калібрування.", "Увага");
                return;
            }

            // Обчислюємо відстань між точкою кліку та точкою атаки в пікселях
            float dx = clickedPointMarker.Value.X - attackZone.AttackPoint.X;
            float dy = clickedPointMarker.Value.Y - attackZone.AttackPoint.Y;
            float distanceToAttack = MathF.Sqrt(dx * dx + dy * dy);

            // Конвертуємо пікселі в метри
            float distanceInMeters = distanceToAttack * pixelsToMeters.GetValueOrDefault();

            // Округлюємо до сотень (наприклад, 1250 -> 1300)
            selectedrange_tmp = (int)(MathF.Round(distanceInMeters / 100f) * 100f);

            // Оновлюємо відображення дистанції
            UpdateDistanceDisplay(selectedrange_tmp);
        }

        // Handle clicks on GMap: coordinates are already client pixels of the map control
        // NOTE: Do not store the click into `clickedPointMarker` (screen overlay) for GMap —
        // we want the red marker to be part of the map control itself so it moves with the map.
        private void HandleGMapClick(Point screen)
        {
            Console.WriteLine($"HandleGMapClick at {screen}");

            // Use the provided screen coordinates for measurements, but do NOT persist
            // them into `clickedPointMarker` (that would create a fixed overlay marker).
            var screenPt = new PointF(screen.X, screen.Y);

            LoadScaleValue();

            if (pixelsToMeters is null || pixelsToMeters <= 0f)
            {
                MessageBox.Show("Карта ще не відкалібрована! Виконайте калібрування.", "Увага");
                return;
            }

            float dx = screenPt.X - attackZone.AttackPoint.X;
            float dy = screenPt.Y - attackZone.AttackPoint.Y;
            float distanceToAttack = MathF.Sqrt(dx * dx + dy * dy);
            float distanceInMeters = distanceToAttack * pixelsToMeters.GetValueOrDefault();
            selectedrange_tmp = (int)(MathF.Round(distanceInMeters / 100f) * 100f);
            UpdateDistanceDisplay(selectedrange_tmp);

            // Ensure side panel updates (distance from center to newly placed marker)
            UpdateDistanceFromCenterToMarker();
        }

        private void UpdateDistanceDisplay(int distance)
        {
            // Перевіряємо, чи існує основний лейбл
            if (range2 == null || label_range_Value == null ||
                !range2.IsHandleCreated || !label_range_Value.IsHandleCreated) return;

            string newText = $"{distance} м";

            if (label_range_Value.Text != newText)
            {
                label_range_Value.Text = newText;

                //автоматичне позиціонування 
                PositionLabelBelow(range2, label_range_Value);
            }
        }

        private void HandleCourseMeasurement(PointF p1, PointF p2)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;

            float angleRad = MathF.Atan2(dy, dx);
            int angleDeg = (int)Math.Round((angleRad * 180f / MathF.PI) + 90f);
            angleDeg = ((angleDeg % 360) + 360) % 360; // 0..359

            attackCourse = angleDeg;
            if (label_course_Value != null)
                UpdateCourseDisplay(angleDeg);
        }

        private void UpdateCourseDisplay(int angle)
        {
            // Перевірка наявності елементів
            if (label_course_Value == null || label_Course == null ||
                !label_course_Value.IsHandleCreated || !label_Course.IsHandleCreated)
                return;

            string newText = $"{angle}°";
            if (label_course_Value.Text != newText)
            {
                label_course_Value.Text = newText;

                //автоматичне позиціонування 
                PositionLabelBelow(label_Course, label_course_Value);
            }
        }

        private void PositionLabelBelow(Label baseLabel, Label valueLabel, int offsetY = 5)
        {
            if (baseLabel == null || valueLabel == null ||
                !baseLabel.IsHandleCreated || !valueLabel.IsHandleCreated)
                return;

            Size textSize = TextRenderer.MeasureText(valueLabel.Text, valueLabel.Font);
            int newLeft = baseLabel.Left + (baseLabel.Width - textSize.Width) / 2;
            int newTop = baseLabel.Bottom + offsetY;

            if (valueLabel.Location != new Point(newLeft, newTop))
            {
                valueLabel.Location = new Point(newLeft, newTop);
            }
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            if (cachedBitmap == null) return;

            Graphics g = e.Graphics;
            PrepareGraphics(g);
            DrawBaseMap(g);

            if (attackZone.AttackPoint != PointF.Empty)
            {
                DrawAttackZone(g);
                DrawClickedPoint(g);
                DrawCourseLine(g);
            }

            DrawCalibrationMarkers(g);
            DrawCornerHints(g);
            DrawScaleMeasurementMarkers(g);
            DrawHighlightedLocalities(g);
            DrawLocalityLabels(g);
        }

        private RectangleF GetAttackZoneBounds()
        {
            if (attackZone.AttackPoint == PointF.Empty) return RectangleF.Empty;

            float radius = Math.Max(attackZone.RayLength, attackZone.SectorRadius);
            float scaledRadius = radius * _scale;
            return new RectangleF(
                (attackZone.AttackPoint.X * _scale + _imageOffset.X) - scaledRadius,
                (attackZone.AttackPoint.Y * _scale + _imageOffset.Y) - scaledRadius,
                scaledRadius * 2,
                scaledRadius * 2);
        }

        private void PrepareGraphics(Graphics g)
        {
            // Під час взаємодії — швидше, після — якісніше
            g.SmoothingMode = _isInteracting ? SmoothingMode.HighSpeed : SmoothingMode.AntiAlias;
            g.InterpolationMode = _isInteracting ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.None;

            // Спочатку зміщення, потім масштаб
            if (_imageOffset != PointF.Empty)
                g.TranslateTransform(_imageOffset.X, _imageOffset.Y);

            if (Math.Abs(_scale - 1.0f) > 0.0001f)
                g.ScaleTransform(_scale, _scale);
        }

        private void DrawBaseMap(Graphics g) // Малювання базової карти
        {
            g.DrawImage(cachedBitmap, 0, 0);
        }

        private void DrawAttackZone(Graphics g) //Сектор атаки та напрямок
        {
            var (endX, endY, drawAngle) = CalculateAttackLine();

            using Pen attackPen = new Pen(Color.Red, 1);
            using Brush attackBrush = new SolidBrush(Color.FromArgb(70, Color.Red));

            g.FillPie(attackBrush, attackZone.AttackPoint.X - attackZone.SectorRadius, attackZone.AttackPoint.Y - attackZone.SectorRadius,
                      attackZone.SectorRadius * 2, attackZone.SectorRadius * 2, drawAngle - (attackZone.SectorWidth / 2), attackZone.SectorWidth);

            g.DrawLine(attackPen, attackZone.AttackPoint.X, attackZone.AttackPoint.Y, endX, endY);
        }

        private (float endX, float endY, float drawAngle) CalculateAttackLine()
        {
            if (attackZone.RayLength <= 0) return (attackZone.AttackPoint.X, attackZone.AttackPoint.Y, attackZone.Angle); // Запобігання некоректним значенням
            float drawAngle = attackZone.Angle - 90;
            float drawAngleRad = MathF.PI * (attackZone.Angle - 90) / 180;
            float endX = attackZone.AttackPoint.X + MathF.Cos(drawAngleRad) * attackZone.RayLength;
            float endY = attackZone.AttackPoint.Y + MathF.Sin(drawAngleRad) * attackZone.RayLength;
            return (endX, endY, drawAngle);
        }

        // Paint handler for GMap: draw overlays (attack zone) onto the control
        private void MapControl_Paint(object? sender, PaintEventArgs e)
        {
            try
            {
                if (attackZone.AttackPoint != PointF.Empty)
                {
                    DrawAttackZoneOnGMap(e.Graphics);
                }
            }
            catch { }
        }

        // Draw attack zone using GMap client coordinates (pixel-based, similar to SVG)
        private void DrawAttackZoneOnGMap(Graphics g)
        {
            if (mapControl == null) return;
            if (attackZone.AttackPoint == PointF.Empty) return;

            var center = attackZone.AttackPoint;

            float drawAngle = attackZone.Angle - 90f;
            float drawAngleRad = MathF.PI * (attackZone.Angle - 90) / 180f;

            // Draw filled sector (using SectorRadius as pixels, like SVG)
            using (var attackBrush = new SolidBrush(Color.FromArgb(70, Color.Red)))
            using (var attackPen = new Pen(Color.Red, 2))
            {
                float radius = attackZone.SectorRadius;

                try
                {
                    g.FillPie(attackBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2,
                        drawAngle - (attackZone.SectorWidth / 2), attackZone.SectorWidth);
                }
                catch { }

                // Draw ray (RayLength interpreted as pixels)
                float endX = center.X + MathF.Cos(drawAngleRad) * attackZone.RayLength;
                float endY = center.Y + MathF.Sin(drawAngleRad) * attackZone.RayLength;

                g.DrawLine(attackPen, center.X, center.Y, endX, endY);

                // Small center marker for visibility
                using (var centerBrush = new SolidBrush(Color.Red))
                {
                    float r = 6f;
                    g.FillEllipse(centerBrush, center.X - r / 2, center.Y - r / 2, r, r);
                }
            }
        }

        private void DrawClickedPoint(Graphics g) //Точка кліку користувача
        {
            if (clickedPointMarker == null) return;

            PointF point = clickedPointMarker.Value;
            float radius = 18f;
            DrawEllipse(g, Brushes.Red, clickedPointMarker.Value, radius);
        }

        private void UpdateDebugLabel()
        {
            try
            {
                label2.Text = $"Attack: {(attackZone.AttackPoint.IsEmpty ? "none" : attackZone.AttackPoint.ToString())} | Angle: {attackZone.Angle:F0} | AwaitingSet: {isAttakPointSet} | Localities: {localityService?.Localities?.Count ?? 0}";
            }
            catch { }
        }

        private void DrawEllipse(Graphics g, Brush brush, PointF point, float radius)// Загальний метод для малювання еліпсів
        {
            float scaledRadius = radius / _scale;
            g.FillEllipse(brush, point.X - scaledRadius / 2, point.Y - scaledRadius / 2, scaledRadius, scaledRadius);
        }

        private void DrawCourseLine(Graphics g) //Лінія курсу
        {
            if (_courseStartPoint.HasValue && _courseEndPoint.HasValue)
            {
                g.DrawLine(Pens.Yellow, _courseStartPoint.Value, _courseEndPoint.Value);
            }
        }

        private void DrawCalibrationMarkers(Graphics g)  //Маркери калібрування
        {
            if (calibrationMarkers.Length == 0) return;

            int markerCount = Math.Clamp(currentCornerIndex + 1, 0, calibrationMarkers.Length);
            for (int i = 0; i < markerCount; i++)
            {
                float r = 2f;
                g.FillEllipse(Brushes.Yellow, calibrationMarkers[i].X - r, calibrationMarkers[i].Y - r, r * 2, r * 2);
            }
        }

        private void DrawCornerHints(Graphics g)  //Підсвітка кутів для калібрування
        {
            if (!isCollectingCorners) return;

            using Brush hintBrush = new SolidBrush(Color.FromArgb(60, Color.Yellow));
            for (int i = 0; i < 4; i++)
            {
                RectangleF screenRect = GetMapCornerRectangle(i);
                g.FillRectangle(hintBrush, screenRect);
            }
        }

        private void DrawScaleMeasurementMarkers(Graphics g) //Маркери для вимірювання відстані
        {
            if (!isScaleMarkersVisible || referencePoints.Count < 4)
                return;

            PointF topLeft = referencePoints[0].Pixel;
            PointF bottomRight = referencePoints[3].Pixel;

            float r = 2f;
            using Brush brush = new SolidBrush(Color.Cyan);
            g.FillEllipse(brush, topLeft.X - r, topLeft.Y - r, r * 2, r * 2);
            g.FillEllipse(brush, bottomRight.X - r, bottomRight.Y - r, r * 2, r * 2);
        }

        private void DrawHighlightedLocalities(Graphics g)
        {
            if (highlightedLocalities == null || highlightedLocalities.Count == 0 || cachedBitmap == null)
                return;

            using var pen = new Pen(Color.LimeGreen, 2);

            // Отримуємо поточні розміри карти (враховуючи масштабування)
            int currentWidth = cachedBitmap.Width;
            int currentHeight = cachedBitmap.Height;

            foreach (var name in highlightedLocalities)
            {
                if (!LocalityCoordinates.TryGetValue(name, out var utmPts) || utmPts == null || utmPts.Count == 0)
                    continue;

                // Конвертуємо UTM → пікселі з врахуванням поточного масштабу
                var pix = utmPts.Select(utmPoint =>
                {
                    var pixel = UTMToPixel(utmPoint);

                    // Масштабуємо координати, якщо потрібно
                    if (_originalImageSize.Width != currentWidth || _originalImageSize.Height != currentHeight)
                    {
                        float scaleX = currentWidth / (float)_originalImageSize.Width;
                        float scaleY = currentHeight / (float)_originalImageSize.Height;
                        return new PointF(pixel.X * scaleX, pixel.Y * scaleY);
                    }

                    return pixel;
                })
                .Where(p => p != PointF.Empty && p.X >= 0 && p.Y >= 0 && p.X <= currentWidth && p.Y <= currentHeight)
                .ToArray();

                if (pix.Length == 0) continue;

                // Малюємо точки (еліпси)
                foreach (var p in pix)
                    DrawEllipse(g, pen.Brush, p, 10f);

                // Малюємо полігон (якщо достатньо точок)
                if (pix.Length >= 3)
                    g.DrawPolygon(pen, pix);
            }
        }

        private void DrawLocalityLabels(Graphics g)
        {
            if (cachedBitmap == null || LocalityCoordinates == null) return;

            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            using var font = new Font("Arial", 9f / _scale);
            using var brush = new SolidBrush(Color.White);
            using var outline = new Pen(Color.Black, 0.5f / _scale);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            Size originalSize = _originalImageSize;
            Size currentSize = cachedBitmap.Size;

            foreach (var kvp in LocalityCoordinates)
            {
                string cacheKey = $"{kvp.Key}-{originalSize.Width}x{originalSize.Height}-{currentSize.Width}x{currentSize.Height}";

                if (!_scaledPointsCache.TryGetValue(cacheKey, out var validPoints))
                {
                    validPoints = GetValidScaledPixels(kvp.Value, originalSize, currentSize);
                    _scaledPointsCache[cacheKey] = validPoints;
                }

                if (validPoints.Count == 0) continue;

                float avgX = validPoints.Average(p => p.X);
                float avgY = validPoints.Average(p => p.Y);
                var labelPos = new PointF(avgX, avgY);

                g.DrawRectangle(outline, labelPos.X - 1, labelPos.Y - 1, 2, 2);
                g.DrawString(kvp.Key, font, brush, labelPos, format);
            }

        }

        private List<PointF> GetValidScaledPixels(IEnumerable<PointF> utmPoints, Size originalSize, Size currentSize)
        {
            var result = new List<PointF>();

            // Додати перевірку на нульові розміри
            if (currentSize.Width <= 0 || currentSize.Height <= 0)
                return result;

            // Швидший варіант, якщо розміри однакові
            bool needsScaling = originalSize.Width > 0 && originalSize.Height > 0 &&
                               (originalSize.Width != currentSize.Width ||
                                originalSize.Height != currentSize.Height);

            foreach (var utm in utmPoints)
            {
                var pixel = UTMToPixel(utm);

                // Масштабування
                if (needsScaling)
                {
                    float scaleX = currentSize.Width / (float)originalSize.Width;
                    float scaleY = currentSize.Height / (float)originalSize.Height;
                    pixel = new PointF(pixel.X * scaleX, pixel.Y * scaleY);
                }

                // Перевірка валідності
                if (pixel != PointF.Empty &&
                    pixel.X >= 0 && pixel.Y >= 0 &&
                    pixel.X <= currentSize.Width && pixel.Y <= currentSize.Height)
                {
                    result.Add(pixel);
                }
            }

            return result;
        }

        private PointF UTMToPixel(PointF utm)
        {
            if (coordinateConverter == null) return PointF.Empty;
            return coordinateConverter.UTMToPixel(utm);
        }

        public void UpdateAttackParameters(float newSectorRadius, float newRayLength, float newAngle)//данні для оновлення параметрів
        {
            attackZone.SectorRadius = newSectorRadius;
            attackZone.RayLength = newRayLength;
            attackZone.Angle = newAngle;
            pictureBox1.Invalidate();
        }

        private void FitMapToScreen()//вспоміжні дані координат для об'єкту
        {
            float scaleX = (float)panelMap.Width / _originalImageSize.Width;
            float scaleY = (float)panelMap.Height / _originalImageSize.Height;
            _scale = Math.Max(scaleX, scaleY);
        }

        private void CenterImage()//центрування карти
        {
            int newWidth = (int)(_originalImageSize.Width * _scale);
            int newHeight = (int)(_originalImageSize.Height * _scale);

            _imageOffset.X = (panelMap.Width - newWidth) / 2;
            _imageOffset.Y = (panelMap.Height - newHeight) / 2;

            ConstrainImagePosition();
        }

        private void ConstrainImagePosition()//підрахунок положення після змінень
        {
            // Movement constraints removed: allow free panning of the SVG image in Maps view.
            return;
        }

        private void InvalidateMap()
        {
            // If a GMap provider is active — refresh it.
            if (_gmapProvider != null)
            {
                try
                {
                    // Prefer invalidating the control to avoid reloading tiles
                    if (_gmapProvider.Control != null)
                    {
                        _gmapProvider.Control.Invalidate();
                    }
                    else
                    {
                        _gmapProvider.Refresh();
                    }
                    return;
                }
                catch { }
            }

            // Якщо SVG-провайдер активний — оновлюємо його
            if (_svgProvider != null)
            {
                try { _svgProvider.Refresh(); return; } catch { }
            }

            // Fallback: invalidate pictureBox
            try { if (pictureBox1 != null) pictureBox1.Invalidate(); } catch { }
        }

        public void CreateEmptyJsonFile()
        {
            //Shablon.CreateEmptyJsonFile(filePath_shablon);
        }

        private void LoadJsonData()
        {
            try
            {
                PopulatePilotComboBox();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void FillTargetTypeCombo()
        {
            var source = Shablon.TargetTypeShablon;

            if (source == null || source.Count == 0) return;

            var items = Shablon.TargetTypeShablon
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            Target_Type.BeginUpdate();
            try
            {
                Target_Type.Items.Clear();
                Target_Type.Items.AddRange(items);
                if (Target_Type.Items.Count > 0) Target_Type.SelectedIndex = 0;
            }
            finally
            {
                Target_Type.EndUpdate();
            }
        }

        private void PopulatePilotComboBox()
        {
            Position.Items.Clear();
            foreach (var key in Shablon.Position_Point.Keys)
            {
                Position.Items.Add(key);
            }
        }

        private void ComboBoxPilot_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedKey = Position.SelectedItem.ToString();
            PopulatePositionComboBox(selectedKey);
        }

        private void PopulatePositionComboBox(string key)
        {
            Pilot.Items.Clear();
            if (Shablon.Position_Point.TryGetValue(key, out var positionList))
            {
                foreach (var position in positionList)
                {
                    Pilot.Items.Add(position);
                }
            }

            DroneBy.Items.Clear();
            if (Shablon.DroneByPosition.TryGetValue(key, out var droneList))
            {
                foreach (var drone in droneList)
                {
                    DroneBy.Items.Add(drone);
                }
            }
        }

        private string GetTime()
        {
            DateTime timeholder = DateTime.Now;
            DateTime timePlus5 = timeholder.AddMinutes(5);
            time_Start = timePlus5;
            DateTime timePlus45 = timeholder.AddMinutes(45);
            return $"{timePlus5:HH:mm} {timePlus45:HH:mm}";
        }

        // GenerateTextFromTemplate moved to Controllers.ReportController


        // ApplyReplacements moved to Controllers.ReportController


        private PointF GetUTM(PointF? clickedPointMarker)
        {
            if (clickedPointMarker == null)
            {
                MessageBox.Show("Спочатку оберіть точку на карті!", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return PointF.Empty; // Коректний повернений об'єкт
            }

            return PixelToUTM(clickedPointMarker.Value);
        }

        private int GetCourseValue()
        {
            return attackCourse ?? random.Next(190, 341);
        }

        private void Start_of_Work_Click(object sender, EventArgs e)
        {
            // Delegate to ReportController
            try { _reportController?.Start_of_Work_Click(sender, e); } catch { }
        }

        private void End_of_Work_Click(object sender, EventArgs e)
        {
            try { _reportController?.End_of_Work_Click(sender, e); } catch { }
        }

        private void Combat_Work_Click(object sender, EventArgs e)
        {
            try { _reportController?.Combat_Work_Click(sender, e); } catch { }
        }

        private float CalculateAzimuth(PointF fromPoint, PointF toPoint)
        {
            float dx = toPoint.X - fromPoint.X;
            float dy = toPoint.Y - fromPoint.Y;

            // Обчислюємо кут в радіанах
            float angleRad = MathF.Atan2(dy, dx);

            // Конвертуємо в градуси (0-360)
            float angleDeg = (angleRad * (180f / MathF.PI)) + 90;
            angleDeg = (int)(angleDeg + 360f) % 360f; // Нормалізуємо до 0-360

            return angleDeg;
        }

        private void LogFlightIfValid()
        {
            if (!attackZone.AttackPoint.IsEmpty && time_Start != default)
            {
                if (isStart_and_Work)
                {
                    var selectedPilot = Pilot.SelectedItem?.ToString();
                    var selectedDron = DroneBy.SelectedItem?.ToString();
                    var selectedPosition = Position.SelectedItem?.ToString() ?? "Позиція невідома";

                    if (!string.IsNullOrWhiteSpace(selectedPilot) && !string.IsNullOrWhiteSpace(selectedDron))
                    {
                        DateTime date_time = DateTime.Today.Add(TimeSpan.Parse(timeString));
                        flightLogger.AddFlight(selectedPilot, selectedDron, selectedPosition, selectedrange_tmp, date_time);
                    }
                }
            }
        }

        private void CopyToClipboardWithNotification(string text)
        {
            try
            {
                Clipboard.SetText(text);
                // Можна додати невелику підказку замість MessageBox
                toolTip1.Show("Текст скопійовано!", textBox1, 2000);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка копіювання: {ex.Message}", "Помилка");
            }
        }

        private bool ClickedPointMarker()
        {
            if (clickedPointMarker == null)
            {
                MessageBox.Show("Куди летим?", "Увага", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        #region IReportContext implementation
        string Controllers.IReportContext.GetSelectedPosition() => Position.SelectedItem?.ToString() ?? "";
        string Controllers.IReportContext.GetSelectedPilot() => Pilot.SelectedItem?.ToString() ?? "";
        string Controllers.IReportContext.GetSelectedDrone() => DroneBy.SelectedItem?.ToString() ?? "";
        System.Collections.Generic.IEnumerable<string> Controllers.IReportContext.GetLocalCities() => LocalCities;
        string Controllers.IReportContext.GetShootingTarget() => shootingTarget?.Text ?? "";
        string Controllers.IReportContext.GetHeightText() => height?.Text ?? "";
        int Controllers.IReportContext.GetSelectedRange() => selectedrange_tmp;
        bool Controllers.IReportContext.TryGetUTM(out PointF utm)
        {
            if (clickedPointMarker == null)
            {
                MessageBox.Show("Спочатку оберіть точку на карті!", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                utm = PointF.Empty;
                return false;
            }
            utm = PixelToUTM(clickedPointMarker.Value);
            return true;
        }
        string Controllers.IReportContext.FormatShortMGRSFromUTM(PointF utm) => coordinateConverter != null ? coordinateConverter.FormatShortMGRSFromUTM(utm) : FormatShortMGRSFromUTM(utm);
        string Controllers.IReportContext.FindClosestLocality(PointF utm) => FindClosestLocality(utm);
        int Controllers.IReportContext.GetCourseValue() => GetCourseValue();
        string Controllers.IReportContext.GetTargetType() => Target_Type?.Text ?? "";
        string Controllers.IReportContext.GetAndSetTime() => GetTime();
        string Controllers.IReportContext.GetCurrentTimeString() => timeString ?? "";
        ShablonManager Controllers.IReportContext.GetShablon() => Shablon;
        bool Controllers.IReportContext.IsTargetDestroyed() => targetDestroyedCheckBox.Checked;
        bool Controllers.IReportContext.IsTargetBoardLost() => targetBoardCheckBox.Checked;
        void Controllers.IReportContext.SetReportText(string text) { textBox1.Text = text; }
        void Controllers.IReportContext.CopyToClipboardWithNotification(string text) => CopyToClipboardWithNotification(text);
        void Controllers.IReportContext.LogFlightIfValid() => LogFlightIfValid();
        PointF? Controllers.IReportContext.GetClickedPoint() => clickedPointMarker;
        PointF Controllers.IReportContext.GetAttackPoint() => attackZone.AttackPoint;
        string Controllers.IReportContext.GetLabelScaleText() => labelScale?.Text ?? "";
        #endregion

        #region IInputContext implementation
        bool Controllers.IInputContext.IsMainTabActive => IsMainTabActive;
        bool Controllers.IInputContext.HasAttackPoint() => attackZone.AttackPoint != PointF.Empty;
        void Controllers.IInputContext.SetInteracting(bool value) => _isInteracting = value;
        void Controllers.IInputContext.StartTimer() { try { timer?.Start(); } catch { } }
        void Controllers.IInputContext.StopTimer() { try { timer?.Stop(); } catch { } }
        void Controllers.IInputContext.AdjustAttackAngle(float delta) { attackZone.Angle += delta; attackZone.Angle = (attackZone.Angle % 360 + 360) % 360; }
        void Controllers.IInputContext.AdjustServoAngle(float delta) { labelScale_serva_attak += delta; }
        void Controllers.IInputContext.AdjustSectorWidth(float delta)
        {
            attackZone.SectorWidth += delta;
            // Clamp sector width to reasonable range (min 5°, max 180°)
            if (attackZone.SectorWidth < 5f) attackZone.SectorWidth = 5f;
            if (attackZone.SectorWidth > 180f) attackZone.SectorWidth = 180f;
        }
        float Controllers.IInputContext.GetAttackAngle() => attackZone.Angle;
        float Controllers.IInputContext.GetSectorWidth() => attackZone.SectorWidth;
        RectangleF Controllers.IInputContext.GetAttackZoneBounds() => GetAttackZoneBounds();
        void Controllers.IInputContext.UpdateAngleDisplays(float angle, float servo) { if (labelScale != null) labelScale.Text = $"Кут: {angle:F1}°"; if (labelScale_serva != null) labelScale_serva.Text = $"СЕРВА: {labelScale_serva_attak:F1}"; }
        void Controllers.IInputContext.SafeInvalidate(RectangleF bounds) => SafeInvalidate(bounds);
        void Controllers.IInputContext.InvalidateMap() => InvalidateMap();
        void Controllers.IInputContext.InvalidateMapImmediate()
        {
            // Force an immediate refresh to minimize perceived lag (used for instant feedback on keypress)
            if (_gmapProvider != null)
            {
                try
                {
                    if (_gmapProvider.Control != null)
                    {
                        _gmapProvider.Control.Refresh();
                    }
                    else
                    {
                        _gmapProvider.Refresh();
                    }
                    return;
                }
                catch { }
            }

            if (_svgProvider != null)
            {
                try { _svgProvider.Refresh(); return; } catch { }
            }

            try { if (pictureBox1 != null) pictureBox1.Refresh(); } catch { }
        }
        #endregion

        private void GenerateButtons(List<string> elements)
        {
            elements.Sort((x, y) => x.Length.CompareTo(y.Length));
            flowLayoutLocalities.Controls.Clear();

            foreach (var element in elements)
            {
                var button = new System.Windows.Forms.Button
                {
                    Text = element,
                    AutoSize = true,
                    UseVisualStyleBackColor = true,
                    Margin = new Padding(3),
                    Tag = false
                };

                button.Click += ToggleButtonSelection;
                flowLayoutLocalities.Controls.Add(button);
            }
        }

        private void ToggleButtonSelection(object sender, EventArgs e)
        {
            if (sender is System.Windows.Forms.Button button)
            {
                // Отримуємо поточний стан зі списку міст (головне джерело правди)
                var cityName = button.Text.Trim();
                var isSelected = LocalCities.Contains(cityName, StringComparer.CurrentCultureIgnoreCase);

                // Інвертуємо стан
                isSelected = !isSelected;

                // Оновлюємо список міст
                if (isSelected) LocalCities.Add(cityName);
                else LocalCities.Remove(cityName);

                // Оновлюємо кнопку
                button.UseVisualStyleBackColor = false;
                button.BackColor = isSelected ? Color.DarkGray : SystemColors.Control;
                button.ForeColor = SystemColors.ControlText;
                button.Tag = isSelected; // Синхронізуємо Tag
            }
        }

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            Console.WriteLine($"pictureBox1_MouseDown Button={e.Button} Modifiers={Control.ModifierKeys} isAttakPointSet={isAttakPointSet} Location={e.Location}");
            if (label2 != null) label2.Text = $"MouseDown: {e.Button} Mod={Control.ModifierKeys} AwaitingSet={isAttakPointSet}";
            _isInteracting = true;

            if (e.Button == MouseButtons.Right)
            {
                _courseStartPoint = ScreenToMapCoordinates(e.Location);
                _courseEndPoint = null; // очищення попередньої лінії
            }

            if (e.Button == MouseButtons.Left)
            {
                _isPanning = true;
                _panStart = e.Location;
            }

            // Якщо користуємося SVG-провайдером — не дозволяємо внутрішньому провайдеру окремо робити панінг.
            // Ми перехоплюємо пани через існуючі обробники Maps (вище) і потім оновлюємо провайдер через InvalidateMap().
            //if (e.Button == MouseButtons.Right && Control.ModifierKeys.HasFlag(Keys.Control) && isAttakPointSet)
            //{
            //    attackPoint = ScreenToMapCoordinates(e.Location);

            //    var data = new AttackPointData
            //    {
            //        X = attackPoint.X,
            //        Y = attackPoint.Y,
            //        // Додаємо розміри карти для майбутнього масштабування
            //        MapWidth = cachedBitmap?.Width ?? 0,  // перевірка на null
            //        MapHeight = cachedBitmap?.Height ?? 0
            //    };

            //    Directory.CreateDirectory(Path.GetDirectoryName(attackPointPath));
            //    File.WriteAllText(attackPointPath, JsonConvert.SerializeObject(data, Formatting.Indented));

            //    isAttakPointSet = false;
            //    MessageBox.Show("Точку атаки збережено.");
            //    pictureBox1.Invalidate();
            //}

            //if (e.Button == MouseButtons.Right && Control.ModifierKeys.HasFlag(Keys.Control) && isAttakPointSet)
            //{
            //    attackPoint = ScreenToMapCoordinates(e.Location);

            //    // Зберігаємо в НОВОМУ форматі (з розмірами)
            //    var dataExtended = new AttackPointDataExtended
            //    {
            //        X = attackPoint.X,
            //        Y = attackPoint.Y,
            //        MapWidth = cachedBitmap?.Width ?? 0,
            //        MapHeight = cachedBitmap?.Height ?? 0
            //    };

            //    Directory.CreateDirectory(Path.GetDirectoryName(attackPointPath));
            //    File.WriteAllText(attackPointPath, JsonConvert.SerializeObject(dataExtended, Formatting.Indented));

            //    isAttakPointSet = false;
            //    MessageBox.Show("Точку атаки збережено.");
            //    pictureBox1.Invalidate();
            //}
            if (e.Button == MouseButtons.Right && Control.ModifierKeys.HasFlag(Keys.Control) && isAttakPointSet)
            {
                var newAttackPoint = ScreenToMapCoordinates(e.Location);
                Console.WriteLine($"Saving new attack point at pixel {newAttackPoint} (isAttakPointSet={isAttakPointSet})");
                if (label2 != null) label2.Text = $"Saved: {newAttackPoint}";
                attackZone.AttackPoint = newAttackPoint;

                var data = new AttackPointData
                {
                    X = newAttackPoint.X,
                    Y = newAttackPoint.Y,
                    MapWidth = cachedBitmap?.Width ?? 0,
                    MapHeight = cachedBitmap?.Height ?? 0
                };

                var dir = Path.GetDirectoryName(attackPointPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(attackPointPath, JsonConvert.SerializeObject(data, Formatting.Indented));

                isAttakPointSet = false;
                MessageBox.Show("Точку атаки збережено.");
                pictureBox1.Invalidate();
            }

            SafeInvalidate(GetAttackZoneBounds());
        }

        private void PrepareAttackPoint()
        {
            isAttakPointSet = true;
            Console.WriteLine("PrepareAttackPoint: isAttakPointSet = true");
            if (label2 != null) label2.Text = "Очікування: CTRL + ПКМ по карті для встановлення точки атаки";
            // If GMap is visible, move focus so it receives the next click reliably
            try { if (mapControl != null && mapControl.Visible) mapControl.Focus(); } catch { }
            MessageBox.Show("CTRL + ПКМ по карті, щоб виставити точку атаки.", "Інструкція");
        }

        // Debug helper: set a sample attack point (center of current map)
        private void SetSampleAttackPoint()
        {
            PointF center = PointF.Empty;
            if (cachedBitmap != null)
            {
                center = new PointF((cachedBitmap.Width) / 2f, (cachedBitmap.Height) / 2f);
            }
            else if (mapControl != null)
            {
                center = new PointF(mapControl.Width / 2f, mapControl.Height / 2f);
            }

            attackZone.AttackPoint = center;
            var data = new AttackPointData
            {
                X = center.X,
                Y = center.Y,
                MapWidth = cachedBitmap?.Width ?? 0,
                MapHeight = cachedBitmap?.Height ?? 0
            };

            var dir = Path.GetDirectoryName(attackPointPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(attackPointPath, JsonConvert.SerializeObject(data, Formatting.Indented));

            isAttakPointSet = false;
            Console.WriteLine($"SetSampleAttackPoint: {center}");
            if (label2 != null) label2.Text = $"Sample attack point set: {center}";
            pictureBox1.Invalidate();
            SafeInvalidate(GetAttackZoneBounds());
            MessageBox.Show("Прикладна точка атаки встановлена.", "Debug");
        }

        private void HandleCornerClick(PointF pixel)
        {
            string[] cornerNames = { "лівий верхній", "правий верхній", "лівий нижній", "правий нижній" };

            // Використання окремого методу для отримання області допустимого кліку
            expectedCornerRegion = GetMapCornerRectangle(currentCornerIndex);

            // Перевірка, чи клік користувача знаходиться в потрібній області
            if (!expectedCornerRegion.Contains(pixel))
            {
                MessageBox.Show($"Натисніть ближче до {cornerNames[currentCornerIndex]} кута карти!", "Попередження");
                return;
            }
            calibrationMarkers[currentCornerIndex] = pixel; // збережи точку
            pictureBox1.Invalidate();

            // Вводимо координати у форматі: Easting Northing, наприклад: 90582 17528
            string input = Microsoft.VisualBasic.Interaction.InputBox(
            $"Введіть координати UTM (Easting, Northing) для {cornerNames[currentCornerIndex]}, наприклад: 517000,5403000",
            "UTM координата");

            if (!TryParseUtm(input, out double easting, out double northing))
            {
                MessageBox.Show("Помилка! Введіть ДВА ДОДАТНІ ЧИСЛА через кому, наприклад: 517000,5403000", "Некоректний формат");
                return;
            }

            referencePoints.Add(new ReferencePoint
            {
                Pixel = pixel,
                Easting = easting,
                Northing = northing
            });

            currentCornerIndex++;

            if (currentCornerIndex == 4)
            {
                isCollectingCorners = false;

                var msg = string.Join(Environment.NewLine,
                    referencePoints.Select((p, i) => $"Point {i + 1}: Pixel ({p.Pixel.X:F2}, {p.Pixel.Y:F2}) → UTM ({p.Easting}, {p.Northing})"));

                ShowCopyableMessage("Усі 4 точки зібрані:\n\n" + msg);

                // Тепер розраховуємо коефіцієнти
                var pixels = referencePoints.Select(p => p.Pixel).ToList();
                var eastings = referencePoints.Select(p => new PointF((float)p.Easting, 0)).ToList();
                var northings = referencePoints.Select(p => new PointF((float)p.Northing, 0)).ToList();

                var eastingCoeffsLocal = (coordinateConverter ??= new Services.Map.CoordinateConverter()).SolveAffineTransform(pixels, eastings);
                var northingCoeffsLocal = coordinateConverter.SolveAffineTransform(pixels, northings);

                // Встановлюємо в CoordinateConverter
                coordinateConverter.SetCoefficients(eastingCoeffsLocal, northingCoeffsLocal);

                // +++ ДОДАЄМО ВИЗНАЧЕННЯ МЕЖ +++
                CalculateMapBoundsFromCalibration();

                Array.Clear(calibrationMarkers, 0, referencePoints.Count);

                pictureBox1.Invalidate();

                //var data = new CalibrationData { Points = referencePoints };
                //File.WriteAllText(calibrationFilePath, JsonConvert.SerializeObject(data, Formatting.Indented));

                SaveCalibrationData();
            }
            else
            {
                MessageBox.Show($"Натисніть на {cornerNames[currentCornerIndex]} край карти.");
            }

            pictureBox1.Invalidate();

        }

        private bool TryParseUtm(string input, out double easting, out double northing)
        {
            easting = 0;
            northing = 0;

            input = input.Replace('.', ','); // автоматична заміна

            var parts = input.Split(',');

            if (parts.Length != 2) return false;
            if (!double.TryParse(parts[0], out easting)) return false;
            if (!double.TryParse(parts[1], out northing)) return false;

            if (easting < 100000 || easting > 900000) return false;
            if (northing < 0 || northing > 10000000) return false;

            return true;
        }

        private void StartCalibration()
        {
            var result = MessageBox.Show(
                "Почати калібрування карти? \nПоточні координати будуть стерті.",
                "Підтвердження",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            // Продовжити калібрування
            currentCornerIndex = 0;
            referencePoints.Clear();
            Array.Clear(calibrationMarkers, 0, calibrationMarkers.Length);
            isCollectingCorners = true;
            pictureBox1.Invalidate();

            MessageBox.Show("Клікніть по лівому верхньому куту карти", "Вимірювання кута", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private double[] SolveAffineTransform(List<PointF> pixels, List<PointF> coords)
        {
            if (coordinateConverter != null)
            {
                return coordinateConverter.SolveAffineTransform(pixels, coords);
            }

            if (pixels.Count < 4 || coords.Count < 4)
                return new double[3]; // або кидай виключення з повідомленням

            double[,] matrix = new double[4, 3]; // 4 точки, кожна має X, Y, +1 (для зсуву)
            var vector = new double[4];         // Вектор результатів

            for (int i = 0; i < 4; i++)
            {
                matrix[i, 0] = pixels[i].X;     // X пікселя
                matrix[i, 1] = pixels[i].Y;     // Y пікселя
                matrix[i, 2] = 1;               // константа для зсуву
                vector[i] = coords[i].X;        // Координата X (Easting) або Y (Northing)
            }


            return matrix.PseudoInverse().Dot(vector); // Розв’язання СЛР → отримуємо коефіцієнти
        }

        private PointF PixelToUTM(PointF pixel)
        {
            if (coordinateConverter == null) return PointF.Empty;
            return coordinateConverter.PixelToUTM(pixel);
        }

        private void ShowCopyableMessage(string text)
        {
            Form form = new Form();
            form.Text = "Результати";
            form.Size = new Size(500, 300);

            System.Windows.Forms.TextBox textBox = new System.Windows.Forms.TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Text = text,
                ScrollBars = ScrollBars.Vertical
            };


            System.Windows.Forms.Button okButton = new System.Windows.Forms.Button
            {
                Text = "OK",
                Dock = DockStyle.Bottom
            };
            okButton.Click += (s, e) => form.Close();

            form.Controls.Add(textBox);
            form.Controls.Add(okButton);
            form.StartPosition = FormStartPosition.CenterParent;
            form.ShowDialog();
        }

        private void CalculateMapBoundsFromCalibration()
        {
            if (referencePoints == null || referencePoints.Count < 4) return;

            // Знаходимо мінімальні/максимальні UTM координати з каліброваних точок
            _mapMinUtmX = (float)referencePoints.Min(p => p.Easting);
            _mapMaxUtmX = (float)referencePoints.Max(p => p.Easting);
            _mapMinUtmY = (float)referencePoints.Min(p => p.Northing);
            _mapMaxUtmY = (float)referencePoints.Max(p => p.Northing);

            // Додаємо невеликий запас (1%) для безпеки
            float paddingX = (_mapMaxUtmX - _mapMinUtmX) * 0.01f;
            float paddingY = (_mapMaxUtmY - _mapMinUtmY) * 0.01f;

            _mapMinUtmX -= paddingX;
            _mapMaxUtmX += paddingX;
            _mapMinUtmY -= paddingY;
            _mapMaxUtmY += paddingY;

            Console.WriteLine($"Межі карти з калібрування:");
            Console.WriteLine($"Easting: {_mapMinUtmX:F1} - {_mapMaxUtmX:F1}");
            Console.WriteLine($"Northing: {_mapMinUtmY:F1} - {_mapMaxUtmY:F1}");
        }

        private RectangleF GetMapCornerRectangle(int index)
        {
            float w = _originalImageSize.Width / 9;
            float h = _originalImageSize.Height / 9;
            float fullW = _originalImageSize.Width;
            float fullH = _originalImageSize.Height;

            switch (index)
            {
                case 0: return new RectangleF(0, 0, w, h);                         // Лівий верх
                case 1: return new RectangleF(fullW - w, 0, w, h);                 // Правий верх
                case 2: return new RectangleF(0, fullH - h, w, h);                 // Лівий низ
                case 3: return new RectangleF(fullW - w, fullH - h, w, h);         // Правий низ
                default: return RectangleF.Empty;
            }
        }

        private string FormatShortMGRSFromUTM(PointF utm)
        {
            if (coordinateConverter != null)
                return coordinateConverter.FormatShortMGRSFromUTM(utm);

            // Фолбек
            try
            {
                string hemisphere = utm.Y > 0 ? "N" : "S";
                var tempUtm = new UniversalTransverseMercator(hemisphere, 37, utm.X, utm.Y);
                var coord = UniversalTransverseMercator.ConvertUTMtoLatLong(tempUtm);
                double latitude = coord.Latitude.DecimalDegree;
                double longitude = coord.Longitude.DecimalDegree;
                int utmZone = (int)Math.Floor((longitude + 180) / 6) + 1;
                char bandLetter = GetUTMBandLetter(latitude);
                var utmWithCorrectZone = new UniversalTransverseMercator(hemisphere, utmZone, utm.X, utm.Y);
                var correctedCoord = UniversalTransverseMercator.ConvertUTMtoLatLong(utmWithCorrectZone);
                string fullMgrsString = correctedCoord.MGRS.ToString();
                var parts = fullMgrsString.Split(' ');
                if (parts.Length >= 4 && parts[2].Length >= 2 && parts[3].Length >= 2)
                {
                    string square = parts[1];
                    string shortEast = parts[2].Substring(0, 2);
                    string shortNorth = parts[3].Substring(0, 2);
                    return $"{utmZone}{bandLetter} {square} {shortEast} {shortNorth}";
                }
                return fullMgrsString;
            }
            catch
            {
                return "Невірні координати";
            }
        }

        private char GetUTMBandLetter(double latitude)
        {
            string bands = "CDEFGHJKLMNPQRSTUVWX";
            int index = (int)Math.Floor((latitude + 80) / 8);

            if (index < 0) index = 0;
            if (index > 19) index = 19;

            return bands[index];
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            if (!ValidatePanelOptions()) return;

            SavePreviousControls();
            InitializeSettingsPanel();
        }

        // === Основні методи ===
        private bool ValidatePanelOptions()
        {
            if (panelOptions == null)
            {
                ShowErrorMessage("Панель для відображення не ініціалізована");
                return false;
            }
            return true;
        }

        private void SavePreviousControls()
        {
            previousControls = panelOptions.Controls.Count > 0
                ? panelOptions.Controls.Cast<Control>().ToArray()
                : Array.Empty<Control>();
            panelOptions.Controls.Clear();
        }

        private void InitializeSettingsPanel()
        {
            var settingsControl = CreateSettingsControl();
            ConfigureSettingsEvents(settingsControl);
            panelOptions.Controls.Add(settingsControl);
        }

        // === Створення контролів ===
        private SettingsControl CreateSettingsControl()
        {
            return new SettingsControl { Dock = DockStyle.Fill };
        }

        private LocalityControl CreateLocalityControl(SettingsControl settingsControl)
        {
            var control = new LocalityControl();
            control.SetLocalities(GetLocalityNames());
            ConfigureLocalityEvents(control, settingsControl);
            return control;
        }

        private string[] GetLocalityNames()
        {
            return localityService?.Localities?.Keys?
                .Where(name => !string.IsNullOrEmpty(name))
                .ToArray() ?? Array.Empty<string>();
        }

        // === Налаштування подій ===
        private void ConfigureSettingsEvents(SettingsControl settingsControl)
        {
            // Основні події
            // Замість використання SubscribeEvent робимо прямо:
            settingsControl.CalibrateClicked -= OnCalibrateClicked;
            settingsControl.CalibrateClicked += OnCalibrateClicked;

            settingsControl.SetPointClicked -= OnSetPointClicked;
            settingsControl.SetPointClicked += OnSetPointClicked;

            settingsControl.CalculateScaleClicked -= OnScaleClicked;
            settingsControl.CalculateScaleClicked += OnScaleClicked;

            // Спеціальні події
            settingsControl.InfoClicked -= ShowInfoWindow;
            settingsControl.InfoClicked += ShowInfoWindow;

            settingsControl.BackClicked -= RestorePreviousControls;
            settingsControl.BackClicked += RestorePreviousControls;

            settingsControl.LocalitiesClicked -= ShowLocalitiesPanel;
            settingsControl.LocalitiesClicked += ShowLocalitiesPanel;

            settingsControl.RenameClicked -= OnRenameClicked;
            settingsControl.RenameClicked += OnRenameClicked;

        }

        private void OnCalibrateClicked(object sender, EventArgs e) => StartCalibration();
        private void OnSetPointClicked(object sender, EventArgs e) => PrepareAttackPoint();
        private void OnScaleClicked(object sender, EventArgs e) => CalculateScale();

        private void ConfigureLocalityEvents(LocalityControl localityControl, SettingsControl settingsControl)
        {
            localityControl.LocalitySelected -= HighlightLocality;
            localityControl.LocalitySelected += HighlightLocality;

            localityControl.BackClicked -= ReturnToSettings;
            localityControl.BackClicked += ReturnToSettings;

            void ReturnToSettings(object s, EventArgs e)
            {
                localityControl.LocalitySelected -= HighlightLocality;
                localityControl.BackClicked -= ReturnToSettings;
                ShowSettingsPanel(settingsControl);
            }
        }

        private void ShowInfoWindow(object sender, EventArgs e)
        {
            if (flightLogger == null)
            {
                MessageBox.Show("Логер даних не ініціалізований.", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string stats = flightLogger.GetStatistics() ?? "Немає даних";
            using (var form = new FlightStatsForm(stats))
            {
                form.ShowDialog();
            }
        }

        private void HighlightLocality(string name)
        {
            try
            {
                ToggleLocalityHighlight(name);
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Помилка при виділенні міста: {ex.Message}");
            }
        }

        private void ShowLocalitiesPanel(object sender, EventArgs e)
        {
            //if (sender is SettingsControl settingsControl)
            //{
            try
            {
                var localityPanel = CreateLocalityControl(settingsControl);
                panelSettings.Visible = false;
                panelSettingsView.Controls.Clear();
                panelSettingsView.Controls.Add(localityPanel);
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Помилка при завантаженні списку міст", ex);
                ShowSettingsPanel(settingsControl);
            }
            //}
        }

        private void OnRenameClicked(object sender, EventArgs e)
        {
            // Зберігаємо поточний стан
            if (panelSettingsView.Controls.Count > 0)
            {
                panelHistory.Push(panelSettingsView.Controls[0]);
            }

            var editor = CreateNameEditorControl();
            panelSettings.Visible = false;
            panelSettingsView.Controls.Clear();
            panelSettingsView.Controls.Add(editor);

            editor.BackClicked -= Editor_BackClicked;
            editor.BackClicked += Editor_BackClicked;


        }

        private void OnResetUserDataClicked(object sender, EventArgs e)
        {
            try
            {
                // Шлях до файлу з користувацькими даними
                var userDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Maps", "UserOverrides.json");

                // Видаляємо файл
                if (File.Exists(userDataPath))
                {
                    File.Delete(userDataPath);
                }

                // Перезавантажуємо дані (тільки програмні)
                Shablon.InitializeData();

                // Оновлюємо UI
                PopulatePilotComboBox();
                if (Position.Items.Count > 0) Position.SelectedIndex = 0;

                MessageBox.Show("Користувацькі налаштування видалено!", "Успіх",
                               MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка при видаленні: {ex.Message}", "Помилка",
                               MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Обробник кнопки "Назад" з редактора
        private void Editor_BackClicked(object sender, EventArgs e)
        {
            ReturnToPreviousView();
        }

        // Метод для повернення до попереднього виду
        private void ReturnToPreviousView()
        {
            if (panelHistory.Count > 0)
            {
                // Повертаємо попередній контрол
                var previousControl = panelHistory.Pop();
                panelSettingsView.Controls.Clear();
                panelSettingsView.Controls.Add(previousControl);
            }
            else
            {
                // Якщо історія порожня, повертаємо основну панель
                panelSettings.Visible = true;
                panelSettingsView.Controls.Clear();
            }
        }

        private NameEditorControl CreateNameEditorControl()
        {
            var c = new NameEditorControl { Dock = DockStyle.Fill };

            // 1) Підтягнути поточні Позиції
            var positions = Shablon.Position_Point?.Keys?.ToArray() ?? Array.Empty<string>();
            c.SetPositions(positions);

            // Встановлюємо поточну назву підрозділу
            c.SetUnitName(Shablon.CustomUnit); // Використовуємо метод замість прямого доступу

            // Встановлюємо ShablonManager для редактора шаблонів
            c.SetShablonManager(Shablon); // ← Додаємо цей рядок

            // Підписуємось на подію
            c.UnitNameChanged += (_, unitName) =>
            {
                Shablon.CustomUnit = unitName;
                Shablon.SaveShablon();
            };

            // Підписуємось на події для шаблону бойової доповіді
            c.ReportTemplateChanged += (_, template) =>
            {
                Shablon.SaveCustomReportTemplate(template);
                MessageBox.Show("Шаблон бойової доповіді збережено!", "Успіх");
            };

            c.ReportTemplateReset += (_, __) =>
            {
                Shablon.ResetReportTemplate();
                MessageBox.Show("Шаблон скинуто до програмного варіанту!", "Успіх");
            };

            c.ResetUserDataClicked -= OnResetUserDataClicked;
            c.ResetUserDataClicked += OnResetUserDataClicked;

            // 2) Обробники
            c.BackClicked += (_, __) =>
            {
                panelSettingsView.Controls.Clear();
                panelSettingsView.Controls.Add(settingsControl);
                panelSettings.Visible = true;
            };

            c.AddPositionRequested += (_, newPosRaw) =>
            {
                var newPos = Normalize(newPosRaw);
                if (string.IsNullOrEmpty(newPos))
                {
                    MessageBox.Show("Вкажіть назву позиції.", "Увага");
                    return;
                }

                // уникаємо дублів (без урахування регістру)
                if (Shablon.Position_Point.Keys.Any(k => string.Equals(k, newPos, StringComparison.CurrentCultureIgnoreCase)))
                {
                    MessageBox.Show("Така позиція вже існує.", "Увага");
                    return;
                }

                // додаємо
                Shablon.Position_Point[newPos] = new List<string>(); // порожній список пілотів
                if (!Shablon.DroneByPosition.ContainsKey(newPos))
                    Shablon.DroneByPosition[newPos] = new List<string>();

                // збережемо
                Shablon.SaveShablon();

                // оновимо UI (редактор + головну)
                c.SetPositions(Shablon.Position_Point.Keys.ToArray());
                Position.Items.Add(newPos);

                MessageBox.Show("Позицію додано.", "OK");
            };

            c.AddPilotRequested += (_, data) =>
            {
                var pos = data.position;
                var pilot = Normalize(data.pilot);
                if (string.IsNullOrEmpty(pos))
                {
                    MessageBox.Show("Оберіть позицію для пілота.", "Увага");
                    return;
                }
                if (string.IsNullOrEmpty(pilot))
                {
                    MessageBox.Show("Вкажіть позивний пілота.", "Увага");
                    return;
                }

                if (!Shablon.Position_Point.TryGetValue(pos, out var pilots))
                {
                    MessageBox.Show("Такої позиції не знайдено.", "Помилка");
                    return;
                }

                if (pilots.Any(p => string.Equals(p, pilot, StringComparison.CurrentCultureIgnoreCase)))
                {
                    MessageBox.Show("Такий пілот вже є для цієї позиції.", "Увага");
                    return;
                }

                pilots.Add(pilot);
                Shablon.SaveShablon();

                // якщо на головній вибрана саме ця позиція — підкинемо елемент у її комбобокс
                if (string.Equals(Position.SelectedItem?.ToString(), pos, StringComparison.CurrentCultureIgnoreCase))
                    Pilot.Items.Add(pilot);

                MessageBox.Show("Пілота додано.", "OK");

            };

            c.AddDroneRequested += (_, data) =>
            {
                var pos = data.position;
                var drone = Normalize(data.drone);
                if (string.IsNullOrEmpty(pos))
                {
                    MessageBox.Show("Оберіть позицію для дрона.", "Увага");
                    return;
                }
                if (string.IsNullOrEmpty(drone))
                {
                    MessageBox.Show("Вкажіть назву дрона.", "Увага");
                    return;
                }

                if (!Shablon.DroneByPosition.TryGetValue(pos, out var drones))
                {
                    // якщо ключа не було — створимо
                    drones = new List<string>();
                    Shablon.DroneByPosition[pos] = drones;
                }

                if (drones.Any(d => string.Equals(d, drone, StringComparison.CurrentCultureIgnoreCase)))
                {
                    MessageBox.Show("Такий дрон вже є для цієї позиції.", "Увага");
                    return;
                }

                drones.Add(drone);
                Shablon.SaveShablon();

                // якщо на головній вибрана саме ця позиція — підкинемо елемент у комбобокс дронів
                if (string.Equals(Position.SelectedItem?.ToString(), pos, StringComparison.CurrentCultureIgnoreCase))
                    DroneBy.Items.Add(drone);

                MessageBox.Show("Дрон доданий.", "OK");
            };

            //Shablon.SaveOverrides(overridesPath);

            return c;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();

            // прибрати подвоєні пробіли всередині (красиво)
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s;
        }

        private void RestorePreviousControls(object sender, EventArgs e)
        {
            panelSettingsView.Controls.Clear();

            if (previousControls != null)
            {
                panelSettingsView.Controls.AddRange(previousControls);
            }
        }

        private void ShowSettingsPanel(SettingsControl settingsControl)
        {
            if (panelSettingsView == null || settingsControl == null)
                return;

            panelSettingsView.Controls.Clear();
            panelSettingsView.Controls.Add(settingsControl);
            panelSettings.Visible = true;
        }

        // === Допоміжні методи ===
        private void ShowErrorMessage(string message, Exception ex = null)
        {
            string details = ex != null ? $"\n\nДеталі: {ex.Message}" : "";
            MessageBox.Show(message + details, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SaveUserSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(userSettingsPath);
                var settings = new UserSettings
                {
                    SelectedPosition = Position.SelectedItem?.ToString(),
                    SelectedPilot = Pilot.SelectedItem?.ToString(),
                    SelectedDrone = DroneBy.SelectedItem?.ToString(),
                    Height = height.Text,
                    SelectedTarget = Target_Type.Text,
                    SelectedLocalCities = LocalCities.ToList()
                };

                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);

                File.WriteAllText(userSettingsPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка при збереженні налаштувань: {ex.Message}");
            }
        }

        private void LoadUserSettings()
        {
            if (!File.Exists(userSettingsPath)) return;

            try
            {
                var json = File.ReadAllText(userSettingsPath);
                var settings = JsonConvert.DeserializeObject<UserSettings>(json);
                //var match = Position.Items.Cast<object>()
                //.FirstOrDefault(i => string.Equals(i?.ToString(), settings.SelectedPosition, StringComparison.CurrentCultureIgnoreCase));

                if (settings == null) return;

                if (Position.Items.Contains(settings.SelectedPosition))
                    Position.SelectedItem = settings.SelectedPosition;
                else if (!string.IsNullOrEmpty(settings.SelectedPosition))
                    MessageBox.Show($"Збережена позиція \"{settings.SelectedPosition}\" відсутня у списку.", "Увага");

                if (Pilot.Items.Contains(settings.SelectedPilot))
                    Pilot.SelectedItem = settings.SelectedPilot;
                else if (!string.IsNullOrEmpty(settings.SelectedPilot))
                    MessageBox.Show($"Збережений пілот \"{settings.SelectedPilot}\" відсутній у списку.", "Увага");

                if (DroneBy.Items.Contains(settings.SelectedDrone))
                    DroneBy.SelectedItem = settings.SelectedDrone;
                else if (!string.IsNullOrEmpty(settings.SelectedDrone))
                    MessageBox.Show($"Збережений дрон \"{settings.SelectedDrone}\" відсутній у списку.", "Увага");

                if (Target_Type.Items.Contains(settings.SelectedTarget))
                    Target_Type.SelectedItem = settings.SelectedTarget;

                if (!string.IsNullOrEmpty(settings.Height))
                    height.Text = settings.Height;

                LocalCities.Clear();

                if (settings.SelectedLocalCities?.Any() == true)
                {
                    var cleanedCities = settings.SelectedLocalCities
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                    foreach (var city in cleanedCities)
                        LocalCities.Add(city);
                }

                // Оновлюємо стан ВСІХ кнопок
                foreach (var btn in flowLayoutLocalities.Controls.OfType<System.Windows.Forms.Button>())
                {
                    var name = btn.Text.Trim();
                    var isSelected = LocalCities.Contains(name);


                    btn.UseVisualStyleBackColor = false; // щоб BackColor не з’їдався темою
                    btn.BackColor = isSelected ? Color.DarkGray : SystemColors.Control;
                    btn.ForeColor = SystemColors.ControlText;
                    btn.Tag = isSelected; // опційно
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося завантажити налаштування:\n{ex.Message}", "Помилка");
            }

        }

        private void shootingTarget_Enter(object sender, EventArgs e)
        {
            if (shootingTarget.Text == "Патрулювання")
            {
                shootingTarget.Text = "";
                shootingTarget.ForeColor = SystemColors.WindowText;
            }
        }

        private void shootingTarget_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(shootingTarget.Text))
            {
                shootingTarget.Text = "Патрулювання";
                shootingTarget.ForeColor = Color.Gray;
            }
        }

        // Тепер Locality дані завантажуються через сервіс `LocalityService` (Data/localities.json)
        public Dictionary<string, List<PointF>> LocalityCoordinates => localityService?.Localities ?? new Dictionary<string, List<PointF>>();

        private string FindClosestLocality(PointF utm)
        {
            string nearest = "невідомо";
            double minDistance = double.MaxValue;

            if (LocalityCoordinates?.Count == 0 || utm == PointF.Empty || float.IsNaN(utm.X) || float.IsNaN(utm.Y))
                return "невідомо";

            foreach (var kvp in LocalityCoordinates) // Використовуємо правильний словник
            {
                foreach (var point in kvp.Value)
                {
                    double distance = Math.Sqrt(Math.Pow(point.X - utm.X, 2) + Math.Pow(point.Y - utm.Y, 2));
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearest = kvp.Key;
                    }
                }
            }

            return nearest;
        }

        private void CalculateScale()
        {
            if (referencePoints.Count < 4)
            {
                MessageBox.Show("Карта ще не відкалібрована.");
                return;
            }

            isScaleMarkersVisible = true;
            pictureBox1.Invalidate();

            float? realDistance = PromptForRealDistance();
            if (!realDistance.HasValue)
            {
                return; // Якщо некоректне значення, просто виходимо
            }

            bool success = ComputeAndSaveScale(realDistance.Value);

            isScaleMarkersVisible = false;
            pictureBox1.Invalidate();

            if (success)
            {
                MessageBox.Show("Дистанцію відкалібровано.");
            }
        }

        private bool ComputeAndSaveScale(float realDistance)
        {
            var topLeft = referencePoints[0];
            var bottomRight = referencePoints[3];

            float pixelDistance = MathF.Sqrt(
                MathF.Pow(bottomRight.Pixel.X - topLeft.Pixel.X, 2) +
                MathF.Pow(bottomRight.Pixel.Y - topLeft.Pixel.Y, 2));

            if (pixelDistance <= 0 || float.IsNaN(pixelDistance))
            {
                MessageBox.Show("Некоректна відстань між точками! Калібрування не виконано.", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            pixelsToMeters = realDistance / pixelDistance;
            SaveScaleValue();
            return true;
        }

        private float? PromptForRealDistance()
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "Введіть реальну відстань (в метрах) між верхньою лівою та нижньою правою точками карти.",
                "Обчислення масштабу");

            if (float.TryParse(input.Replace(",", "."), System.Globalization.NumberStyles.Any,
                               System.Globalization.CultureInfo.InvariantCulture, out float meters) && meters > 0)
            {
                return meters;
            }

            MessageBox.Show("Некоректна відстань! Введіть значення більше 0.", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        private void SaveScaleValue()
        {
            if (pixelsToMeters > 0)
            {
                File.WriteAllText(scaleFilePath, JsonConvert.SerializeObject(pixelsToMeters));
            }
            else
            {
                Console.WriteLine("Не збережено некоректне значення масштабу!");
            }
        }

        private void LoadScaleValue()
        {
            if (File.Exists(scaleFilePath))
            {
                string json = File.ReadAllText(scaleFilePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        pixelsToMeters = JsonConvert.DeserializeObject<float>(json);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Помилка десеріалізації масштабу: {ex.Message}");
                        pixelsToMeters = 0;
                    }
                }
                else
                {
                    Console.WriteLine("Файл масштабу порожній!");
                    pixelsToMeters = 0;
                }
            }
        }

        private void ToggleLocalityHighlight(string name)
        {
            if (highlightedLocalities.Contains(name))
                highlightedLocalities.Remove(name);
            else
                highlightedLocalities.Add(name);

            pictureBox1.Invalidate(); // Перемалювати
        }

        private void Maps_FormClosing(object sender, FormClosingEventArgs e)
        {
            TrySaveAll();
        }

        private void TrySaveAll()
        {
            if (_saved) return;
            try
            {
                SaveUserSettings();
                flightLogger?.SaveData();
                _saved = true;
            }
            catch (Exception ex)
            {
                // опціонально: лог/повідомлення
                // MessageBox.Show($"Помилка збереження: {ex.Message}");
            }
        }

        private void Maps_FormClosed(object sender, FormClosedEventArgs e)
        {
            // 1) Таймери
            try
            {
                timer?.Stop();
                timer?.Dispose();
            }
            catch { }
            finally
            {
                timer = null;
            }

            // 2) GMapControl (наш mapControl)
            try
            {
                if (mapControl != null)
                {
                    // відписки (дуже важливо, щоб не було "підвисань")
                    mapControl.OnPositionChanged -= Gmap_OnPositionChanged;
                    mapControl.OnMapZoomChanged -= Map_OnMapZoomChanged;   // якщо ти його реально підписував

                    mapControl.MouseDown -= Gmap_MouseDown;
                    mapControl.MouseMove -= Gmap_MouseMove;
                    mapControl.MouseUp -= Gmap_MouseUp;

                    // прибираємо з панелі, щоб точно не лишився в Controls
                    panelMap?.Controls.Remove(mapControl);

                    mapControl.Dispose();
                }
            }
            catch { }
            finally
            {
                mapControl = null;
            }

            // 4) Твій кеш-бітмап
            try
            {
                cachedBitmap?.Dispose();
            }
            catch { }
            finally
            {
                cachedBitmap = null;
            }

            // 5) На всяк випадок — форсуємо очищення, щоб швидко відпустило ресурси
            // (не обовʼязково, але іноді допомагає коли багато Bitmap/Graphics)
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { }
        }

    }   

}

//1 - добавити перевірку якщо населений пункт за межами карти, то щоб показувалось повіддомлення За межами карти
//Зробити валідацію висоти