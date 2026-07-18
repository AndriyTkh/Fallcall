using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using OsuUnity.Beatmaps;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Local, on-disk history of every play, keyed per beatmap-difficulty. Full history is kept (not just
    /// the best) so a later local-ranking UI can list, sort and filter plays. All records live in one JSON
    /// file under <see cref="Application.persistentDataPath"/>; the file is loaded once and cached.
    ///
    /// Storage: <c>&lt;persistentDataPath&gt;/local-scores.json</c>. Layout is a versioned wrapper
    /// (<see cref="StoreFile"/>) around a flat <c>List&lt;ScoreRecord&gt;</c> — JsonUtility can't serialize a
    /// top-level list, so it must be wrapped. Each record carries its own <see cref="ScoreRecord.MapKey"/>,
    /// so a single file serves every map and queries just filter by key.
    ///
    /// Key: <see cref="KeyFor"/> prefers the real osu! beatmap hash (MD5 of the .osu file, what osu! itself
    /// keys on — stable across renames/moves and portable between installs) and falls back to a metadata
    /// composite when the source file can't be read.
    /// </summary>
    public static class LocalScoreStore
    {
        /// <summary>Bump when <see cref="StoreFile"/>/<see cref="ScoreRecord"/> change shape incompatibly.
        /// Loaders should tolerate older values (append-only fields), so this rarely moves.</summary>
        public const int SchemaVersion = 1;

        private const string FileName = "local-scores.json";

        /// <summary>JsonUtility-friendly root: a version stamp plus the flat record list.</summary>
        [Serializable]
        private sealed class StoreFile
        {
            public int Version = SchemaVersion;
            public List<ScoreRecord> Records = new List<ScoreRecord>();
        }

        private static StoreFile _cache;
        private static readonly object _lock = new object();

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        // ------------------------------------------------------------------ keys

        /// <summary>
        /// Stable per-difficulty key for <paramref name="map"/>. Uses the .osu file's MD5 when the source
        /// path is readable ("md5:…", the genuine osu! beatmap hash); otherwise a sanitized metadata
        /// composite ("meta:Artist - Title [Version]|creator|beatmapId"). Never returns null/empty.
        /// </summary>
        public static string KeyFor(Beatmap map)
        {
            if (map == null) return "meta:(null)";

            string hash = TryHashOsuFile(map.SourcePath);
            if (hash != null) return "md5:" + hash;

            var m = map.Metadata;
            string composite = $"{m.Artist} - {m.Title} [{m.Version}]|{m.Creator}|{m.BeatmapID}";
            return "meta:" + composite;
        }

        private static string TryHashOsuFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (!File.Exists(path)) return null;
                using var md5 = MD5.Create();
                byte[] hash = md5.ComputeHash(File.ReadAllBytes(path));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"LocalScoreStore: could not hash '{path}': {e.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------------ write

        /// <summary>Append one play to the history and persist. No-op on a null record.</summary>
        public static void Submit(ScoreRecord record)
        {
            if (record == null) return;
            if (string.IsNullOrEmpty(record.MapKey)) record.MapKey = "meta:(unknown)";
            if (string.IsNullOrEmpty(record.TimestampUtc))
                record.TimestampUtc = DateTime.UtcNow.ToString("o");

            lock (_lock)
            {
                var store = Load();
                store.Records.Add(record);
                Persist(store);
            }
        }

        // ------------------------------------------------------------------ read / query

        /// <summary>All plays for <paramref name="key"/>, newest first. Empty list if none.</summary>
        public static List<ScoreRecord> GetHistory(string key)
        {
            var result = new List<ScoreRecord>();
            if (string.IsNullOrEmpty(key)) return result;

            lock (_lock)
            {
                foreach (var r in Load().Records)
                    if (r != null && r.MapKey == key) result.Add(r);
            }

            // Newest first. TimestampUtc is round-trip ("o"), so an ordinal string compare == chronological.
            result.Sort((a, b) => string.CompareOrdinal(b.TimestampUtc, a.TimestampUtc));
            return result;
        }

        /// <summary>Convenience overload keyed straight off a beatmap.</summary>
        public static List<ScoreRecord> GetHistory(Beatmap map) => GetHistory(KeyFor(map));

        /// <summary>Highest-scoring play for <paramref name="key"/>, or null if none exist.</summary>
        public static ScoreRecord GetBest(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            ScoreRecord best = null;
            lock (_lock)
            {
                foreach (var r in Load().Records)
                {
                    if (r == null || r.MapKey != key) continue;
                    if (best == null || r.Score > best.Score) best = r;
                }
            }
            return best;
        }

        /// <summary>Convenience overload keyed straight off a beatmap.</summary>
        public static ScoreRecord GetBest(Beatmap map) => GetBest(KeyFor(map));

        // ------------------------------------------------------------------ IO

        private static StoreFile Load()
        {
            if (_cache != null) return _cache;

            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var parsed = JsonUtility.FromJson<StoreFile>(json);
                    if (parsed != null)
                    {
                        parsed.Records ??= new List<ScoreRecord>();
                        _cache = parsed;
                        return _cache;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"LocalScoreStore: load failed, starting fresh: {e.Message}");
            }

            _cache = new StoreFile();
            return _cache;
        }

        private static void Persist(StoreFile store)
        {
            store.Version = SchemaVersion;
            try
            {
                string json = JsonUtility.ToJson(store, prettyPrint: true);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"LocalScoreStore: save failed: {e.Message}");
            }
        }
    }
}
