////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) Martin Bustos @FronkonGames <fronkongames@gmail.com>. All rights reserved.
//
// THIS FILE CAN NOT BE HOSTED IN PUBLIC REPOSITORIES.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
using UnityEditor;

namespace FronkonGames.Glitches.Interferences.Editor
{
  /// <summary> Interferences inspector. </summary>
  [CustomEditor(typeof(InterferencesVolume))]
  public class InterferencesVolumeInspector : Inspector
  {
    protected override void InspectorGUI()
    {
      /////////////////////////////////////////////////
      // Common.
      /////////////////////////////////////////////////
      DrawFloatSliderWithReset("intensity");

      /////////////////////////////////////////////////
      // Interferences.
      /////////////////////////////////////////////////
      Separator();

      DrawEnumDropdownWithReset("blend", "Blend", ColorBlends.Solid);
      DrawFloatSliderWithReset("offset");

      DrawFloatSliderWithReset("distortion");
      IndentLevel++;
      DrawFloatSliderWithReset("distortionSpeed", "Speed");
      DrawFloatSliderWithReset("distortionDensity", "Density");
      DrawFloatSliderWithReset("distortionAmplitude", "Amplitude");
      DrawFloatSliderWithReset("distortionFrequency", "Frequency");
      IndentLevel--;

      DrawFloatSliderWithReset("scanlines");
      IndentLevel++;
      DrawFloatSliderWithReset("scanlinesDensity", "Density");
      DrawFloatSliderWithReset("scanlinesOpacity", "Opacity");
      IndentLevel--;
    }

    protected override void ResetValues() => ((InterferencesVolume)target).Reset();

    protected override void CheckForErrors()
    {
      if (Interferences.IsInAnyRenderFeatures() == false)
      {
        Separator();

        EditorGUILayout.HelpBox($"Renderer Feature '{Constants.Asset.Name}' not found. You must add it as a Render Feature.", MessageType.Error);
      }
      else
      {
        Interferences[] effects = Interferences.Instances;

        bool anyEnabled = false;
        for (int i = 0; i < effects.Length; i++)
        {
          if (effects[i].isActive == true)
          {
            anyEnabled = true;
            break;
          }
        }

        if (anyEnabled == false)
        {
          Separator();

          EditorGUILayout.HelpBox($"No Renderer Feature '{Constants.Asset.Name}' is active. You must activate it in the Render Features.", MessageType.Warning);
        }
      }
    }
  }
}
