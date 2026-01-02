# 🔍 SQLite Cache Diagnostics Guide

## Проблема: Не всі тайли кешуються

Ви завантажили 720 тайлів, але в БД тільки 300. Можливі причини:

---

## 1. 🔧 Додано логування для діагностики

### Output Window логи (Debug режим):

```
[SQLite] WRITE SUCCESS: Zoom=7, X=74, Y=42, Size=15234B
[SQLite] WRITE FAILED: Zoom=7, X=75, Y=43 - constraint failed
[SQLite] READ SUCCESS: Zoom=7, X=74, Y=42, Size=15234B
[SQLite] READ MISS: Zoom=7, X=76, Y=44 - not found in DB
```

### CacheEngine логи:
```
CacheEngine: start
CacheEngine[45]: storing tile {7,74,42}, 15kB...
```

---

## 2. 📊 Кнопка діагностики в UI

Додано нову кнопку: **"📊 Cache Diagnostics"** у секції Cache.

Показує:
- Шлях до БД файлу
- Розмір файлу (MB)
- Кількість тайлів у БД (реальна)
- Статистика завантажень
- Налаштування кешу

---

## 3. ❓ Чому файл завжди 32MB?

**Відповідь:** Pre-allocation (попереднє виділення місця).

```csharp
// SQLitePureImageCache.cs, рядок 136-188
void CheckPreAllocation()
{
    // Якщо вільного місця < 4MB → додати ще 32MB
    if (freeMB <= waitUntilMB)
    {
        PreAllocateDB(_db, addSizeMB); // addSizeMB = 32
    }
}
```

**Для чого:**
- Зменшити фрагментацію файлу
- Прискорити запис (не потрібно постійно збільшувати файл)

**Як побачити реальний розмір:**
1. Запустіть `VACUUM` (оптимізація БД)
2. Або використайте SQLite browser для перегляду `SELECT COUNT(*) FROM Tiles`

---

## 4. 🐛 Можливі причини втрати тайлів

### A) Черга кешу не встигає записати

**Проблема:** 
- Тайли додаються в чергу швидко (720 за секунди)
- CacheEngine обробляє повільно (1 тайл кожні 333ms = ~3 тайли/сек)
- При закритті програми черга очищається

**Рішення:**
```csharp
// Прискорити запис (видалити затримки)
GMaps.Instance.BoostCacheEngine = true;

// Вимкнути блокування запису під час читання
GMaps.Instance.CacheOnIdleRead = false;

// Почекати завершення запису
GMaps.Instance.OnTileCacheComplete += () => {
    Debug.WriteLine("All tiles cached!");
};
```

### B) Дублікати не записуються

**Проблема:**
```sql
INSERT INTO Tiles(X, Y, Zoom, Type, ...) VALUES(...)
-- UNIQUE constraint на (X, Y, Zoom, Type)
-- Якщо тайл вже є → CONSTRAINT FAILED
```

**Перевірка:**
- Подивіться логи `[SQLite] WRITE FAILED: ... - constraint failed`
- Це нормально якщо тайл вже є в БД

### C) Режим доступу не ServerAndCache

**Проблема:**
```csharp
// Якщо Mode = ServerOnly → тайли не кешуються
MainMap.Manager.Mode = AccessMode.ServerOnly;
```

**Перевірка:**
```csharp
Debug.WriteLine("Mode: " + MainMap.Manager.Mode); // має бути ServerAndCache
```

### D) PrimaryCache = null або не SQLite

**Проблема:**
```csharp
if (GMaps.Instance.PrimaryCache == null) {
    // Кеш не працює!
}
```

**Перевірка:** Кнопка діагностики покаже помилку якщо PrimaryCache не SQLite.

---

## 5. 🧪 Тестування кешування

### Тест 1: Перевірити що тайли додаються в чергу

1. Відкрийте Output Window (View → Output, Ctrl+Alt+O)
2. Виберіть "Debug"
3. Гортайте карту
4. Має з'явитись:
```
CacheEngine[45]: storing tile {7,74,42}, 15kB...
[SQLite] WRITE SUCCESS: Zoom=7, X=74, Y=42, Size=15234B
```

### Тест 2: Почекати завершення запису

