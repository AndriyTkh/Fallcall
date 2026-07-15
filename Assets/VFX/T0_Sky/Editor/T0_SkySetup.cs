using UnityEditor;
using UnityEngine;

namespace OsuUnity.VFX.Sky.EditorTools
{
    /// <summary>Builds the T0 test rig in the open scene, so the effect can be looked at without
    /// hand-wiring anything. Scratch convenience only — nothing in the effect depends on it.</summary>
    public static class T0_SkySetup
    {
        [MenuItem("Fallcall/VFX/Setup T0 Sky Rig")]
        public static void Setup()
        {
            var existing = Object.FindObjectOfType<TimeOfDaySky>();
            if (existing != null)
            {
                // Rigs built before 2026-07-15 have no CloudControls, and without it the cloud globals
                // are never pushed — the layer would render off zeroed coverage and scale. Add it
                // rather than bailing, so an old rig repairs itself instead of looking broken.
                if (existing.GetComponent<CloudControls>() == null)
                {
                    Undo.AddComponent<CloudControls>(existing.gameObject);
                    Debug.Log("[T0_Sky] Added missing CloudControls to the existing rig.", existing);
                }

                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[T0_Sky] Rig already present — selected it.", existing);
                return;
            }

            var sun = FindOrCreateSun();

            var go = new GameObject("T0 Sky");
            Undo.RegisterCreatedObjectUndo(go, "Setup T0 Sky Rig");
            var sky = go.AddComponent<TimeOfDaySky>();
            sky.Sun = sun;

            // Every cloud knob lives here. Without it the shader's cloud globals stay at zero.
            go.AddComponent<CloudControls>();

            // Without this the camera paints a flat colour over the sky and nothing looks like it works.
            var cam = Camera.main;
            if (cam != null && cam.clearFlags != CameraClearFlags.Skybox)
            {
                Undo.RecordObject(cam, "Setup T0 Sky Rig");
                cam.clearFlags = CameraClearFlags.Skybox;
            }

            Selection.activeGameObject = go;
            Debug.Log("[T0_Sky] Rig created. Scrub TimeOfDay (Time Of Day Sky) and the cloud knobs " +
                      "(Cloud Controls) on the 'T0 Sky' object — edit mode is enough.", go);
        }

        /// <summary>Adds the T0.5 extruded cloud layer. Separate menu item, not folded into the sky
        /// rig: T0.5 is a second, independent consumer of the same field, and being able to have the
        /// sky without it is what makes the two comparable.</summary>
        [MenuItem("Fallcall/VFX/Setup T0.5 Cloud Extrude")]
        public static void SetupExtrude()
        {
            var existing = Object.FindObjectOfType<CloudExtrude>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[T0.5] Extrude rig already present — selected it.", existing);
                return;
            }

            // The extruder reads the cloud globals; without CloudControls pushing them it bails with
            // an empty mesh. Make the dependency impossible to forget rather than documenting it.
            if (Object.FindObjectOfType<CloudControls>() == null)
            {
                Setup();
                Debug.Log("[T0.5] No sky rig found — built one first, since the extruder reads its globals.");
            }

            var go = new GameObject("T0.5 Cloud Extrude", typeof(MeshFilter), typeof(MeshRenderer));
            Undo.RegisterCreatedObjectUndo(go, "Setup T0.5 Cloud Extrude");
            go.AddComponent<CloudExtrude>();

            Selection.activeGameObject = go;
            Debug.Log("[T0.5] Extrude rig created. Move the camera — the parallax is the point. " +
                      "Toggle 'Enable Clouds' on Cloud Controls to compare against T0's flat layer.", go);
        }

        private static Light FindOrCreateSun()
        {
            foreach (var l in Object.FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) return l;

            var go = new GameObject("Directional Light");
            Undo.RegisterCreatedObjectUndo(go, "Setup T0 Sky Rig");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            return light;
        }
    }
}
