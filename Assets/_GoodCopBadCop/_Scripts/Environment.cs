using UnityEngine;
using VolumetricFogAndMist2;

[CreateAssetMenu(fileName = "Environment", menuName = "Scriptable Objects/Environment")]
public class Environment : ScriptableObject
{ 
    public Color fogColor;
    public VolumetricFogProfile volumetricFogProfile;
    public Material Skybox;
    public float fogDensity;
}
