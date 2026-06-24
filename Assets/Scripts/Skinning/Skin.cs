using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OsuUnity.Skinning
{
    /// <summary>
    /// A loaded osu! skin: a folder of named .png elements plus a parsed <see cref="SkinConfig"/>.
    /// Textures load lazily and synchronously the first time an element is requested.
    ///
    /// osu! skins ship two resolutions: <c>foo.png</c> (legacy / SD) and <c>foo@2x.png</c> (HD, double
    /// the pixels for the same on-screen size). We prefer the @2x variant and remember its scale so
    /// world sizing stays resolution-independent. See https://osu.ppy.sh/wiki/en/Skinning/osu! .
    /// </summary>
    public sealed class Skin
    {
        /// <summary>The active skin, or null to use the procedural fallback art.</summary>
        public static Skin Current;

        public readonly string Directory;
        public readonly SkinConfig Config;

        private struct Element { public Texture2D Tex; public int Scale; } // Scale: 1 (SD) or 2 (@2x)

        private readonly Dictionary<string, Element> _elements = new Dictionary<string, Element>();
        private readonly Dictionary<string, Sprite> _unitSprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, Sprite> _glyphSprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, List<Sprite>> _animations = new Dictionary<string, List<Sprite>>();
        private readonly HashSet<string> _missing = new HashSet<string>();

        private Skin(string dir, SkinConfig cfg) { Directory = dir; Config = cfg; }

        public static Skin Load(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return null;
            var cfg = SkinConfig.Parse(Path.Combine(dir, "skin.ini"));
            Debug.Log($"[Skin] Loaded '{(string.IsNullOrEmpty(cfg.Name) ? Path.GetFileName(dir) : cfg.Name)}'" +
                      $" by {cfg.Author} ({cfg.ComboColours.Count} combo colours)");
            return new Skin(dir, cfg);
        }

        public bool Has(string name) => TryLoad(name, out _);

        /// <summary>True if the digit glyphs for <paramref name="prefix"/> (e.g. "default-0") exist.</summary>
        public bool HasGlyphs(string prefix) => Has(prefix + "-0");

        /// <summary>
        /// First of <paramref name="names"/> that exists, as a sprite that spans exactly one world unit
        /// (so callers scale it by the desired diameter, matching the procedural fallback convention).
        /// Returns null when none are present.
        /// </summary>
        public Sprite GetUnit(params string[] names)
        {
            foreach (string name in names)
            {
                if (_unitSprites.TryGetValue(name, out var cached)) return cached;
                if (!TryLoad(name, out var el)) continue;

                // pixelsPerUnit == width -> the sprite is exactly 1 world unit across at scale 1.
                var sprite = Sprite.Create(el.Tex, new Rect(0, 0, el.Tex.width, el.Tex.height),
                    new Vector2(0.5f, 0.5f), el.Tex.width);
                sprite.name = name;
                _unitSprites[name] = sprite;
                return sprite;
            }
            return null;
        }

        /// <summary>
        /// Element as a sprite whose world size equals its legacy pixel size (pixelsPerUnit == the
        /// source scale, so @2x art reports the same size as SD). Used for laying out number fonts.
        /// </summary>
        public Sprite GetGlyph(string name)
        {
            if (_glyphSprites.TryGetValue(name, out var cached)) return cached;
            if (!TryLoad(name, out var el)) return null;

            var sprite = Sprite.Create(el.Tex, new Rect(0, 0, el.Tex.width, el.Tex.height),
                new Vector2(0.5f, 0.5f), el.Scale);
            sprite.name = name;
            _glyphSprites[name] = sprite;
            return sprite;
        }

        /// <summary>
        /// Frames of an animatable element (<c>name-0</c>, <c>name-1</c>, … as in osu! animations).
        /// Falls back to the single un-numbered <paramref name="name"/> when no numbered frames exist;
        /// returns an empty (cached) list when nothing is present. <paramref name="glyph"/> selects
        /// aspect-preserving glyph sizing (hit results) over one-world-unit sizing.
        /// See https://osu.ppy.sh/wiki/en/Skinning/osu! — "Animations".
        /// </summary>
        public List<Sprite> GetFrames(string name, bool glyph)
        {
            string key = (glyph ? "g:" : "u:") + name;
            if (_animations.TryGetValue(key, out var cached)) return cached;

            var frames = new List<Sprite>();
            for (int i = 0; ; i++)
            {
                Sprite sp = glyph ? GetGlyph(name + "-" + i) : GetUnit(name + "-" + i);
                if (sp == null) break;
                frames.Add(sp);
            }
            if (frames.Count == 0)
            {
                Sprite single = glyph ? GetGlyph(name) : GetUnit(name);
                if (single != null) frames.Add(single);
            }
            _animations[key] = frames;
            return frames;
        }

        // ----------------------------------------------------------------- texture loading

        private bool TryLoad(string name, out Element element)
        {
            if (_elements.TryGetValue(name, out element)) return element.Tex != null;
            if (_missing.Contains(name)) { element = default; return false; }

            string hd = Path.Combine(Directory, name + "@2x.png");
            string sd = Path.Combine(Directory, name + ".png");
            bool is2x = File.Exists(hd);
            string file = is2x ? hd : (File.Exists(sd) ? sd : null);

            if (file == null) { _missing.Add(name); element = default; return false; }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            if (!tex.LoadImage(File.ReadAllBytes(file)))
            {
                Object.Destroy(tex);
                _missing.Add(name);
                element = default;
                return false;
            }
            tex.filterMode = FilterMode.Bilinear;

            element = new Element { Tex = tex, Scale = is2x ? 2 : 1 };
            _elements[name] = element;
            return true;
        }
    }
}
