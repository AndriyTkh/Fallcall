using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// INDEX: Idempotent UI reconcile kit — get-or-create nodes/components so a screen can be built from the editor and re-synced without duplicating or clobbering hand edits. Also the list-row contract (UiRow) + placeholder-then-data list binder (UiListView) that lets developers style beatmap/difficulty rows as prefabs.
namespace OsuUnity.UI
{
    /// <summary>
    /// The <b>reconcile</b> layer that turns Fallcall's runtime-built UI into an <i>editor-authorable</i>
    /// one without giving up the "press Play, no wiring" default. Instead of blindly
    /// <c>new GameObject</c>/<c>AddComponent</c>, a screen's build routes every node through
    /// <see cref="Child"/> / <see cref="Ensure{T}"/>: an existing node is <b>reused and never
    /// re-styled</b> (the developer owns it), a missing one is created and gets the code defaults.
    ///
    /// <para>Contract for callers (the rule that makes "developer layers on top, never removed" hold):
    /// capture the returned reference <b>always</b>, but apply visual defaults (layout, colours, sprites)
    /// <b>only when <c>created</c>/<c>added</c> is true</b>. Re-running the build then fills gaps and
    /// binds references, but leaves every hand-tweaked property alone.</para>
    /// </summary>
    public static class UiScaffold
    {
        /// <summary>
        /// True while a build is running from the editor button (edit mode) rather than at play. Lets a
        /// screen show placeholder rows / skip data-only work. Set by the authoring entry point.
        /// </summary>
        public static bool EditAuthoring;

        /// <summary>
        /// Find a direct child by exact name; create a <see cref="RectTransform"/> child if absent.
        /// <paramref name="created"/> is true only when a new node was made (guard your defaults with it).
        /// </summary>
        public static RectTransform Child(Transform parent, string name, out bool created)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name)
                {
                    created = false;
                    return c as RectTransform ?? EnsureRect(c.gameObject);
                }
            }
            created = true;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Overload without the created flag (for lookups where you re-apply nothing).</summary>
        public static RectTransform Child(Transform parent, string name) => Child(parent, name, out _);

        /// <summary>Get an existing component or add one. <paramref name="added"/> is true only when added.</summary>
        public static T Ensure<T>(GameObject go, out bool added) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c != null) { added = false; return c; }
            added = true;
            return go.AddComponent<T>();
        }

        /// <summary>Overload without the added flag.</summary>
        public static T Ensure<T>(GameObject go) where T : Component => Ensure<T>(go, out _);

        private static RectTransform EnsureRect(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            return r != null ? r : go.AddComponent<RectTransform>();
        }

        /// <summary>
        /// Self-heal a rounded-rect sprite reference: the kit's sprites are generated at runtime and do
        /// <b>not</b> serialize, so an Image authored in the editor loses its sprite on reload. Re-assign
        /// the themed sprite <b>only when it is missing</b> — never touch the developer's colour, type, or
        /// a sprite they deliberately swapped in.
        /// </summary>
        public static void HealRoundedSprite(Image img, int radius)
        {
            if (img == null || img.sprite != null) return;
            img.sprite = UiTheme.RoundedRect(radius);
            img.type = Image.Type.Sliced;
        }
    }

    /// <summary>
    /// Owns a vertical list container and turns it into a <b>placeholder-in-editor / data-at-play</b> list.
    /// If a <see cref="rowPrefab"/> is assigned (developer-authored) it is instantiated per item; otherwise
    /// a code fallback builds the default-styled row — so the list works with zero prefabs and the developer
    /// layers a styled prefab on top when they want. Rows are transient data (always regenerated); static
    /// chrome must live outside the content node.
    /// </summary>
    public sealed class UiListView : MonoBehaviour
    {
        [Tooltip("Optional developer-authored row prefab. Must carry a UiRow. Null = use the code default.")]
        public GameObject rowPrefab;

        // Code fallback that builds the default row when no prefab is set. Assigned by the screen at build.
        [NonSerialized] public Func<Transform, UiRow> fallbackFactory;

        /// <summary>Instantiate one row (prefab if set, else the code fallback) parented to this list.</summary>
        public UiRow CreateRow()
        {
            if (rowPrefab != null)
            {
                var go = Instantiate(rowPrefab, transform);
                go.name = rowPrefab.name;
                var row = go.GetComponent<UiRow>();
                if (row == null) row = go.AddComponent<UiRow>();
                return row;
            }
            return fallbackFactory != null ? fallbackFactory(transform) : null;
        }

        /// <summary>Destroy every current row (placeholders included) so the list can be rebuilt.</summary>
        public void Clear()
        {
            var kids = new List<GameObject>(transform.childCount);
            for (int i = 0; i < transform.childCount; i++) kids.Add(transform.GetChild(i).gameObject);
            foreach (var k in kids) SafeDestroy(k);
        }

        /// <summary>
        /// Edit-time preview: clear and drop <paramref name="count"/> placeholder rows so a developer can
        /// see and style the row layout in context. Each is tagged <see cref="UiPlaceholder"/>.
        /// </summary>
        public void ShowPlaceholders(int count, Action<int, UiRow> decorate = null)
        {
            Clear();
            for (int i = 0; i < count; i++)
            {
                var row = CreateRow();
                if (row == null) continue;
                if (row.GetComponent<UiPlaceholder>() == null) row.gameObject.AddComponent<UiPlaceholder>();
                decorate?.Invoke(i, row);
            }
        }

        private static void SafeDestroy(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
