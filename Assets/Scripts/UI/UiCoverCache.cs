using System;
using System.Collections;
using System.Collections.Generic;
using OsuUnity.Gameplay;
using OsuUnity.Util;
using UnityEngine;
using UnityEngine.Networking;

// INDEX: Shared list-artwork loader — fetches every listed map's card image up front (mirror covers over HTTP, .osz backgrounds off disk) through a small request window, downscales each to thumbnail size, and keeps an LRU of the decoded textures so re-listing a result set costs nothing.
namespace OsuUnity.UI
{
    /// <summary>
    /// The artwork behind <see cref="UiMapCard"/>. A list asks for <i>every</i> visible map's image at once —
    /// artwork is what makes the list scannable, so it can't wait for selection — and this turns that burst
    /// into something the frame budget and the mirrors both survive:
    ///
    /// <list type="bullet">
    /// <item><b>A request window</b> (<see cref="MaxConcurrent"/>): 50 results is 50 requests, and firing them
    /// together stalls the download of the one the player is actually looking at (and the ppy CDN is
    /// rate-limited per-IP, docs/osu-api.md).</item>
    /// <item><b>Downscaling</b> to <see cref="MaxEdge"/>: a map's own background is 1920×1080 (~8 MB decoded)
    /// and lands in a ~730×120 card. Keeping the full texture per row costs hundreds of MB for pixels no one
    /// ever sees.</item>
    /// <item><b>An LRU</b>: filtering and sorting rebuild every row, and re-fetching the same artwork on each
    /// keystroke would be both slow and rude to the mirrors.</item>
    /// </list>
    ///
    /// <para>Keyed by URL, so the same texture serves the local library (<c>file://</c>, via
    /// <see cref="AssetLoader.ToFileUrl"/>) and the mirrors (<c>https://</c>) through one path.</para>
    ///
    /// <para><b>Known edge:</b> eviction destroys the texture even if a row is still showing it — that row
    /// goes blank until the list rebuilds. <see cref="CacheSize"/> is set above a full page of results so it
    /// takes a library larger than the cache to see it; a refcount is the fix if it ever bites.</para>
    /// </summary>
    public sealed class UiCoverCache : MonoBehaviour
    {
        /// <summary>Requests allowed in flight at once — the rest queue (see the class summary).</summary>
        public const int MaxConcurrent = 4;

        /// <summary>Longest edge kept, in px. Above this the image is downscaled once, on arrival.</summary>
        public const int MaxEdge = 512;

        /// <summary>Decoded textures kept. Comfortably above one page of mirror results (50).</summary>
        private const int CacheSize = 96;

        private static UiCoverCache _instance;

        /// <summary>The process-wide cache; created on first use, survives scene loads.</summary>
        public static UiCoverCache Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("UiCoverCache") { hideFlags = HideFlags.DontSave };
                _instance = go.AddComponent<UiCoverCache>();
                if (Application.isPlaying) DontDestroyOnLoad(go);
                return _instance;
            }
        }

        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        private readonly Queue<string> _order = new Queue<string>();          // LRU (insertion) order
        private readonly HashSet<string> _failed = new HashSet<string>();     // don't re-ask for a 404 every rebuild
        private readonly Dictionary<string, List<Action<Texture2D>>> _waiting =
            new Dictionary<string, List<Action<Texture2D>>>();                // one fetch, many rows
        private readonly Queue<string> _queue = new Queue<string>();
        private int _active;

        /// <summary>
        /// Load the artwork at <paramref name="url"/> and hand it to <paramref name="onLoaded"/> — immediately
        /// if it is cached, otherwise once it lands. Never fires on failure; the card just keeps its plain
        /// surface. Callers must null-check their row: a rebuild may have destroyed it in the meantime.
        /// </summary>
        public void Request(string url, Action<Texture2D> onLoaded)
        {
            // Edit-time previews have no coroutines to run this on, and placeholder rows have no real art.
            if (!Application.isPlaying || string.IsNullOrEmpty(url)) return;

            if (_cache.TryGetValue(url, out var tex) && tex != null) { onLoaded?.Invoke(tex); return; }
            if (_failed.Contains(url)) return;

            if (_waiting.TryGetValue(url, out var waiting))
            {
                if (onLoaded != null) waiting.Add(onLoaded);   // already in flight — ride along
                return;
            }

            var list = new List<Action<Texture2D>>();
            if (onLoaded != null) list.Add(onLoaded);
            _waiting[url] = list;
            _queue.Enqueue(url);
            Pump();
        }

        /// <summary>Warm the cache without binding it to anything (a list the player is about to reach).</summary>
        public void Prefetch(string url) => Request(url, null);

        private void OnDestroy()
        {
            foreach (var t in _cache.Values) if (t != null) Destroy(t);
            _cache.Clear();
            if (_instance == this) _instance = null;
        }

        private void Pump()
        {
            while (_active < MaxConcurrent && _queue.Count > 0)
            {
                string url = _queue.Dequeue();
                if (!_waiting.ContainsKey(url)) continue;
                _active++;
                StartCoroutine(Load(url));
            }
        }

        private IEnumerator Load(string url)
        {
            Texture2D art = null;

            using (var req = UnityWebRequestTexture.GetTexture(url))
            {
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var raw = DownloadHandlerTexture.GetContent(req);
                    art = Downscale(raw);
                    if (raw != null && art != raw) Destroy(raw);
                }
                else
                {
                    // Not logged per-request on success: a search is 50 of these, and a hundred log lines per
                    // keystroke would bury the requests worth reading. A failure is worth a line.
                    _failed.Add(url);
                    ApiLog.Note("art", $"{(int)req.responseCode} {req.error}  {url}");
                }
            }

            _active--;
            if (art != null) Cache(url, art);

            if (_waiting.TryGetValue(url, out var waiting))
            {
                _waiting.Remove(url);
                if (art != null)
                    foreach (var cb in waiting) cb?.Invoke(art);
            }

            Pump();
        }

        // Scale to MaxEdge on the long axis, on the GPU (a per-pixel resize of a 1080p background on the main
        // thread is a visible hitch). Returns the source untouched when it is already small enough.
        private static Texture2D Downscale(Texture2D src)
        {
            if (src == null) return null;

            int longest = Mathf.Max(src.width, src.height);
            if (longest <= MaxEdge) return src;

            float k = (float)MaxEdge / longest;
            int w = Mathf.Max(1, Mathf.RoundToInt(src.width * k));
            int h = Mathf.Max(1, Mathf.RoundToInt(src.height * k));

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            src.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply(false, false);

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }

        private void Cache(string url, Texture2D tex)
        {
            _cache[url] = tex;
            _order.Enqueue(url);

            while (_order.Count > CacheSize)
            {
                string old = _order.Dequeue();
                if (old == url || !_cache.TryGetValue(old, out var t)) continue;
                _cache.Remove(old);
                if (t != null) Destroy(t);
            }
        }
    }
}
