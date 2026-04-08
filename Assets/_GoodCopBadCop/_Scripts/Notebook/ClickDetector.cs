using UnityEngine;

public class ClickDetector : MonoBehaviour
{
    void Update()
    {
        if (Input.GetMouseButtonDown(0)) // Left click
        {
            Debug.Log("Left click detected");
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 100f, ~0, QueryTriggerInteraction.Collide))
            {
                var clickable = hit.collider.GetComponent<IClickable>();
                if (clickable != null)
                {
                    clickable.OnClick();
                }
            }
        }
    }
}