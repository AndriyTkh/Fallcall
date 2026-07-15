using UnityEngine;

// INDEX: Edit-time placeholder marker — tags sample nodes (preview canvas, sample list rows) the build creates so they can be auto-removed before real data binds / on entering Play. Own file because it can be scene/prefab-serialized.
namespace OsuUnity.UI
{
    /// <summary>
    /// Marks a node the build created purely as an <b>edit-time placeholder</b> (a preview canvas, or the
    /// sample list rows a developer styles in context). <see cref="UiListView"/> destroys tagged rows before
    /// it binds real data, and <c>SongSelectUI</c> drops a tagged preview canvas on entering Play — so
    /// placeholders never leak into the running game.
    ///
    /// <para>Its own file (matching the class name) because it can be serialized in a saved scene/prefab;
    /// Unity needs that to restore the tag so the auto-cleanup can find it.</para>
    /// </summary>
    public sealed class UiPlaceholder : MonoBehaviour { }
}
