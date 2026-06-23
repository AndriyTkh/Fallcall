using UnityEngine;

namespace OsuUnity.Util
{
    /// <summary>Shared unlit/transparent materials used for procedurally created meshes (sliders).</summary>
    public static class MaterialFactory
    {
        private static Material _transparent;

        /// <summary>An unlit, vertex-colour-tinted transparent material that works in the built-in pipeline.</summary>
        public static Material UnlitTransparent
        {
            get
            {
                if (_transparent != null) return _transparent;

                // "Sprites/Default" is always available, respects vertex colour and supports transparency.
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                if (shader == null) shader = Shader.Find("Standard");

                _transparent = new Material(shader) { name = "OsuUnlitTransparent" };
                return _transparent;
            }
        }
    }
}
