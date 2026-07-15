using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// INDEX: Fallcall UI design tokens — palette, typography scale, spacing/radii, motion + shared TMP font and rounded-rect sprite resources. The single source of styling for the UI kit (U1).
namespace OsuUnity.UI
{
    /// <summary>
    /// Fallcall's own visual language for screen-space UI (menus, settings, song select) — the
    /// design-token layer the whole <see cref="UiKit"/> draws from. Per <c>docs/UI-DESIGN.md</c> §0,
    /// this is deliberately <b>not</b> osu!'s look: a neutral grey/blue placeholder palette (real art
    /// direction is decided later against complete layouts, see PLAN.md open questions) tuned for the
    /// falling-geometric theme — contrast-first and colorblind-safe (never encode meaning by hue alone).
    ///
    /// All tokens are plain <c>static</c> fields so later blocks (U2) can rebind the palette to
    /// player settings live; call <see cref="RaiseChanged"/> after mutating so listening widgets can
    /// refresh. Colours are semantic <i>slots</i> (Surface / Accent / Text…), not raw hex scattered
    /// through call sites — change one here, every screen follows (§1.6 consistency).
    /// </summary>
    public static class UiTheme
    {
        // ------------------------------------------------------------------ palette (semantic slots)
        // Grey/blue placeholder (human call, 2026-07-11): neutral, no osu! pink. Contrast built by
        // brightness/saturation steps so it survives greyscale (colorblind-safe, UI-DESIGN §1.2/§1.5).

        /// <summary>Deepest layer — the ambient backdrop behind all panels.</summary>
        public static Color Background = Hex("#0E1420");
        /// <summary>Panel / overlay body.</summary>
        public static Color Surface = Hex("#172232");
        /// <summary>Raised elements on a panel (cards, list rows, controls).</summary>
        public static Color SurfaceRaised = Hex("#1F2E42");
        /// <summary>Hover fill for a raised interactive element.</summary>
        public static Color SurfaceHover = Hex("#27384F");
        /// <summary>Pressed/active fill for a raised interactive element.</summary>
        public static Color SurfaceActive = Hex("#324863");

        /// <summary>Primary interactive accent (primary buttons, slider fill, selection).</summary>
        public static Color Accent = Hex("#4C8DFF");
        public static Color AccentHover = Hex("#6AA4FF");
        public static Color AccentActive = Hex("#3B78E0");
        /// <summary>Readable text/icon colour to place on top of an <see cref="Accent"/> fill.</summary>
        public static Color OnAccent = Hex("#0B1220");

        /// <summary>High-contrast keyboard-focus ring — must read clearly against every surface (§1.5).</summary>
        public static Color Focus = Hex("#8FD9FF");

        public static Color TextPrimary = Hex("#EAF0F8");
        public static Color TextSecondary = Hex("#9DB0C6");
        public static Color TextDisabled = Hex("#5A6A7E");

        public static Color Positive = Hex("#56C271");
        public static Color Danger = Hex("#E06B6B");

        /// <summary>Hairline separator between grouped content.</summary>
        public static Color Divider = new Color(1f, 1f, 1f, 0.08f);
        /// <summary>Inactive track behind a slider fill / toggle.</summary>
        public static Color Track = new Color(1f, 1f, 1f, 0.14f);
        /// <summary>Full-screen dark scrim placed behind text over artwork (§1.2 readability).</summary>
        public static Color Scrim = new Color(0f, 0f, 0f, 0.72f);

        // ------------------------------------------------------------------ typography scale (px @1080)

        public enum Text { Display, Title, Heading, Body, Label, Caption }

        /// <summary>Point size for a typography role at the 1080p reference (CanvasScaler scales it).</summary>
        public static int Size(Text role) => role switch
        {
            Text.Display => 40,
            Text.Title => 30,
            Text.Heading => 22,
            Text.Body => 16,
            Text.Label => 14,
            Text.Caption => 12,
            _ => 16,
        };

