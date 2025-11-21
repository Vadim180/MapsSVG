//namespace Maps
//{
//    internal static class Program
//    {
//        /// <summary>
//        ///  The main entry point for the application.
//        /// </summary>
//        [STAThread]
//        static void Main()
//        {
//            // To customize application configuration such as set high DPI settings or default font,
//            // see https://aka.ms/applicationconfiguration.
//            Application.SetHighDpiMode(HighDpiMode.SystemAware); // Додаємо підтримку правильного масштабування DPI
//            Application.EnableVisualStyles();
//            Application.SetCompatibleTextRenderingDefault(false);
//            ApplicationConfiguration.Initialize();
//            Application.Run(new Maps());
//        }
//    }
//}

namespace Maps
{
    internal static class Program
    {
        [STAThread]

        static void Main()
        {
            // Сучасний підхід для .NET 6+ - автоматично застосовує налаштування з .csproj
            ApplicationConfiguration.Initialize();

            // TEST MSG
            // Запуск головної форми
            Application.Run(new Maps());
        }
    }
}