using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    public enum BeatmapDownloadStatus { NotDownloaded, Downloading, Downloaded, Failed }

    public sealed class BeatmapDifficultyInfo
    {
        public string OsuPath;
        public string Artist;
        public string Title;
        public string Version;

        /// <summary>Lightweight star-rating estimate (NOT osu!'s official SR — a note-density proxy nudged
        /// by AR/OD, computed cheaply at scan time) used to order difficulties easiest → hardest.</summary>
        public double Stars;
    }

    public sealed class BeatmapSetInfo
    {
        public string SetName;
        public string OszPath;
        public int? OnlineSetId;
        public BeatmapDownloadStatus Status;

        /// <summary>When this set entered the library (UTC) — the "Date Added" sort key. Stamped once on
        /// first sight and persisted, so it survives rescans and moves. See <see cref="BeatmapLibrary"/>.</summary>
        public DateTime DateAddedUtc;

        public readonly List<BeatmapDifficultyInfo> Difficulties = new List<BeatmapDifficultyInfo>();
    }

    /// <summary>
    /// Scans, caches, and downloads osu! beatmap sets. Extracted folders live under
    /// <see cref="Application.temporaryCachePath"/> (disposable, rebuilt from the .osz via
    /// <see cref="OszImporter"/>); downloaded .osz archives are kept permanently under
    /// <see cref="SongsFolder"/> (Application.persistentDataPath/Songs).
    /// Listing API for the song-select UI (block B).
    /// <para>Add-time ("Date Added") is tracked in <see cref="DateAddedFile"/>: each .osz is stamped the
    /// first time a scan sees it and keeps that stamp forever after, which is what makes an import-order
    /// sort stable — filesystem times alone drift (a move preserves the original creation time, and some
    /// filesystems don't record one at all).</para>
    /// </summary>
    public static class BeatmapLibrary
    {
        private static readonly Regex SetIdPrefix = new Regex(@"^(\d+)\s", RegexOptions.Compiled);

        /// <summary>path → first-seen UTC. Null until <see cref="LoadAddedTimes"/> runs.</summary>
        private static Dictionary<string, DateTime> _added;
        private static bool _addedDirty;

        private static string DateAddedFile => Path.Combine(SongsFolder, "date-added.txt");

        public static string SongsFolder
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "Songs");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>Rescans every candidate root + the downloaded-songs folder for .osz files.</summary>
        public static List<BeatmapSetInfo> Scan()
        {
            LoadAddedTimes();
            var sets = new List<BeatmapSetInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string osz in FindAllOsz())
            {
                if (!seen.Add(Path.GetFullPath(osz))) continue;
                var set = BuildSetInfo(osz);
                if (set.Difficulties.Count > 0) sets.Add(set);
            }

            SaveAddedTimes(seen);   // also drops stamps for .osz files that are gone, so the file stays bounded
            sets.Sort((a, b) => string.Compare(a.SetName, b.SetName, StringComparison.OrdinalIgnoreCase));
            return sets;
        }

        /// <summary>Downloads a beatmap set by its online set id via mirror, then extracts it into the library.</summary>
        public static IEnumerator DownloadSet(int setId, Action<float> onProgress, Action<BeatmapSetInfo> onDone)
        {
            string dest = Path.Combine(SongsFolder, setId + ".osz");
            bool ok = false;

            foreach (string url in BeatmapDownloader.MirrorUrls(setId))
            {
                yield return BeatmapDownloader.Download(url, dest, onProgress, success => ok = success);
                if (ok) break;
            }

            if (!ok)
            {
                onDone(null);
                yield break;
            }

            // A download is an add, even when it replaces a set already in the library — re-stamp so it
            // sorts to the top of Date Added rather than keeping the older copy's date.
            LoadAddedTimes();
            _added[Path.GetFullPath(dest)] = DateTime.UtcNow;
            _addedDirty = true;
            SaveAddedTimes(null);

            onDone(BuildSetInfo(dest));
        }

        private static BeatmapSetInfo BuildSetInfo(string oszPath)
        {
            string setName = Path.GetFileNameWithoutExtension(oszPath);
            var set = new BeatmapSetInfo
            {
                SetName = setName,
                OszPath = oszPath,
                Status = BeatmapDownloadStatus.Downloaded,
                OnlineSetId = ParseSetId(setName),
                DateAddedUtc = AddedTime(oszPath)
            };

            string folder = OszImporter.Extract(oszPath);
            foreach (string osuPath in OszImporter.FindOsuFiles(folder))
            {
                var meta = ReadHeader(osuPath);
                if (meta.Mode != 0) continue; // osu!standard only, matches Bootstrap's picker
                set.Difficulties.Add(new BeatmapDifficultyInfo
                {
                    OsuPath = osuPath,
                    Artist = meta.Artist,
                    Title = meta.Title,
                    Version = meta.Version,
                    Stars = EstimateStars(meta)
                });
            }
            // Order difficulties easiest → hardest so the detail panel reads like osu!'s.
            set.Difficulties.Sort((a, b) => a.Stars.CompareTo(b.Stars));
            return set;
        }

        // ------------------------------------------------------------- date added

        /// <summary>
        /// First-seen UTC for an .osz. Stamped on first sight and remembered from then on; the initial stamp
        /// is seeded from the file's creation time so a library that predates this feature still sorts in a
        /// sensible order instead of collapsing to one timestamp.
        /// </summary>
        private static DateTime AddedTime(string oszPath)
        {
            LoadAddedTimes();
            string key = Path.GetFullPath(oszPath);
            if (_added.TryGetValue(key, out DateTime t)) return t;

            t = SeedTime(oszPath);
            _added[key] = t;
            _addedDirty = true;
            return t;
        }

        /// <summary>Creation time = when the file appeared here (a copy or a download stamps it "now", which is
        /// exactly the add). Some filesystems don't record one — fall back to last-write.</summary>
        private static DateTime SeedTime(string oszPath)
        {
            try
            {
                DateTime c = File.GetCreationTimeUtc(oszPath);
                if (c.Year > 1990) return c;
                return File.GetLastWriteTimeUtc(oszPath);
            }
            catch { return DateTime.UtcNow; }
        }

        /// <summary>Loads "&lt;ticks&gt;|&lt;full path&gt;" lines once per session. A corrupt line is skipped, not fatal —
        /// worst case that map gets re-seeded from its file time.</summary>
        private static void LoadAddedTimes()
        {
            if (_added != null) return;
            _added = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _addedDirty = false;
            try
            {
                if (!File.Exists(DateAddedFile)) return;
                foreach (string line in File.ReadLines(DateAddedFile))
                {
                    int bar = line.IndexOf('|');
                    if (bar <= 0 || bar == line.Length - 1) continue;
                    if (!long.TryParse(line.Substring(0, bar), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)) continue;
                    _added[line.Substring(bar + 1)] = new DateTime(ticks, DateTimeKind.Utc);
                }
            }
            catch { /* unreadable manifest → everything re-seeds from file times */ }
        }

        /// <summary>Persists the stamps. <paramref name="keep"/> (when non-null) is the set of paths a scan just
        /// found; anything else is dropped so the file can't grow forever as maps are deleted.</summary>
        private static void SaveAddedTimes(HashSet<string> keep)
        {
            if (_added == null) return;
            if (keep != null)
            {
                var stale = new List<string>();
                foreach (var kv in _added)
                    if (!keep.Contains(kv.Key)) stale.Add(kv.Key);
                foreach (string k in stale) { _added.Remove(k); _addedDirty = true; }
            }
            if (!_addedDirty) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in _added)
                    sb.Append(kv.Value.Ticks.ToString(CultureInfo.InvariantCulture)).Append('|').Append(kv.Key).Append('\n');
                File.WriteAllText(DateAddedFile, sb.ToString());
                _addedDirty = false;
            }
            catch { /* read-only install → sort still works this session, just isn't remembered */ }
        }

        private static int? ParseSetId(string setName)
        {
            var m = SetIdPrefix.Match(setName);
            return m.Success ? int.Parse(m.Groups[1].Value) : (int?)null;
        }

        private struct Header
        {
            public string Artist, Title, Version;
            public int Mode;
            public float Ar, Od;                 // approach rate / overall difficulty (default 5 if absent)
            public int Objects, FirstMs, LastMs; // hit-object count + span, for the density estimate
        }

        private static Header ReadHeader(string path)
        {
            var h = new Header
            {
                Artist = "", Title = "", Version = Path.GetFileNameWithoutExtension(path),
                Ar = 5f, Od = 5f, FirstMs = int.MaxValue, LastMs = 0
            };
            try
            {
                bool inObjects = false;
                bool arSet = false;
                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;

                    if (line[0] == '[')
                    {
                        inObjects = line.StartsWith("[HitObjects]");
                        continue;
                    }

                    if (inObjects)
                    {
                        // "x,y,time,type,..." — count objects and track the map's playing span.
                        int c1 = line.IndexOf(',');
                        int c2 = c1 >= 0 ? line.IndexOf(',', c1 + 1) : -1;
                        int c3 = c2 >= 0 ? line.IndexOf(',', c2 + 1) : -1;
                        if (c3 > c2 && int.TryParse(line.Substring(c2 + 1, c3 - c2 - 1), out int t))
                        {
                            h.Objects++;
                            if (t < h.FirstMs) h.FirstMs = t;
                            if (t > h.LastMs) h.LastMs = t;
                        }
                        continue;
                    }

                    if (line.StartsWith("Title:")) h.Title = line.Substring(6).Trim();
                    else if (line.StartsWith("Artist:")) h.Artist = line.Substring(7).Trim();
                    else if (line.StartsWith("Version:")) h.Version = line.Substring(8).Trim();
                    else if (line.StartsWith("Mode:")) int.TryParse(line.Substring(5).Trim(), out h.Mode);
                    else if (line.StartsWith("OverallDifficulty:")) float.TryParse(line.Substring(18).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out h.Od);
                    else if (line.StartsWith("ApproachRate:")) { float.TryParse(line.Substring(13).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out h.Ar); arSet = true; }
                    // Older maps omit ApproachRate and inherit it from OverallDifficulty.
                    if (!arSet) h.Ar = h.Od;
                }
            }
            catch { /* ignore, fall back to filename + neutral difficulty */ }
            if (h.FirstMs == int.MaxValue) h.FirstMs = 0;
            return h;
        }

        /// <summary>
        /// Cheap star-rating estimate for ordering only. osu!'s real SR needs a full per-object aim/speed
        /// pass; this approximates it from note density (objects per second of drain time) nudged by AR/OD,
        /// tuned to land typical maps in the familiar ~1–8 range. Good enough to sort, not to display as SR.
        /// </summary>
        private static double EstimateStars(Header h)
        {
            if (h.Objects <= 0) return 0;
            double drainSec = System.Math.Max(1.0, (h.LastMs - h.FirstMs) / 1000.0);
            double density = h.Objects / drainSec;                 // notes per second
            double stars = System.Math.Sqrt(density) * 1.1 + h.Ar * 0.20 + h.Od * 0.10;
            return System.Math.Round(stars, 2);
        }

        private static List<string> FindAllOsz()
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in CandidateRoots())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                try
                {
                    foreach (string file in Directory.GetFiles(root, "*.osz", SearchOption.AllDirectories))
                        if (seen.Add(Path.GetFullPath(file))) found.Add(file);
                }
                catch { /* skip unreadable roots */ }
            }
            return found;
        }

        private static IEnumerable<string> CandidateRoots()
        {
            yield return SongsFolder;
            yield return Application.persistentDataPath;
            yield return Application.streamingAssetsPath;
            yield return Application.dataPath;                              // Assets/ in the editor
            yield return Directory.GetParent(Application.dataPath)?.FullName; // project root
        }
    }
}
