using UnityEngine;

// INDEX: Camera-space black quad that dims everything behind gameplay (video, skybox, far scene).
namespace OsuUnity.Gameplay
{
    /// <summary>
    /// osu!'s "background dim": a semi-transparent black quad parented to the camera, placed beyond the
    /// gameplay chunk but in front of the video backdrop / skybox, so the video, skybox and any future far
    /// background scene darken while gameplay stays at full brightness. Being the farthest thing in the
    /// transparent queue is not what orders it — any sibling with an explicit sorting order would jump it —
    /// so it claims the floor of that queue explicitly (<see cref="Util.RenderOrder.BackgroundDim"/>).
    /// Alpha is driven live from <see cref="GameSettings.BackgroundDim"/>.
    /// </summary>
    public sealed class BackgroundDim : MonoBehaviour
    {
        private Transform _quad;
        private MeshRenderer _renderer;
        private Material _mat;
        private Camera _cam;
        private float _distance;

        /// <summary>Create the dim quad. <paramref name="distance"/> must sit beyond the gameplay chunk
        /// radius but in front of the video backdrop (closer than it) and inside the far clip plane.</summary>
        public void Init(Camera cam, float distance)
        {
            _cam = cam;
            _distance = distance;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BackgroundDimQuad";
            Destroy(go.GetComponent<Collider>());
            _quad = go.transform;
            _quad.SetParent(cam.transform, false);
            _quad.localPosition = new Vector3(0, 0, distance);
            _quad.localRotation = Quaternion.identity;

            // Sprites/Default is transparent (renderQueue 3000) and whitelisted in Always Included Shaders,
            // so it survives player builds where Shader.Find would otherwise return null (see VideoPlayback).
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            _mat = new Material(shader);
            _renderer = go.GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = _mat;
            _renderer.sortingOrder = Util.RenderOrder.BackgroundDim;

            SetDim(GameSettings.BackgroundDim);
            Resize();
        }

        /// <summary>Set the dim amount (0 = clear, 1 = fully black). Applied live.</summary>
        public void SetDim(float dim)
        {
            if (_mat == null) return;
            dim = Mathf.Clamp01(dim);
            _mat.color = new Color(0f, 0f, 0f, dim);
            // Nothing to draw when fully transparent — skip it so it never competes in the transparent sort.
            if (_renderer != null) _renderer.enabled = dim > 0f;
        }

        // Cover the frustum at the quad's distance. The active view mode (ortho vs perspective) and aspect
        // can change mid-map (ViewModeController's [Tab]), so re-fit every frame like VideoPlayback does.
        private void Resize()
        {
            if (_cam == null || _quad == null) return;
            float halfHeight = _cam.orthographic
                ? _cam.orthographicSize
                : _distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * _cam.aspect;
            _quad.localScale = new Vector3(halfWidth * 2f, halfHeight * 2f, 1f);
        }

        private void Update() => Resize();

        private void OnDestroy()
        {
            // Quad is parented to the (persistent) camera, not this GameObject, so tear it down explicitly.
            if (_quad != null) Destroy(_quad.gameObject);
        }
    }
}
