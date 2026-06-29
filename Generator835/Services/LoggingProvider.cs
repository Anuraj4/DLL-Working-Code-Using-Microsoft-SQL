using System;
using System.IO;
using Serilog;

namespace Edi.Generator835.Services
{
    public static class LoggingProvider
    {
        private static bool _isInitialized = false;

        // Exposed so EnterpriseGenerator and other classes can use it
        public static IEdiLogger? Logger { get; private set; }

        /// <summary>
        /// Safe init: tries Serilog, falls back to plain file logging.
        /// Use this from A360Runner.Execute().
        /// </summary>
        public static void InitializeSafe(string logDirectory = "logs")
        {
            try
            {
                Initialize(logDirectory);  // try Serilog path
            }
            catch (Exception ex)
            {
                // Serilog failed (version conflict) — use plain logger
                Console.WriteLine($"[LoggingProvider] Serilog unavailable, using fallback: {ex.Message}");
                Logger = new PlainFileLogger(logDirectory);
                _isInitialized = true;
            }
        }

        /// <summary>
        /// Original Serilog init — used by Runner.exe directly.
        /// </summary>
        public static void Initialize(string logDirectory = "logs")
        {
            if (_isInitialized)
            {
                Serilog.Log.CloseAndFlush();
                _isInitialized = false;
            }

            Directory.CreateDirectory(logDirectory);

            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(logDirectory, "edi.log"))
                .CreateLogger();

            // Wire Serilog into the shared Logger property too
            Logger = new SerilogAdapter();

            _isInitialized = true;

            Serilog.Log.Information("Logging initialized. Directory: {LogDir}",
                Path.GetFullPath(logDirectory));
        }

        public static void Shutdown()
        {
            Serilog.Log.CloseAndFlush();
            _isInitialized = false;
        }
    }

    // Thin adapter so Serilog satisfies IEdiLogger
    internal class SerilogAdapter : IEdiLogger
    {
        public void Debug(string message)       => Serilog.Log.Debug(message);
        public void Information(string message) => Serilog.Log.Information(message);
        public void Warning(string message)     => Serilog.Log.Warning(message);
        public void Error(string message, Exception ex = null)
        {
            if (ex != null) Serilog.Log.Error(ex, message);
            else            Serilog.Log.Error(message);
        }
    }
}