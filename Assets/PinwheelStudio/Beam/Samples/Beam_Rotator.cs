using UnityEngine;

namespace Pinwheel.Beam.Demo
{
    [ExecuteInEditMode]
    public class Beam_Rotator : MonoBehaviour
    {
        public Vector3 speed;

        void Update()
        {
            transform.Rotate(speed * Time.deltaTime, Space.Self);
        }
    }
}