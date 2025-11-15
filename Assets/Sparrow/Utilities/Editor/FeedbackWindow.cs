// 
// Copyright (c) 2024 Off The Beaten Track UG
// All rights reserved.
// 
// Maintainer: Jens Bahr
//

#if UNITY_EDITOR

using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;  

namespace Sparrow.Utilities
{
    public class FeedbackWindow : EditorWindow
    {
        private static string k_EndpointURL = "https://api.jensbahr.com/sparrowFeedback";
        string inputText;
        string context = "";
        string version = "";

        bool shouldClose = false;

        [MenuItem("Window/Sparrow/Submit feedback")]
        public static void ShowWindow()
        {
            ShowWindow("Editor Menu", "");
        }

        public static void ShowWindow(string context, string version)
        {
            var window = CreateInstance<FeedbackWindow>();
            window.version = version + " // Unity: " + Application.unityVersion;
            window.context = context;
            window.maxSize = new Vector2(350f, 400f);
            window.minSize = window.maxSize; 
            var logo = Resources.Load<Texture>("sparrow_logo");
            window.titleContent.text = "Sparrow: Little Helpers - Feedback";
            window.titleContent.image = logo;
            window.Show();
        }

        [Serializable]
        private class FeedbackRequest
        {
            public string context;
            public string version;
            public string feedbacktext;
        }

        public static async Task SendFeedback(string context, string version, string feedbacktext)
        {
            if (string.IsNullOrEmpty(feedbacktext)) return;
            using (HttpClient client = new HttpClient())
            {
                FeedbackRequest request = new FeedbackRequest();
                request.context = string.IsNullOrEmpty(context) ? "no context provided" : context;
                request.version = string.IsNullOrEmpty(version) ? "no version provided" : version;
                request.feedbacktext = feedbacktext;

                var httpContent = new StringContent(JsonUtility.ToJson(request), Encoding.UTF8, "application/json");
                var result = await client.PostAsync(k_EndpointURL, httpContent);

                if (result.IsSuccessStatusCode)
                {
                    Debug.Log("[Sparrow Feedback] Thank you for submitting your feedback!");
                }
                else
                {
                    Debug.Log("[Sparrow Feedback] Error sending your feedback: " + result.StatusCode);
                }
            }
        }

        void OnGUI()
        {
            EditorUtils.DrawLogoHeader("Send us feedback", "sparrow_logo", feedback: false);

            EditorGUILayout.BeginVertical();
            GUIStyle style_label = new GUIStyle(EditorStyles.label);
            style_label.wordWrap = true;
            GUILayout.Label("Please type your feedback below:", style_label);

            EditorUtils.Space();
            GUIStyle style = new GUIStyle(EditorStyles.textArea);
            style.wordWrap = true;
            style.stretchHeight = true; 
            inputText = EditorGUILayout.TextArea(inputText, style);
            EditorUtils.Space();

            // Draw OK / Cancel buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Send", GUILayout.ExpandWidth(true)))
            {
                if (!string.IsNullOrEmpty(inputText))
                {
                    _ = SendFeedback(context, version, inputText);
                }
                shouldClose = true;
            }

            if (GUILayout.Button("Cancel", GUILayout.ExpandWidth(true)))
            {
                inputText = null;   
                shouldClose = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            if (shouldClose) Close();
        }
    }
}

#endif