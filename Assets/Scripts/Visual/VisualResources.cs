using UnityEngine;

namespace OsuUnity.Visual
{
    /// <summary>Lazily-loaded shared visual resources (the built-in runtime font for 3D text).</summary>
    public static class VisualResources
    {
        private static Font _font;

        public static Font NumberFont
        {
            get
            {
                if (_font != null) return _font;
                // Unity 2022's built-in dynamic font.
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 16);
                return _font;
            }
        }
    }
}
