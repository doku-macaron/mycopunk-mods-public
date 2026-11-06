using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;

namespace MycopunkShared
{
    /// <summary>
    /// Writes one plugin's log lines to its own file per launch, because BepInEx/LogOutput.log is overwritten
    /// on every launch. The file is BepInEx/&lt;folder&gt;/logs/&lt;prefix&gt;-&lt;start time&gt;.log.
    ///
    /// Compiled into every mod (Directory.Build.props), so each assembly has its own internal copy and the
    /// mods do not depend on each other at runtime.
    /// </summary>
    public sealed class SessionLogFile : IDisposable
    {
        private sealed class Listener : ILogListener
        {
            private readonly ManualLogSource _source;
            private readonly StreamWriter _writer;

            public Listener(ManualLogSource source, string path)
            {
                _source = source;
                _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                _writer.NewLine = "\n";
                _writer.AutoFlush = true;
            }

            public void LogEvent(object sender, LogEventArgs e)
            {
                if (!ReferenceEquals(e.Source, _source)) return;
                try
                {
                    _writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [" + e.Level + "] " + e.Data);
                }
                catch
                {
                    // Never let logging break the game.
                }
            }

            public void Dispose()
            {
                try { _writer.Dispose(); } catch { }
            }
        }

        private Listener _listener;

        /// <summary>Full path of the log file.</summary>
        public string FilePath { get; }

        private SessionLogFile(Listener listener, string path)
        {
            _listener = listener;
            FilePath = path;
        }

        /// <summary>BepInEx/&lt;folder&gt;/logs, created if missing.</summary>
        public static string LogDirectory(string folder)
        {
            var dir = Path.Combine(Paths.BepInExRootPath, folder, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Starts copying the lines of <paramref name="log"/> to a new file. Returns null (and logs a warning) on failure.</summary>
        public static SessionLogFile Open(ManualLogSource log, string folder, string prefix)
        {
            try
            {
                var path = Path.Combine(LogDirectory(folder), prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                var listener = new Listener(log, path);
                Logger.Listeners.Add(listener);
                return new SessionLogFile(listener, path);
            }
            catch (Exception e)
            {
                log.LogWarning("Session log file disabled: " + e.Message);
                return null;
            }
        }

        public void Dispose()
        {
            if (_listener == null) return;
            Logger.Listeners.Remove(_listener);
            _listener.Dispose();
            _listener = null;
        }
    }
}
