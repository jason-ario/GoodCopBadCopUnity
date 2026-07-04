using UnityEngine;

[ExecuteAlways]
public class WindManager : MonoBehaviour
{
    [Header("Global Wind Parameters")]
    public Vector3 windDirection = new Vector3(1f, 0f, 0.5f);
    [Range(0f, 5f)] public float windSpeed = 1.5f;
    [Range(0f, 0.5f)] public float windAmplitude = 0.08f;

    private static readonly int WindDirID = Shader.PropertyToID("_WindDirection");
    private static readonly int WindSpeedID = Shader.PropertyToID("_WindSpeed");
    private static readonly int WindAmpID = Shader.PropertyToID("_WindAmplitude");

    private void Update()
    {
        Shader.SetGlobalVector(WindDirID, windDirection.normalized);
        Shader.SetGlobalFloat(WindSpeedID, windSpeed);
        Shader.SetGlobalFloat(WindAmpID, windAmplitude);
    }
}