using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using LLMUnity;
using TMPro;
using UnityEngine.UI;
using NotImplementedException = System.NotImplementedException;

namespace LLMUnitySamples
{
    public class SuspectChatBot : MonoBehaviour
    {
        public Color playerColor = new Color32(81, 164, 81, 255);
        public Color aiColor = new Color32(29, 29, 73, 255);
        public Color fontColor = Color.white;
        public Font font;
        public int fontSize = 16;
        public int bubbleWidth = 600;
        public LLMCharacter llmCharacter;
        public float textPadding = 10f;
        public float bubbleSpacing = 10f;
        public Sprite sprite;

        private List<Bubble> chatBubbles = new List<Bubble>();
        private bool blockInput = true;
        private BubbleUI playerUI, aiUI;
        private bool warmUpDone = false;
        private int lastBubbleOutsideFOV = -1;
        [SerializeField] private AudioClip[] voiceSounds;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private SubtitlesController subtitles;

        [Header("UI (set in Inspector)")]
        [SerializeField] private TMP_InputField inputFieldTMP;
        [SerializeField] float timeToClearChat = 1f;

        void Start()
        {
            subtitles.SetText("");

            if (inputFieldTMP == null)
            {
                Debug.LogError($"{nameof(SuspectChatBot)}: Please assign {nameof(inputFieldTMP)} in the Inspector.");
                enabled = false;
                return;
            }

            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            playerUI = new BubbleUI
            {
                sprite = sprite,
                font = font,
                fontSize = fontSize,
                fontColor = fontColor,
                bubbleColor = playerColor,
                bottomPosition = 0,
                leftPosition = 0,
                textPadding = textPadding,
                bubbleOffset = bubbleSpacing,
                bubbleWidth = bubbleWidth,
                bubbleHeight = -1
            };
            aiUI = playerUI;
            aiUI.bubbleColor = aiColor;
            aiUI.leftPosition = 1;

            inputFieldTMP.onSubmit.AddListener(onInputFieldSubmit);
            inputFieldTMP.onValueChanged.AddListener(onValueChanged);
            inputFieldTMP.interactable = false;
            inputFieldTMP.text = "";
            SetInputPlaceholder("Loading...");
            _ = llmCharacter.Warmup(WarmUpCallback);
        }

        void OnDestroy()
        {
            if (inputFieldTMP != null)
            {
                inputFieldTMP.onSubmit.RemoveListener(onInputFieldSubmit);
                inputFieldTMP.onValueChanged.RemoveListener(onValueChanged);
            }
        }

        private void OnDisable()
        {
            inputFieldTMP.text = "";
        }

        void onInputFieldSubmit(string newText)
        {
            inputFieldTMP.ActivateInputField();

            if (blockInput || newText.Trim() == "" || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                StartCoroutine(BlockInteraction());
                return;
            }

            blockInput = true;

            // replace vertical_tab
            string message = inputFieldTMP.text.Replace("\v", "\n");

            Task chatTask = llmCharacter.Chat(message, subtitles.SetText, AllowInput);
            audioSource.PlayOneShot(voiceSounds[Random.Range(0, voiceSounds.Length)]);
            inputFieldTMP.text = "";
        }

        public void WarmUpCallback()
        {
            warmUpDone = true;
            SetInputPlaceholder("Type a message and press Enter to send");
            AllowInput();
        }

        public void AllowInput()
        {
            blockInput = false;
            inputFieldTMP.interactable = true;
            inputFieldTMP.ActivateInputField();
            inputFieldTMP.Select();
            StartCoroutine(WaitAndClearChat());
        }

        IEnumerator WaitAndClearChat()
        {
            yield return new WaitForSeconds(timeToClearChat);
            if (blockInput == false)
            {
                subtitles.SetText("");
            }
        }

        public void CancelRequests()
        {
            llmCharacter.CancelRequests();
            AllowInput();
        }

        IEnumerator<string> BlockInteraction()
        {
            // prevent from change until next frame
            inputFieldTMP.interactable = false;
            yield return null;
            inputFieldTMP.interactable = true;

            // change the caret position to the end of the text
            inputFieldTMP.MoveTextEnd(false);
        }

        void onValueChanged(string newText)
        {
            // Get rid of newline character added when we press enter
            if (Input.GetKey(KeyCode.Return))
            {
                if (inputFieldTMP.text.Trim() == "")
                {
                    inputFieldTMP.text = "";
                }
                
                UIController.Instance.ToggleChatUI();
            }
        }

        void Update()
        {
            if (inputFieldTMP == null) return;

            if (!inputFieldTMP.isFocused && warmUpDone)
            {
                inputFieldTMP.ActivateInputField();
                StartCoroutine(BlockInteraction());
            }

            if (lastBubbleOutsideFOV != -1)
            {
                // destroy bubbles outside the container
                for (int i = 0; i <= lastBubbleOutsideFOV; i++)
                {
                    chatBubbles[i].Destroy();
                }
                chatBubbles.RemoveRange(0, lastBubbleOutsideFOV + 1);
                lastBubbleOutsideFOV = -1;
            }
        }

        public void ExitGame()
        {
            Debug.Log("Exit button clicked");
            Application.Quit();
        }

        bool onValidateWarning = true;
        void OnValidate()
        {
            if (onValidateWarning && !llmCharacter.remote && llmCharacter.llm != null && llmCharacter.llm.model == "")
            {
                Debug.LogWarning($"Please select a model in the {llmCharacter.llm.gameObject.name} GameObject!");
                onValidateWarning = false;
            }
        }

        private void SetInputPlaceholder(string text)
        {
            if (inputFieldTMP.placeholder is TMP_Text tmpPlaceholder)
            {
                tmpPlaceholder.text = text;
            }
            else if (inputFieldTMP.placeholder != null)
            {
                var legacyText = inputFieldTMP.placeholder.GetComponent<UnityEngine.UI.Text>();
                if (legacyText != null) legacyText.text = text;
            }
        }
    }
}
