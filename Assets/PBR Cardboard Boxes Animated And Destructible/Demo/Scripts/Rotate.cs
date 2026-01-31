using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rotate : MonoBehaviour
{
    public float speedRotate = 3.0f;
    public bool rotate = false;

    public void SetRotate( bool rotate)
    {
        this.rotate = rotate;
    }

    void Update()
    {
        if (rotate == true)
        {
            if (Time.timeScale != 0.0f)
            {
                transform.Rotate(Vector3.up * speedRotate);
            }
        }
    }
}
