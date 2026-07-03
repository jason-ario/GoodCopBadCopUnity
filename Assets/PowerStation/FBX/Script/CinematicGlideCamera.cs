using UnityEngine;
using UnityEngine.InputSystem; // Required for the new Input System
using System.Collections.Generic;

public class CinematicGlideCamera : MonoBehaviour
{
    [Header("Path Settings")]
    public Transform pathParent;
    public float moveSpeed = 2.0f;
    public float rotationSpeed = 1.5f;

    [Header("Playback")]
    public bool loopPath = false;
    
    [Header("Input Settings")]
    [Tooltip("Assign your Interact InputActionReference here.")]
    public InputActionReference glideStartAction;

    private List<Transform> waypoints = new List<Transform>();
    private int currentIndex = 0;
    private bool isMoving = false;

    // We must enable and subscribe to the input action when this script turns on
    private void OnEnable()
    {
        if (glideStartAction != null)
        {
            glideStartAction.action.Enable();
            // .performed fires when the button is pressed (or when a Hold is completed)
            glideStartAction.action.performed += StartGlide;
        }
    }

    // We must unsubscribe when the script turns off to prevent memory leaks
    private void OnDisable()
    {
        if (glideStartAction != null)
        {
            glideStartAction.action.performed -= StartGlide;
            glideStartAction.action.Disable();
        }
    }

private void Start()
    {
        if (pathParent == null)
        {
            Debug.LogWarning("No Path Parent assigned to the camera!");
            return;
        }

        foreach (Transform child in pathParent)
        {
            waypoints.Add(child);
        }

        // Add this line to see exactly what Unity is reading!
        Debug.Log($"Glide Camera initialized. Total waypoints found inside parent: {waypoints.Count}");

        if (waypoints.Count > 0)
        {
            transform.position = waypoints[0].position;
            transform.rotation = waypoints[0].rotation;
            currentIndex = 1;
        }
    }

    // This is the custom method called by the Input System when the action is performed
    private void StartGlide(InputAction.CallbackContext context)
    {
        if (!isMoving)
        {
            isMoving = true;
            Debug.Log("Cinematic camera glide started via Input System!");
        }
    }

    private void LateUpdate()
    {
        if (!isMoving || waypoints.Count == 0 || currentIndex >= waypoints.Count) return;

        Transform target = waypoints[currentIndex];

        transform.position = Vector3.MoveTowards(transform.position, target.position, moveSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, target.rotation, rotationSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, target.position) < 0.1f)
        {
            currentIndex++;

            if (currentIndex >= waypoints.Count && loopPath)
            {
                currentIndex = 0; 
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (pathParent == null) return;

        Transform[] visualPoints = pathParent.GetComponentsInChildren<Transform>();
        
        if (visualPoints.Length > 2)
        {
            Gizmos.color = Color.cyan;
            for (int i = 1; i < visualPoints.Length - 1; i++)
            {
                Gizmos.DrawLine(visualPoints[i].position, visualPoints[i + 1].position);
            }

            if (loopPath)
            {
                Gizmos.DrawLine(visualPoints[visualPoints.Length - 1].position, visualPoints[1].position);
            }
        }
    }
}