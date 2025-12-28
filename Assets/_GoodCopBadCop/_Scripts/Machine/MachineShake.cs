using UnityEngine;

public class MachineShake : MonoBehaviour
{
    [Header("Shake Settings")]
    public bool isRunning = true;
    public float positionStrength = 0.02f;
    public float rotationStrength = 1.5f;
    public float noiseSpeed = 5f;

    Vector3 startPos;
    Quaternion startRot;
    float seed;

    void Start()
    {
        startPos = transform.localPosition;
        //startRot = transform.localRotation;
        seed = Random.value * 1000f;
    }

    void Update()
    {
        if (!isRunning)
        {
            //transform.localPosition = Vector3.Lerp(transform.localPosition, startPos, Time.deltaTime * 10f);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, startRot, Time.deltaTime * 10f);
            return;
        }

        float t = Time.time * noiseSpeed;

        float x = Mathf.PerlinNoise(seed, t) - 0.5f;
        float y = Mathf.PerlinNoise(seed + 1f, t) - 0.5f;
        float z = Mathf.PerlinNoise(seed + 2f, t) - 0.5f;

        transform.localPosition = startPos + new Vector3(x, y, z) * positionStrength;
        //transform.localRotation = startRot * Quaternion.Euler(x, y, z * rotationStrength);
    }
}