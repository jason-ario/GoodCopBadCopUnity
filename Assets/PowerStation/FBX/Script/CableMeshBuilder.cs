using System.Collections.Generic;
using UnityEngine;

public class CableMeshData
{
    public List<Vector3> vertices = new List<Vector3>();
    public List<List<int>> submeshes = new List<List<int>>();
    public List<Vector2> uvs = new List<Vector2>();
    public List<Color> colors = new List<Color>();

    public CableMeshData(int subMeshCount = 1)
    {
        for (int i = 0; i < subMeshCount; i++)
        {
            submeshes.Add(new List<int>());
        }
    }

    public void AddTriangle(int submeshIndex, int a, int b, int c)
    {
        submeshes[submeshIndex].Add(a);
        submeshes[submeshIndex].Add(b);
        submeshes[submeshIndex].Add(c);
    }

    public Mesh ToMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "ProceduralCable_Mesh";
        mesh.SetVertices(vertices);
        mesh.subMeshCount = submeshes.Count;
        for (int i = 0; i < submeshes.Count; i++)
        {
            mesh.SetTriangles(submeshes[i], i);
        }
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        
        // Recalculate cleanly without NaN generation
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }
}

public static class CableMeshBuilder
{
    public static CableMeshData BuildCableMesh(
        List<CableSample> samples,
        List<Quaternion> frames,
        float mainRadius, int mainSides,
        int wireCount, float wireRadius, int wireSides,
        float wrapPitch, float jitter,
        float uvTilingPerMeter, bool windEnabled,
        bool isHanging, bool hasCapMaterial)
    {
        // If hanging AND a metal cap material is provided, use 2 submeshes. Otherwise, just 1.
        int totalSubmeshes = (isHanging && hasCapMaterial) ? 2 : 1;
        var data = new CableMeshData(totalSubmeshes);
        
        int segments = samples.Count - 1;
        if (segments <= 0) return data;

        float totalLength = samples[samples.Count - 1].distance;

        // 1. Build Main Cable Core (Submesh 0)
        BuildTube(data, samples, frames, mainRadius, mainSides, uvTilingPerMeter, windEnabled, totalLength, Vector3.zero, isHanging, 0);

        // 2. Build Helix-Wrapped Secondary Wires (Submesh 0)
        for (int w = 0; w < wireCount; w++)
        {
            float strandPitch = wrapPitch * (1f + ((w % 2 == 0 ? 1f : -1f) * jitter * 0.5f));
            float strandRadius = (mainRadius + wireRadius) * (1f + ((w * 0.13f) % jitter));
            float basePhase = (w / (float)wireCount) * Mathf.PI * 2f;

            BuildWrappedTube(data, samples, frames, wireRadius, wireSides, uvTilingPerMeter, windEnabled, totalLength, strandRadius, strandPitch, basePhase, isHanging, 0);
        }

        // 3. Build Solid Metal End-Caps if Hanging (Submesh 1, or Submesh 0 if no metal mat assigned)
        if (isHanging)
        {
            int capSubmesh = (totalSubmeshes > 1) ? 1 : 0;
            CableSample tipSample = samples[samples.Count - 1];
            Quaternion tipFrame = frames[frames.Count - 1];

            // Cap the main core
            BuildEndCap(data, tipSample.position, tipSample.distance, tipFrame, mainRadius, mainSides, capSubmesh);

            // Cap the wrapped wires so they aren't hollow
            for (int w = 0; w < wireCount; w++)
            {
                float strandRadius = (mainRadius + wireRadius) * (1f + ((w * 0.13f) % jitter));
                float wrapAngle = (totalLength / Mathf.Max(0.01f, wrapPitch)) * Mathf.PI * 2f + ((w / (float)wireCount) * Mathf.PI * 2f);
                Vector3 localOffset = new Vector3(Mathf.Cos(wrapAngle), Mathf.Sin(wrapAngle), 0f) * strandRadius;
                Vector3 wireTipPos = tipSample.position + (tipFrame * localOffset);

                BuildEndCap(data, wireTipPos, tipSample.distance, tipFrame, wireRadius, wireSides, capSubmesh);
            }
        }

        return data;
    }

