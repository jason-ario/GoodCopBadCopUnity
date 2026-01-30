using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HalfToneDemo
{
#if UNITY_EDITOR
    [CustomEditor(typeof(CoreUrpChange))]
    public class CoreUrpChangeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            if (GUILayout.Button("Open ProjectSettings/Graphics"))
            {
                SettingsService.OpenProjectSettings("Project/Graphics");
            }
        }
    }
#endif

    [DefaultExecutionOrder(-10)]
    [HelpURL("https://docs.unity3d.com/ja/2021.2/Manual/srp-setting-render-pipeline-asset.html")]
    public class CoreUrpChange : MonoBehaviour
    {
        public enum PipelineType
        {
            None,
            Builtin,
            URP,
        }
        [SerializeField] PipelineType m_startPipeline;
        [SerializeField] RenderPipelineAsset m_URPAsset;
        [SerializeField] bool m_isChangable;
        [SerializeField, Multiline(4)] string m_beareMessage;
        RenderPipelineAsset m_previousRPAsset;
        RenderPipelineAsset m_previousRPQualityRPAsset;

        private void Awake()
        {
        }
        void Start()
        {
            if (m_beareMessage == "")
            {
#if UNITY_6000_0_OR_NEWER
                m_beareMessage = "<color=#FFFF00>BEAWRE! YOU MUST SET</color>\n" +
                    "<b>PorjectSettings >Graphics > Default Render Pipeline</b> to <color=#00FFFF>{URP_ASSET_NAME}\n</color>" +
                    "and <b>PorjectSettings > Quality > Render Pipeline Asset</b> to <color=#00FFFF>{URP_ASSET_NAME}</color>\n" +
                    "<color=#FFFF00>BEFORE PLAY!</color>";
#else
                m_beareMessage = "<color=#FFFF00>BEAWRE! YOU MUST SET</color>\n" +
                    "<b>PorjectSettings >Graphics > Scriptable Render Pipeline Settings</b> to <color=#00FFFF>{URP_ASSET_NAME}\n</color>" +
                    "and <b>PorjectSettings > Quality > Render Pipeline Asset</b> to <color=#00FFFF>{URP_ASSET_NAME}</color>\n" +
                    "<color=#FFFF00>BEFORE PLAY!</color>";
#endif
            }
            if (!string.IsNullOrEmpty(m_beareMessage))
            {
                string name = (m_URPAsset == null || m_startPipeline == PipelineType.Builtin) ? "None" : m_URPAsset.name;
                m_beareMessage = m_beareMessage.Replace("{URP_ASSET_NAME}", name);
            }

            m_previousRPQualityRPAsset = QualitySettings.renderPipeline;
            m_previousRPAsset = GraphicsSettings.defaultRenderPipeline;
            if (m_startPipeline != PipelineType.None)
            {
                SetRenderPipeline(m_startPipeline == PipelineType.URP ? m_URPAsset : null);
            }
        }
        void Update()
        {
        }

        private void OnDestroy()
        {
            if (m_startPipeline != PipelineType.None)
            {
                GraphicsSettings.defaultRenderPipeline = m_previousRPAsset;
                QualitySettings.renderPipeline = m_previousRPQualityRPAsset;
            }
        }

        private void OnApplicationQuit()
        {
        }

        /// <summary>
        /// Set defaultRenderPipeline
        /// </summary>
        /// <param name="_asset"></param>
        /// <returns></returns>
        public RenderPipelineAsset SetRenderPipeline(RenderPipelineAsset _asset)
        {
            RenderPipelineAsset previousAsset = GraphicsSettings.defaultRenderPipeline;
            if (_asset != previousAsset)
            {
                if (_asset == null && (previousAsset != null))
                {
                    GraphicsSettings.defaultRenderPipeline = _asset;
                }
                else if (_asset != null && (previousAsset == null || previousAsset.name != _asset.name))
                {
                    GraphicsSettings.defaultRenderPipeline = _asset;
                }
            }
            QualitySettings.renderPipeline = GraphicsSettings.defaultRenderPipeline;
            return previousAsset;
        }

        void OnGUI()
        {
            if (!string.IsNullOrEmpty(m_beareMessage))
            {
                GUI.Label(new Rect(4, 4, 640, 120), m_beareMessage);
            }
            if (m_isChangable)
            {
                if (GUI.Button(new Rect(10, 150, 150, 20), "Change to Default"))
                {
                    SetRenderPipeline(null);
                }
                if (GUI.Button(new Rect(10, 170, 150, 20), "Change to URP"))
                {
                    SetRenderPipeline(m_URPAsset);
                }
            }
        }
    }
}
