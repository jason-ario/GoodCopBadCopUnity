using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SC_Vefects_Vignette_Screen_Size_Auto_Scale : MonoBehaviour
{

    private Vector2 screenResolution;
    public float scaleCompensation = 10.0f;

    void Start()
    {
        screenResolution = new Vector2(Screen.width, Screen.height);
        MatchScaleToScreenSize();
    }

    void Update()
    {
        //check if resolution x and y are what they should be, and if not, scale the plane
        if (screenResolution.x != Screen.width || screenResolution.y != Screen.height)
        {
            MatchScaleToScreenSize();
            screenResolution.x = Screen.width;
            screenResolution.y = Screen.height;
        }

    }
    
    // scale the plane to match the screen size
    private void MatchScaleToScreenSize()
    {
        float planeToCameraDistance = Vector3.Distance(gameObject.transform.position, Camera.main.transform.position);
        float planeHeightScale = (2.0f * Mathf.Tan(0.5f * Camera.main.fieldOfView * Mathf.Deg2Rad) * planeToCameraDistance) * scaleCompensation;
        float planeWidthScale = planeHeightScale * Camera.main.aspect;
        gameObject.transform.localScale = new Vector3(planeWidthScale, planeHeightScale, 1);
    }


}
