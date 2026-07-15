using TMPro;
using UnityEngine;
using UnityEngine.UI;

// INDEX: List-row presentation contract — named slots (button/content/title/subtitle/marker) the screen binds data through. Lives on both the code-default row and a developer's styled row prefab, so rows can be restyled in the editor without touching gameplay code. Own file because it is prefab-serialized.
namespace OsuUnity.UI
{
    /// <summary>
    /// The presentation contract for one list row (beatmap set / difficulty). The build attaches this to
    /// the default code-built row and a developer's row prefab carries it too — so the screen binds data
    /// through these named slots and the developer is free to restyle everything around them (custom art,
    /// effects, layout) as long as the slots stay wired.
    ///
    /// <para>In its own file (matching the class name) because it is serialized onto developer prefabs —
    /// Unity needs that to restore the component, unlike the runtime-only helpers in <c>UiKit.cs</c>.</para>
    /// </summary>
    public sealed class UiRow : MonoBehaviour
    {
        [Tooltip("Click target for the whole row (select / play / download).")]
        public Button button;
        [Tooltip("Inset content area labels + art are placed in.")]
        public RectTransform content;
        [Tooltip("Primary line — beatmap title or difficulty label.")]
        public TMP_Text title;
        [Tooltip("Secondary line — set subtitle (diff count / stars). Optional; difficulty rows leave it null.")]
        public TMP_Text subtitle;
        [Tooltip("Selection accent; the screen enables it on the selected row.")]
        public Image marker;
    }
}
