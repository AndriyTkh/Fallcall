using System;
using System.Collections;
using System.Collections.Generic;
using OsuUnity.Gameplay;
using UnityEngine;
using UnityEngine.Networking;

// INDEX: Map browser media — streams the ppy audio demo (Ogg/Vorbis despite the .mp3 URL) and fetches set covers, both debounced + guarded against a stale response landing after the player moved on (U6).
namespace OsuUnity.UI
{
    /// <summary>
    /// The browser's two ppy-CDN reads: the ~10 s audio demo and the set cover. Both are debounced (arrowing
    /// through a list must not fire a request per row) and carry a token guard so a slow response for a set the
    /// player has already left is dropped instead of overwriting the current one.
    ///
    /// <para><b>The clip is Ogg/Vorbis even though the URL ends in <c>.mp3</c></b> (<c>docs/osu-api.md</c> §1) —
    /// decoding it as <see cref="AudioType.MPEG"/> fails silently, which is exactly the live bug this block was
    /// assigned to fix. Nothing here touches the <c>.osz</c>: ~100 KB per demo, not ~13 MB.</para>
    /// </summary>
    public sealed class MapBrowserMedia : MonoBehaviour
    {
        [Tooltip("Seconds before a selected map's demo/cover is actually fetched (arrow-key scrubbing).")]
        public float debounce = 0.30f;

        /// <summary>Covers are small and re-selected constantly; keep the last few decoded. Oldest is evicted.</summary>
        private const int CoverCacheSize = 48;

        private AudioSource _audio;
        private Coroutine _demoCo, _coverCo;
        private int _token;   // bumped by every Play/LoadCover/Stop → in-flight responses for older tokens are dropped

        private readonly Dictionary<int, Texture2D> _covers = new Dictionary<int, Texture2D>();
        private readonly Queue<int> _coverOrder = new Queue<int>();

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;
        }

        private void OnDestroy()
        {
            foreach (var t in _covers.Values) if (t != null) Destroy(t);
            _covers.Clear();
        }

        /// <summary>Play the set's audio demo (debounced). Supersedes any demo already playing or pending.</summary>
        public void Play(int setId)
        {
            StopAudio();
            _demoCo = StartCoroutine(DemoRoutine(setId, ++_token));
        }

        /// <summary>Fetch the set cover (debounced); <paramref name="onLoaded"/> only fires if it is still current.</summary>
        public void LoadCover(int setId, Action<Texture2D> onLoaded)
        {
            if (_coverCo != null) StopCoroutine(_coverCo);
            _coverCo = StartCoroutine(CoverRoutine(setId, ++_token, onLoaded));
        }

        /// <summary>Stop the demo and abandon anything in flight (leaving the screen, launching a map).</summary>
        public void Stop()
        {
            _token++;
            StopAudio();
            if (_coverCo != null) { StopCoroutine(_coverCo); _coverCo = null; }
        }

        private void StopAudio()
        {
            if (_demoCo != null) { StopCoroutine(_demoCo); _demoCo = null; }
            if (_audio != null && _audio.isPlaying) _audio.Stop();
        }

        private IEnumerator DemoRoutine(int setId, int token)
        {
            yield return new WaitForSeconds(debounce);
            if (token != _token) yield break;

            // OGGVORBIS, not MPEG: b.ppy.sh serves Vorbis under the .mp3 extension (docs/osu-api.md §1).
            using var req = UnityWebRequestMultimedia.GetAudioClip(BeatmapDownloader.PreviewUrl(setId), AudioType.OGGVORBIS);
            if (req.downloadHandler is DownloadHandlerAudioClip dh) dh.streamAudio = true;
            yield return req.SendWebRequest();

            if (token != _token) yield break;
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MapBrowserMedia] Demo fetch failed ({setId}): {req.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip == null) yield break;

            _audio.clip = clip;
            _audio.time = 0f;
            _audio.volume = GameSettings.MusicVolume;
            _audio.Play();
        }

        private IEnumerator CoverRoutine(int setId, int token, Action<Texture2D> onLoaded)
        {
            if (_covers.TryGetValue(setId, out var cached) && cached != null)
            {
                onLoaded?.Invoke(cached);
                yield break;
            }

            yield return new WaitForSeconds(debounce);
            if (token != _token) yield break;

            using var req = UnityWebRequestTexture.GetTexture(BeatmapDownloader.CoverUrl(setId));
            yield return req.SendWebRequest();
            if (token != _token || req.result != UnityWebRequest.Result.Success) yield break;

            var tex = DownloadHandlerTexture.GetContent(req);
            if (tex == null) yield break;

            Cache(setId, tex);
            onLoaded?.Invoke(tex);
        }

        private void Cache(int setId, Texture2D tex)
        {
            _covers[setId] = tex;
            _coverOrder.Enqueue(setId);
            while (_coverOrder.Count > CoverCacheSize)
            {
                int old = _coverOrder.Dequeue();
                if (old == setId || !_covers.TryGetValue(old, out var t)) continue;
                _covers.Remove(old);
                if (t != null) Destroy(t);
            }
        }
    }
}
