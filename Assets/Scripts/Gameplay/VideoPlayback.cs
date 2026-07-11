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
        private const float DriftToleranceMs = 100f;

        private VideoPlayer _player;
        private RenderTexture _rt;
        private Transform _quad;
        private MeshRenderer _quadRenderer;
        private Camera _cam;
        private float _farDistance;
        private double _offsetMs;
        private bool _ready;
        private bool _paused;

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
            // Sprites/Default renders in the transparent queue (3000) with ZWrite off, so it paints over the
            // guide arrows / UI. But dropping it all the way to Background (1000) is too far — with ZWrite
            // off the skybox pass then overwrites it and the video vanishes entirely. Park it at 2501: after
            // the skybox + opaque geometry (so it stays visible), before the transparent gameplay layer
            // (3000) so arrows and UI draw on top of it.
            mat.renderQueue = 2501; // RenderQueue.GeometryLast (2500) + 1
            _quadRenderer = quadGo.GetComponent<MeshRenderer>();
            _quadRenderer.sharedMaterial = mat;
            _quadRenderer.enabled = false; // hidden until the video actually starts (offset may be > 0)

            _player.prepareCompleted += _ => _ready = true;
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
                _quadRenderer.enabled = false;
                return;
            }

            if (!_player.isPlaying)
            {
                _player.time = videoTimeMs / 1000.0;
                _player.Play();
            }
            else
            {
                double drift = videoTimeMs - _player.time * 1000.0;
                if (Math.Abs(drift) > DriftToleranceMs) _player.time = videoTimeMs / 1000.0;
            }
            _quadRenderer.enabled = true;
        }

        /// <summary>Mirrors <see cref="GameClock"/>'s pause state (called from GameManager's pause toggle).</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_player == null) return;
            if (paused) _player.Pause();
            else if (_ready) _player.Play();
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
