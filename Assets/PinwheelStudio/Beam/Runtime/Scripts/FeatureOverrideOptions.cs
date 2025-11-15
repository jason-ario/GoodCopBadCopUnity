using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Beam
{
    /// <summary>
    /// Override URP settings for specific features of the asset
    /// </summary>
    public enum FeatureOverrideOptions
    {
        /// <summary>
        /// Use URP settings for that feature. 
        /// </summary>
        UseRenderPipelineSettings,
        /// <summary>
        /// Force the feature to be off.
        /// </summary>
        Off
    }
}