        // ------------------------------------------------------------------ spacing / radii / sizing

        public const float SpaceXS = 4f;
        public const float SpaceSM = 8f;
        public const float SpaceMD = 12f;
        public const float SpaceLG = 16f;
        public const float SpaceXL = 24f;
        public const float SpaceXXL = 32f;

        public const int RadiusSM = 6;   // controls, list rows
        public const int RadiusMD = 10;  // cards, buttons
        public const int RadiusLG = 16;  // panels, overlays

        public const float ControlHeight = 40f;     // buttons, fields, rows
        public const float ControlHeightSm = 30f;   // compact chips / icon buttons
        public const float FocusRingWidth = 2.5f;   // outline thickness of the keyboard focus ring

        // ------------------------------------------------------------------ motion
        // Slow/small near the playfield (§1.1); overlays slide from their anchored edge (§1.6).

        public const float DurFast = 0.08f;    // hover / press feedback
        public const float DurNormal = 0.16f;  // control state changes
        public const float DurSlow = 0.28f;    // overlay slide / crossfade

        /// <summary>Standard ease for UI transitions (soft in/out — never a snappy pop, §1.1).</summary>
        public static readonly AnimationCurve Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // ------------------------------------------------------------------ change notification

        /// <summary>Raised when a token (e.g. palette slot) is mutated at runtime so live widgets refresh.</summary>
        public static event Action Changed;
        public static void RaiseChanged() => Changed?.Invoke();

        // ------------------------------------------------------------------ shared TMP font

        private static TMP_FontAsset _font;

        /// <summary>
        /// The single TMP font every widget uses (crisp at any scale — never legacy <c>Text</c>, per
        /// PLAN U1). Resolves the project's default TMP asset, falls back to the built-in Liberation SDK
        /// font, and as a last resort builds one from an OS font so a project without TMP Essentials
        /// imported still renders instead of showing blank labels.
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (_font != null) return _font;
                _font = TMP_Settings.defaultFontAsset;
                if (_font == null) _font = Resources.Load<TMP_FontAsset>("LiberationSans SDK");
                if (_font == null) _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDK");
                if (_font == null)
                {
                    var os = UnityEngine.Font.CreateDynamicFontFromOSFont(
                        new[] { "Segoe UI", "Arial", "Liberation Sans", "Helvetica" }, 16);
                    if (os != null) _font = TMP_FontAsset.CreateFontAsset(os);
                }
                return _font;
            }
        }

        // ------------------------------------------------------------------ rounded-rect sprites
        // uGUI Images are square by default; the kit's rounded corners come from procedurally-built
        // 9-sliced sprites (no imported art — same philosophy as Util/TextureFactory). Cached per radius.

        private static readonly Dictionary<int, Sprite> _rounded = new Dictionary<int, Sprite>();

        /// <summary>
        /// A white 9-sliced rounded-rectangle sprite for the given corner radius (px). Tint via the
        /// Image colour; the border keeps the radius crisp at any control size. Cached.
        /// </summary>
        public static Sprite RoundedRect(int radius)
        {
            radius = Mathf.Max(1, radius);
            if (_rounded.TryGetValue(radius, out var cached) && cached != null) return cached;

            int n = radius * 2 + 2;                 // +2 keeps a 1px straight run between corners
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Nearest point of the inner (corner-centre) rect, then distance to it → rounded alpha.
                float cx = Mathf.Clamp(x, radius, n - 1 - radius);
                float cy = Mathf.Clamp(y, radius, n - 1 - radius);
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);   // ~1px antialiased edge
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();

            var border = new Vector4(radius, radius, radius, radius);
            var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, border);
            _rounded[radius] = sprite;
            return sprite;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Parse a <c>#RRGGBB</c> / <c>#RRGGBBAA</c> string to a linear-safe <see cref="Color"/>.</summary>
        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        /// <summary>Same colour at a different alpha — for scrims, disabled tints, ghost fills.</summary>
        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
