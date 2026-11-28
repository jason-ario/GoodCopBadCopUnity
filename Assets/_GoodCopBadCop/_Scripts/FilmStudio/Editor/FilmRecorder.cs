using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Records a PlayableDirector-driven film to MP4 via Unity Recorder.
    /// Record() stores a pending request in SessionState and enters play mode; recording starts on
    /// EnteredPlayMode, stops when the director finishes (or times out), then exits play mode and
    /// raises RecordingFinished once back in edit mode. SessionState survives domain reloads.
    /// </summary>
    [InitializeOnLoad]
    public static class FilmRecorder
    {
        private const string KeyPending = "FilmStudio.Recorder.Pending";
        private const string KeyStarted = "FilmStudio.Recorder.Started";
        private const string KeyDirectorPath = "FilmStudio.Recorder.DirectorPath";
        private const string KeyDuration = "FilmStudio.Recorder.Duration";
        private const string KeyBaseName = "FilmStudio.Recorder.BaseName";
        private const string KeyOutputDir = "FilmStudio.Recorder.OutputDir";
        private const string KeyLastOutput = "FilmStudio.Recorder.LastOutputPath";
        private const string KeyFinishPending = "FilmStudio.Recorder.FinishPending";
        private const string KeyFinishPath = "FilmStudio.Recorder.FinishPath";

        private static RecorderController s_Controller;
        private static MovieRecorderSettings s_MovieSettings;
        private static PlayableDirector s_Director;
        private static float s_Duration;
        private static float s_FrameTime;
        private static double s_StartRealtime;
        private static bool s_Polling;

        public static event Action<string> RecordingFinished;

        public static bool IsRecording =>
            SessionState.GetBool(KeyPending, false) || (s_Controller != null && s_Controller.IsRecording());

        public static string LastOutputPath => SessionState.GetString(KeyLastOutput, "");

        static FilmRecorder()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Fallbacks in case the play mode event was dispatched before this domain subscribed.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying)
                {
                    TryStartRecording();
                }
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    if (SessionState.GetBool(KeyPending, false) && !SessionState.GetBool(KeyStarted, false))
                    {
                        // Stale request (e.g. play mode entry failed); drop it.
                        ClearPending();
                    }
                    ProcessFinish();
                }
            };
        }

        public static void Record(string directorGameObjectPath, float durationSec, string outputBaseName)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[FilmStudio] Cannot start recording: editor is already in (or entering) play mode.");
                return;
            }
            if (IsRecording)
            {
                Debug.LogError("[FilmStudio] Cannot start recording: a recording is already in progress.");
                return;
            }
            if (string.IsNullOrWhiteSpace(directorGameObjectPath))
            {
                Debug.LogError("[FilmStudio] Cannot start recording: director GameObject path is empty.");
                return;
            }
            if (!(durationSec > 0f))
            {
                Debug.LogError($"[FilmStudio] Cannot start recording: duration must be > 0 (got {durationSec}).");
                return;
            }
            // Recordings always run in Film Mode: Main.unity environment + overlay, gameplay suppressed.
            if (!StudioSceneUtility.EnterFilmMode(out _))
            {
                Debug.LogError("[FilmStudio] Cannot start recording: Film Mode could not be entered.");
                return;
            }

            var settings = FilmStudioSettings.instance;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDir = Path.GetFullPath(Path.Combine(projectRoot, settings.recordingsFolder));
            Directory.CreateDirectory(outputDir);

            string baseName = SanitizeFileName(string.IsNullOrWhiteSpace(outputBaseName) ? "film" : outputBaseName);

            SessionState.SetString(KeyDirectorPath, directorGameObjectPath.Trim());
            SessionState.SetFloat(KeyDuration, durationSec);
            SessionState.SetString(KeyBaseName, baseName);
            SessionState.SetString(KeyOutputDir, outputDir);
            SessionState.SetBool(KeyStarted, false);
            SessionState.SetBool(KeyFinishPending, false);
            SessionState.EraseString(KeyFinishPath);
            SessionState.SetBool(KeyPending, true);

            EditorApplication.EnterPlaymode();
        }

        public static void Cancel()
        {
            if (s_Controller != null)
            {
                StopAndExit();
                return;
            }

            if (SessionState.GetBool(KeyPending, false))
            {
                ClearPending();
                MarkFinish(null);
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                }
                else
                {
                    ProcessFinish();
                }
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    TryStartRecording();
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    // User exited manually (or we did): make sure the recorder is stopped cleanly.
                    if (s_Controller != null)
                    {
                        StopRecordingInternal();
                    }
                    else if (SessionState.GetBool(KeyPending, false))
                    {
                        ClearPending();
                        MarkFinish(null);
                    }
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    if (SessionState.GetBool(KeyPending, false))
                    {
                        ClearPending();
                        if (!SessionState.GetBool(KeyFinishPending, false))
                        {
                            MarkFinish(null);
                        }
                    }
                    ProcessFinish();
                    break;
            }
        }

        private static void TryStartRecording()
        {
            if (!EditorApplication.isPlaying) return;
            if (!SessionState.GetBool(KeyPending, false)) return;
            if (SessionState.GetBool(KeyStarted, false) || s_Controller != null) return;
            SessionState.SetBool(KeyStarted, true);

            string directorPath = SessionState.GetString(KeyDirectorPath, "");
            float duration = SessionState.GetFloat(KeyDuration, 0f);
            string baseName = SessionState.GetString(KeyBaseName, "film");
            string outputDir = SessionState.GetString(KeyOutputDir, "");

            GameObject go = FindGameObjectByPath(directorPath);
            PlayableDirector director = go != null ? go.GetComponent<PlayableDirector>() : null;
            if (director == null)
            {
                Debug.LogError(go == null
                    ? $"[FilmStudio] Recording aborted: GameObject '{directorPath}' not found in the loaded scenes."
                    : $"[FilmStudio] Recording aborted: GameObject '{directorPath}' has no PlayableDirector.");
                AbortAndExit();
                return;
            }

            try
            {
                if (!go.activeInHierarchy)
                {
                    go.SetActive(true);
                    for (Transform t = go.transform.parent; t != null; t = t.parent)
                    {
                        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    }
                }

                var settings = FilmStudioSettings.instance;
                int fps = Mathf.Max(1, settings.fps);
                Directory.CreateDirectory(outputDir);

                var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movie.name = "FilmStudio Movie Recorder";
                movie.Enabled = true;
                movie.EncoderSettings = new CoreEncoderSettings
                {
                    Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High
                };
                movie.ImageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = settings.outputWidth,
                    OutputHeight = settings.outputHeight
                };
                movie.CaptureAudio = true;
                movie.OutputFile = Path.Combine(outputDir, baseName).Replace('\\', '/');

                controllerSettings.AddRecorderSettings(movie);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
                controllerSettings.FrameRate = fps;
                controllerSettings.CapFrameRate = true;

                RecorderOptions.VerboseMode = false;

                s_Controller = new RecorderController(controllerSettings);
                s_MovieSettings = movie;
                s_Director = director;
                s_Duration = duration;
                s_FrameTime = 1f / fps;

                s_Controller.PrepareRecording();
                if (!s_Controller.StartRecording())
                {
                    Debug.LogError("[FilmStudio] Recording aborted: Unity Recorder failed to start.");
                    s_Controller = null;
                    s_MovieSettings = null;
                    s_Director = null;
                    AbortAndExit();
                    return;
                }

                director.time = 0;
                director.Play();

                s_StartRealtime = EditorApplication.timeSinceStartup;
                if (!s_Polling)
                {
                    EditorApplication.update += Poll;
                    s_Polling = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FilmStudio] Recording aborted: {e}");
                if (s_Controller != null)
                {
                    try { s_Controller.StopRecording(); } catch { /* ignore */ }
                }
                s_Controller = null;
                s_MovieSettings = null;
                s_Director = null;
                AbortAndExit();
            }
        }

        private static void Poll()
        {
            if (s_Controller == null)
            {
                StopPolling();
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - s_StartRealtime;
            bool done;
            if (s_Director == null)
            {
                done = true;
            }
            else
            {
                done = s_Director.time >= s_Duration - s_FrameTime
                       || (elapsed >= 0.2 && s_Director.state != PlayState.Playing);
            }

            if (!done && elapsed > s_Duration * 3f + 10f)
            {
                Debug.LogWarning("[FilmStudio] Recording safety timeout reached; stopping.");
                done = true;
            }

            if (done)
            {
                StopAndExit();
            }
        }

        private static void StopAndExit()
        {
            StopRecordingInternal();
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
            else
            {
                ProcessFinish();
            }
        }

        private static void StopRecordingInternal()
        {
            StopPolling();

            string outputPath = null;
            if (s_Controller != null)
            {
                try
                {
                    s_Controller.StopRecording();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FilmStudio] Error while stopping recorder: {e}");
                }

                if (s_MovieSettings != null)
                {
                    outputPath = Path.GetFullPath(s_MovieSettings.OutputFile + ".mp4");
                    SessionState.SetString(KeyLastOutput, outputPath);
                }
            }

            s_Controller = null;
            s_MovieSettings = null;
            s_Director = null;
            Time.timeScale = 1f;

            ClearPending();
            MarkFinish(outputPath);
        }

        private static void AbortAndExit()
        {
            StopPolling();
            Time.timeScale = 1f;
            ClearPending();
            MarkFinish(null);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
        }

        private static void StopPolling()
        {
            if (s_Polling)
            {
                EditorApplication.update -= Poll;
                s_Polling = false;
            }
        }

        private static void ClearPending()
        {
            SessionState.EraseBool(KeyPending);
            SessionState.EraseBool(KeyStarted);
            SessionState.EraseString(KeyDirectorPath);
            SessionState.EraseFloat(KeyDuration);
            SessionState.EraseString(KeyBaseName);
            SessionState.EraseString(KeyOutputDir);
        }

        private static void MarkFinish(string path)
        {
            SessionState.SetBool(KeyFinishPending, true);
            SessionState.SetString(KeyFinishPath, path ?? "");
        }

        private static void ProcessFinish()
        {
            if (!SessionState.GetBool(KeyFinishPending, false)) return;
            string path = SessionState.GetString(KeyFinishPath, "");
            SessionState.EraseBool(KeyFinishPending);
            SessionState.EraseString(KeyFinishPath);
            Time.timeScale = 1f;

            if (string.IsNullOrEmpty(path))
            {
                path = null;
            }
            else if (File.Exists(path))
            {
                Debug.Log($"[FilmStudio] Recorded {path}");
                EditorUtility.RevealInFinder(path);
            }
            else
            {
                Debug.LogWarning($"[FilmStudio] Recording stopped but output file was not found: {path}");
            }

            try
            {
                RecordingFinished?.Invoke(path);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static GameObject FindGameObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string[] parts = path.Trim('/').Split('/');
            if (parts.Length == 0) return null;

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name != parts[0]) continue;
                    Transform current = root.transform;
                    for (int i = 1; i < parts.Length && current != null; i++)
                    {
                        current = FindChild(current, parts[i]);
                    }
                    if (current != null) return current.gameObject;
                }
            }
            return null;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name) return child;
            }
            return null;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Replace('<', '_').Replace('>', '_').Trim();
        }
    }
}
