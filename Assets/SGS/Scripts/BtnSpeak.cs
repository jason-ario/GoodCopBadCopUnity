using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UnityText2Speech {
    public class BtnSpeak : MonoBehaviour {
        [System.Serializable]
        public class Text2SpeechEvent : UnityEvent<string> { }

        public Text2SpeechEvent OnText2Speech = new Text2SpeechEvent( );

        public Text InputText;
        // Start is called before the first frame update
        public void OnClick( ) {
            if ( !string.IsNullOrEmpty( InputText.text ) ) {
                OnText2Speech?.Invoke( InputText.text );
            }
        }
    }
}
