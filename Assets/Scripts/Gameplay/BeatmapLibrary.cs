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
        public readonly List<BeatmapDifficultyInfo> Difficulties = new List<BeatmapDifficultyInfo>();
    }

    /// <summary>
    /// Scans, caches, and downloads osu! beatmap sets. Extracted folders live under
    /// <see cref="Application.temporaryCachePath"/> (disposable, rebuilt from the .osz via
    /// <see cref="OszImporter"/>); downloaded .osz archives are kept permanently under
    /// <see cref="SongsFolder"/> (Application.persistentDataPath/Songs).
    /// Listing API for the song-select UI (block B).
    /// </summary>
    public static class BeatmapLibrary
    {
        private static readonly Regex SetIdPrefix = new Regex(@"^(\d+)\s", RegexOptions.Compiled);

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
            var sets = new List<BeatmapSetInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string osz in FindAllOsz())
            {
                if (!seen.Add(Path.GetFullPath(osz))) continue;
                var set = BuildSetInfo(osz);
                if (set.Difficulties.Count > 0) sets.Add(set);
            }

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
                OnlineSetId = ParseSetId(setName)
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
