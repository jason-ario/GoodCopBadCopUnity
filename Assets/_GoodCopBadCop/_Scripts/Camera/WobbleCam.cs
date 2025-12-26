using UnityEngine;

public class WobbleCam : MonoBehaviour
{
    [Header("Position Wobble")]
    public float positionStrength = 0.02f;
    public float positionSpeed = 0.8f;

    [Header("Rotation Wobble")]
    public float rotationStrength = 0.6f;
    public float rotationSpeed = 0.7f;

    private Vector3 startPos;
    private Quaternion startRot;

    private float noiseOffsetX;
    private float noiseOffsetY;
    private float noiseOffsetRot;

    void Start()
    {
        startPos = transform.localPosition;
        startRot = transform.localRotation;

        noiseOffsetX = Random.Range(0f, 100f);
        noiseOffsetY = Random.Range(0f, 100f);
        noiseOffsetRot = Random.Range(0f, 100f);
    }

    void Update()
    {
        float t = Time.time;

        // POSITION
        float x = (Mathf.PerlinNoise(noiseOffsetX, t * positionSpeed) - 0.5f) * positionStrength;
        float y = (Mathf.PerlinNoise(noiseOffsetY, t * positionSpeed) - 0.5f) * positionStrength;

        transform.localPosition = startPos + new Vector3(x, y, 0);

        // ROTATION
        float rotZ = (Mathf.PerlinNoise(noiseOffsetRot, t * rotationSpeed) - 0.5f) * rotationStrength;
        transform.localRotation = startRot * Quaternion.Euler(0, 0, rotZ);
    }
}