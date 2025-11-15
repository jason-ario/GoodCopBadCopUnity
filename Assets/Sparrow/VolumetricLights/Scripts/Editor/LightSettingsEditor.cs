
using Sparrow.Utilities;
using UnityEditor;
using UnityEngine;

namespace Sparrow.VolumetricLight.Editor
{
    public class LightSettingsEditor
    {
        public static bool DrawEditor(LightSettings settings)
        {
            bool hasChanged = false;

            EditorGUILayout.LabelField("Blending Mode", EditorStyles.boldLabel);
            var blendingMode = (VolumetricLight.BlendingMode)EditorGUILayout.EnumPopup("Blending Mode", settings.blendingMode);
            if (blendingMode != settings.blendingMode)
            {
                settings.blendingMode = blendingMode;
                hasChanged = true;
            }

            if (settings.blendingMode == VolumetricLight.BlendingMode.Alpha)
            {
                EditorGUI.indentLevel++;
                var alpha = EditorGUILayout.FloatField("Alpha", settings.alpha);
                if (alpha != settings.alpha)
                {
                    settings.alpha = alpha;
                    hasChanged = true;
                }
                EditorGUI.indentLevel--;
            }

            EditorUtils.Separator();

            var overrideColor = EditorGUILayout.Toggle("Override Light Color", settings.overrideLightColor);
            if (overrideColor != settings.overrideLightColor)
            {
                settings.overrideLightColor = overrideColor;
                hasChanged = true;
            }

            EditorGUILayout.Space();
            EditorGUI.indentLevel++;

            if (overrideColor)
            {
                var newColor = EditorGUILayout.ColorField("Color", settings.newColor);
                if (newColor != settings.newColor)
                {
                    settings.newColor = newColor;
                    hasChanged = true;
                }

                var newIntensity = EditorGUILayout.FloatField("Intensity", settings.newIntensity);
                if (newIntensity != settings.newIntensity)
                {
                    settings.newIntensity = newIntensity;
                    hasChanged = true;
                }
            }
            else
            {
                var intensityMultiplier = EditorGUILayout.FloatField("Intensity Multiplier", settings.intensityMultiplier);
                if (intensityMultiplier != settings.intensityMultiplier)
                {
                    settings.intensityMultiplier = intensityMultiplier;
                    hasChanged = true;
                }
            }

            EditorGUI.indentLevel--;
            EditorUtils.Separator();

            var overrideFading = EditorGUILayout.Toggle("Override Fade Distances", settings.overrideFading);
            if (overrideFading != settings.overrideFading)
            {
                settings.overrideFading = overrideFading;
                hasChanged = true;
            }

            if (overrideFading)
            {
                EditorGUILayout.Space();
                EditorGUI.indentLevel++;

                var depthFadeDistance = EditorGUILayout.FloatField("Geometry Distance", settings.depthFadeDistance);
                if (depthFadeDistance != settings.depthFadeDistance)
                {
                    settings.depthFadeDistance = depthFadeDistance;
                    hasChanged = true;
                }

                var cameraFadeDistance = EditorGUILayout.FloatField("Camera Distance", settings.cameraFadeDistance);
                if (cameraFadeDistance != settings.cameraFadeDistance)
                {
                    settings.cameraFadeDistance = cameraFadeDistance;
                    hasChanged = true;
                }

                var fresnelFadeStrength = EditorGUILayout.FloatField("Edge Fade Strength", settings.fresnelFadeStrength);
                if (fresnelFadeStrength != settings.fresnelFadeStrength)
                {
                    settings.fresnelFadeStrength = fresnelFadeStrength;
                    hasChanged = true;
                }

                EditorGUI.indentLevel--;
            }

            EditorUtils.Separator();

            var useCustomFade = EditorGUILayout.Toggle("Custom Fading", settings.useCustomFade);
            if (useCustomFade != settings.useCustomFade)
            {
                settings.useCustomFade = useCustomFade;
                hasChanged = true;
            }

            if (useCustomFade)
            {
                EditorGUI.indentLevel++;
                var fadeGradient = EditorGUILayout.GradientField("Fade Gradient", settings.fadeGradient);
                if (!fadeGradient.Equals(settings.fadeGradient))
                {
                    settings.fadeGradient = fadeGradient;
                    hasChanged = true;
                }
                settings.ResetFadeTexture();
                EditorGUI.indentLevel--;
            }

            EditorUtils.Separator();

            var useFogTexture = EditorGUILayout.Toggle("Use Fog Texture", settings.useFogTexture);
            if (useFogTexture != settings.useFogTexture)
            {
                settings.useFogTexture = useFogTexture;
                hasChanged = true;
            }

            if (useFogTexture)
            {
                EditorGUI.indentLevel++;
                var fogTexture = EditorGUILayout.ObjectField("Fog Texture", settings.fogTexture, typeof(Texture2D), false) as Texture2D;
                if (fogTexture != settings.fogTexture)
                {
                    settings.fogTexture = fogTexture;
                    hasChanged = true;
                }

                var fogTextureTiling = EditorGUILayout.FloatField("Fog Texture Tiling", settings.fogTextureTiling);
                if (fogTextureTiling != settings.fogTextureTiling)
                {
                    settings.fogTextureTiling = fogTextureTiling;
                    hasChanged = true;
                }

                var fogOpacity = EditorGUILayout.FloatField("Fog Opacity", settings.fogOpacity);
                if (fogOpacity != settings.fogOpacity)
                {
                    settings.fogOpacity = fogOpacity;
                    hasChanged = true;
                }

                var verticalScrollingSpeed = EditorGUILayout.FloatField("Vertical Scrolling Speed", settings.verticalScrollingSpeed);
                if (verticalScrollingSpeed != settings.verticalScrollingSpeed)
                {
                    settings.verticalScrollingSpeed = verticalScrollingSpeed;
                    hasChanged = true;
                }

                var horizontalScrollingSpeed = EditorGUILayout.FloatField("Horizontal Scrolling Speed", settings.horizontalScrollingSpeed);
                if (horizontalScrollingSpeed != settings.horizontalScrollingSpeed)
                {
                    settings.horizontalScrollingSpeed = horizontalScrollingSpeed;
                    hasChanged = true;
                }
                EditorGUI.indentLevel--;
            }

            return hasChanged;
        }
    }
}