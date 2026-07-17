using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

// INDEX: Tracing for outbound HTTP (mirror search, .osz download, ppy CDN preview/cover) — logs each request's start and outcome with status, size and elapsed time to a per-session file under persistentDataPath/logs, and to the editor console.
namespace OsuUnity.Util
{
    /// <summary>
    /// Traces outbound HTTP: one line when a request goes out, one when it lands. The mirrors and the ppy
    /// CDN are third-party and rate-limited per-IP (docs/osu-api.md), so seeing what was actually sent —
    /// and how long it took — is the difference between debugging our code and guessing at theirs.
    /// <para>
    /// Every line is written to a per-session file (<see cref="SessionPath"/>), in builds as well as the
    /// editor, so a run can be diagnosed after the fact from the log alone. Console output is editor-only:
    /// in a player the file is the record, and mirroring to <c>Debug</c> would just duplicate it into
    /// <c>player.log</c>. Requests are network-bound and infrequent, so the formatting cost is noise.
    /// </para>
    /// </summary>
    public static class ApiLog
    {
        /// <summary>Session logs kept on disk; older ones are pruned at startup, newest first.</summary>
        private const int KeepSessions = 5;

        private static StreamWriter _writer;
        private static bool _opened;   // the open was attempted — success or not, don't retry per line

        /// <summary>Where session logs live. Safe to surface in UI (e.g. an "open log folder" action).</summary>
        public static string LogDirectory => Path.Combine(Application.persistentDataPath, "logs");

        /// <summary>This run's log file, or <c>null</c> if the log couldn't be opened.</summary>
        public static string SessionPath { get; private set; }

        // Statics outlive a play session when "Enter Play Mode Options" has domain reload switched off, so
        // the previous session would leave a disposed writer behind an already-attempted open flag — every
        // later session silently logging to console only. Start each session from scratch.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForSession()
        {
            Close();
            _opened = false;
            SessionPath = null;
        }

        /// <summary>Log a request going out. <paramref name="tag"/> is the calling system, e.g. "search".</summary>
        public static void Begin(string tag, string url) => Write($"→ {tag}  {url}", warn: false);

        /// <summary>
        /// Log the outcome of <paramref name="req"/> — status, payload size and elapsed time.
        /// <paramref name="sw"/> is the stopwatch started next to the matching <see cref="Begin"/>.
        /// </summary>
        public static void End(string tag, UnityWebRequest req, Stopwatch sw)
        {
            if (req == null) return;
            bool ok = req.result == UnityWebRequest.Result.Success;
            string size = Size(req.downloadedBytes);
            string ms = sw != null ? $"{sw.ElapsedMilliseconds} ms" : "?";
            string line = $"{(ok ? "←" : "×")} {tag}  {(int)req.responseCode}  {size}  {ms}  {req.url}";

            Write(ok ? line : $"{line}\n      {req.result}: {req.error}", warn: !ok);
        }

        /// <summary>Log something about a request that the status line alone doesn't show.</summary>
        public static void Note(string tag, string message) => Write($"  {tag}  {message}", warn: false);

        // ------------------------------------------------------------------ sink

        private static void Write(string line, bool warn)
        {
            string stamped = $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {line}";

            var w = Writer();
            if (w != null)
            {
                try { w.WriteLine(stamped); }
                catch (Exception e)
                {
                    // A mid-session write failure (disk full, file yanked) must not take the request with it.
                    Debug.LogWarning($"[api] log write failed, continuing without it: {e.Message}");
                    try { w.Dispose(); } catch { /* already broken */ }
                    _writer = null;
                }
            }

#if UNITY_EDITOR
            if (warn) Debug.LogWarning($"[api] {line}");
            else Debug.Log($"[api] {line}");
#endif
        }

        private static StreamWriter Writer()
        {
            if (_opened) return _writer;
            _opened = true;

            try
            {
                Directory.CreateDirectory(LogDirectory);
                Prune();

                SessionPath = Path.Combine(LogDirectory,
                    $"api-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log");

                // AutoFlush: sessions end by being killed at least as often as they end cleanly, and a log
                // that loses its tail describes every run except the one being debugged.
                _writer = new StreamWriter(SessionPath, append: false) { AutoFlush = true };
                _writer.WriteLine($"# Fallcall {Application.version}  ·  {Application.platform}  ·  " +
                                  $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");

                Application.quitting += Close;
#if UNITY_EDITOR
                Debug.Log($"[api] logging to {SessionPath}");
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[api] couldn't open log in {LogDirectory}, console only: {e.Message}");
                _writer = null;
                SessionPath = null;
            }

            return _writer;
        }

        private static void Prune()
        {
            try
            {
                var stale = new DirectoryInfo(LogDirectory)
                    .GetFiles("api-*.log")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Skip(KeepSessions - 1);   // this session is about to add one
                foreach (var f in stale) f.Delete();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[api] log prune failed: {e.Message}");   // not worth failing the session over
            }
        }

        private static void Close()
        {
            Application.quitting -= Close;
            try { _writer?.Dispose(); } catch { /* shutting down anyway */ }
            _writer = null;
        }

        private static string Size(ulong bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:0.#} KB";
            return $"{bytes / (1024f * 1024f):0.##} MB";
        }
    }
}
