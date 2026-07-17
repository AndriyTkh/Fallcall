using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// INDEX: Cover-fill for a RawImage — crops its uvRect so artwork fills the rect without stretching, recomputed whenever the rect or the texture changes. What lets one map card shape hold both a 800×280 mirror cover and a 1920×1080 .osz background.
namespace OsuUnity.UI
{
    /// <summary>
    /// Makes a <see cref="RawImage"/> behave like CSS <c>background-size: cover</c>: the texture is scaled to
    /// fill the rect and the overflow is cropped off the long axis, centred — never letterboxed, never
    /// stretched. Map artwork arrives at wildly different aspects (mirror covers are ~2.9:1, a map's own
    /// background is 16:9) and lands in the same card, so the card can't assume either.
    ///
    /// <para>The crop depends on the rect, which layout only settles after a frame — hence
    /// <see cref="UIBehaviour"/>: it re-fits on <see cref="OnRectTransformDimensionsChange"/> rather than
    /// computing once at build and being wrong at every other window size.</para>
    ///
    /// <para>In its own file (matching the class name) because it is serialized onto developer row prefabs,
    /// same as <see cref="UiRow"/>.</para>
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class UiCoverFit : UIBehaviour
    {
        private RawImage _img;
        private RawImage Img => _img != null ? _img : (_img = GetComponent<RawImage>());

        /// <summary>Show <paramref name="tex"/> cover-filled, or nothing when it is null.</summary>
        public void SetTexture(Texture tex)
        {
            var img = Img;
            if (img == null) return;
            img.texture = tex;
            img.enabled = tex != null;
            Fit();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Fit();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Fit();
        }

        private void Fit()
        {
            var img = Img;
            if (img == null || img.texture == null) return;

            var rect = ((RectTransform)transform).rect;
            float rw = rect.width, rh = rect.height;
            float tw = img.texture.width, th = img.texture.height;
            if (rw <= 0f || rh <= 0f || tw <= 0f || th <= 0f) return;

            float rectAspect = rw / rh, texAspect = tw / th;
            if (texAspect > rectAspect)
            {
                float u = rectAspect / texAspect;              // wider than the rect → trim the sides
                img.uvRect = new Rect((1f - u) * 0.5f, 0f, u, 1f);
            }
            else
            {
                float v = texAspect / rectAspect;              // taller than the rect → trim top and bottom
                img.uvRect = new Rect(0f, (1f - v) * 0.5f, 1f, v);
            }
        }
    }
}
