using UnityEngine;

namespace OsuUnity.Util
{
    /// <summary>Shared unlit/transparent materials used for procedurally created meshes (sliders).</summary>
    public static class MaterialFactory
    {
        private static Material _transparent;
        private static Shader _sliderShader;
        private static bool _sliderShaderLoaded;

        /// <summary>An unlit, vertex-colour-tinted transparent material that works in the built-in pipeline.</summary>
        public static Material UnlitTransparent
        {
            get
            {
                if (_transparent != null) return _transparent;

                // "Sprites/Default" is always available, respects vertex colour and supports transparency.
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

                _transparent = new Material(shader) { name = "OsuUnlitTransparent" };
                return _transparent;
            }
        }

        /// <summary>A fresh slider-fill material. Stencil ref 2 so it paints each pixel once and layers
        /// over the border. One instance per slider — the caller owns its <c>_Color</c> tint and must
        /// Destroy it when the slider dies.</summary>
        public static Material CreateSliderBody() => NewStencil("OsuSliderBody", 2);

        /// <summary>A fresh slider-outline material. Stencil ref 1, a distinct ref from the body so neither
        /// blocks the other. One instance per slider; caller owns and Destroys it.</summary>
        public static Material CreateSliderBorder() => NewStencil("OsuSliderBorder", 1);

        /// <summary>Stencil single-write material (Osu/SliderBody); falls back to the plain transparent one.
        /// Shader loaded from Resources so it survives the build's shader stripping (Shader.Find would return
        /// null in a player for a shader no material asset references).</summary>
        private static Material NewStencil(string name, int stencilRef)
        {
            if (!_sliderShaderLoaded)
            {
                _sliderShader = Resources.Load<Shader>("SliderBody");
                _sliderShaderLoaded = true;
            }
            if (_sliderShader == null)
                return new Material(UnlitTransparent);   // shader missing from build — degrade, don't crash

            var m = new Material(_sliderShader) { name = name };
            m.SetInt("_StencilRef", stencilRef);
            return m;
        }
    }
}
