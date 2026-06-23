using OsuUnity.Gameplay;
using UnityEngine;

namespace OsuUnity.Visual
{
    /// <summary>A short-lived 3D text that pops a judgement result and fades upward.</summary>
    public sealed class FloatingText : MonoBehaviour
    {
        private TextMesh _text;
        private float _age;
        private float _life = 0.6f;
        private Vector3 _drift;

        public static void Spawn(Judgement j, Vector3 worldPos, float size, int order, Camera cam)
        {
            var go = new GameObject("Judgement");
            go.transform.position = worldPos;
            var ft = go.AddComponent<FloatingText>();
            ft.Setup(j, size, order, cam);
        }

        private void Setup(Judgement j, float size, int order, Camera cam)
        {
            _text = gameObject.AddComponent<TextMesh>();
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = 48;
            _text.font = VisualResources.NumberFont;
            _text.characterSize = size;
            _text.GetComponent<MeshRenderer>().sharedMaterial = _text.font.material;
            _text.GetComponent<MeshRenderer>().sortingOrder = order;

            switch (j)
            {
                case Judgement.Great: _text.text = "300"; _text.color = new Color(0.4f, 0.8f, 1f); break;
                case Judgement.Ok: _text.text = "100"; _text.color = new Color(0.5f, 1f, 0.5f); break;
                case Judgement.Meh: _text.text = "50"; _text.color = new Color(1f, 0.9f, 0.4f); break;
                default: _text.text = "X"; _text.color = new Color(1f, 0.3f, 0.3f); break;
            }

            _drift = Vector3.up * size * 8f;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / _life;
            transform.position += _drift * Time.deltaTime;
            if (_text != null)
            {
                Color c = _text.color;
                c.a = Mathf.Clamp01(1f - t);
                _text.color = c;
            }
            if (_age >= _life) Destroy(gameObject);
        }
    }
}
