using System;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkDrawableLine : NetworkBehaviour
{
    [Header("Drawing")]
    public Camera drawCamera;

    [Tooltip("THIS is the surface you draw on. Assign the collider directly.")]
    [SerializeField] private Collider drawingSurface;

    public float minPointDistance = 0.02f;
    public float sendInterval = 0.06f;
    public float lineWidth = 0.02f;
    public Color32 lineColor = new Color32(0, 0, 0, 255);
    private PlayerPickupController _playerPickupController;

    [Header("Line Renderer")]
    public Material lineMaterial;
    public int lineCornerVertices = 4;
    public int lineCapVertices = 4;

    [Header("History")]
    public int maxEventsKept = 20000;

    private enum StrokeEventType : byte { Start, Point, End }
    

    private struct StrokeEvent :
        INetworkSerializable,
        IEquatable<StrokeEvent>
    {
        public uint strokeId;
        public StrokeEventType type;
        public Vector3 localPosition; // LOCAL to drawingSurface
        public Color32 color;
        public float width;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref strokeId);

            byte t = (byte)type;
            serializer.SerializeValue(ref t);
            type = (StrokeEventType)t;

            serializer.SerializeValue(ref localPosition);
            serializer.SerializeValue(ref color);
            serializer.SerializeValue(ref width);
        }

        public bool Equals(StrokeEvent other)
        {
            return strokeId == other.strokeId &&
                   type == other.type &&
                   localPosition == other.localPosition &&
                   color.Equals(other.color) &&
                   Mathf.Approximately(width, other.width);
        }

        public override bool Equals(object obj)
        {
            return obj is StrokeEvent other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = strokeId.GetHashCode();
                hash = (hash * 397) ^ type.GetHashCode();
                hash = (hash * 397) ^ localPosition.GetHashCode();
                hash = (hash * 397) ^ color.GetHashCode();
                hash = (hash * 397) ^ width.GetHashCode();
                return hash;
            }
        }
    }

    private NetworkList<StrokeEvent> _events;

    private readonly Dictionary<uint, LineRenderer> _lines = new();
    private readonly Dictionary<uint, List<Vector3>> _points = new();

    private uint _currentStrokeId;
    private bool _isDrawing;
    private bool _hasClickedToStart;
    private float _sendTimer;

    private readonly List<Vector3> _pendingPoints = new();
    private Vector3 _lastQueuedPoint;
    private bool _hasLastQueuedPoint;

    [SerializeField] private GameObject virtualCamera;
    [SerializeField] private Transform ikTarget;
    [SerializeField] private Vector3 ikOffset;

    private void Awake()
    {
        _events = new NetworkList<StrokeEvent>(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server
        );
        
        drawCamera = Camera.main;
    }

    public override void OnNetworkSpawn()
    {
        if (drawingSurface == null)
        {
            Debug.LogError("NetworkDrawableLine: drawingSurface is NOT assigned.");
            enabled = false;
            return;
        }

        _events.OnListChanged += OnEventsChanged;
        RebuildFromHistory();
    }

    public override void OnNetworkDespawn()
    {
        _events.OnListChanged -= OnEventsChanged;
        Cleanup();
    }


    
    public void EnterDrawMode(PlayerPickupController playerPickupController)
    {
        enabled = true;
        _hasClickedToStart = false;
        drawingSurface.enabled = true;
        
        _playerPickupController = playerPickupController;
        PlayerMovementController playerMovementController = _playerPickupController.PlayerMovementController;
        playerMovementController.LookAtTarget(transform);
        playerMovementController.CameraTransform.DOMove(virtualCamera.transform.position, .5f); 
        playerMovementController.CameraTransform.DORotate(virtualCamera.transform.rotation.eulerAngles, .5f);

        _playerPickupController.PlayerAnimationController.SetArmRigWeightSmooth(1,1);
        _playerPickupController.PlayerAnimationController.ArmIKTarget.DOMove(ikTarget.position, .25f);
        _playerPickupController.PlayerAnimationController.ArmIKTarget.DORotate(ikTarget.rotation.eulerAngles, .25f);
    }

    void SetIKTargetPos(Vector3 pos)
    {
        ikTarget.localPosition = pos + ikOffset;
        _playerPickupController.PlayerAnimationController.ArmIKTarget.transform.position = ikTarget.position;
        _playerPickupController.PlayerAnimationController.ArmIKTarget.transform.rotation = ikTarget.rotation;
    }

    public void ExitDrawMode()
    {
        drawingSurface.enabled = false;
        _playerPickupController.PlayerMovementController.ResetCameraPos(false, .5f);
        _playerPickupController.PlayerAnimationController.SetArmRigWeightSmooth(0,1);
        
        enabled = false;
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (drawCamera == null) drawCamera = Camera.main;
        
        if (Input.GetMouseButtonDown(0) && TryGetLocalPoint(out var p))
            BeginStroke(p);

        if (_isDrawing && Input.GetMouseButton(0) && TryGetLocalPoint(out p))
            AddPoint(p);

        if (_isDrawing && Input.GetMouseButtonUp(0))
            EndStroke();

        if (_isDrawing)
        {
            _sendTimer += Time.deltaTime;
            if (_sendTimer >= sendInterval)
            {
                _sendTimer = 0f;
                FlushPoints();
            }
        }
    }
    

    // =========================================================
    // Input → surface-local space
    // =========================================================

    private bool TryGetLocalPoint(out Vector3 localPoint)
    {
        localPoint = default;

        Ray ray = drawCamera.ScreenPointToRay(Input.mousePosition);

        if (!drawingSurface.Raycast(ray, out RaycastHit hit, 500f))
            return false;

        // Convert world → surface LOCAL
        localPoint = drawingSurface.transform.InverseTransformPoint(hit.point);

        // Optional tiny offset to avoid z-fighting
        //localPoint += Vector3.forward * 0.0005f;

        return true;
    }

    // =========================================================
    // Local drawing
    // =========================================================

    private void BeginStroke(Vector3 localPoint)
    {
        _isDrawing = true;
        _sendTimer = 0f;
        _pendingPoints.Clear();
        _hasLastQueuedPoint = false;

        _currentStrokeId =
            ((uint)OwnerClientId << 16) |
            (uint)UnityEngine.Random.Range(1, 65535);

        EnsureLine(_currentStrokeId, lineColor, lineWidth);
        AddPointLocal(_currentStrokeId, localPoint);

        SubmitStrokeStartServerRpc(
            _currentStrokeId, lineColor, lineWidth, localPoint);

        QueuePoint(localPoint);
        FlushPoints();
    }

    private void AddPoint(Vector3 localPoint)
    {
        var last = GetLastPoint(_currentStrokeId);
        if ((localPoint - last).sqrMagnitude <
            minPointDistance * minPointDistance)
            return;

        AddPointLocal(_currentStrokeId, localPoint);
        SetIKTargetPos(localPoint);    
        QueuePoint(localPoint);
    }

    private void EndStroke()
    {
        FlushPoints();
        SubmitStrokeEndServerRpc(_currentStrokeId);
        _isDrawing = false;
        _pendingPoints.Clear();
        _hasLastQueuedPoint = false;
    }

    private void QueuePoint(Vector3 p)
    {
        if (_hasLastQueuedPoint &&
            (p - _lastQueuedPoint).sqrMagnitude <
            minPointDistance * minPointDistance)
            return;

        _pendingPoints.Add(p);
        _lastQueuedPoint = p;
        _hasLastQueuedPoint = true;
    }

    private void FlushPoints()
    {
        if (_pendingPoints.Count == 0) return;
        SubmitStrokePointsServerRpc(_currentStrokeId, _pendingPoints.ToArray());
        _pendingPoints.Clear();
    }

    // =========================================================
    // Networking
    // =========================================================

    [ServerRpc]
    private void SubmitStrokeStartServerRpc(
        uint strokeId, Color32 color, float width, Vector3 localPos)
    {
        AddEvent(new StrokeEvent
        {
            strokeId = strokeId,
            type = StrokeEventType.Start,
            localPosition = localPos,
            color = color,
            width = width
        });
    }

    [ServerRpc]
    private void SubmitStrokePointsServerRpc(uint strokeId, Vector3[] localPoints)
    {
        foreach (var p in localPoints)
        {
            AddEvent(new StrokeEvent
            {
                strokeId = strokeId,
                type = StrokeEventType.Point,
                localPosition = p
            });
        }
    }

    [ServerRpc]
    private void SubmitStrokeEndServerRpc(uint strokeId)
    {
        AddEvent(new StrokeEvent
        {
            strokeId = strokeId,
            type = StrokeEventType.End
        });
    }

    private void AddEvent(StrokeEvent e)
    {
        _events.Add(e);
        if (_events.Count > maxEventsKept)
            _events.RemoveAt(0);
    }

    // =========================================================
    // Playback
    // =========================================================

    private void OnEventsChanged(NetworkListEvent<StrokeEvent> change)
    {
        if (change.Type == NetworkListEvent<StrokeEvent>.EventType.Add)
            ApplyEvent(change.Value);
        else
            RebuildFromHistory();
    }

    private void RebuildFromHistory()
    {
        Cleanup();
        foreach (var e in _events)
            ApplyEvent(e);
    }

    private void ApplyEvent(StrokeEvent e)
    {
        switch (e.type)
        {
            case StrokeEventType.Start:
                EnsureLine(e.strokeId, e.color, e.width);
                AddPointLocal(e.strokeId, e.localPosition);
                break;

            case StrokeEventType.Point:
                EnsureLine(e.strokeId, lineColor, lineWidth);
                AddPointLocal(e.strokeId, e.localPosition);
                break;
        }
    }

    // =========================================================
    // Line rendering
    // =========================================================

    private void EnsureLine(uint strokeId, Color32 color, float width)
    {
        if (_lines.ContainsKey(strokeId))
            return;

        var go = new GameObject($"Stroke_{strokeId}");
        go.transform.SetParent(drawingSurface.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        //go.transform.localPosition = Vector3.forward * 0.0005f;

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.material = lineMaterial;
        lr.widthMultiplier = width;
        lr.numCornerVertices = lineCornerVertices;
        lr.numCapVertices = lineCapVertices;
        lr.startColor = color;
        lr.endColor = color;
        lr.positionCount = 0;

        _lines[strokeId] = lr;
        _points[strokeId] = new List<Vector3>(64);
        
    }

    private void AddPointLocal(uint strokeId, Vector3 localPoint)
    {
        if (!_lines.TryGetValue(strokeId, out var lr))
            return;

        var pts = _points[strokeId];

        if (pts.Count == 0)
        {
            pts.Add(localPoint);
            lr.positionCount = 1;
            lr.SetPosition(0, localPoint);
            return;
        }

        int lastIndex = pts.Count - 1;
        Vector3 lastPoint = pts[lastIndex];

        float sqrDist = (localPoint - lastPoint).sqrMagnitude;
        float minSqr = minPointDistance * minPointDistance;

        // If points are very close → merge by averaging
        if (sqrDist < minSqr)
        {
            Vector3 averaged = Vector3.Lerp(lastPoint, localPoint, 0.6f);

            pts[lastIndex] = averaged;
            lr.SetPosition(lastIndex, averaged);
            return;
        }

        // Otherwise, add a new vertex
        pts.Add(localPoint);
        lr.positionCount = pts.Count;
        lr.SetPosition(pts.Count - 1, localPoint);
    }


    private Vector3 GetLastPoint(uint strokeId)
    {
        return _points.TryGetValue(strokeId, out var pts) && pts.Count > 0
            ? pts[^1]
            : Vector3.zero;
    }

    private void Cleanup()
    {
        foreach (var lr in _lines.Values)
            if (lr != null)
                Destroy(lr.gameObject);

        _lines.Clear();
        _points.Clear();
    }
}
