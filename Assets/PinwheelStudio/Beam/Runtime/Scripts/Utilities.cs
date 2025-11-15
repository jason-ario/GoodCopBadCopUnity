using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Beam
{
    public static class Utilities
    {
        /// <summary>
        /// Get the next multiple-of-8 value
        /// </summary>
        /// <param name="value">Input value</param>
        /// <returns>Next multiple-of-8 value</returns>
        public static int CeilMulOf8(int value)
        {
            float vf = (float)value;
            return Mathf.CeilToInt(vf / 8.0f) * 8;
        }

        /// <summary>
        /// Turn a compute shader's keyword on/off.
        /// </summary>
        /// <param name="shader">The compute shader</param>
        /// <param name="kw">The keyword</param>
        /// <param name="enable">Keyword state</param>
        public static void SetKeyword(this ComputeShader shader, string kw, bool enable)
        {
            if (enable)
            {
                shader.EnableKeyword(kw);
            }
            else
            {
                shader.DisableKeyword(kw);
            }
        }
    }
}
