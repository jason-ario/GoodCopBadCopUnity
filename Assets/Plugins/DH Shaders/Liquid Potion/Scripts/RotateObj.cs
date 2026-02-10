using UnityEngine;

namespace DH.Shaders.LiquidPotion
{
    public class RotateObj : MonoBehaviour
    {
        [SerializeField] Vector3 axis;
        [SerializeField] float _speed = 1;
        void Update()
        {
            transform.Rotate(axis * _speed * Time.deltaTime);
        }
    }
}