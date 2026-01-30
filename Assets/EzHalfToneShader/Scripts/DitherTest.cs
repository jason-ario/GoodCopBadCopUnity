using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfToneDemo
{
    public class DitherTest : MonoBehaviour
    {
        [SerializeField] Material m_fullscreenMaterial;
        [SerializeField] Texture2D[] m_dotTexArr;
        [GradientUsage(true)] public Gradient m_hdrGradient;

        Texture2D m_defaoultDotTex;
        float m_defaultDotRate;
        float m_defaultTone;
        float m_defaultRotation;
        Color m_dotColor;

        // Start is called before the first frame update
        void Start()
        {
            if (m_fullscreenMaterial)
            {
                m_defaoultDotTex = m_fullscreenMaterial.GetTexture("_DotTex") as Texture2D;
                m_defaultDotRate = m_fullscreenMaterial.GetFloat("_DotRate");
                m_defaultTone = m_fullscreenMaterial.GetFloat("_Tone");
                m_dotColor = m_fullscreenMaterial.GetColor("_Color");
                m_defaultRotation = m_fullscreenMaterial.GetFloat("_Rotation");

                StartCoroutine(dotTexChangeCo());
                StartCoroutine(rotationChangeCo());
                StartCoroutine(toneChangeCo());
                //StartCoroutine(dotColChangeCo());
                StartCoroutine(dotAlphaChangeCo());
            }
        }

        // Update is called once per frame
        void Update()
        {

        }

        private void OnDestroy()
        {
            if (m_fullscreenMaterial)
            {
                m_fullscreenMaterial.SetTexture("_DotTex", m_defaoultDotTex);
                m_fullscreenMaterial.SetFloat("_DotRate", m_defaultDotRate);
                m_fullscreenMaterial.SetFloat("_Tone", m_defaultTone);
                m_fullscreenMaterial.SetColor("_Color", m_dotColor);
                m_fullscreenMaterial.SetFloat("_Rotation", m_defaultRotation);
            }
        }

        IEnumerator dotTexChangeCo()
        {
            while (true)
            {
                for (int i = 0; i < m_dotTexArr.Length; i++)
                {
                    m_fullscreenMaterial.SetTexture("_DotTex", m_dotTexArr[i]);
                    yield return StartCoroutine(dotRateChangeCo());
                }
                yield return null;
            }
        }

        IEnumerator dotRateChangeCo()
        {
            float stt = 100f;
            float end = 200f;
            float rate = 0.0f;
            while(rate < 360.0f)
            {
                rate += Time.deltaTime * 50.0f;
                float aRate = Mathf.Cos(rate * Mathf.Deg2Rad) * 0.5f + 0.5f;
                m_fullscreenMaterial.SetFloat("_DotRate", Mathf.Lerp(stt, end, aRate));
                yield return null;
            }
        }

        IEnumerator rotationChangeCo()
        {
            float rate = 0.0f;
            while (true)
            {
                rate += Time.deltaTime * 1.0f;
                float aRate = Mathf.Cos(rate * Mathf.Deg2Rad) * 0.5f + 0.5f;
                m_fullscreenMaterial.SetFloat("_Rotation", Mathf.Lerp(0, 360, aRate));
                yield return null;
            }
        }
        IEnumerator dotColChangeCo()
        {
            float rate = 0.0f;
            while (true)
            {
                rate += Time.deltaTime * 10.0f;
                float aRate = Mathf.Cos(rate * Mathf.Deg2Rad) * 0.5f + 0.5f;
                m_fullscreenMaterial.SetColor("_Color", m_hdrGradient.Evaluate(aRate));
                yield return null;
            }
        }
        IEnumerator dotAlphaChangeCo()
        {
            float rate = 0.0f;
            while (true)
            {
                rate += Time.deltaTime * 20.0f;
                float aRate = Mathf.Cos(rate * Mathf.Deg2Rad) * 0.5f + 0.5f;
                Color dotCol = m_fullscreenMaterial.GetColor("_Color");
                dotCol.a = Mathf.Lerp(0.0f, 0.5f, aRate);
                m_fullscreenMaterial.SetColor("_Color", dotCol);
                yield return null;
            }
        }
        IEnumerator toneChangeCo()
        {
            float rate = 0.0f;
            while (true)
            {
                rate += Time.deltaTime * 20.0f;
                float aRate = Mathf.Cos(rate * Mathf.Deg2Rad) * 0.5f + 0.5f;
                m_fullscreenMaterial.SetFloat("_Tone", 32f + aRate * 32f);
                yield return null;
            }
        }
    }
}
