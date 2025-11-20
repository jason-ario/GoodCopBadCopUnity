using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VolFx
{
    [AddComponentMenu("")]
    public class VolFx_SetUrpAsset : MonoBehaviour
    {
        public UniversalRenderPipelineAsset _urp;
        public UnityEvent _onStart;
        
        // =======================================================================
        private void Start()
        {
            QualitySettings.renderPipeline = _urp;
            _onStart.Invoke();
        }
    }
}
