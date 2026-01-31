using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Move : MonoBehaviour
{
    public float bias = 0.2f;
    public float speed = 0.5f;
    public string state = "down";

    public Vector3 newPosition;


    private void Start()
    {
        newPosition = transform.position;
    }

    public void MoveUp()
    {
        if (state == "down")
        {
            newPosition = transform.position;
            newPosition.y += bias;
            this.state = "up";
        }
    }

    public void MoveDown()
    {
        if (state == "up")
        {
            newPosition = transform.position;
            newPosition.y -= bias;
            this.state = "down";
        }
    }

    void Update()
    {
        float step = speed * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, newPosition, step);
    }
}
