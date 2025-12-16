# Аналіз проекту Maps та план міграції на GMap.NET

## 📋 Зміст
1. [Загальний опис проекту](#загальний-опис-проекту)
2. [Поточна архітектура](#поточна-архітектура)
3. [Аналіз SVG-залежностей](#аналіз-svg-залежностей)
4. [Аналіз GMap.NET інтеграції](#аналіз-gmapnet-інтеграції)
5. [Проблеми поточної архітектури](#проблеми-поточної-архітектури)
6. [План міграції](#план-міграції)
7. [Нова архітектура](#нова-архітектура)
8. [Чеклист виконання](#чеклист-виконання)

---

## Загальний опис проекту

**Maps** — Windows Forms застосунок (.NET 10) для роботи з картами у військовому контексті:
- Відображення карти місцевості
- Калібрування карти за UTM-координатами
- Вимірювання відстаней та азимутів
- Генерація звітів (початок/закінчення/бойова робота)
- Логування польотів дронів
- Робота з сервоприводом для керування напрямком

### Основні бібліотеки
| Бібліотека | Призначення |
|------------|-------------|
| `Svg` (3.4.7) | Завантаження та рендер SVG-карт |
| `GMap.NET.Core` + `GMap.NET.WindowsForms` | Онлайн-карти (новий функціонал) |
| `CoordinateSharp` | Конвертація UTM ↔ MGRS ↔ LatLng |
| `ProjNet` | Проекційні системи координат |
| `Accord.Math` | Матричні обчислення (афінні перетворення) |
| `Newtonsoft.Json` | Серіалізація налаштувань |
| `SharpDX.Direct2D1` | (невикористовується активно) |
| `System.IO.Ports` | Зв'язок з Arduino/сервоприводом |

---

## Поточна архітектура

### Структура папок

```
Maps/
├── Controllers/          # Порожня (заплановано)
├── Data/
│   └── calibration.json  # Калібрувальні дані
├── Maps/
│   └── MapTest.svg       # SVG-карта
├── Models/
│   ├── AttackPointData.cs
│   ├── CalibrationData.cs
│   ├── FlightLogEntry.cs
│   ├── Locality.cs
│   ├── ReferencePoint.cs
│   └── UserSettings.cs
├── Rendering/
│   └── Overlays/         # Порожня (заплановано)
├── Services/
│   ├── FlightLogger.cs
│   ├── ServoController.cs
│   ├── ShablonManager.cs
│   └── Map/              # Порожня (заплановано)
├── Utilities/
│   └── AffineTransformHelper.cs  # Майже порожній
├── Views/
│   ├── Maps.cs           # ГОЛОВНИЙ ФАЙЛ (~3274 рядки!) 
│   ├── Maps.Designer.cs
│   ├── SettingsControl.cs
│   ├── SimpleGMapForm.cs # Тестова форма GMap
│   ├── FlightStatsForm.cs
│   └── LocalityControl.Designer.cs
├── LocalityControl.cs
├── NameEditorControl.cs
├── CalibrationData.cs    # Дублікат!
├── AttackPointDataExtended.cs
├── ShablonOverrides.cs
└── Program.cs
```

### Діаграма залежностей

```
                    ┌─────────────┐
                    │  Program.cs │
                    └──────┬──────┘
                           │
                    ┌──────▼──────┐
                    │   Maps.cs   │ ◄──── МОНОЛІТНА ФОРМА (~3274 рядки)
                    │  (MainForm) │
                    └──────┬──────┘
           ┌───────────────┼───────────────┐
           │               │               │
    ┌──────▼──────┐ ┌──────▼──────┐ ┌──────▼──────┐
    │SettingsCtrl │ │LocalityCtrl │ │NameEditor  │
    └─────────────┘ └─────────────┘ └─────────────┘
           │
    ┌──────▼──────────────────────────────────────┐
    │                  Services                    │
    ├──────────────┬──────────────┬───────────────┤
    │FlightLogger  │ShablonManager│ServoController│
    └──────────────┴──────────────┴───────────────┘
           │
    ┌──────▼──────────────────────────────────────┐
    │                   Models                     │
    ├────────────┬───────────┬───────────┬────────┤
    │ReferencePoint│Locality │FlightLog │UserSet │
    └────────────┴───────────┴───────────┴────────┘
```

---

## Аналіз SVG-залежностей

### Де використовується SVG

| Файл | Функціонал | Рядки |
|------|------------|-------|
| `Maps.cs` | `LoadSvg()` - завантаження карти | ~710-790 |
| `Maps.cs` | `DrawBaseMap()` - малювання cachedBitmap | ~1320 |
| `Maps.cs` | `pictureBox1_Paint()` - рендер через Graphics | ~1280-1295 |
| `Maps.cs` | `ConvertToPixels()` - конвертація SVG одиниць | ~690-740 |
| `Maps.csproj` | Залежність `Svg` 3.4.7 | - |

### SVG-специфічний код

```csharp
// Завантаження SVG
Svg.SvgDocument doc = Svg.SvgDocument.Open(filePath);
cachedBitmap = doc.Draw(targetW, targetH);

// Конвертація одиниць
Svg.SvgUnit unit; // Svg.SvgUnitType enum

// Шлях до SVG файлу
string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Maps", "MapTest.svg");
```

### Пов'язані дані

1. **Калібрування** - прив'язка пікселів карти до UTM:
   - `referencePoints` (4 точки)
   - `eastingCoeffs`, `northingCoeffs` (афінні коефіцієнти)
   - `_inv` (обернена матриця для UTM→Pixel)

2. **Масштабування**:
   - `_scale` - поточний масштаб
   - `_originalImageSize` - розмір растрової карти
   - `pixelsToMeters` - метри на піксель

3. **Позиціонування**:
   - `_imageOffset` - зміщення карти
   - `attackPoint` - точка атаки (в піксельних координатах)
   - `clickedPointMarker` - вибрана точка

---

## Аналіз GMap.NET інтеграції

### Поточний стан

**SimpleGMapForm.cs** - повністю робоча тестова форма:
- Вибір провайдера карт (OSM, Google, Bing)
- Зум, переміщення
- Відображення координат

**Maps.cs** - частково інтегрований GMapControl:
```csharp
private GMap.NET.WindowsForms.GMapControl? mapControl;

// BtnShowGMap_Click - створює mapControl і додає до panelMap
// Gmap_MouseDown/Move/Up - обробка перетягування
// Map_OnMapZoomChanged - обмеження зуму
// EnsureViewAreaInsideAllowed - обмеження області
// _allowedArea - RectLatLng меж карти
```

### Проблеми інтеграції

1. **Паралельне існування двох систем**:
   - `pictureBox1` з SVG-растром
   - `mapControl` з GMap.NET
   - Кнопка "Test" перемикає видимість

2. **Координатні системи не синхронізовані**:
   - SVG: пікселі → UTM через афінне перетворення
   - GMap: LatLng безпосередньо

3. **Overlay-функціонал не перенесений**:
   - Зона атаки (DrawAttackZone)
   - Маркери калібрування
   - Підписи населених пунктів
   - Лінія курсу

---

## Проблеми поточної архітектури

### 🔴 Критичні

1. **God Object**: `Maps.cs` = 3274 рядки з ~100+ полями
   - UI логіка
   - Бізнес-логіка
   - Рендеринг
   - Калібрування
   - Генерація звітів
   - Робота з файлами

2. **Змішані відповідальності**:
   - Form відповідає за все
   - Немає чіткого поділу Model-View-Controller

3. **Дублювання коду**:
   - `CalibrationData.cs` існує в корені та в Models
   - Координати населених пунктів хардкодені в Maps.cs

### 🟡 Середні

1. **Відсутність абстракцій для карти**:
   - Немає інтерфейсу IMapProvider
   - Пряма залежність від SVG/GMap

2. **Overlay логіка вбудована в Paint**:
   - Немає окремих класів для оверлеїв
   - Складно тестувати

3. **Координатна логіка розкидана**:
   - `PixelToUTM`, `UTMToPixel` в Maps.cs
   - `FormatShortMGRSFromUTM` в Maps.cs
   - Немає CoordinateService

### 🟢 Незначні

1. Невикористовувані папки (Controllers, Rendering/Overlays)
2. `AffineTransformHelper` майже порожній
3. Коментарі різними мовами

---

## План міграції

### Етап 1: Підготовка (без зміни функціоналу)

#### 1.1 Створення сервісу координат
**Файл**: `Services/Map/CoordinateConverter.cs`

```
Функціонал для перенесення:
- PixelToUTM()
- UTMToPixel()
- FormatShortMGRSFromUTM()
- GetUTMBandLetter()
- RebuildInverse()
- SolveAffineTransform()
```

**Тестування**: Запустити програму, перевірити відображення координат при русі миші.

#### 1.2 Винесення даних населених пунктів
**Файл**: `Data/localities.json`

```
Перенести: LocalityCoordinates (словник UTM координат)
Створити: LocalityService для завантаження
```

**Тестування**: Перевірити відображення підписів населених пунктів.

#### 1.3 Створення моделі для зони атаки
**Файл**: `Models/AttackZone.cs`

```
Поля:
- AttackPoint (PointF)
- Angle (float)
- RayLength (float)
- SectorRadius (float)
- SectorWidth (float)
```

**Тестування**: Зона атаки має відображатися коректно.

---

### Етап 2: Абстракція карти

#### 2.1 Інтерфейс провайдера карти
**Файл**: `Services/Map/IMapProvider.cs`

```csharp
interface IMapProvider
{
    void Initialize(Control container);
    void SetBounds(double north, double south, double east, double west);
    PointF ScreenToGeo(Point screen);
    Point GeoToScreen(PointF geo);
    void AddOverlay(IMapOverlay overlay);
    void Refresh();
    event Action<PointF> OnClick;
    event Action<PointF> OnPositionChanged;
}
```

#### 2.2 Реалізація для GMap.NET
**Файл**: `Services/Map/GMapProvider.cs`

**Тестування**: Карта має працювати через новий провайдер.

#### 2.3 Адаптер для SVG (тимчасовий)
**Файл**: `Services/Map/SvgMapProvider.cs`

```
Обгортка для поточного функціоналу SVG.
Потрібна для поступової міграції.
```

**Тестування**: Обидва провайдери мають працювати однаково.

---

### Етап 3: Створення оверлеїв

#### 3.1 Базовий інтерфейс оверлея
**Файл**: `Rendering/Overlays/IMapOverlay.cs`

```csharp
interface IMapOverlay
{
    bool Visible { get; set; }
    void Render(Graphics g, Func<PointF, Point> geoToScreen);
}
```

#### 3.2 Оверлей зони атаки
**Файл**: `Rendering/Overlays/AttackZoneOverlay.cs`

```
Перенести:
- DrawAttackZone()
- CalculateAttackLine()
- GetAttackZoneBounds()
```

**Тестування**: Зона атаки на GMap карті.

#### 3.3 Оверлей населених пунктів
**Файл**: `Rendering/Overlays/LocalityOverlay.cs`

```
Перенести:
- DrawLocalityLabels()
- DrawHighlightedLocalities()
```

**Тестування**: Підписи на GMap карті.

#### 3.4 Оверлей маркерів
**Файл**: `Rendering/Overlays/MarkerOverlay.cs`

```
Перенести:
- DrawClickedPoint()
- DrawCalibrationMarkers()
- DrawScaleMeasurementMarkers()
```

**Тестування**: Маркери на GMap карті.

---

### Етап 4: Контролери

#### 4.1 MapController
**Файл**: `Controllers/MapController.cs`

```
Відповідальність:
- Ініціалізація провайдера карти
- Обробка подій карти (клік, зум, перетягування)
- Управління оверлеями
- Калібрування (для SVG режиму)
```

#### 4.2 ReportController
**Файл**: `Controllers/ReportController.cs`

```
Перенести:
- GenerateTextFromTemplate()
- Start_of_Work_Click логіку
- End_of_Work_Click логіку
- Combat_Work_Click логіку
```

**Тестування**: Генерація звітів.

#### 4.3 InputController
**Файл**: `Controllers/InputController.cs`

```
Перенести:
- Обробку клавіатури (A, D, стрілки)
- Timer_Tick (обертання зони атаки)
```

**Тестування**: Керування кутом атаки.

---

### Етап 5: Рефакторинг головної форми

#### 5.1 Створення MainForm
**Файл**: `Views/MainForm.cs`

```
Замінює Maps.cs
Залишається тільки:
- Ініціалізація UI
- Підключення контролерів
- Делегування подій
```

#### 5.2 Видалення SVG коду
```
Видалити:
- LoadSvg()
- ConvertToPixels()
- cachedBitmap
- pictureBox1 (замінити на mapControl)
- Всі SVG-специфічні обчислення
```

#### 5.3 Видалення пакету Svg
```xml
<!-- Видалити з Maps.csproj -->
<PackageReference Include="Svg" Version="3.4.7" />
```

**Тестування**: Повний функціонал на GMap.NET.

---

### Етап 6: Фінальне очищення

#### 6.1 Видалення тимчасових файлів
```
Видалити:
- SimpleGMapForm.cs
- SvgMapProvider.cs
- Maps/MapTest.svg
```

#### 6.2 Оновлення налаштувань
```
Оновити:
- calibration.json структуру (LatLng замість пікселів)
- attack_point.json (LatLng)
```

#### 6.3 Документація
```
Оновити:
- README.md
- Коментарі в коді
```

---

## Нова архітектура

### Цільова структура

```
Maps/
├── Controllers/
│   ├── MapController.cs         # Управління картою
│   ├── ReportController.cs      # Генерація звітів
│   ├── InputController.cs       # Обробка вводу
│   └── UINavigationController.cs # Навігація між екранами
│
├── Data/
│   ├── calibration.json
│   └── localities.json          # НОВЕ: населені пункти
│
├── Models/
│   ├── AttackZone.cs            # НОВЕ
│   ├── Locality.cs
│   ├── ReferencePoint.cs
│   ├── FlightLogEntry.cs
│   ├── CalibrationData.cs
│   └── UserSettings.cs
│
├── Rendering/
│   └── Overlays/
│       ├── IMapOverlay.cs       # Інтерфейс
│       ├── AttackZoneOverlay.cs
│       ├── LocalityOverlay.cs
│       └── MarkerOverlay.cs
│
├── Services/
│   ├── Map/
│   │   ├── IMapProvider.cs      # Інтерфейс карти
│   │   ├── GMapProvider.cs      # GMap.NET реалізація
│   │   ├── CoordinateConverter.cs
│   │   └── LocalityService.cs
│   │
│   ├── FlightLogger.cs
│   ├── ServoController.cs
│   └── ShablonManager.cs
│
├── Views/
│   ├── MainForm.cs              # Головна форма (~500 рядків)
│   ├── MainForm.Designer.cs
│   ├── SettingsControl.cs
│   ├── LocalityControl.cs
│   ├── NameEditorControl.cs
│   └── FlightStatsForm.cs
│
└── Program.cs
```

### Діаграма нової архітектури

```
┌─────────────────────────────────────────────────────────────┐
│                         MainForm                             │
│  (тонка оболонка: тільки UI + делегування контролерам)      │
└─────────────────────────────────────────────────────────────┘
        │              │              │              │
        ▼              ▼              ▼              ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│MapController │ │ReportController│ │InputController│ │UINavController│
└──────────────┘ └──────────────┘ └──────────────┘ └──────────────┘
        │                                    
        ▼                                    
┌──────────────────────────────────────────┐
│            IMapProvider                   │
│  ┌────────────────────────────────────┐  │
│  │        GMapProvider                │  │
│  │  - GMapControl                     │  │
│  │  - Overlays collection             │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
        │
        ▼
┌──────────────────────────────────────────┐
│              Overlays                     │
├──────────────┬──────────────┬────────────┤
│AttackZone    │ Locality     │ Marker     │
│Overlay       │ Overlay      │ Overlay    │
└──────────────┴──────────────┴────────────┘
        │
        ▼
┌──────────────────────────────────────────┐
│              Services                     │
├──────────────┬──────────────┬────────────┤
│Coordinate    │ Locality     │ Flight     │
│Converter     │ Service      │ Logger     │
└──────────────┴──────────────┴────────────┘
```

---

## Чеклист виконання

### Етап 1: Підготовка
- [ ] 1.1 Створити `CoordinateConverter.cs`
- [ ] 1.1 Перенести координатні методи
- [ ] 1.1 Тест: координати при русі миші
- [ ] 1.2 Створити `localities.json`
- [ ] 1.2 Створити `LocalityService.cs`
- [ ] 1.2 Тест: підписи населених пунктів
- [ ] 1.3 Створити `AttackZone.cs` модель
- [ ] 1.3 Тест: зона атаки

### Етап 2: Абстракція карти
- [ ] 2.1 Створити `IMapProvider.cs`
- [ ] 2.2 Створити `GMapProvider.cs`
- [ ] 2.2 Тест: GMap через провайдер
- [ ] 2.3 Створити `SvgMapProvider.cs` (тимчасовий)
- [ ] 2.3 Тест: SVG через провайдер

### Етап 3: Оверлеї
- [ ] 3.1 Створити `IMapOverlay.cs`
- [ ] 3.2 Створити `AttackZoneOverlay.cs`
- [ ] 3.2 Тест: зона атаки на GMap
- [ ] 3.3 Створити `LocalityOverlay.cs`
- [ ] 3.3 Тест: підписи на GMap
- [ ] 3.4 Створити `MarkerOverlay.cs`
- [ ] 3.4 Тест: маркери на GMap

### Етап 4: Контролери
- [ ] 4.1 Створити `MapController.cs`
- [ ] 4.2 Створити `ReportController.cs`
- [ ] 4.2 Тест: генерація звітів
- [ ] 4.3 Створити `InputController.cs`
- [ ] 4.3 Тест: керування кутом
- [ ] 4.4 Створити `UINavigationController.cs`

### Етап 5: Рефакторинг форми
- [ ] 5.1 Створити `MainForm.cs`
- [ ] 5.1 Перенести UI з `Maps.cs`
- [ ] 5.2 Видалити SVG код з `Maps.cs`
- [ ] 5.2 Видалити `pictureBox1`
- [ ] 5.3 Видалити пакет `Svg`
- [ ] 5.3 Тест: повний функціонал на GMap

### Етап 6: Очищення
- [ ] 6.1 Видалити `SimpleGMapForm.cs`
- [ ] 6.1 Видалити `SvgMapProvider.cs`
- [ ] 6.1 Видалити `Maps/MapTest.svg`
- [ ] 6.2 Оновити формат `calibration.json`
- [ ] 6.2 Оновити формат `attack_point.json`
- [ ] 6.3 Оновити документацію

---

## Примітки

### Ризики міграції
1. **Калібрування**: GMap.NET працює з LatLng, а не пікселями. Потрібно адаптувати логіку.
2. **Офлайн режим**: Переконатися, що GMap.NET підтримує кешування тайлів.
3. **Точність**: Перевірити відповідність координат UTM ↔ LatLng.

### Рекомендації
1. Робити коміти після кожного підетапу.
2. Тестувати після кожної зміни.
3. Зберігати резервну копію `Maps.cs` до повної міграції.

---

*Документ створено: 16 грудня 2025*
*Автор: GitHub Copilot*
