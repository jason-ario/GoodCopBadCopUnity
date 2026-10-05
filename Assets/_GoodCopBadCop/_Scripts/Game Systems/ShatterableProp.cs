using UnityEngine;

/// <summary>
/// Breakable variant of <see cref="HittableProp"/> for bottles, jars, etc. Instead of wobbling, the
/// first bullet / melee hit plays the hit sound (use a glass-shatter clip), hides the prop's renderers
/// and colliders, and bursts physical glass shards built from the prop's own material.
///
/// Rides the existing <see cref="HittableProp"/> weapon pipeline unchanged: the attacker calls
/// <see cref="HittableProp.TryHitAt"/> locally and the weapon relays the hit point to every other
/// client, so the shatter is seen by everyone. Purely cosmetic and local — not synced to late joiners.
/// Shards are particles (not rigidbodies) so they never block later raycasts or bullets.
/// </summary>
public class ShatterableProp : HittableProp
{
    // Configuration

    [Header("Shatter")]
    [Tooltip("Extra behaviours to disable while shattered (e.g. a HighlightEffect).")]
    [SerializeField] private Behaviour[] _disableOnShatter;

    [Tooltip("Optional extra VFX (e.g. liquid splash) spawned at the prop's centre, facing the shot.")]
    [SerializeField] private GameObject _shatterVFXPrefab;
    [SerializeField] private float _shatterVFXLifetime = 4f;

    [Tooltip("Seconds until the prop reappears intact. 0 = stays broken.")]
    [SerializeField] private float _respawnDelay = 0f;

    [Header("Shards")]
    [Tooltip("Shard material. Defaults to the first material on the prop's renderer.")]
    [SerializeField] private Material _shardMaterial;
    [SerializeField, Range(0, 64)] private int _shardCount = 18;
    [Tooltip("Shard size range in metres.")]
    [SerializeField] private Vector2 _shardSizeRange = new Vector2(0.06f, 0.2f);
    [Tooltip("Max outward speed (m/s) of the shards.")]
    [SerializeField] private float _shardSpeed = 3.5f;
    [Tooltip("How strongly shards are pushed along the shot direction (0 = pure radial burst).")]
    [SerializeField, Range(0f, 2f)] private float _shotDirectionBias = 0.8f;
    [SerializeField] private float _shardLifetime = 3f;

    [Header("Dust Poof")]
    [Tooltip("Soft smoke material (billboard particles). Leave empty to skip the dust poof.")]
    [SerializeField] private Material _dustMaterial;
    [Tooltip("Flipbook grid of the dust texture (columns, rows). (1,1) for a single-frame texture.")]
    [SerializeField] private Vector2Int _dustFlipbookTiles = new Vector2Int(1, 1);
    [SerializeField, Range(0, 32)] private int _dustCount = 7;
    [SerializeField] private Color _dustColor = new Color(0.78f, 0.74f, 0.68f, 0.55f);
    [Tooltip("Start size range of each puff in metres; puffs grow over their lifetime.")]
    [SerializeField] private Vector2 _dustSizeRange = new Vector2(0.35f, 0.7f);
    [Tooltip("How much each puff grows by the end of its life (multiplier).")]
    [SerializeField] private float _dustGrowth = 2.2f;
    [Tooltip("Initial outward speed (m/s) of the puffs — drag slows them quickly.")]
    [SerializeField] private float _dustSpeed = 1.2f;
    [SerializeField] private Vector2 _dustLifetimeRange = new Vector2(0.9f, 1.6f);


    // Internal

    private static Mesh s_shardMesh;

    private Renderer[] _renderers;
    private Collider[] _colliders;

    public bool IsShattered { get; private set; }


    // Public API

    public override void Hit(Vector3 hitPoint, Vector3 direction)
    {
        if (IsShattered) return;
        IsShattered = true;

        CacheParts();

        Bounds bounds = ComputeBounds(hitPoint);
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;

        PlayHitSound(hitPoint);
        SpawnShards(bounds, dir);
        SpawnDust(bounds, dir);

        if (_shatterVFXPrefab != null)
        {
            GameObject vfx = Instantiate(_shatterVFXPrefab, bounds.center, Quaternion.LookRotation(dir));
            Destroy(vfx, _shatterVFXLifetime);
        }

        SetIntact(false);

        if (_respawnDelay > 0f)
            Invoke(nameof(Restore), _respawnDelay);
    }

    /// <summary>Restores the prop to its intact state.</summary>
    public void Restore()
    {
        CancelInvoke(nameof(Restore));
        if (!IsShattered) return;

        CacheParts();
        SetIntact(true);
        IsShattered = false;
    }


    // Helpers

    private void CacheParts()
    {
        if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
        if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
    }

    private void SetIntact(bool intact)
    {
        foreach (Renderer r in _renderers) if (r != null) r.enabled = intact;
        foreach (Collider c in _colliders) if (c != null) c.enabled = intact;

        if (_disableOnShatter == null) return;
        foreach (Behaviour b in _disableOnShatter) if (b != null) b.enabled = intact;
    }

