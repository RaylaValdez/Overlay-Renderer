namespace Overlay_Renderer.Helpers
{
    public static class Logger
    {
        private static readonly object _lock = new();
        private static StreamWriter? _logFile;
        private static bool _initialized;
        private static string _logPath = string.Empty;

        public static void Initialize(string? customPath = null)
        {
            lock (_lock)
            {
                if (_initialized)
                    return;

                string folder = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(folder);

                _logPath = customPath ?? Path.Combine(folder,
                    $"OverlayRenderer_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

                _logFile = new StreamWriter(_logPath, append: true)
                {
                    AutoFlush = true
                };

                _initialized = true;
                Info($"Logger initialized → {_logPath}");
            }
        }

        public static void Info(string message) => Write("INFO", ConsoleColor.Cyan, message);
        public static void Warn(string message) => Write("WARN", ConsoleColor.Yellow, message);
        public static void Error(string message) => Write("ERROR", ConsoleColor.Red, message);
        public static void Debug(string message) => Write("DEBUG", ConsoleColor.Gray, message);

        public static void Exception(Exception ex, string? context = null)
        {
            string msg = context != null
                ? $"{context}: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}"
                : $"{ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}";
            Write("EX", ConsoleColor.Red, msg);
        }

        private static void Write(string level, ConsoleColor color, string message)
        {
            lock (_lock)
            {
                if (!_initialized)
                    Initialize();

                string time = DateTime.Now.ToString("HH:mm:ss.fff");
                string line = $"[{time}] [{level}] {message}";

                // Console
                var oldColor = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(line);
                Console.ForegroundColor = oldColor;

                // Debug output (e.g. VS Output window)
                System.Diagnostics.Debug.WriteLine(line);

                // File
                _logFile?.WriteLine(line);
            }
        }

        public static void Close()
        {
            lock (_lock)
            {
                _logFile?.Dispose();
                _logFile = null;
                _initialized = false;
            }
        }
    }
}
