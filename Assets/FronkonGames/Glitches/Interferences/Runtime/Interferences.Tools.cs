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
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FronkonGames.Glitches.Interferences
{
  /// <summary> Manager tools. </summary>
  public sealed partial class Interferences
  {
    private const string RenderListFieldName = "m_RendererDataList";

    private const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly Interferences[] NoEffects = new Interferences[0];

    /// <summary> Is it in the default render pipeline? </summary>
    /// <returns> True / false </returns>
    /// <remarks> This function use Reflection, so it can be slow. </remarks>
    public static bool IsInRenderFeatures() => Instance != null;

    /// <summary> Is it in any renders pipeline? </summary>
    /// <returns> True / false </returns>
    /// <remarks> This function use Reflection, so it can be slow. </remarks>
    public static bool IsInAnyRenderFeatures() => Instances.Length > 0;

    /// <summary> Returns the effect in the default render pipeline or null </summary>
    /// <returns> Effect or null </returns>
    /// <remarks> This function use Reflection, so it can be slow. </remarks>
    public static Interferences Instance
    {
      get
      {
        UniversalRenderPipelineAsset pipelineAsset = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
        if (pipelineAsset != null)
        {
          FieldInfo propertyInfo = pipelineAsset.GetType().GetField(RenderListFieldName, bindingFlags);
          ScriptableRendererData scriptableRendererData = ((ScriptableRendererData[])propertyInfo?.GetValue(pipelineAsset))?[0];
          for (int i = 0; i < scriptableRendererData.rendererFeatures.Count; ++i)
          {
            if (scriptableRendererData.rendererFeatures[i] is Interferences)
              return scriptableRendererData.rendererFeatures[i] as Interferences;
          }
        }

        return null;
      }
    }

    /// <summary> Returns an array with all the effects found. </summary>
    /// <returns> Array with effects </returns>
    /// <remarks> This function use Reflection, so it can be slow. </remarks>
    public static Interferences[] Instances
    {
      get
      {
        if (UniversalRenderPipeline.asset != null)
        {
          ScriptableRendererData[] rendererDataList = (ScriptableRendererData[])typeof(UniversalRenderPipelineAsset)
            .GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(UniversalRenderPipeline.asset);

          List<Interferences> effects = new();
          for (int i = 0; i < rendererDataList.Length; ++i)
          {
            if (rendererDataList[i] != null && rendererDataList[i].rendererFeatures.Count > 0)
              foreach (var feature in rendererDataList[i].rendererFeatures)
                if (feature is Interferences)
                  effects.Add(feature as Interferences);
          }

          return effects.ToArray();
        }

        return NoEffects;
      }
    }
  }
}