    private static void BuildTube(
        CableMeshData data, List<CableSample> samples, List<Quaternion> frames,
        float radius, int sides, float uvTiling, bool windEnabled, float totalLength, Vector3 fixedOffset,
        bool isHanging, int submeshIndex)
    {
        int startVertIndex = data.vertices.Count;
        int vertsPerRing = sides + 1; // +1 for UV seam wrapping

        for (int i = 0; i < samples.Count; i++)
        {
            float t = (totalLength > 0f) ? samples[i].distance / totalLength : 0f;
            
            // PENDULUM WIND: Standard cables pin at 0 and 1. Hanging cables pin at 0, swing freely at 1!
            float windWeight = 0f;
            if (windEnabled)
            {
                windWeight = isHanging ? (t * t) : (4f * t * (1f - t));
            }

            float windPhase = Mathf.Repeat(samples[i].distance * 0.37f, 1f);
            Color vertColor = new Color(windWeight, windPhase, 0f, 1f);

            Vector3 center = samples[i].position + (frames[i] * fixedOffset);

            for (int s = 0; s <= sides; s++)
            {
                float angle = (s / (float)sides) * Mathf.PI * 2f;
                Vector3 localPos = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                Vector3 worldPos = center + (frames[i] * localPos);

                data.vertices.Add(worldPos);
                data.uvs.Add(new Vector2(s / (float)sides, samples[i].distance * uvTiling));
                data.colors.Add(vertColor);
            }
        }

        AddTubeTriangles(data, samples.Count - 1, sides, startVertIndex, vertsPerRing, submeshIndex);
    }

    private static void BuildWrappedTube(
        CableMeshData data, List<CableSample> samples, List<Quaternion> frames,
        float radius, int sides, float uvTiling, bool windEnabled, float totalLength,
        float wrapRadius, float wrapPitch, float basePhase, bool isHanging, int submeshIndex)
    {
        int startVertIndex = data.vertices.Count;
        int vertsPerRing = sides + 1;

        for (int i = 0; i < samples.Count; i++)
        {
            float t = (totalLength > 0f) ? samples[i].distance / totalLength : 0f;
            
            float windWeight = 0f;
            if (windEnabled)
            {
                windWeight = isHanging ? (t * t) : (4f * t * (1f - t));
            }

            float windPhase = Mathf.Repeat(samples[i].distance * 0.37f, 1f);
            Color vertColor = new Color(windWeight, windPhase, 0f, 1f);

            float wrapAngle = (samples[i].distance / Mathf.Max(0.01f, wrapPitch)) * Mathf.PI * 2f + basePhase;
            Vector3 localOffset = new Vector3(Mathf.Cos(wrapAngle), Mathf.Sin(wrapAngle), 0f) * wrapRadius;
            Vector3 center = samples[i].position + (frames[i] * localOffset);

            for (int s = 0; s <= sides; s++)
            {
                float angle = (s / (float)sides) * Mathf.PI * 2f;
                Vector3 localPos = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                Vector3 worldPos = center + (frames[i] * localPos);

                data.vertices.Add(worldPos);
                data.uvs.Add(new Vector2(s / (float)sides, samples[i].distance * uvTiling));
                data.colors.Add(vertColor);
            }
        }

        AddTubeTriangles(data, samples.Count - 1, sides, startVertIndex, vertsPerRing, submeshIndex);
    }

    private static void BuildEndCap(
        CableMeshData data, Vector3 centerPos, float distance, Quaternion frame,
        float radius, int sides, int submeshIndex)
    {
        int centerIndex = data.vertices.Count;
        
        // Center vertex of the cap
        data.vertices.Add(centerPos);
        data.uvs.Add(new Vector2(0.5f, 0.5f));
        data.colors.Add(new Color(1f, Mathf.Repeat(distance * 0.37f, 1f), 0f, 1f)); // 100% wind weight at tip

        int ringStartIndex = data.vertices.Count;
        for (int s = 0; s < sides; s++)
        {
            float angle = (s / (float)sides) * Mathf.PI * 2f;
            Vector3 localPos = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            data.vertices.Add(centerPos + (frame * localPos));
            
            // Planar UV projection for the metal texture
            data.uvs.Add(new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f));
            data.colors.Add(new Color(1f, Mathf.Repeat(distance * 0.37f, 1f), 0f, 1f));
        }

        // Clean single-sided winding order (facing outwards) to prevent normal calculation conflicts!
        for (int s = 0; s < sides; s++)
        {
            int current = ringStartIndex + s;
            int next = ringStartIndex + ((s + 1) % sides);

            // Front facing outward triangle only
            data.AddTriangle(submeshIndex, centerIndex, current, next);
        }
    }

    private static void AddTubeTriangles(CableMeshData data, int segments, int sides, int startVertIndex, int vertsPerRing, int submeshIndex)
    {
        for (int i = 0; i < segments; i++)
        {
            int ringA = startVertIndex + (i * vertsPerRing);
            int ringB = startVertIndex + ((i + 1) * vertsPerRing);

            for (int s = 0; s < sides; s++)
            {
                int current = ringA + s;
                int next = ringA + s + 1;
                int currentNextRing = ringB + s;
                int nextNextRing = ringB + s + 1;

                // Quad -> 2 Triangles
                data.AddTriangle(submeshIndex, current, currentNextRing, next);
                data.AddTriangle(submeshIndex, next, currentNextRing, nextNextRing);
            }
        }
    }
}