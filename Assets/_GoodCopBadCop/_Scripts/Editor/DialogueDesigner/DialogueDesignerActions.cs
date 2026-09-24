using System;
using System.Collections.Generic;
using System.Linq;
using Bezi;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Custom Bezi actions backing the Dialogue Designer Proof: read suspect dialogue data,
/// save edits back to SuspectData / ScriptedDialogue assets, and render animation previews
/// from each suspect's own dialogue cameras.
/// </summary>
public static class DialogueDesignerActions
{
    const string SuspectFolder = "Assets/_GoodCopBadCop/_Data/Suspects/Suspect Datas";
    const string IntroFolder = "Assets/_GoodCopBadCop/_Data/Dialogue/Intro";
    const string FullMutantFolder = "Assets/_GoodCopBadCop/_Data/Dialogue/FullMutant";
    const string FaceCamKey = "SuspectFaceCam";

    // ---------------------------------------------------------------- DTOs

    [Serializable] public class ChoiceDTO
    {
        public string playerChoiceText;
        public string npcResponse;
        public string animationTrigger;
        public bool playLaughSfx;
    }

    [Serializable] public class NodeDTO
    {
        public int type;
        public string npcLine;
        public string animationTrigger;
        public string cameraTrigger;
        public bool playLaughSfx;
        public string wobbleProfile;
        public string fontOverride;
        public ChoiceDTO[] choices;
    }

    [Serializable] public class ScriptedDTO
    {
        public string assetPath;
        public bool isForced;
        public NodeDTO[] nodes;
    }

    [Serializable] public class AnimTriggerDTO
    {
        public string trigger;
        public string clip;
        public float length;
    }

    [Serializable] public class SuspectDTO
    {
        public string assetPath;
        public string id;
        public string firstName;
        public string lastName;
        public string nickname;
        public string occupation;
        public string sex;
        public string dateOfBirth;
        public string description;
        public string arc;
        public string alterEgoName;
        public string normalBehaviorNotes;
        public string[] basePersonalityDescriptors;
        public int startingInfectionScore;
        public string prefabPath;
        public bool hasMutatedVersion;
        public bool hasWideCam;
        public bool hasFaceCam;
        public string idPhoto;
        public SuspectData.EntryReasonSet entryReasons;
        public SuspectData.EntryReasonSet invalidEntryReasons;
        public SuspectData.DialogueByVerdict entryDialogues;
        public SuspectData.DialogueByVerdict uncannyEntryDialogues;
        public SuspectData.DialogueByVerdict exitDialoguesPassed;
        public SuspectData.DialogueByVerdict exitDialoguesQuarantined;
        public SuspectData.DialogueByVerdict exitDialoguesKilled;
        public string[] quarantineExitLines;
        public string[] killExitLines;
        public SuspectData.QuestionResponseSet[] questionResponses;
        public SuspectData.BarkSet idleBarks;
        public ScriptedDTO introDialogue;
        public ScriptedDTO fullMutantDialogue;
        public AnimTriggerDTO[] baseTriggers;
        public AnimTriggerDTO[] mutantTriggers;
    }

    [Serializable] public class DatabaseDTO
    {
        public SuspectDTO[] suspects;
        public string[] sceneCameraKeys;
        public string generatedAt;
    }

    [Serializable] public class SuspectEditDTO
    {
        public SuspectData.EntryReasonSet entryReasons;
        public SuspectData.EntryReasonSet invalidEntryReasons;
        public SuspectData.DialogueByVerdict entryDialogues;
        public SuspectData.DialogueByVerdict uncannyEntryDialogues;
        public SuspectData.DialogueByVerdict exitDialoguesPassed;
        public SuspectData.DialogueByVerdict exitDialoguesQuarantined;
        public SuspectData.DialogueByVerdict exitDialoguesKilled;
        public string[] quarantineExitLines;
        public string[] killExitLines;
        public SuspectData.QuestionResponseSet[] questionResponses;
        public SuspectData.BarkSet idleBarks;
        public string[] basePersonalityDescriptors;
        public string normalBehaviorNotes;
    }

