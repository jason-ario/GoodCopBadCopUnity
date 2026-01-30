using UnityEngine;

namespace HalfToneDemo
{
    [RequireComponent(typeof(Camera))]
    public class BuiltinBlit : MonoBehaviour
    {
        [SerializeField] Material material;
        Camera m_camera;
        
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            m_camera = GetComponent<Camera>();
            m_camera.depthTextureMode |= DepthTextureMode.Depth;
            //m_camera.SetReplacementShader(shader, "");
        }

        // Update is called once per frame
        void Update()
        {

        }

        // OnRenderImage is called after all rendering is complete to render image
        void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            Graphics.Blit(src, dest, material);
        }
    }
}