```csharp
// У MainWindow.xaml.cs, додайте в InitializeComponent():
MainMap.Manager.OnTileCacheComplete += () => {
    Dispatcher.Invoke(() => {
        MessageBox.Show("Cache writing complete!");
    });
};
```

### Тест 3: Перевірити реальну кількість у БД

1. Завантажте тайли
2. Почекайте 5-10 секунд (для запису)
3. Натисніть кнопку "📊 Cache Diagnostics"
4. Подивіться "Tiles in DB: XXX"

### Тест 4: Використати SQLite Browser

1. Закрийте програму (щоб відпустити БД)
2. Відкрийте `TileDBv5\en\Data.gmdb` у [DB Browser for SQLite](https://sqlitebrowser.org/)
3. Execute SQL:
```sql
SELECT COUNT(*) FROM Tiles;
SELECT COUNT(*) FROM TilesData;
```

---

## 6. ⚡ Прискорення кешування

### Варіант 1: Boost Cache Engine

```csharp
// У MainWindow constructor або Loaded:
MainMap.Manager.BoostCacheEngine = true;  // Видалити Thread.Sleep(333)
MainMap.Manager.CacheOnIdleRead = false;  // Не чекати на читання
```

**Результат:** ~10x швидше (30 тайлів/сек замість 3)

### Варіант 2: Підписатись на OnTileCacheProgress

```csharp
int lastProgress = -1;
MainMap.Manager.OnTileCacheProgress += (left) => {
    if (left != lastProgress) {
        lastProgress = left;
        Dispatcher.Invoke(() => {
            GroupBox3.Header = $"Caching: {left} tiles left";
        });
    }
};

MainMap.Manager.OnTileCacheComplete += () => {
    Dispatcher.Invoke(() => {
        GroupBox3.Header = "Cache complete!";
        // Оновити діагностику
        buttonCacheDiag_Click(null, null);
    });
};
```

---

## 7. 🔍 Діагностичні команди

### Перевірити що файл існує:
```csharp
var cache = GMaps.Instance.PrimaryCache as SQLitePureImageCache;
var dbPath = Path.Combine(cache.GtileCache, GMapProvider.LanguageStr, "Data.gmdb");
Debug.WriteLine("DB exists: " + File.Exists(dbPath));
Debug.WriteLine("DB size: " + new FileInfo(dbPath).Length);
```

### Перевірити що черга порожня:
```csharp
// Після завантаження тайлів, почекайте і перевірте:
Thread.Sleep(5000); // 5 секунд
// Якщо OnTileCacheComplete не викликався → черга ще обробляється
```

### Примусово зачекати на завершення:
```csharp
// Примітка: це блокує UI!
while (GMaps.Instance.TileCacheQueue.Count > 0) {
    Thread.Sleep(100);
}
Debug.WriteLine("Cache queue empty!");
```

---

## 8. 📋 Checklist діагностики

- [ ] Output Window показує `[SQLite] WRITE SUCCESS`?
- [ ] Кнопка діагностики показує правильний шлях до БД?
- [ ] `Mode = ServerAndCache`?
- [ ] `PrimaryCache != null`?
- [ ] Почекали 5+ секунд після завантаження?
- [ ] `OnTileCacheComplete` викликався?
- [ ] Реальна кількість у БД = очікувана?
- [ ] SQLite Browser показує правильну кількість?

---

## 9. 🚀 Швидке рішення

```csharp
// Додайте в MainWindow constructor або Loaded event:

// 1. Прискорити кешування
MainMap.Manager.BoostCacheEngine = true;
MainMap.Manager.CacheOnIdleRead = false;

// 2. Показати прогрес
MainMap.Manager.OnTileCacheProgress += (left) => {
    Dispatcher.Invoke(() => {
        Debug.WriteLine($"Caching: {left} tiles left");
    });
};

// 3. Показати завершення
MainMap.Manager.OnTileCacheComplete += () => {
    Dispatcher.Invoke(() => {
        var cache = MainMap.Manager.PrimaryCache as SQLitePureImageCache;
        int count = cache?.GetTileCount() ?? 0;
        MessageBox.Show($"Cache complete! Total tiles: {count}", "GMap.NET");
    });
};
```

---

**🎯 Рекомендація:** Запустіть програму, натисніть "📊 Cache Diagnostics" ПЕРЕД і ПІСЛЯ завантаження тайлів, порівняйте "Tiles in DB".