    [Serializable] public class PreviewDTO
    {
        public string[] frames;
        public string clip;
        public float clipLength;
        public string cameraSource;
        public string note;
    }

    // ---------------------------------------------------------------- Read

    [BeziAction("Dialogue Designer: list every SuspectData asset with all dialogue text, scripted intro/full-mutant dialogue nodes, animator triggers, and dialogue camera availability. Returns JSON.", IsReadOnly = true)]
    public static string ListDialogueSuspects(bool includePhotos)
    {
        var guids = AssetDatabase.FindAssets("t:SuspectData", new[] { SuspectFolder });
        var list = new List<SuspectDTO>();
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<SuspectData>(path);
            if (data != null) list.Add(BuildSuspect(data, path, includePhotos));
        }
        list.Sort((a, b) => string.Compare(a.firstName, b.firstName, StringComparison.OrdinalIgnoreCase));
        var db = new DatabaseDTO
        {
            suspects = list.ToArray(),
            sceneCameraKeys = GetSceneCameraKeys(),
            generatedAt = DateTime.Now.ToString("s"),
        };
        return JsonUtility.ToJson(db);
    }

    [BeziAction("Dialogue Designer: get one SuspectData asset's full dialogue data as JSON.", IsReadOnly = true)]
    public static string GetDialogueSuspect(string suspectAssetPath)
    {
        var data = LoadSuspect(suspectAssetPath);
        return JsonUtility.ToJson(BuildSuspect(data, AssetDatabase.GetAssetPath(data), false));
    }

    static SuspectDTO BuildSuspect(SuspectData d, string path, bool includePhoto)
    {
        var dto = new SuspectDTO
        {
            assetPath = path,
            id = System.IO.Path.GetFileNameWithoutExtension(path),
            firstName = d.FirstName, lastName = d.LastName, nickname = d.Nickname,
            occupation = d.Occupation, sex = d.Sex, dateOfBirth = d.DateOfBirth,
            description = d.CharacterDescription, arc = d.CharacterArc,
            alterEgoName = d.alterEgoName, normalBehaviorNotes = d.normalBehaviorNotes,
            basePersonalityDescriptors = d.basePersonalityDescriptors ?? Array.Empty<string>(),
            startingInfectionScore = d.startingInfectionScore,
            entryReasons = d.entryReasons, invalidEntryReasons = d.invalidEntryReasons,
            entryDialogues = d.entryDialogues, uncannyEntryDialogues = d.uncannyEntryDialogues,
            exitDialoguesPassed = d.exitDialoguesPassed,
            exitDialoguesQuarantined = d.exitDialoguesQuarantined,
            exitDialoguesKilled = d.exitDialoguesKilled,
            quarantineExitLines = d.quarantineExitLines ?? Array.Empty<string>(),
            killExitLines = d.killExitLines ?? Array.Empty<string>(),
            questionResponses = d.questionResponses ?? Array.Empty<SuspectData.QuestionResponseSet>(),
            idleBarks = d.idleBarks,
            introDialogue = BuildScripted(d.introDialogue),
            fullMutantDialogue = BuildScripted(d.fullMutantDialogue),
            baseTriggers = Array.Empty<AnimTriggerDTO>(),
            mutantTriggers = Array.Empty<AnimTriggerDTO>(),
            idPhoto = includePhoto ? TextureToBase64(d.IDPhoto, 160) : "",
        };

        if (d.CharacterPrefab != null)
        {
            dto.prefabPath = AssetDatabase.GetAssetPath(d.CharacterPrefab);
            var so = new SerializedObject(d.CharacterPrefab);
            var baseAnim = so.FindProperty("animator")?.objectReferenceValue as Animator;
            var mutated = so.FindProperty("_mutatedVersion")?.objectReferenceValue as GameObject;
            dto.hasMutatedVersion = mutated != null;
            dto.hasWideCam = so.FindProperty("_suspectCam")?.objectReferenceValue != null;
            dto.hasFaceCam = so.FindProperty("_suspectFaceCam")?.objectReferenceValue != null;
            if (baseAnim == null) baseAnim = d.CharacterPrefab.GetComponentInChildren<Animator>(true);
            dto.baseTriggers = CollectTriggers(baseAnim);
            if (mutated != null) dto.mutantTriggers = CollectTriggers(mutated.GetComponentInChildren<Animator>(true));
        }
        return dto;
    }

    static ScriptedDTO BuildScripted(ScriptedDialogue sd)
    {
        if (sd == null) return new ScriptedDTO { assetPath = "", nodes = Array.Empty<NodeDTO>() };
        return new ScriptedDTO
        {
            assetPath = AssetDatabase.GetAssetPath(sd),
            isForced = sd.isForced,
            nodes = (sd.nodes ?? Array.Empty<ScriptedDialogueNode>()).Select(n => new NodeDTO
            {
                type = (int)n.type,
                npcLine = n.npcLine,
                animationTrigger = n.animationTrigger,
                cameraTrigger = n.cameraTrigger,
                playLaughSfx = n.playLaughSfx,
                wobbleProfile = n.wobbleProfileOverride != null ? n.wobbleProfileOverride.name : "",
                fontOverride = n.fontOverride != null ? n.fontOverride.name : "",
                choices = (n.choices ?? Array.Empty<ScriptedDialogueChoice>()).Select(c => new ChoiceDTO
                {
                    playerChoiceText = c.playerChoiceText,
                    npcResponse = c.npcResponse,
                    animationTrigger = c.animationTrigger,
                    playLaughSfx = c.playLaughSfx,
                }).ToArray(),
            }).ToArray(),
        };
    }

    static string[] GetSceneCameraKeys()
    {
        var keys = new List<string>();
        foreach (var runner in Resources.FindObjectsOfTypeAll<ScriptedDialogueRunner>())
        {
            if (EditorUtility.IsPersistent(runner) || !runner.gameObject.scene.IsValid()) continue;
            var arr = new SerializedObject(runner).FindProperty("_cameras");
            if (arr == null) continue;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var key = arr.GetArrayElementAtIndex(i).FindPropertyRelative("key")?.stringValue;
                if (!string.IsNullOrEmpty(key) && !keys.Contains(key)) keys.Add(key);
            }
        }
        return keys.ToArray();
    }

    // ---------------------------------------------------------------- Write

    [BeziAction("Dialogue Designer: save edited booth dialogue text (entry/exit/uncanny dialogues, exit line pools, question responses, idle barks, descriptors) to a SuspectData asset. Expects the JSON shape produced by ListDialogueSuspects.")]
    public static string SaveSuspectDialogue(string suspectAssetPath, string json)
    {
        var data = LoadSuspect(suspectAssetPath);
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("json is empty");
        var e = JsonUtility.FromJson<SuspectEditDTO>(json) ?? throw new ArgumentException("json could not be parsed");

        Undo.RecordObject(data, "Dialogue Designer: Edit Suspect Dialogue");
        data.entryReasons = e.entryReasons;
        data.invalidEntryReasons = e.invalidEntryReasons;
        data.entryDialogues = e.entryDialogues;
        data.uncannyEntryDialogues = e.uncannyEntryDialogues;
        data.exitDialoguesPassed = e.exitDialoguesPassed;
        data.exitDialoguesQuarantined = e.exitDialoguesQuarantined;
        data.exitDialoguesKilled = e.exitDialoguesKilled;
        data.quarantineExitLines = e.quarantineExitLines ?? Array.Empty<string>();
        data.killExitLines = e.killExitLines ?? Array.Empty<string>();
        data.questionResponses = e.questionResponses ?? Array.Empty<SuspectData.QuestionResponseSet>();
        data.idleBarks = e.idleBarks;
        if (e.basePersonalityDescriptors != null) data.basePersonalityDescriptors = e.basePersonalityDescriptors;
        if (e.normalBehaviorNotes != null) data.normalBehaviorNotes = e.normalBehaviorNotes;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        return $"Saved dialogue for {data.name}";
    }

    [BeziAction("Dialogue Designer: save edited nodes to a ScriptedDialogue asset. Existing wobble/font overrides are preserved by node index. Expects ScriptedDTO JSON { isForced, nodes:[{type,npcLine,animationTrigger,cameraTrigger,playLaughSfx,choices:[...]}] }.")]
    public static string SaveScriptedDialogue(string dialogueAssetPath, string json)
    {
        var sd = AssetDatabase.LoadAssetAtPath<ScriptedDialogue>(NormalizePath(dialogueAssetPath))
                 ?? throw new ArgumentException($"No ScriptedDialogue at '{dialogueAssetPath}'");
        var dto = JsonUtility.FromJson<ScriptedDTO>(json) ?? throw new ArgumentException("json could not be parsed");
        var incoming = dto.nodes ?? Array.Empty<NodeDTO>();

        Undo.RecordObject(sd, "Dialogue Designer: Edit Scripted Dialogue");
        var old = sd.nodes ?? Array.Empty<ScriptedDialogueNode>();
        var nodes = new ScriptedDialogueNode[incoming.Length];
        for (int i = 0; i < incoming.Length; i++)
        {
            var src = incoming[i];
            var n = new ScriptedDialogueNode
            {
                type = src.type == 1 ? ScriptedDialogueNodeType.Choice : ScriptedDialogueNodeType.Monologue,
                npcLine = src.npcLine ?? "",
                animationTrigger = src.animationTrigger ?? "",
                cameraTrigger = src.cameraTrigger ?? "",
                playLaughSfx = src.playLaughSfx,
                choices = (src.choices ?? Array.Empty<ChoiceDTO>()).Select(c => new ScriptedDialogueChoice
                {
                    playerChoiceText = c.playerChoiceText ?? "",
                    npcResponse = c.npcResponse ?? "",
                    animationTrigger = c.animationTrigger ?? "",
                    playLaughSfx = c.playLaughSfx,
                }).ToArray(),
            };
            if (i < old.Length && old[i] != null)
            {
                n.wobbleProfileOverride = old[i].wobbleProfileOverride;
                n.fontOverride = old[i].fontOverride;
            }
            nodes[i] = n;
        }
        sd.isForced = dto.isForced;
        sd.nodes = nodes;
        EditorUtility.SetDirty(sd);
        AssetDatabase.SaveAssetIfDirty(sd);
        return $"Saved {nodes.Length} nodes to {sd.name}";
    }

    [BeziAction("Dialogue Designer: create an empty ScriptedDialogue asset for a suspect's intro or fullMutant slot and assign it. slot must be 'intro' or 'fullMutant'. Returns the new asset path.")]
    public static string CreateSuspectScriptedDialogue(string suspectAssetPath, string slot)
    {
        var data = LoadSuspect(suspectAssetPath);
        bool intro = string.Equals(slot, "intro", StringComparison.OrdinalIgnoreCase);
        bool mutant = string.Equals(slot, "fullMutant", StringComparison.OrdinalIgnoreCase);
        if (!intro && !mutant) throw new ArgumentException("slot must be 'intro' or 'fullMutant'");
        if (intro && data.introDialogue != null) return AssetDatabase.GetAssetPath(data.introDialogue);
        if (mutant && data.fullMutantDialogue != null) return AssetDatabase.GetAssetPath(data.fullMutantDialogue);

        var baseName = string.IsNullOrEmpty(data.FirstName) ? data.name : data.FirstName.Replace(" ", "");
        var folder = intro ? IntroFolder : FullMutantFolder;
        var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baseName}{(intro ? "IntroDialogue" : "FullMutantDialogue")}.asset");
        var sd = ScriptableObject.CreateInstance<ScriptedDialogue>();
        sd.nodes = new[] { new ScriptedDialogueNode { npcLine = "..." } };
        AssetDatabase.CreateAsset(sd, path);
        Undo.RegisterCreatedObjectUndo(sd, "Dialogue Designer: Create Scripted Dialogue");

        Undo.RecordObject(data, "Dialogue Designer: Assign Scripted Dialogue");
        if (intro) data.introDialogue = sd; else data.fullMutantDialogue = sd;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        return path;
    }

    // ---------------------------------------------------------------- Preview render

    [BeziAction("Dialogue Designer: render a suspect prefab playing the clip bound to an Animator trigger, viewed through a dialogue camera key ('' = wide booth cam, 'SuspectFaceCam' = face cam). Returns JSON with base64 JPEG frames sampled across the clip.", IsReadOnly = true)]
    public static string RenderDialoguePreview(string suspectAssetPath, string animationTrigger, string cameraKey, bool mutated, int frameCount, int width, int height)
    {
        var data = LoadSuspect(suspectAssetPath);
        if (data.CharacterPrefab == null) throw new ArgumentException($"{data.name} has no CharacterPrefab assigned");
        frameCount = Mathf.Clamp(frameCount, 1, 24);
        width = Mathf.Clamp(width, 64, 1280);
        height = Mathf.Clamp(height, 64, 1280);

        var result = new PreviewDTO { frames = Array.Empty<string>(), clip = "", cameraSource = "" };
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture rt = null;
        Texture2D readback = null;
        PlayableGraph graph = default;
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(data.CharacterPrefab.gameObject, scene);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var character = instance.GetComponent<SuspectCharacter>();
            var so = new SerializedObject(character);
            var baseVersion = so.FindProperty("_baseVersion")?.objectReferenceValue as GameObject;
            var mutatedVersion = so.FindProperty("_mutatedVersion")?.objectReferenceValue as GameObject;
            bool useMutant = mutated && mutatedVersion != null;
            if (useMutant)
            {
                if (baseVersion != null) baseVersion.SetActive(false);
                mutatedVersion.SetActive(true);
            }
            else if (mutated)
            {
                result.note = "No Mutated Version on this prefab; showing base form. ";
            }

            Animator animator = useMutant
                ? mutatedVersion.GetComponentInChildren<Animator>(true)
                : (so.FindProperty("animator")?.objectReferenceValue as Animator ?? instance.GetComponentInChildren<Animator>(true));

            // Camera pose
            Camera cam = new GameObject("DialogueDesignerPreviewCam").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(cam.gameObject, scene);
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.1f, 0.12f, 1f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 200f;

            GameObject camSource = null;
            bool face = string.Equals(cameraKey, FaceCamKey, StringComparison.Ordinal);
            if (string.IsNullOrEmpty(cameraKey) || face)
            {
                string field = face ? (useMutant ? "_mutantFaceCam" : "_suspectFaceCam") : (useMutant ? "_mutantSuspectCam" : "_suspectCam");
                camSource = so.FindProperty(field)?.objectReferenceValue as GameObject;
                if (camSource == null && useMutant)
                    camSource = so.FindProperty(face ? "_suspectFaceCam" : "_suspectCam")?.objectReferenceValue as GameObject;
            }
            else
            {
                result.note += $"'{cameraKey}' is a scene camera (ScriptedDialogueRunner); previewing the character with a framed shot instead. ";
            }

            // Lights
            var key = new GameObject("KeyLight").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(key.gameObject, scene);
            key.type = LightType.Directional; key.intensity = 1.3f;
            key.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            var fill = new GameObject("FillLight").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(fill.gameObject, scene);
            fill.type = LightType.Directional; fill.intensity = 0.5f; fill.color = new Color(0.75f, 0.8f, 1f);
            fill.transform.rotation = Quaternion.Euler(20f, -60f, 0f);

            // Clip resolution
            AnimationClip clip = ResolveClip(animator, animationTrigger, out string clipNote);
            result.note += clipNote;
            if (animator != null && clip != null)
            {
                graph = PlayableGraph.Create("DialogueDesignerPreview");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "out", animator);
                var cp = AnimationClipPlayable.Create(graph, clip);
                cp.SetApplyFootIK(false);
                output.SetSourcePlayable(cp);
                result.clip = clip.name;
                result.clipLength = clip.length;
            }

            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            readback = new Texture2D(width, height, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            cam.aspect = (float)width / height;

            var frames = new List<string>();
            for (int f = 0; f < frameCount; f++)
            {
                if (graph.IsValid())
                {
                    float t = frameCount == 1 ? clip.length * 0.5f : clip.length * f / (frameCount - 1);
                    ((AnimationClipPlayable)graph.GetOutput(0).GetSourcePlayable()).SetTime(t);
                    graph.Evaluate();
                }
                PlaceCamera(cam, camSource, instance, face, result);
                RenderCamera(cam, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readback.Apply();
                RenderTexture.active = prev;
                frames.Add(Convert.ToBase64String(readback.EncodeToJPG(82)));
            }
            result.frames = frames.ToArray();
        }
        finally
        {
            if (graph.IsValid()) graph.Destroy();
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        return JsonUtility.ToJson(result);
    }

    static void PlaceCamera(Camera cam, GameObject source, GameObject character, bool face, PreviewDTO result)
    {
        if (source != null)
        {
            cam.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            if (TryReadLens(source, out float fov, out float near)) { cam.fieldOfView = fov; cam.nearClipPlane = Mathf.Max(0.01f, near); }
            result.cameraSource = source.name;
            return;
        }
        var renderers = character.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.up, Vector3.one * 2f);
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        Vector3 target = face ? new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * 0.1f, bounds.center.z) : bounds.center;
        float span = face ? bounds.size.y * 0.25f : bounds.size.y * 0.6f;
        float dist = span / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 fwd = character.transform.forward;
        cam.transform.position = target + fwd * dist;
        cam.transform.LookAt(target);
        result.cameraSource = face ? "auto face framing" : "auto wide framing";
    }

    static bool TryReadLens(GameObject go, out float fov, out float near)
    {
        fov = 40f; near = 0.05f;
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) continue;
            var so = new SerializedObject(c);
            var fovProp = so.FindProperty("Lens.FieldOfView") ?? so.FindProperty("m_Lens.FieldOfView");
            if (fovProp == null) continue;
            fov = fovProp.floatValue;
            var nearProp = so.FindProperty("Lens.NearClipPlane") ?? so.FindProperty("m_Lens.NearClipPlane");
            if (nearProp != null) near = nearProp.floatValue;
            return true;
        }
        var unityCam = go.GetComponent<Camera>();
        if (unityCam != null) { fov = unityCam.fieldOfView; near = unityCam.nearClipPlane; return true; }
        return false;
    }

    static void RenderCamera(Camera cam, RenderTexture rt)
    {
        var request = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
        else cam.Render();
    }

    // ---------------------------------------------------------------- Animator helpers

    static AnimTriggerDTO[] CollectTriggers(Animator animator)
    {
        var ac = GetController(animator?.runtimeAnimatorController, out var overrides);
        if (ac == null) return Array.Empty<AnimTriggerDTO>();
        return ac.parameters.Where(p => p.type == AnimatorControllerParameterType.Trigger).Select(p =>
        {
            var clip = FindClipForTrigger(ac, overrides, p.name);
            return new AnimTriggerDTO { trigger = p.name, clip = clip != null ? clip.name : "", length = clip != null ? clip.length : 0f };
        }).ToArray();
    }

    static AnimationClip ResolveClip(Animator animator, string trigger, out string note)
    {
        note = "";
        var ac = GetController(animator?.runtimeAnimatorController, out var overrides);
        if (ac == null) { note = "No AnimatorController found; showing bind pose. "; return null; }
        if (!string.IsNullOrEmpty(trigger))
        {
            var clip = FindClipForTrigger(ac, overrides, trigger);
            if (clip != null) return clip;
            note = $"Trigger '{trigger}' has no clip on this controller; showing idle. ";
        }
        if (ac.layers.Length == 0) return null;
        return MotionToClip(ac.layers[0].stateMachine.defaultState?.motion, overrides);
    }

    static AnimatorController GetController(RuntimeAnimatorController rac, out Dictionary<AnimationClip, AnimationClip> overrides)
    {
        overrides = new Dictionary<AnimationClip, AnimationClip>();
        int guard = 0;
        while (rac is AnimatorOverrideController aoc && guard++ < 8)
        {
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            aoc.GetOverrides(pairs);
            foreach (var kv in pairs)
                if (kv.Key != null && kv.Value != null && !overrides.ContainsKey(kv.Key)) overrides[kv.Key] = kv.Value;
            rac = aoc.runtimeAnimatorController;
        }
        return rac as AnimatorController;
    }

    static AnimationClip FindClipForTrigger(AnimatorController ac, Dictionary<AnimationClip, AnimationClip> overrides, string trigger)
    {
        foreach (var layer in ac.layers)
        {
            var clip = SearchMachine(layer.stateMachine, trigger, overrides);
            if (clip != null) return clip;
        }
        return null;
    }

    static AnimationClip SearchMachine(AnimatorStateMachine sm, string trigger, Dictionary<AnimationClip, AnimationClip> overrides)
    {
        IEnumerable<AnimatorStateTransition> transitions = sm.anyStateTransitions;
        foreach (var s in sm.states) transitions = transitions.Concat(s.state.transitions);
        foreach (var t in transitions)
        {
            if (t.destinationState == null || !t.conditions.Any(c => c.parameter == trigger)) continue;
            var clip = MotionToClip(t.destinationState.motion, overrides);
            if (clip != null) return clip;
        }
        foreach (var child in sm.stateMachines)
        {
            var clip = SearchMachine(child.stateMachine, trigger, overrides);
            if (clip != null) return clip;
        }
        return null;
    }

    static AnimationClip MotionToClip(Motion motion, Dictionary<AnimationClip, AnimationClip> overrides)
    {
        AnimationClip clip = motion as AnimationClip;
        if (clip == null && motion is BlendTree tree)
            clip = tree.children.Select(c => MotionToClip(c.motion, overrides)).FirstOrDefault(c => c != null);
        if (clip != null && overrides.TryGetValue(clip, out var o)) clip = o;
        return clip;
    }

    // ---------------------------------------------------------------- Utils

    static SuspectData LoadSuspect(string assetPath)
    {
        var data = AssetDatabase.LoadAssetAtPath<SuspectData>(NormalizePath(assetPath));
        if (data == null) throw new ArgumentException($"No SuspectData at '{assetPath}'");
        return data;
    }

    static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        path = path.Replace('\\', '/');
        int i = path.IndexOf("Assets/", StringComparison.Ordinal);
        return i > 0 ? path.Substring(i) : path;
    }

    static string TextureToBase64(Texture2D tex, int maxSize)
    {
        if (tex == null) return "";
        float scale = Mathf.Min(1f, (float)maxSize / Mathf.Max(tex.width, tex.height));
        int w = Mathf.Max(1, Mathf.RoundToInt(tex.width * scale));
        int h = Mathf.Max(1, Mathf.RoundToInt(tex.height * scale));
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(w, h, TextureFormat.RGB24, false);
        copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var b64 = Convert.ToBase64String(copy.EncodeToJPG(80));
        UnityEngine.Object.DestroyImmediate(copy);
        return b64;
    }
}
