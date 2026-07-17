using TMPro;
using UnityEngine;
using UnityEngine.UI;

// INDEX: The map card — the one list element behind both map selection and map search: the map's artwork bleeding to the card's rounded edges, a scrim over it that doubles as the hover tint, and title/subtitle sitting on the scrim. Built from the U1 kit; a developer prefab carrying the same UiRow slots supersedes it.
namespace OsuUnity.UI
{
    /// <summary>
    /// The shared beatmap-set card: artwork first, text over it. Both browse (mirror results) and song select
    /// (the local carousel) list maps, and a player scanning either is looking for a picture they recognise
    /// long before they read a title — so the card is art-led and tall enough to show one, and the two screens
    /// build the <i>same</i> card rather than each keeping their own near-copy.
    ///
    /// <para><b>Artwork loads for every card in the list, not just the selected one</b> — a list of grey
    /// rectangles that fill in one-by-one as you arrow through it is not a list you can scan.
    /// <see cref="UiCoverCache"/> is what keeps that affordable; <see cref="Bind"/> is the whole API.</para>
    ///
    /// <para>The scrim is doing two jobs at once, deliberately: it is the §1.2 readability wash that keeps
    /// text legible over <i>any</i> artwork, and it is the graphic <see cref="UiInteractive"/> tints for
    /// hover/press — so hovering reads as the art brightening, and the card needs no separate hover fill on
    /// top of its own picture.</para>
    /// </summary>
    public static class UiMapCard
    {
        /// <summary>
        /// Card height. Tall enough that a ~2.9:1 mirror cover survives the crop with something recognisable
        /// left, short enough that a screenful is still a list (~7 cards) and not a gallery.
        /// </summary>
        public const float Height = 120f;

        // The readability wash, and the hover/press states UiInteractive drives it through. Lighter on hover
        // (the art comes up), darker on press. Not UiTheme.Scrim: that one is a full-screen backdrop wash.
        private static readonly Color ScrimNormal = new Color(0f, 0f, 0f, 0.62f);
        private static readonly Color ScrimHover = new Color(0f, 0f, 0f, 0.44f);
        private static readonly Color ScrimActive = new Color(0f, 0f, 0f, 0.72f);

        /// <summary>
        /// Build a map card under <paramref name="parent"/> with every <see cref="UiRow"/> slot wired. Fill
        /// <c>title</c>/<c>subtitle</c> and drive <c>marker</c> as with any row; hand the artwork to
        /// <see cref="Bind"/>.
        /// </summary>
        public static UiRow Build(Transform parent)
        {
            var btn = UiKit.Row(parent, Height, null, out var content);
            var fill = (RectTransform)content.parent;

            // Art and scrim go inside the row's Fill, under Content. The Fill's own rounded image becomes the
            // mask, so the artwork is clipped to the card's silhouette instead of squaring off its corners —
            // and it still shows through as the plain surface for a map whose art is missing or not in yet.
            var mask = fill.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var coverRect = UiKit.NewRect("Cover", fill);
            UiKit.Stretch(coverRect);
            coverRect.SetAsFirstSibling();
            var cover = coverRect.gameObject.AddComponent<RawImage>();
            cover.raycastTarget = false;
            cover.enabled = false;                       // nothing to show until Bind lands something
            coverRect.gameObject.AddComponent<UiCoverFit>();

            var scrimRect = UiKit.NewRect("Scrim", fill);
            UiKit.Stretch(scrimRect);
            scrimRect.SetSiblingIndex(1);                // over the art, under the text
            var scrim = scrimRect.gameObject.AddComponent<Image>();
            scrim.color = ScrimNormal;
            scrim.raycastTarget = false;

            // Re-point the row's hover/press states at the scrim: the Fill they were tinting is behind the
            // artwork now, so tinting it would leave the card with no hover feedback at all.
            var interactive = btn.GetComponent<UiInteractive>();
            var focusRing = btn.transform.Find("FocusRing");
            if (interactive != null)
                interactive.Configure(scrim, focusRing != null ? focusRing.GetComponent<Image>() : null,
                                      ScrimNormal, ScrimHover, ScrimActive);

            var marker = SelectionMarker(content);

            // Text sits at the bottom, leaving the top of the card as clean artwork.
            var title = UiKit.Label(content, "", UiTheme.Text.Body, TextAlignmentOptions.BottomLeft);
            UiKit.Anchor(title.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                         new Vector2(8, 28), new Vector2(0, 54));
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Ellipsis;

            var sub = UiKit.Label(content, "", UiTheme.Text.Caption, TextAlignmentOptions.BottomLeft, UiTheme.TextSecondary);
            UiKit.Anchor(sub.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                         new Vector2(8, 2), new Vector2(0, 26));
            sub.enableWordWrapping = false;
            sub.overflowMode = TextOverflowModes.Ellipsis;

            var row = btn.gameObject.AddComponent<UiRow>();
            row.button = btn;
            row.content = content;
            row.title = title;
            row.subtitle = sub;
            row.marker = marker;
            row.cover = cover;
            row.scrim = scrim;
            return row;
        }

        /// <summary>
        /// Point <paramref name="row"/> at the artwork at <paramref name="artUrl"/> (an <c>https://</c> mirror
        /// cover or a <c>file://</c> map background — see <see cref="UiCoverCache"/>). Safe to call for every
        /// row of a list at once; a null/empty url just clears the card back to its plain surface.
        /// </summary>
        public static void Bind(UiRow row, string artUrl)
        {
            if (row == null) return;
            SetArt(row, null);
            if (string.IsNullOrEmpty(artUrl)) return;

            // The row may be destroyed by a rebuild (a keystroke, a filter) before its art lands.
            UiCoverCache.Instance.Request(artUrl, tex => { if (row != null) SetArt(row, tex); });
        }

        /// <summary>Show <paramref name="tex"/> on a card, cover-filled. Null clears it.</summary>
        public static void SetArt(UiRow row, Texture tex)
        {
            if (row == null || row.cover == null) return;

            var fit = row.cover.GetComponent<UiCoverFit>();
            if (fit != null) { fit.SetTexture(tex); return; }

            row.cover.texture = tex;                 // a developer prefab may leave the fitter off
            row.cover.enabled = tex != null;
        }

        /// <summary>
        /// The thin accent bar marking persistent selection on a row's left edge. Separate from the row fill
        /// because <see cref="UiInteractive"/> re-tints that on hover — selection must survive the mouse
        /// passing over a different row. Disabled by default.
        /// </summary>
        public static Image SelectionMarker(Transform content)
        {
            var r = UiKit.NewRect("SelMarker", content);
            UiKit.Anchor(r, new Vector2(0, 0), new Vector2(0, 1), new Vector2(-6, 3), new Vector2(-2, -3));
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.RoundedRect(UiTheme.RadiusSM);
            img.type = Image.Type.Sliced;
            img.color = UiTheme.Accent;
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }
    }
}
