# 📊 Cache Statistics Display

## UI Статистика кешу

В інтерфейсі програми (в секції "Coordinates") відображається статистика завантаження тайлів:

```
Cache: RAM:35 SQLite:8 Net:2
```

### Що означають цифри:
- **RAM** - кількість тайлів завантажених з оперативної пам'яті (миттєво ⚡)
- **SQLite** - кількість тайлів завантажених з локальної бази даних (~50-200ms 💾)
- **Net** - кількість тайлів завантажених з інтернету (500-2000ms 🌐)

### Кольорове кодування:
- 🟢 **Зелений** - всі тайли з кешу (RAM або SQLite), мережа не використовувалась
- 🟠 **Оранжевий** - комбінація: частина з кешу, частина з мережі
- 🔴 **Червоний** - тільки з мережі (перший запуск або очищений кеш)
- ⚪ **Сірий** - немає даних

### Оптимізації:
- ✅ **Debouncing**: Статистика оновлюється максимум раз на 500ms
- ✅ **Тільки при змінах**: UI оновлюється лише якщо цифри змінились
- ✅ **Без логування**: Debug логи в Output Window видалені для продуктивності

---

## 📊 Програмний доступ до статистики

```csharp
// Отримати статистику
int ramHits = GMaps.Instance.TilesFromMemoryCache;
int sqliteHits = GMaps.Instance.TilesFromSQLiteCache;
int networkLoads = GMaps.Instance.TilesFromNetwork;

Debug.WriteLine($"Tiles from RAM: {ramHits}");
Debug.WriteLine($"Tiles from SQLite: {sqliteHits}");
Debug.WriteLine($"Tiles from Network: {networkLoads}");

// Скинути статистику
GMaps.Instance.ResetCacheStatistics();
```

---

## 🔄 Як працює кешування

```
1. Запит тайла
   ↓
2. Перевірка MemoryCache (RAM, 64MB)
   ✅ Знайдено → Повернути миттєво
   ❌ Не знайдено → Крок 3
   ↓
3. Перевірка SQLite (Диск)
   ✅ Знайдено → Додати в MemoryCache → Повернути
   ❌ Не знайдено → Крок 4
   ↓
4. Завантаження з мережі
   → Додати в MemoryCache
   → Зберегти в SQLite
   → Повернути
```

---

## 🧪 Як протестувати

### Тест 1: Перший запуск (всі з мережі)
1. Видаліть кеш: `%LOCALAPPDATA%\GMap.NET\`
2. Запустіть програму
3. UI покаже: `Cache: RAM:0 SQLite:0 Net:45` 🔴

### Тест 2: Повторний перегляд (з RAM)
1. Перетягніть карту вправо
2. Перетягніть назад вліво
3. UI покаже: `Cache: RAM:45 SQLite:0 Net:0` 🟢

### Тест 3: Перезапуск програми (з SQLite)
1. Закрийте програму
2. Запустіть знову
3. UI покаже: `Cache: RAM:0 SQLite:45 Net:0` 🟢

### Тест 4: Нова область (комбінація)
1. Перейдіть в нову область карти
2. Деякі тайли будуть з SQLite (якщо їх переглядали раніше)
3. Деякі з мережі (нові тайли)
4. UI покаже: `Cache: RAM:10 SQLite:20 Net:15` 🟠

---

## 📁 Розташування SQLite кешу

**Windows:**
```
%LOCALAPPDATA%\GMap.NET\TileDBv5\en\
```

**Повний шлях:**
```
C:\Users\{YourName}\AppData\Local\GMap.NET\TileDBv5\en\
```

**Файли:**
- `Data.gmdb` - основна SQLite база з тайлами
- `{Provider}.gmdb` - окремі бази для різних провайдерів

---

## 🎯 Оптимальна конфігурація

```csharp
// У MainWindow.xaml.cs або при ініціалізації:

// 1. Memory Cache (за замовчуванням 64MB, можна збільшити)
GMaps.Instance.MemoryCache.Capacity = 128; // 128MB для більшого кешу

// 2. Tile Matrix Buffer (за замовчуванням +100 тайлів)
MainMap.Manager.Core.Matrix.MaxTilesPerLevelBeyondVisible = 200; // Більше для drag

// 3. Mode (за замовчуванням ServerAndCache)
MainMap.Manager.Mode = AccessMode.ServerAndCache; // Стандартний режим
// AccessMode.CacheOnly - тільки кеш (offline)
// AccessMode.ServerOnly - тільки мережа (без кешування)
```

---

## 🐛 Діагностика проблем

### Проблема: Тайли не кешуються (завжди NETWORK)

**Перевірка 1:** SQLite ввімкнений?
```csharp
Debug.WriteLine("PrimaryCache: " + (GMaps.Instance.PrimaryCache != null));
```

**Перевірка 2:** Режим доступу
```csharp
Debug.WriteLine("Mode: " + GMaps.Instance.Mode);
// Має бути ServerAndCache
```

**Перевірка 3:** Provider підтримує кешування?
```csharp
Debug.WriteLine("BypassCache: " + MainMap.MapProvider.BypassCache);
// Має бути false
```

### Проблема: Тайли завжди з SQLite (не потрапляють в RAM)

**Перевірка:** MemoryCache ввімкнений?
```csharp
Debug.WriteLine("UseMemoryCache: " + GMaps.Instance.UseMemoryCache);
Debug.WriteLine("MemoryCache Size: " + GMaps.Instance.MemoryCache.Size + "MB");
Debug.WriteLine("MemoryCache Capacity: " + GMaps.Instance.MemoryCache.Capacity + "MB");
```

---

## ⚡ Переваги оптимізованої статистики

- **Без спаму**: Оновлення не частіше 1 разу на 500ms
- **Тільки зміни**: UI оновлюється лише при зміні значень
- **Без логів**: Немає Debug.WriteLine в Output Window
- **Продуктивність**: Мінімальний вплив на швидкість роботи

---

**🎉 Легка і ефективна статистика кешу!**
