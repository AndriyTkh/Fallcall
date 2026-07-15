using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OsuUnity.UI.EditorTools
{
    /// <summary>Builds the DejaVu Sans TMP font asset and registers it as a global TMP fallback.
    /// Liberation Sans has no glyph for star (U+2605) or the reset arrow (U+21BA), so without this
    /// the song list and settings rows render those as boxes. Re-runnable: reuses the existing
    /// asset and skips registration if it is already in the fallback list.</summary>
    public static class SymbolFallbackFontSetup
    {
        const string FontPath = "Assets/Fonts/DejaVuSans.ttf";
        const string FontAssetPath = "Assets/Fonts/DejaVuSans SDF.asset";

        // Matches the LiberationSans SDF asset that ships with the project, so the fallback rasterizes
        // at the same density and pads the same way. Dynamic population keeps the atlas empty on disk
        // and renders glyphs on demand — only the handful we actually use ever gets generated.
        const int SamplingPointSize = 90;
        const int AtlasPadding = 9;
        const int AtlasSize = 1024;

        [MenuItem("Fallcall/Fonts/Setup Symbol Fallback Font")]
        public static void Setup()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath) ?? CreateFontAsset();
            if (fontAsset == null)
                return;

            RegisterAsFallback(fontAsset);
        }

        static TMP_FontAsset CreateFontAsset()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogError($"[Fonts] No font at {FontPath}. Symbol fallback not created.");
                return null;
            }

            var fontAsset = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding,
                GlyphRenderMode.SDFAA, AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null)
                return null; // CreateFontAsset logs why (usually "Include Font Data" off in the importer).

            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            // CreateFontAsset builds the atlas texture and material in memory only. They have to become
            // sub-assets or the next domain reload drops them and the asset reloads with a null material.
            var atlas = fontAsset.atlasTextures[0];
            atlas.name = "DejaVuSans SDF Atlas";
            AssetDatabase.AddObjectToAsset(atlas, fontAsset);

            fontAsset.material.name = atlas.name + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

            fontAsset.creationSettings = new FontAssetCreationSettings
            {
                sourceFontFileName = string.Empty,
                sourceFontFileGUID = AssetDatabase.AssetPathToGUID(FontPath),
                pointSize = fontAsset.faceInfo.pointSize,
                pointSizeSamplingMode = 0,
                padding = AtlasPadding,
                packingMode = 0,
                atlasWidth = AtlasSize,
                atlasHeight = AtlasSize,
                characterSetSelectionMode = 7, // Unicode range — the Font Asset Creator's dynamic default.
                characterSequence = string.Empty,
                referencedFontAssetGUID = string.Empty,
                referencedTextAssetGUID = string.Empty,
                renderMode = (int)GlyphRenderMode.SDFAA,
            };

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Fonts] Created {FontAssetPath}.");
            return fontAsset;
        }

        static void RegisterAsFallback(TMP_FontAsset fontAsset)
        {
            var settings = TMP_Settings.instance;
            if (settings == null)
            {
                Debug.LogError("[Fonts] No TMP Settings asset. Symbol fallback not registered.");
                return;
            }

            var fallbacks = TMP_Settings.fallbackFontAssets;
            if (fallbacks.Contains(fontAsset))
            {
                Debug.Log("[Fonts] Symbol fallback already registered.");
                return;
            }

            fallbacks.Add(fontAsset);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[Fonts] Registered DejaVuSans SDF as a global TMP fallback.");
        }
    }
}
