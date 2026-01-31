using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DemoControl : MonoBehaviour
{
    public float time;

    public GameObject ForDeleteCardboardBox_20x10x7_Animated;
    public GameObject ForDeleteCardboardBox_20x20x10_Animated;
    public GameObject ForDeleteCardboardBox_20x20x20_Animated;
    public GameObject ForDeleteCardboardBox_30x20x20_Animated;

    public GameObject StuffCan;
    public GameObject StuffAmmunition;
    public GameObject StuffJar;
    public GameObject StuffMoney;

    public GameObject CardboardBox_20x10x7_Animated;
    public GameObject CardboardBox_20x20x10_Animated;
    public GameObject CardboardBox_20x20x20_Animated;
    public GameObject CardboardBox_30x20x20_Animated;

    public GameObject CardboardBox_20x10x7_Fragmented;
    public GameObject CardboardBox_20x20x10_Fragmented;
    public GameObject CardboardBox_20x20x20_Fragmented;
    public GameObject CardboardBox_30x20x20_Fragmented;

    private bool instantiate = false;
    private bool instantiate1 = false;


    private GameObject Obj_StuffCan;
    private GameObject Obj_StuffAmmunition;
    private GameObject Obj_StuffJar;
    private GameObject Obj_StuffMoney;

    private GameObject Obj_CardboardBox_20x10x7_Animated;
    private GameObject Obj_CardboardBox_20x20x10_Animated;
    private GameObject Obj_CardboardBox_20x20x20_Animated;
    private GameObject Obj_CardboardBox_30x20x20_Animated;

    private GameObject Obj_CardboardBox_20x10x7_Fragmented;
    private GameObject Obj_CardboardBox_20x20x10_Fragmented;
    private GameObject Obj_CardboardBox_20x20x20_Fragmented;
    private GameObject Obj_CardboardBox_30x20x20_Fragmented;

    void Start()
    {
        Destroy(ForDeleteCardboardBox_20x10x7_Animated);
        Destroy(ForDeleteCardboardBox_20x20x10_Animated);
        Destroy(ForDeleteCardboardBox_20x20x20_Animated);
        Destroy(ForDeleteCardboardBox_30x20x20_Animated);
    }

    void Update()
    {
        time += Time.deltaTime;

        if (time >= 0.0f && time <= 0.1f)
        {
            
            StuffCan.GetComponent<Transform>().rotation = Quaternion.Euler(0, 0, 0);
            StuffAmmunition.GetComponent<Transform>().rotation = Quaternion.Euler(0, 0, 0);
            StuffJar.GetComponent<Transform>().rotation = Quaternion.Euler(0, 0, 0);
            StuffMoney.GetComponent<Transform>().rotation = Quaternion.Euler(0, 0, 0);

            if (instantiate1 == false)
            {
                Destroy(Obj_CardboardBox_20x10x7_Fragmented);
                Destroy(Obj_CardboardBox_20x20x10_Fragmented);
                Destroy(Obj_CardboardBox_20x20x20_Fragmented);
                Destroy(Obj_CardboardBox_30x20x20_Fragmented);

                Destroy(Obj_StuffCan);
                Destroy(Obj_StuffAmmunition);
                Destroy(Obj_StuffJar);
                Destroy(Obj_StuffMoney);

                Obj_CardboardBox_20x10x7_Animated = Instantiate(CardboardBox_20x10x7_Animated) as GameObject;
                Obj_CardboardBox_20x20x10_Animated = Instantiate(CardboardBox_20x20x10_Animated) as GameObject;
                Obj_CardboardBox_20x20x20_Animated = Instantiate(CardboardBox_20x20x20_Animated) as GameObject;
                Obj_CardboardBox_30x20x20_Animated = Instantiate(CardboardBox_30x20x20_Animated) as GameObject;

                Obj_StuffCan = Instantiate(StuffCan) as GameObject;
                Obj_StuffAmmunition = Instantiate(StuffAmmunition) as GameObject;
                Obj_StuffJar = Instantiate(StuffJar) as GameObject;
                Obj_StuffMoney = Instantiate(StuffMoney) as GameObject;

                instantiate1 = true;
            }

        }
        if (time >= 1.0f && time <= 1.1f)
        {
            Obj_CardboardBox_20x10x7_Animated.GetComponent<Animation>().Play("Open");
            Obj_CardboardBox_20x20x10_Animated.GetComponent<Animation>().Play("Open");
            Obj_CardboardBox_20x20x20_Animated.GetComponent<Animation>().Play("Open");
            Obj_CardboardBox_30x20x20_Animated.GetComponent<Animation>().Play("Open");
        }
        if (time >= 1.4f && time <= 1.5f)
        {
            Obj_StuffCan.GetComponent<Move>().MoveUp();
            Obj_StuffAmmunition.GetComponent<Move>().MoveUp();
            Obj_StuffJar.GetComponent<Move>().MoveUp();
            Obj_StuffMoney.GetComponent<Move>().MoveUp();
        }
        if (time >= 1.8f && time <= 1.9f)
        {
            Obj_StuffCan.GetComponent<Rotate>().SetRotate(true);
            Obj_StuffAmmunition.GetComponent<Rotate>().SetRotate(true);
            Obj_StuffJar.GetComponent<Rotate>().SetRotate(true);
            Obj_StuffMoney.GetComponent<Rotate>().SetRotate(true);
        }
        if (time >= 2.8f && time <= 2.9f)
        {
            Obj_StuffCan.GetComponent<Rotate>().SetRotate(false);
            Obj_StuffCan.GetComponent<Move>().MoveDown();
            Obj_StuffAmmunition.GetComponent<Rotate>().SetRotate(false);
            Obj_StuffAmmunition.GetComponent<Move>().MoveDown();
            Obj_StuffJar.GetComponent<Rotate>().SetRotate(false);
            Obj_StuffJar.GetComponent<Move>().MoveDown();
            Obj_StuffMoney.GetComponent<Rotate>().SetRotate(false);
            Obj_StuffMoney.GetComponent<Move>().MoveDown();
            Obj_CardboardBox_20x10x7_Animated.GetComponent<Animation>().Play("Close");
            Obj_CardboardBox_20x20x10_Animated.GetComponent<Animation>().Play("Close");
            Obj_CardboardBox_20x20x20_Animated.GetComponent<Animation>().Play("Close");
            Obj_CardboardBox_30x20x20_Animated.GetComponent<Animation>().Play("Close");
        }
        if (time >= 5.0f && time <= 5.1f)
        {
            if (instantiate == false)
            {
                Destroy(Obj_CardboardBox_20x10x7_Animated);
                Destroy(Obj_CardboardBox_20x20x10_Animated);
                Destroy(Obj_CardboardBox_20x20x20_Animated);
                Destroy(Obj_CardboardBox_30x20x20_Animated);

                Obj_CardboardBox_20x10x7_Fragmented = Instantiate(CardboardBox_20x10x7_Fragmented) as GameObject;
                Obj_CardboardBox_20x20x10_Fragmented = Instantiate(CardboardBox_20x20x10_Fragmented) as GameObject;
                Obj_CardboardBox_20x20x20_Fragmented = Instantiate(CardboardBox_20x20x20_Fragmented) as GameObject;
                Obj_CardboardBox_30x20x20_Fragmented = Instantiate(CardboardBox_30x20x20_Fragmented) as GameObject;
                instantiate = true;
            }
        }
        if (time >= 8.0f && time <= 8.1f)
        {
            time = 0.0f;
            instantiate1 = false;
            instantiate = false;
        }
    }
}
