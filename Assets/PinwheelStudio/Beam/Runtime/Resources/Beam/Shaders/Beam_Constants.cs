#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using System.IO;
using System.Text;
using System.Globalization;

namespace Pinwheel.Beam
{
    public static class Beam_Constants
    {
        [InitializeOnLoadMethod]
        private static void OnInitialize()
        {
            EditorApplication.projectChanged += GenerateIncludesFile;
            GenerateIncludesFile();
        }

        private static void GenerateIncludesFile()
        {
            string directory = GetDirectory();
            string filePath = Path.Combine(directory, $"{nameof(Beam_Constants)}.hlsl");
            StringBuilder text = new StringBuilder();
            text.AppendLine("//This file was generated, don't edit by hand")
                .AppendLine()
                .AppendLine("#ifndef BEAM_CONSTANTS_DEFINED")
                .AppendLine("#define BEAM_CONSTANTS_DEFINED")
                .AppendLine();

            float totalFroxelZ = VolumetricLightingRendererFeature.FROXEL_SLICE_COUNT;
            float accumMul = 1f / totalFroxelZ;
            text.AppendLine($"#define BEAM_FROXEL_COUNT_Z {VolumetricLightingRendererFeature.FROXEL_SLICE_COUNT}");
            text.AppendLine($"#define BEAM_ACCUM_MUL {accumMul.ToString(CultureInfo.InvariantCulture)}");
            text.AppendLine($"#define BEAM_MAX_VISIBLE_LIGHT {VolumetricLightingRendererFeature.MAX_VISIBLE_LIGHT}");
            text.AppendLine($"#define BEAM_MAX_VISIBLE_FOG_VOLUME {VolumetricLightingRendererFeature.MAX_VISIBLE_FOG_VOLUME}");
            text.AppendLine();
#if UNITY_6000_1_OR_NEWER
            text.AppendLine($"#define UNITY_6000_1_OR_NEWER");
#endif
            text.Append("#endif");

            string newContent = text.ToString();
            string oldContent = File.ReadAllText(filePath);
            if (!string.Equals(oldContent, newContent))
            {
                try
                {
                    AssetDatabase.StartAssetEditing();
                    File.WriteAllText(filePath, newContent);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }
        }

        private static string GetDirectory()
        {
            string[] guids = AssetDatabase.FindAssets($"t:Script {nameof(Beam_Constants)}");
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return Path.GetDirectoryName(path);
        }
    }
}
#endif
