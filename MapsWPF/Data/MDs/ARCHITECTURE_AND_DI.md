Огляд архітектури і приклад реєстрації DI

Мета
- Коротко описати поточні рішення по шарам проєкту та показати, як підключити сервіси через DI (Microsoft.Extensions.DependencyInjection).

Структура (рекомендована)
- Services/ — логіка додатка і сервіси (TemplateService, MapService, NotificationService, ClipboardService, Reporting/ReportService)
- Domain/ або Models/ — прості POCO моделі, налаштування (MapStartSettings, AttackSettings)
- ViewModels/ — класи, що прив'язуються до UI (TemplateEditorViewModel, ReportViewModel)
- Views/ — XAML вікна/контроли (MainWindow, CacheStatsWindow)
- Data/MDs/ — документація і реновації

Важливі рішення, виконані в рефакторі
- Логіка генерації звітів винесена в `Services/Reporting/ReportService.cs` (тепер сервіс — plain class у шарі сервісів).
- Інтерфейси для обмеження залежностей:
  - `ISelectionProvider` — дає контролеру доступ до вибору/стану (позиції, пілота, цілі тощо)
  - `IReportOutput` — інтерфейс для виводу звітів / копіювання / показу азимута
- MainWindow реалізує `ISelectionProvider` та `IReportOutput` та надає їх `ReportService` (поки що інстанціювання робиться вручну в MainWindow).

Чому DI корисний тут
- Покращує тестуваність (легко мокнути `ISelectionProvider` і перевірити `ReportService`).
- Центрально реєструє singleton/transient сервіси та дозволяє змінювати реалізації без зміни коду створення.

Приклад: як зреєструвати сервіси через Microsoft DI (App.xaml.cs або Program)

```csharp
// Add package: Microsoft.Extensions.DependencyInjection
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
// Сервіси (singleton - зручний для сервісів стану)
services.AddSingleton<TemplateService>();
services.AddSingleton<MapService>(sp => new MapService(new CoordinateConverter()));
services.AddSingleton<NotificationService>();
services.AddSingleton<ClipboardService>();

// ReportService: можна як singleton (стрімкий контроль стану) або transient
services.AddSingleton<Services.Reporting.ReportService>();

// ViewModels
services.AddTransient<ViewModels.ReportViewModel>();
services.AddTransient<ViewModels.TemplateEditorViewModel>();

var provider = services.BuildServiceProvider();

// Отримати інстанцію в App або в MainWindow
var main = provider.GetRequiredService<MainWindow>();
main.Show();
```

Приклад реєстрації ReportService з конкретними залежностями (якщо не використовувати конструктор-інжекцію для MapService тощо):

```csharp
services.AddSingleton(sp => new Services.Reporting.ReportService(
    sp.GetRequiredService<TemplateService>(),
    sp.GetRequiredService<MapService>(),
    sp.GetRequiredService<NotificationService>(),
    sp.GetRequiredService<ClipboardService>(),
    sp.GetRequiredService<ISelectionProvider>(),
    sp.GetRequiredService<IReportOutput>()));
```

Пряме використання у MainWindow (поточний стан):
- Ми наразі викликаємо конструктора вручну у `MainWindow`:
```csharp
_reportController = new Services.Reporting.ReportController(_templateService, _mapService, _notificationService, _clipboardService, this, this);
```
Це простіше і працює, але DI дає більшу гнучкість (особливо для тестування).

Рекомендація
- Зареєструвати базові сервіси як singletons (TemplateService, MapService, NotificationService, ClipboardService).
- ReportController: singleton якщо ви хочете один контролер для всієї сесії; transient якщо хочете інстанцію для кожного використання.
- MainWindow: отримувати залежності через провайдера або через конструктор при створенні через DI.

Далі я можу:
- Додати приклад інтеграції DI в `App.xaml.cs` (невеликий додатковий коміт),
- Автоматизувати реєстрацію і використання в `MainWindow` (замість ручного new).

---
Файл оновлено: `Data/MDs/ARCHITECTURE_AND_DI.md`