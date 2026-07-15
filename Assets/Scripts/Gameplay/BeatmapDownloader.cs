using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

// INDEX: Public osu! mirror client (no auth) — .osz download by set id (nerinyan→catboy) plus text search returning osu!-API-shaped beatmapset metadata for the song-select Online tab (U5).
namespace OsuUnity.Gameplay
{
    /// <summary>Downloads .osz archives and searches beatmaps via public osu! mirrors (no auth/API key required).</summary>
    public static class BeatmapDownloader
    {
        public static string[] MirrorUrls(int setId) => new[]
        {
            $"https://api.nerinyan.moe/d/{setId}",
            $"https://catboy.best/d/{setId}",
        };

        // ------------------------------------------------------------------ ppy CDN (public, no auth)
        // These two are ppy's own hosts, not a mirror: the browser reads them directly per-client. Never
        // proxy them through a server of ours — throttles are per-IP, and a proxy collapses every player
        // into one bucket (docs/osu-api.md §6).

        /// <summary>The set's ~10 s audio demo. <b>Ogg/Vorbis despite the <c>.mp3</c> extension</b> — decode it
        /// as <see cref="UnityEngine.AudioType.OGGVORBIS"/> or the load fails silently (docs/osu-api.md §1).</summary>
        public static string PreviewUrl(int setId) => $"https://b.ppy.sh/preview/{setId}.mp3";

        /// <summary>The set's cover art (jpg).</summary>
        public static string CoverUrl(int setId) => $"https://assets.ppy.sh/beatmaps/{setId}/covers/cover.jpg";

        public static IEnumerator Download(string url, string destPath, Action<float> onProgress, Action<bool> onDone)
        {
            using var req = UnityWebRequest.Get(url);
            req.downloadHandler = new DownloadHandlerFile(destPath) { removeFileOnAbort = true };
            var op = req.SendWebRequest();

            while (!op.isDone)
            {
                onProgress?.Invoke(req.downloadProgress);
                yield return null;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[BeatmapDownloader] Download failed ({url}): {req.error}");
                onDone(false);
                yield break;
            }

            onProgress?.Invoke(1f);
            onDone(true);
        }

        // ------------------------------------------------------------------ search

        /// <summary>One beatmap (difficulty) as returned by the mirror search API (osu!-API v2 field names).</summary>
        [Serializable]
        public sealed class OnlineBeatmap
        {
            public int mode_int;              // 0 = osu!standard
            public string version;            // difficulty name
            public double difficulty_rating;  // official star rating
            public double cs, ar, bpm;
            public double accuracy;           // = OD
            public double drain;              // = HP
            public int total_length;          // seconds
        }

        /// <summary>One beatmap set as returned by the mirror search API.</summary>
        [Serializable]
        public sealed class OnlineBeatmapset
        {
            public int id;
            public string title, artist, creator;
            public OnlineBeatmap[] beatmaps;
        }

        [Serializable]
        private sealed class SearchWrapper { public OnlineBeatmapset[] items; }

        /// <summary>Search endpoints tried in order. Both mirror osu!'s API shape and return a top-level JSON
        /// array of beatmapsets; an empty query returns a default ranked listing (nerinyan). <paramref name="mode"/>
        /// 0 = osu!standard. Note the two mirrors do <b>not</b> share parameter names (docs/osu-api.md §7):
        /// nerinyan takes <c>q/m/ps</c>, catboy takes <c>query/mode/limit</c> — catboy silently ignores an
        /// unknown <c>q</c> and answers with its default listing, so the query must be spelled its way.</summary>
        public static string[] SearchUrls(string query, int mode)
        {
            string q = UnityWebRequest.EscapeURL(query ?? "");
            return new[]
            {
                $"https://api.nerinyan.moe/search?q={q}&m={mode}&ps=50",
                $"https://catboy.best/api/v2/search?query={q}&mode={mode}&limit=50",
            };
        }

        /// <summary>Runs the mirrors in order, returning the first that parses. <paramref name="onDone"/> gets
        /// <c>null</c> only if every mirror failed/was unreachable.</summary>
        public static IEnumerator Search(string query, int mode, Action<List<OnlineBeatmapset>> onDone)
        {
            foreach (string url in SearchUrls(query, mode))
            {
                List<OnlineBeatmapset> parsed = null;
                yield return SearchOne(url, r => parsed = r);
                if (parsed != null) { onDone(parsed); yield break; }
            }
            onDone(null);
        }

        private static IEnumerator SearchOne(string url, Action<List<OnlineBeatmapset>> onDone)
        {
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Accept", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[BeatmapDownloader] Search failed ({url}): {req.error}");
                onDone(null);
                yield break;
            }

            onDone(Parse(req.downloadHandler.text));
        }

        // JsonUtility can't parse a top-level array, so wrap the raw response in an object. Returns null on any
        // parse failure (e.g. a mirror answering with an unexpected object shape) so Search falls through.
        private static List<OnlineBeatmapset> Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string trimmed = raw.TrimStart();
            if (!trimmed.StartsWith("[")) return null;   // not the array shape we know how to read
            try
            {
                var wrapped = JsonUtility.FromJson<SearchWrapper>("{\"items\":" + raw + "}");
                var list = new List<OnlineBeatmapset>();
                if (wrapped?.items != null)
                    foreach (var s in wrapped.items)
                        if (s != null) list.Add(s);
                return list;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BeatmapDownloader] Search parse failed: {e.Message}");
                return null;
            }
        }
    }
}
