using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using OsuUnity.Util;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

// INDEX: Public osu! mirror client (no auth) — .osz download by set id (osu.direct→catboy) plus text search returning osu!-API-shaped beatmapset metadata for the song-select Online tab (U5). Mirrors must accept TLS 1.2: Unity 2022 can't do 1.3.
namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Downloads .osz archives and searches beatmaps via public osu! mirrors (no auth/API key required).
    /// <para>
    /// <b>Any mirror added here must accept TLS 1.2.</b> UnityWebRequest on Unity 2022 tops out at TLS 1.2,
    /// so a TLS-1.3-only host is unreachable from the game no matter how healthy it looks from curl or a
    /// browser — it fails in ~100 ms with <c>ConnectionError: Unable to complete SSL connection</c>, which
    /// reads like a network blip rather than the permanent, host-wide wall it is. Check before adding:
    /// <c>openssl s_client -connect HOST:443 -servername HOST -tls1_2</c> must reach
    /// <c>Verify return code: 0</c>; TLS-alert 70 (<c>protocol version</c>) means Unity can never reach it.
    /// See <c>docs/osu-api.md</c> §7.
    /// </para>
    /// </summary>
    public static class BeatmapDownloader
    {
        /// <summary>Download mirrors, tried in order; the caller falls through on failure. nerinyan is absent:
        /// as of 2026-07-16 it answers <c>404</c> for every set id tried (catboy and osu.direct serve those
        /// same ids), so it can only cost a wasted round-trip. catboy trails osu.direct because Unity 2022
        /// cannot negotiate TLS with it at all (see the class remarks) — it is kept only so this list stays
        /// correct if that ceiling lifts.</summary>
        public static string[] MirrorUrls(int setId) => new[]
        {
            $"https://osu.direct/api/d/{setId}",
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

        /// <summary>
        /// The set's list-card art (800×280 jpg) — the variant to use when fetching artwork for a whole page
        /// of results. <see cref="CoverUrl"/> is ~4× the bytes for pixels a card-sized rect throws away.
        /// </summary>
        public static string CardUrl(int setId) => $"https://assets.ppy.sh/beatmaps/{setId}/covers/card@2x.jpg";

        public static IEnumerator Download(string url, string destPath, Action<float> onProgress, Action<bool> onDone)
        {
            using var req = UnityWebRequest.Get(url);
            req.downloadHandler = new DownloadHandlerFile(destPath) { removeFileOnAbort = true };
            ApiLog.Begin("download", url);
            var sw = Stopwatch.StartNew();
            var op = req.SendWebRequest();

            while (!op.isDone)
            {
                onProgress?.Invoke(req.downloadProgress);
                yield return null;
            }

            ApiLog.End("download", req, sw);

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
            public string status;            // ranked / qualified / loved / pending / wip / graveyard / approved
            public bool video, storyboard;   // back the "Extra" filters (client-side — see MapBrowserModel)
            public long play_count;
            public int favourite_count;
            public OnlineBeatmap[] beatmaps;
        }

        [Serializable]
        private sealed class SearchWrapper { public OnlineBeatmapset[] items; }

        /// <summary>Search endpoints tried in order. The response is a top-level JSON array of beatmapsets in
        /// osu!'s API shape; an empty query returns the mirror's default listing. <paramref name="mode"/>
        /// 0 = osu!standard. Parameter names are per-mirror and a wrong one is silently ignored rather than
        /// rejected, so each URL must be spelled that mirror's way (docs/osu-api.md §7): osu.direct and catboy
        /// take <c>query/mode/limit</c>, nerinyan <c>q/m/ps</c>.
        /// <para>
        /// <b>nerinyan is deliberately absent.</b> As of 2026-07-16 its search ignores the query entirely and
        /// answers <c>200</c> with the same static listing for every term (no parameter spelling changes it).
        /// That is the one failure <see cref="Search"/> cannot see through: it accepts the first mirror whose
        /// body <i>parses</i>, and well-formed-but-wrong parses fine, so including nerinyan at any position
        /// would serve plausible results for a search nobody ran. An honest "search failed" beats silently
        /// wrong maps. Re-add it only once <c>?q=</c> demonstrably filters again.
        /// </para></summary>
        /// <summary><paramref name="status"/> is the mirror's beatmap-state int (ranked=1, qualified=3,
        /// loved=4, pending=0, wip=-1, graveyard=-2), omitted when <c>null</c> to get the default "has
        /// leaderboard" listing. <paramref name="sort"/> is a mirror-sortable <c>attr:asc|desc</c> string
        /// (verified attrs in <c>docs/osu-api.md</c> §7), omitted when null/empty. Both were confirmed
        /// honoured by osu.direct on 2026-07-16; catboy takes the same names (TLS-unreachable regardless).</summary>
        public static string[] SearchUrls(string query, int mode, int? status = null, string sort = null)
        {
            string q = UnityWebRequest.EscapeURL(query ?? "");
            string extra = (status.HasValue ? $"&status={status.Value}" : "")
                         + (string.IsNullOrEmpty(sort) ? "" : $"&sort={UnityWebRequest.EscapeURL(sort)}");
            return new[]
            {
                $"https://osu.direct/api/v2/search?query={q}&mode={mode}&limit=50{extra}",
                $"https://catboy.best/api/v2/search?query={q}&mode={mode}&limit=50{extra}",
            };
        }

        /// <summary>Runs the mirrors in order, returning the first that parses. <paramref name="onDone"/> gets
        /// <c>null</c> only if every mirror failed/was unreachable.</summary>
        public static IEnumerator Search(string query, int mode, Action<List<OnlineBeatmapset>> onDone)
            => Search(query, mode, null, null, onDone);

        /// <inheritdoc cref="SearchUrls(string,int,int?,string)"/>
        public static IEnumerator Search(string query, int mode, int? status, string sort,
            Action<List<OnlineBeatmapset>> onDone)
        {
            foreach (string url in SearchUrls(query, mode, status, sort))
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
            ApiLog.Begin("search", url);
            var sw = Stopwatch.StartNew();
            yield return req.SendWebRequest();
            ApiLog.End("search", req, sw);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[BeatmapDownloader] Search failed ({url}): {req.error}");
                onDone(null);
                yield break;
            }

            var results = Parse(req.downloadHandler.text);
            // A mirror can answer 200 with a shape we can't read, and Search then silently falls through to
            // the next one — worth seeing, since the request itself looked fine.
            ApiLog.Note("search", results == null
                ? $"unparseable response, trying next mirror  {url}"
                : $"{results.Count} sets  {url}");
            onDone(results);
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