    private Bounds ComputeBounds(Vector3 fallbackCenter)
    {
        Bounds bounds = new Bounds(fallbackCenter, Vector3.one * 0.1f);
        bool first = true;
        foreach (Renderer r in _renderers)
        {
            if (r == null || !r.enabled) continue;
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    private Material ResolveShardMaterial()
    {
        if (_shardMaterial != null) return _shardMaterial;
        foreach (Renderer r in _renderers)
            if (r != null && r.sharedMaterial != null) return r.sharedMaterial;
        return null;
    }

    private void SpawnShards(Bounds bounds, Vector3 direction)
    {
        if (_shardCount <= 0) return;

        Material material = ResolveShardMaterial();
        if (material == null) return;

        GameObject go = new GameObject($"{name} Shards");
        go.transform.position = bounds.center;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake     = false;
        main.loop            = false;
        main.duration        = 0.1f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(_shardLifetime * 0.6f, _shardLifetime);
        main.gravityModifier = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = _shardCount;
        main.stopAction      = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
        spin.enabled      = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-12f, 12f);
        spin.y = new ParticleSystem.MinMaxCurve(-12f, 12f);
        spin.z = new ParticleSystem.MinMaxCurve(-12f, 12f);

        ParticleSystem.SizeOverLifetimeModule shrink = ps.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));

        ParticleSystem.CollisionModule collision = ps.collision;
        collision.enabled      = true;
        collision.type         = ParticleSystemCollisionType.World;
        collision.mode         = ParticleSystemCollisionMode.Collision3D;
        collision.quality      = ParticleSystemCollisionQuality.High;
        collision.bounce       = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        collision.dampen       = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        collision.lifetimeLoss = 0f;
        collision.radiusScale  = 0.5f;

        ParticleSystemRenderer psRenderer = go.GetComponent<ParticleSystemRenderer>();
        psRenderer.renderMode        = ParticleSystemRenderMode.Mesh;
        psRenderer.mesh              = GetShardMesh();
        psRenderer.sharedMaterial    = material;
        psRenderer.alignment         = ParticleSystemRenderSpace.World;
        psRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ps.Play();

        float radius = Mathf.Max(0.01f, bounds.extents.magnitude * 0.5f);
        ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < _shardCount; i++)
        {
            Vector3 offset = Random.insideUnitSphere;
            Vector3 burst  = (offset.normalized + direction * _shotDirectionBias + Vector3.up * 0.3f).normalized;

            emit.position   = bounds.center + Vector3.Scale(offset, bounds.extents) * 0.8f;
            emit.velocity   = burst * Random.Range(_shardSpeed * 0.35f, _shardSpeed);
            emit.startSize  = Random.Range(_shardSizeRange.x, _shardSizeRange.y);
            emit.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
            ps.Emit(emit, 1);
        }

        // Fallback cleanup in case the stop action never fires (e.g. paused simulation).
        Destroy(go, _shardLifetime + 2f);
    }

    /// <summary>
    /// Short-lived burst of soft, billowing dust puffs at the break point: they pop outward, are
    /// dragged to a near stop, drift slightly upward, expand and fade out.
    /// </summary>
    private void SpawnDust(Bounds bounds, Vector3 direction)
    {
        if (_dustMaterial == null || _dustCount <= 0) return;

        GameObject go = new GameObject($"{name} Dust");
        go.transform.position = bounds.center;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake     = false;
        main.loop            = false;
        main.duration        = 0.1f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(_dustLifetimeRange.x, _dustLifetimeRange.y);
        main.gravityModifier = -0.04f; // gentle rise
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = _dustCount;
        main.stopAction      = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag    = 4f;
        drag.multiplyDragByParticleSize     = false;
        drag.multiplyDragByParticleVelocity = true;

        ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

        ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f / Mathf.Max(1f, _dustGrowth), 0f, 3f), new Keyframe(1f, 1f)));

        ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
        fade.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.6f, 0.45f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        int tilesX = Mathf.Max(1, _dustFlipbookTiles.x);
        int tilesY = Mathf.Max(1, _dustFlipbookTiles.y);
        if (tilesX * tilesY > 1)
        {
            ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
            sheet.enabled   = true;
            sheet.mode      = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = tilesX;
            sheet.numTilesY = tilesY;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, tilesX * tilesY * 0.5f);
        }

        ParticleSystemRenderer psRenderer = go.GetComponent<ParticleSystemRenderer>();
        psRenderer.renderMode        = ParticleSystemRenderMode.Billboard;
        psRenderer.sharedMaterial    = _dustMaterial;
        psRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psRenderer.receiveShadows    = false;

        ps.Play();

        ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < _dustCount; i++)
        {
            Vector3 offset = Random.insideUnitSphere;
            Vector3 burst  = (offset.normalized + direction * 0.5f + Vector3.up * 0.2f).normalized;

            emit.position    = bounds.center + Vector3.Scale(offset, bounds.extents) * 0.5f;
            emit.velocity    = burst * Random.Range(_dustSpeed * 0.3f, _dustSpeed);
            emit.startSize   = Random.Range(_dustSizeRange.x, _dustSizeRange.y);
            emit.rotation    = Random.Range(0f, 360f);
            emit.startColor  = _dustColor;
            ps.Emit(emit, 1);
        }

        Destroy(go, _dustLifetimeRange.y + 1f);
    }

    /// <summary>Shared, double-sided, irregular triangle used for every shard (1 unit across).</summary>
    private static Mesh GetShardMesh()
    {
        if (s_shardMesh != null) return s_shardMesh;

        Vector3 a = new Vector3(0f, 0.55f, 0f);
        Vector3 b = new Vector3(-0.3f, -0.45f, 0f);
        Vector3 c = new Vector3(0.4f, -0.3f, 0f);

        s_shardMesh = new Mesh { name = "GlassShard" };
        s_shardMesh.vertices  = new[] { a, b, c, a, b, c };
        s_shardMesh.uv        = new[] { new Vector2(0.5f, 1f), Vector2.zero, Vector2.right, new Vector2(0.5f, 1f), Vector2.zero, Vector2.right };
        s_shardMesh.triangles = new[] { 0, 2, 1, 3, 4, 5 };
        s_shardMesh.RecalculateNormals();
        s_shardMesh.RecalculateBounds();
        return s_shardMesh;
    }
}
