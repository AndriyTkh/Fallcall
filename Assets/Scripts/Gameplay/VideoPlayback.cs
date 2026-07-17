using System;
using UnityEngine;
using UnityEngine.Video;

// INDEX: Plays a beatmap's background video as a camera-space backdrop quad, synced to GameClock.
namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Plays a beatmap's background video (osu! "Video" event) behind gameplay. Renders onto a quad
    /// parented to the camera (so it always fills the frustum regardless of look direction, like a
    /// skybox) at a distance beyond all gameplay geometry. Sync follows <see cref="GameManager"/>'s
    /// <see cref="GameClock"/> song time: the video's own audio track is disabled (the beatmap's music
    /// AudioSource is the only audio source), and playback is started/paused/reseeked against that time
    /// rather than left to free-run, so pausing or restarting the session keeps the video in lockstep.
    /// </summary>
    public sealed class VideoPlayback : MonoBehaviour
    {
        // A seek is a visible glitch (decoder flush + keyframe search), so only resync that way once the
        // drift is bad enough to notice. Anything smaller is pulled in with playbackSpeed instead.
        private const float ResyncMs = 250f;
        private const float SoftDriftMs = 30f;
        private const float SoftRate = 0.05f;        // +/-5%; the video has no audio track, so rate changes are silent
        private const float SeekCooldownSec = 0.5f;  // a seek needs frames to land; re-issuing sooner just cancels it
        private const float SeekTimeoutSec = 1.5f;   // seekCompleted doesn't fire for a no-op seek; don't wait forever

        private VideoPlayer _player;
        private RenderTexture _rt;
        private Transform _quad;
        private MeshRenderer _quadRenderer;
        private Camera _cam;
        private float _farDistance;
        private double _offsetMs;
        private bool _ready;
        private bool _paused;
        private bool _started;
        private bool _seeking;
        private float _lastSeekAt;

        /// <summary>Begin preparing the video. <paramref name="farDistance"/> must sit beyond the
        /// gameplay chunk's radius but inside the camera's far clip plane.</summary>
        public void Init(string path, int offsetMs, Camera cam, float farDistance)
        {
            _offsetMs = offsetMs;
            _cam = cam;
            _farDistance = farDistance;

            _player = gameObject.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.source = VideoSource.Url;
            _player.url = AssetLoader.ToFileUrl(path);
            _player.isLooping = false;
            _player.audioOutputMode = VideoAudioOutputMode.None; // song AudioSource drives sync, not the video's own track

            _rt = new RenderTexture(1920, 1080, 0);
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _rt;

            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "VideoBackdrop";
            Destroy(quadGo.GetComponent<Collider>());
            _quad = quadGo.transform;
            _quad.SetParent(cam.transform, false);
            _quad.localPosition = new Vector3(0, 0, farDistance);
            _quad.localRotation = Quaternion.identity;
            ResizeBackdrop(); // sized once now; re-sized every Tick since the mode (ortho/perspective) can change mid-map

            // Shader.Find only returns shaders included in the build. "Unlit/Texture" is not referenced
            // by any built asset, so it gets stripped and Find returns null in a player build (works in the
            // editor because all shaders are loaded there) -> new Material(null) throws
            // "ArgumentNullException: shader". Fall back to shaders in the project's Always Included Shaders
            // list (Sprites/Default is whitelisted, same as MaterialFactory relies on).
            var shader = Shader.Find("Unlit/Texture")
                      ?? Shader.Find("Sprites/Default")
                      ?? Shader.Find("UI/Default");
            var mat = new Material(shader) { mainTexture = _rt };
            // Position alone (localPosition.z == farDistance) doesn't order this correctly: the fallback
            // Sprites/Default renders in the transparent queue with ZWrite off, so it would paint over the
            // guide arrows / UI. See Util/RenderOrder for the band it belongs to and why.
            mat.renderQueue = Util.RenderOrder.VideoBackdropQueue;
            _quadRenderer = quadGo.GetComponent<MeshRenderer>();
            _quadRenderer.sharedMaterial = mat;
            _quadRenderer.enabled = false; // hidden until the video actually starts (offset may be > 0)

            _player.prepareCompleted += _ => _ready = true;
            _player.seekCompleted += _ => _seeking = false;
            _player.Prepare();
        }

        /// <summary>Recompute the backdrop quad's size for the camera's current projection (Sphere mode
        /// is perspective; Ortho2D is orthographic — <see cref="ViewModeController"/> can switch between
        /// them mid-map via Tab, so this can't be sized once at Init).</summary>
        private void ResizeBackdrop()
        {
            float halfHeight, halfWidth;
            if (_cam.orthographic)
            {
                halfHeight = _cam.orthographicSize;
            }
            else
            {
                halfHeight = _farDistance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }
            halfWidth = halfHeight * _cam.aspect;
            _quad.localScale = new Vector3(halfWidth * 2f, halfHeight * 2f, 1f);
        }

        /// <summary>Call every frame with the current song time (ms); starts/stops/reseeks as needed.</summary>
        public void Tick(double songTimeMs)
        {
            if (!_ready || _paused) return;

            ResizeBackdrop();

            double videoTimeMs = songTimeMs - _offsetMs;
            if (videoTimeMs < 0)
            {
                if (_player.isPlaying) _player.Pause();
                _started = false;   // lead-in, or a skip back before the video's start: re-seek when it comes round again
                _quadRenderer.enabled = false;
                return;
            }

            if (!_started)
            {
                Seek(videoTimeMs);
                _player.Play();
                _started = true;
                return;             // nothing meaningful to measure until that seek lands
            }

            if (!_player.isPlaying) _player.Play();
            _quadRenderer.enabled = true;

            // VideoPlayer.time reports the last *presented* frame, so it stays stale for as long as a seek is
            // in flight. Comparing against that stale time and re-seeking would abort the pending seek and
            // re-issue it every frame -- the decoder never gets to deliver a frame and the video stalls dead.
            // So: don't touch it until seekCompleted fires (with a timeout, since a seek that resolves to the
            // current frame completes silently without ever raising the event).
            if (_seeking)
            {
                if (Time.unscaledTime - _lastSeekAt > SeekTimeoutSec) _seeking = false;
                return;
            }

            double drift = videoTimeMs - _player.time * 1000.0;
            if (Math.Abs(drift) > ResyncMs)
            {
                if (Time.unscaledTime - _lastSeekAt > SeekCooldownSec) Seek(videoTimeMs);
            }
            else if (Math.Abs(drift) > SoftDriftMs)
            {
                // Close the gap by running the decoder a touch fast/slow rather than seeking. Under ~250ms
                // that is invisible, where a seek would be a hard hitch.
                _player.playbackSpeed = 1f + Math.Sign(drift) * SoftRate;
            }
            else
            {
                _player.playbackSpeed = 1f;
            }
        }

        private void Seek(double videoTimeMs)
        {
            _seeking = true;
            _lastSeekAt = Time.unscaledTime;
            _player.playbackSpeed = 1f;
            _player.time = videoTimeMs / 1000.0;
        }

        /// <summary>Mirrors <see cref="GameClock"/>'s pause state (called from GameManager's pause toggle).</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_player == null) return;
            // Resuming is left to Tick: it re-Plays only once the song time says the video should be visible,
            // and reseeks from there. Calling Play() here would also start the video during the lead-in.
            if (paused) _player.Pause();
        }

        private void OnDestroy()
        {
            if (_rt != null) _rt.Release();
            // The backdrop quad is parented to the camera (so it follows the view), not to this GameObject,
            // so it must be torn down explicitly — otherwise it leaks on restart, leaving the old frozen
            // frame stacked in front of the new session's video.
            if (_quad != null) Destroy(_quad.gameObject);
        }
    }
}
