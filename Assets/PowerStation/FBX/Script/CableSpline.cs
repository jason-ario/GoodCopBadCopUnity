using System.Collections.Generic;
using UnityEngine;

public struct CableSample
{
    public Vector3 position;
    public Vector3 tangent;
    public float distance;
}

public static class CableSpline
{
    public static Vector3 GetSaggedPoint(Vector3 a, Vector3 b, float t, float sagAmount)
    {
        Vector3 straight = Vector3.Lerp(a, b, t);
        // Parabola: 0 at t=0 and t=1, 1 at t=0.5
        float sagFactor = 4f * t * (1f - t);
        straight.y -= sagAmount * sagFactor;
        return straight;
    }

    public static List<CableSample> SamplePath(Vector3 a, Vector3 b, float sagAmount, int segments)
    {
        var samples = new List<CableSample>(segments + 1);
        float cumulative = 0f;
        Vector3 prevPos = GetSaggedPoint(a, b, 0f, sagAmount);

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 pos = GetSaggedPoint(a, b, t, sagAmount);
            if (i > 0) cumulative += Vector3.Distance(prevPos, pos);

            Vector3 tangent = (i < segments)
                ? (GetSaggedPoint(a, b, Mathf.Min(t + 0.01f, 1f), sagAmount) - pos).normalized
                : (pos - prevPos).normalized;

            if (tangent == Vector3.zero && i > 0) tangent = samples[i - 1].tangent;

            samples.Add(new CableSample { position = pos, tangent = tangent, distance = cumulative });
            prevPos = pos;
        }
        return samples;
    }

    public static List<Quaternion> BuildParallelTransportFrames(List<CableSample> samples)
    {
        var frames = new List<Quaternion>(samples.Count);
        if (samples.Count == 0) return frames;

        // Prevent alignment flip if cable is running nearly vertical
        Vector3 up = Mathf.Abs(Vector3.Dot(samples[0].tangent, Vector3.up)) > 0.99f
            ? Vector3.forward : Vector3.up;

        Vector3 prevTangent = samples[0].tangent;
        Quaternion frame = Quaternion.LookRotation(prevTangent, up);
        frames.Add(frame);

        for (int i = 1; i < samples.Count; i++)
        {
            Vector3 tangent = samples[i].tangent;
            Quaternion delta = Quaternion.FromToRotation(prevTangent, tangent);
            frame = delta * frame; // Propagate previous frame rotation
            frames.Add(frame);
            prevTangent = tangent;
        }
        return frames;
    }
}