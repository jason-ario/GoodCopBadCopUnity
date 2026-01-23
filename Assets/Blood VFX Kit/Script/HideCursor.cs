using UnityEngine;

namespace Biostart.Game
{
    public class HideCursor : MonoBehaviour
    {
        void Start()
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }
}