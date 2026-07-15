using UnityEditor;
using UnityEngine;

namespace OsuUnity.VFX.Sky.EditorTools
{
    /// <summary>Builds the T2 scatter in the open scene, so the effect can be looked at without
    /// hand-wiring anything. Scratch convenience only — nothing in the effect depends on it.</summary>
    public static class T2_BoxesSetup
    {
        /// <summary>Adds the T2 scattered-primitive layer. Separate menu item, not folded into the sky
        /// rig: T2 is an independent consumer of the same field, and being able to have the sky
        /// without it — and it without T0.5 — is what makes the branches comparable.</summary>
        [MenuItem("Fallcall/VFX/Setup T2 Cloud Boxes")]
        public static void SetupBoxes()
        {
            var existing = Object.FindObjectOfType<CloudBoxes>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[T2] Box rig already present — selected it.", existing);
                return;
            }

            // CloudBoxes reads the cloud globals; without CloudControls pushing them it bails with an
            // empty scatter. Make the dependency impossible to forget rather than documenting it.
            if (Object.FindObjectOfType<CloudControls>() == null)
            {
                T0_SkySetup.Setup();
                Debug.Log("[T2] No sky rig found — built one first, since the boxes read its globals.");
            }

            var go = new GameObject("T2 Cloud Boxes");
            Undo.RegisterCreatedObjectUndo(go, "Setup T2 Cloud Boxes");
            go.AddComponent<CloudBoxes>();

            Selection.activeGameObject = go;
            Debug.Log("[T2] Box rig created. Rotation Jitter is the knob that decides whether this " +
                      "reads as Minecraft or as cloud — start there. Toggle 'Enable Clouds' on Cloud " +
                      "Controls to compare the scatter against T0's flat layer.", go);
        }
    }
}
