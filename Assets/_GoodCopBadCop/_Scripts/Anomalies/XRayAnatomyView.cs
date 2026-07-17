using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.XRay
{
    /// <summary>
    /// Client-local procedural anatomy used by the X-ray station. It never creates a NetworkObject
    /// and restores every replaced renderer material when the X-ray view is closed.
    /// </summary>
    public sealed class XRayAnatomyView : MonoBehaviour
    {
        private const string ShaderName = "GoodCopBadCop/XRayAnatomy";
        private const string AnatomyRootName = "__XRayAnatomy";
        private const string AnatomyPrefabResourcePath = "XRay/CharacterRigSkin";

        private readonly struct BoneSegment
        {
            public readonly Transform Transform;
            public readonly Transform From;
            public readonly Transform To;
            public readonly float Radius;

            public BoneSegment(Transform transform, Transform from, Transform to, float radius)
            {
                Transform = transform;
                From = from;
                To = to;
                Radius = radius;
            }
        }

        private readonly struct BoneAnchor
        {
            public readonly Transform Transform;
            public readonly Transform Bone;

            public BoneAnchor(Transform transform, Transform bone)
            {
                Transform = transform;
                Bone = bone;
            }
        }

        private sealed class ImportedRigBinding
        {
            public Transform ModelBone;
            public Transform TargetBone;
            public Transform ModelScaleReference;
            public Transform TargetScaleReference;
            public float SourceLength;
        }

        private sealed class DirectMeshSegment
        {
            public Transform Visual;
            public Transform From;
            public Transform To;
            public float NativeLength;
            public Vector3 SourceLongAxis;
            public Vector3 SourceSecondaryAxis;
            public float ThicknessMultiplier;
            public float LengthMultiplier;
            public bool AlignSecondaryAxisToBodyUp;
        }

        private sealed class DirectMeshAnchor
        {
            public Transform Visual;
            public Transform Bone;
            public float Scale;
            public Quaternion LocalRotation;
        }

        private sealed class HeadMeshAnchor
        {
            public Transform Visual;
            public Transform Head;
            public Transform Neck;
            public float SourceNeckHeadLength;
        }

        private sealed class ThoraxMeshBinding
        {
            public Transform Visual;
            public Transform TargetChest;
            public Transform TargetHips;
            public Transform TargetLeftArm;
            public Transform TargetRightArm;
            public Transform TargetNeck;
            public Transform TargetHead;
            public float SourceWidth;
            public float SourceHeight;
            public float SourceMinY;
            public float SourceMaxY;
            public bool IsSpine;
        }

        private sealed class ShoulderMeshBinding
        {
            public Transform Visual;
            public Transform TargetShoulder;
            public Transform TargetHips;
            public Transform TargetChest;
            public Transform TargetNeck;
            public Transform TargetLeftArm;
            public Transform TargetRightArm;
            public float SourceWidth;
            public float SourceHeight;
        }

        private sealed class PelvisMeshBinding
        {
            public Transform Visual;
            public Transform TargetHips;
            public Transform TargetChest;
            public Transform TargetLeftArm;
            public Transform TargetRightArm;
            public Transform TargetLeftLeg;
            public Transform TargetRightLeg;
            public float SourceLegWidth;
            public float SourceTorsoHeight;
        }

        private readonly Dictionary<Renderer, Material[]> _originalMaterials = new();
        private readonly List<BoneSegment> _boneSegments = new();
        private readonly List<BoneAnchor> _boneAnchors = new();
        private readonly List<ImportedRigBinding> _importedRigBindings = new();
        private readonly List<DirectMeshSegment> _directMeshSegments = new();
        private readonly List<DirectMeshAnchor> _directMeshAnchors = new();
        private readonly List<HeadMeshAnchor> _headMeshAnchors = new();
        private readonly List<ThoraxMeshBinding> _thoraxMeshes = new();
        private readonly List<PelvisMeshBinding> _pelvisMeshes = new();
        private readonly List<ShoulderMeshBinding> _shoulderMeshes = new();
        private readonly List<GameObjectLayerState> _layerStates = new();

        private Animator _animator;
        private GameObject _anatomyRoot;
        private GameObject _importedAnatomy;
        private Animator _importedAnimator;
        private HumanPoseHandler _sourcePoseHandler;
        private HumanPoseHandler _importedPoseHandler;
        private HumanPose _humanPose;
        private Material _bodyMaterial;
        private Material _anatomyMaterial;
        private Material _anomalyMaterial;
        private bool _isXRayVisible;
        private bool _hasLoggedMissingHumanoid;

        private readonly struct GameObjectLayerState
        {
            public readonly GameObject GameObject;
            public readonly int Layer;

            public GameObjectLayerState(GameObject gameObject, int layer)
            {
                GameObject = gameObject;
                Layer = layer;
            }
        }

        /// <summary>Shows or hides the local X-ray representation and body overlay.</summary>
        public void SetXRayVisible(bool visible)
        {
            if (visible)
            {
                if (!EnsureAnatomy())
                {
                    _isXRayVisible = false;
                    return;
                }

                ApplyBodyMaterial();
                _anatomyRoot.SetActive(true);
                // Cursor scanning renders immediately after this method and hides the anatomy
                // again before LateUpdate. Evaluate the retargeted pose now so the first (and
                // sometimes only) X-ray camera render never sees the prefab's T-pose.
                SyncImportedAnimator();
                _isXRayVisible = true;
                return;
            }

            RestoreBodyMaterials();
            if (_anatomyRoot != null)
                _anatomyRoot.SetActive(false);
            _isXRayVisible = false;
        }

        /// <summary>
        /// Renders this suspect's X-ray view into an isolated camera without changing what the
        /// player's main camera sees. Intended for the editor preview cursor scanner.
        /// </summary>
        public bool RenderXRayTo(Camera camera, RenderTexture targetTexture, int isolatedLayer)
        {
            if (camera == null || targetTexture == null || isolatedLayer < 0 || isolatedLayer > 31)
                return false;

            if (!EnsureAnatomy())
                return false;

            RenderTexture previousTarget = camera.targetTexture;
            int previousCullingMask = camera.cullingMask;
            CameraClearFlags previousClearFlags = camera.clearFlags;
            Color previousBackground = camera.backgroundColor;
            // Material snapshots are an implementation detail. The explicit flag is necessary
            // here: a failed/partial snapshot must never leave the anatomy visible in Game view.
            bool wasVisible = _isXRayVisible;

            try
            {
                SetXRayVisible(true);
                CaptureAndSetLayers(isolatedLayer);

                camera.targetTexture = targetTexture;
                camera.cullingMask = 1 << isolatedLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.Render();
                return true;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.cullingMask = previousCullingMask;
                camera.clearFlags = previousClearFlags;
                camera.backgroundColor = previousBackground;
                RestoreLayers();

                if (!wasVisible)
                    SetXRayVisible(false);
            }
        }

        private void OnDisable()
        {
            SetXRayVisible(false);
        }

        private void LateUpdate()
        {
            if (_anatomyRoot == null || !_anatomyRoot.activeSelf)
                return;

            foreach (BoneSegment segment in _boneSegments)
            {
                if (segment.Transform == null || segment.From == null || segment.To == null)
                    continue;

                Vector3 direction = segment.To.position - segment.From.position;
                float length = direction.magnitude;
                if (length < 0.001f)
                    continue;

                segment.Transform.position = Vector3.Lerp(segment.From.position, segment.To.position, 0.5f);
                segment.Transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);
                segment.Transform.localScale = new Vector3(segment.Radius * 2f, length * 0.5f, segment.Radius * 2f);
            }

            foreach (BoneAnchor anchor in _boneAnchors)
            {
                if (anchor.Transform == null || anchor.Bone == null)
                    continue;

                anchor.Transform.SetPositionAndRotation(anchor.Bone.position, anchor.Bone.rotation);
            }

            foreach (DirectMeshSegment segment in _directMeshSegments)
            {
                if (segment.Visual == null || segment.From == null || segment.To == null)
                    continue;

                Vector3 direction = segment.To.position - segment.From.position;
                float length = direction.magnitude;
                if (length < 0.001f)
                    continue;

                // These cloned meshes contain raw vertices only. Most limb meshes are long on
                // local Z, while the imported clavicles are long on local X. Map each mesh's
                // measured primary axis onto its two target Humanoid joints instead of imposing
                // a single axis convention on every source mesh.
                segment.Visual.SetPositionAndRotation(
                    Vector3.Lerp(segment.From.position, segment.To.position, 0.5f),
                    GetSegmentRotation(segment, direction / length));
                float ratio = length * segment.LengthMultiplier / segment.NativeLength;
                float thickness = Mathf.Sqrt(ratio) * segment.ThicknessMultiplier;
                segment.Visual.localScale = GetSegmentScale(segment.SourceLongAxis, thickness, ratio);
            }

            foreach (DirectMeshAnchor anchor in _directMeshAnchors)
            {
                if (anchor.Visual == null || anchor.Bone == null)
                    continue;

                anchor.Visual.SetPositionAndRotation(anchor.Bone.position, anchor.Bone.rotation * anchor.LocalRotation);
                anchor.Visual.localScale = Vector3.one * anchor.Scale;
            }

            foreach (HeadMeshAnchor anchor in _headMeshAnchors)
            {
                if (anchor.Visual == null || anchor.Head == null || anchor.Neck == null)
                    continue;

                Quaternion targetHeadFrame = BuildHeadFrame(
                    anchor.Neck,
                    anchor.Head,
                    _animator != null ? _animator.transform.forward : Vector3.forward);
                float targetLength = Vector3.Distance(anchor.Neck.position, anchor.Head.position);
                float scale = anchor.SourceNeckHeadLength > 0.001f
                    ? Mathf.Clamp(targetLength / anchor.SourceNeckHeadLength * 0.80f, 0.5f, 2.9f)
                    : 1f;

                anchor.Visual.SetPositionAndRotation(
                    anchor.Head.position,
                    targetHeadFrame);
                anchor.Visual.localScale = Vector3.one * scale;
            }

            foreach (ThoraxMeshBinding binding in _thoraxMeshes)
            {
                if (binding.Visual == null || binding.TargetChest == null || binding.TargetHips == null)
                    continue;

                Quaternion targetFrame = BuildAnatomicalFrame(
                    binding.TargetHips,
                    binding.TargetChest,
                    binding.TargetLeftArm,
                    binding.TargetRightArm);
                Vector3 up = targetFrame * Vector3.up;
                Vector3 visualPosition = binding.TargetChest.position;
                bool fitSpineEndpoints = binding.IsSpine && binding.TargetNeck != null
                    && binding.SourceMaxY - binding.SourceMinY > 0.001f;
                Vector3 spineTop = binding.TargetHead != null
                    ? Vector3.Lerp(binding.TargetNeck.position, binding.TargetHead.position, 0.35f)
                    : binding.TargetNeck != null ? binding.TargetNeck.position : binding.TargetChest.position;
                float targetHeight = fitSpineEndpoints
                    ? Vector3.Dot(spineTop - binding.TargetHips.position, up)
                    : Vector3.Distance(binding.TargetChest.position, binding.TargetHips.position);
                float sourceHeight = fitSpineEndpoints
                    ? binding.SourceMaxY - binding.SourceMinY
                    : binding.SourceHeight;
                float targetWidth = Vector3.Distance(binding.TargetRightArm.position, binding.TargetLeftArm.position);
                float heightScale = sourceHeight > 0.001f
                    ? Mathf.Clamp(targetHeight / sourceHeight, 0.25f, 3.5f)
                    : 1f;
                float widthScale = binding.SourceWidth > 0.001f
                    ? Mathf.Clamp(targetWidth / binding.SourceWidth, 0.25f, 3f)
                    : heightScale;
                float depthScale = Mathf.Sqrt(widthScale * heightScale);

                if (fitSpineEndpoints)
                {
                    float targetHipsY = Vector3.Dot(binding.TargetHips.position - binding.TargetChest.position, up);
                    visualPosition += up * (targetHipsY - binding.SourceMinY * heightScale);
                }
                else if (binding.TargetNeck != null)
                {
                    // Ribs sit in the upper thorax. The source pack's rib root is a little low
                    // relative to its chest bone, so lift only the rib cluster toward the neck.
                    visualPosition += up * Vector3.Dot(binding.TargetNeck.position - binding.TargetChest.position, up) * 0.45f;
                }

                // The mesh vertices are stored in the source anatomical frame (right, up,
                // forward). Scaling this frame independently matches shoulder width and torso
                // height instead of applying one uniform scale to every character.
                binding.Visual.SetPositionAndRotation(visualPosition, targetFrame);
                binding.Visual.localScale = new Vector3(widthScale, heightScale, depthScale);
            }

            foreach (PelvisMeshBinding binding in _pelvisMeshes)
            {
                if (binding.Visual == null || binding.TargetHips == null || binding.TargetChest == null
                    || binding.TargetLeftArm == null || binding.TargetRightArm == null
                    || binding.TargetLeftLeg == null || binding.TargetRightLeg == null)
                    continue;

                float targetLegWidth = Vector3.Distance(binding.TargetRightLeg.position, binding.TargetLeftLeg.position);
                float targetTorsoHeight = Vector3.Distance(binding.TargetChest.position, binding.TargetHips.position);
                float widthScale = binding.SourceLegWidth > 0.001f
                    ? Mathf.Clamp(targetLegWidth / binding.SourceLegWidth, 0.25f, 3f)
                    : 1f;
                float heightScale = binding.SourceTorsoHeight > 0.001f
                    ? Mathf.Clamp(targetTorsoHeight / binding.SourceTorsoHeight, 0.25f, 3f)
                    : widthScale;
                float depthScale = Mathf.Sqrt(widthScale * heightScale);
                Quaternion targetFrame = BuildAnatomicalFrame(
                    binding.TargetHips,
                    binding.TargetChest,
                    binding.TargetLeftArm,
                    binding.TargetRightArm);

                binding.Visual.SetPositionAndRotation(binding.TargetHips.position, targetFrame);
                binding.Visual.localScale = new Vector3(widthScale, heightScale, depthScale);
            }

            foreach (ShoulderMeshBinding binding in _shoulderMeshes)
            {
                if (binding.Visual == null || binding.TargetShoulder == null || binding.TargetHips == null
                    || binding.TargetChest == null || binding.TargetLeftArm == null || binding.TargetRightArm == null)
                    continue;

                float targetWidth = Vector3.Distance(binding.TargetRightArm.position, binding.TargetLeftArm.position);
                float targetHeight = Vector3.Distance(binding.TargetChest.position, binding.TargetHips.position);
                float widthScale = binding.SourceWidth > 0.001f
                    ? Mathf.Clamp(targetWidth / binding.SourceWidth, 0.25f, 3f)
                    : 1f;
                float heightScale = binding.SourceHeight > 0.001f
                    ? Mathf.Clamp(targetHeight / binding.SourceHeight, 0.25f, 3f)
                    : widthScale;
                Quaternion targetFrame = BuildAnatomicalFrame(
                    binding.TargetHips,
                    binding.TargetChest,
                    binding.TargetLeftArm,
                    binding.TargetRightArm);
                Vector3 scale = new Vector3(widthScale, heightScale, Mathf.Sqrt(widthScale * heightScale));

                // A scapula sits on the upper back, not at either the shoulder joint or a raw
                // offset copied from the imported rig.  Build that point from the target chest,
                // neck and shoulder span: it follows the torso, while its lateral position still
                // follows a raised/lowered arm.
                Vector3 up = targetFrame * Vector3.up;
                Vector3 right = targetFrame * Vector3.right;
                Vector3 forward = targetFrame * Vector3.forward;
                Vector3 shoulderDelta = binding.TargetShoulder.position - binding.TargetChest.position;
                float lateral = Vector3.Dot(shoulderDelta, right);
                float shoulderHeight = Vector3.Dot(shoulderDelta, up);
                float neckHeight = binding.TargetNeck == null
                    ? shoulderHeight
                    : Vector3.Dot(binding.TargetNeck.position - binding.TargetChest.position, up);
                float bladeHeight = Mathf.Max(shoulderHeight + neckHeight * 0.15f, neckHeight * 0.65f);
                Vector3 bladePosition = binding.TargetChest.position
                    + right * (lateral * 0.60f)
                    + up * bladeHeight
                    - forward * (targetWidth * 0.10f);

                binding.Visual.SetPositionAndRotation(bladePosition, targetFrame);
                const float shoulderScale = 1.20f;
                binding.Visual.localScale = scale * shoulderScale;
            }

            // Imported anatomy bones are directly parented to their Humanoid counterparts.
            // The imported Animator receives the suspect's state below.
            SyncImportedAnimator();
        }

        private static Vector3 GetSegmentScale(Vector3 longAxis, float thickness, float length)
        {
            if (Mathf.Abs(longAxis.x) > 0.5f)
                return new Vector3(length, thickness, thickness);
            if (Mathf.Abs(longAxis.y) > 0.5f)
                return new Vector3(thickness, length, thickness);
            return new Vector3(thickness, thickness, length);
        }

        private Quaternion GetSegmentRotation(DirectMeshSegment segment, Vector3 direction)
        {
            Quaternion rotation = Quaternion.FromToRotation(segment.SourceLongAxis, direction);
            if (!segment.AlignSecondaryAxisToBodyUp)
                return rotation;

            Vector3 sourceSecondary = segment.SourceSecondaryAxis.sqrMagnitude > 0.0001f
                ? segment.SourceSecondaryAxis
                : Mathf.Abs(Vector3.Dot(segment.SourceLongAxis, Vector3.up)) < 0.9f
                    ? Vector3.up
                    : Vector3.right;
            Vector3 rotatedSecondary = Vector3.ProjectOnPlane(rotation * sourceSecondary, direction);
            Vector3 targetUp = _animator == null
                ? Vector3.ProjectOnPlane(Vector3.up, direction)
                : Vector3.ProjectOnPlane(_animator.transform.up, direction);
            if (rotatedSecondary.sqrMagnitude < 0.0001f || targetUp.sqrMagnitude < 0.0001f)
                return rotation;

            float twist = Vector3.SignedAngle(rotatedSecondary, targetUp, direction);
            return Quaternion.AngleAxis(twist, direction) * rotation;
        }

        private void OnDestroy()
        {
            RestoreBodyMaterials();
            DestroyMaterial(ref _bodyMaterial);
            DestroyMaterial(ref _anatomyMaterial);
            DestroyMaterial(ref _anomalyMaterial);
            DisposePoseHandlers();
        }

        private bool EnsureAnatomy()
        {
            Animator activeAnimator = ResolveAnimator();
            if (activeAnimator == null || !activeAnimator.isHuman)
            {
                if (!_hasLoggedMissingHumanoid)
                {
                    Debug.LogWarning($"[XRayAnatomyView] '{name}' has no Humanoid Animator. X-ray anatomy was skipped.", this);
                    _hasLoggedMissingHumanoid = true;
                }
                return false;
            }

            if (_animator != activeAnimator)
            {
                SetXRayVisible(false);
                if (_anatomyRoot != null)
                    Destroy(_anatomyRoot);

                _anatomyRoot = null;
                _boneSegments.Clear();
                _boneAnchors.Clear();
                _importedRigBindings.Clear();
                _directMeshSegments.Clear();
                _directMeshAnchors.Clear();
                _headMeshAnchors.Clear();
                _thoraxMeshes.Clear();
                _pelvisMeshes.Clear();
                _shoulderMeshes.Clear();
                _importedAnatomy = null;
                _importedAnimator = null;
                DisposePoseHandlers();
                _animator = activeAnimator;
                _hasLoggedMissingHumanoid = false;
            }

            if (_anatomyRoot != null)
                return true;

            if (!CreateMaterials())
                return false;

            _anatomyRoot = new GameObject(AnatomyRootName)
            {
                hideFlags = HideFlags.DontSave
            };
            _anatomyRoot.transform.SetParent(transform, false);

            if (!BuildImportedAnatomy())
            {
                Debug.LogWarning("[XRayAnatomyView] Imported anatomy prefab was not available; using basic skeleton fallback.", this);
                BuildSkeleton();
            }
            _anatomyRoot.SetActive(false);
            return true;
        }

        private Animator ResolveAnimator()
        {
            SuspectCharacter suspect = GetComponent<SuspectCharacter>();
            if (suspect != null && suspect.animator != null)
                return suspect.animator;

            return GetComponentInChildren<Animator>(true);
        }

        private bool CreateMaterials()
        {
            if (_bodyMaterial != null)
                return true;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[XRayAnatomyView] Required shader '{ShaderName}' was not found.", this);
                return false;
            }

            _bodyMaterial = new Material(shader) { name = "X Ray Body (Runtime)" };
            _bodyMaterial.SetFloat("_Mode", 0f);
            _bodyMaterial.SetColor("_Color", new Color(0.08f, 0.32f, 0.48f, 1f));
            _bodyMaterial.SetColor("_EmissionColor", new Color(0.34f, 0.9f, 1f, 1f));
            _bodyMaterial.SetFloat("_Alpha", 0.2f);
            _bodyMaterial.SetFloat("_RimPower", 2.2f);
            _bodyMaterial.SetFloat("_ZWrite", 0f);
            _bodyMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            _anatomyMaterial = new Material(shader) { name = "X Ray Anatomy (Runtime)" };
            _anatomyMaterial.SetFloat("_Mode", 1f);
            _anatomyMaterial.SetColor("_EmissionColor", new Color(0.62f, 0.96f, 1f, 1f));
            _anatomyMaterial.SetFloat("_ZWrite", 0f);
            _anatomyMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            _anatomyMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;

            _anomalyMaterial = new Material(shader) { name = "X Ray Anomaly (Runtime)" };
            _anomalyMaterial.SetFloat("_Mode", 2f);
            _anomalyMaterial.SetColor("_RimColor", new Color(0.85f, 0.05f, 1f, 1f));
            _anomalyMaterial.SetFloat("_RimPower", 2.4f);
            _anomalyMaterial.SetFloat("_ZWrite", 0f);
            _anomalyMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            _anomalyMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 11;
            return true;
        }

        private bool BuildImportedAnatomy()
        {
            GameObject prefab = Resources.Load<GameObject>(AnatomyPrefabResourcePath);
            if (prefab == null)
                return false;

            _importedAnatomy = Instantiate(prefab, _anatomyRoot.transform);
            _importedAnatomy.name = "Imported Skeleton And Organs";
            _importedAnatomy.hideFlags = HideFlags.DontSave;

            _importedAnimator = _importedAnatomy.GetComponentInChildren<Animator>(true);
            if (!ConfigureImportedAnimator())
                return false;

            foreach (Collider collider in _importedAnatomy.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            foreach (Renderer renderer in _importedAnatomy.GetComponentsInChildren<Renderer>(true))
            {
                // Keep the first anatomy pass deliberately readable: large skeletal forms only.
                // Organs, fingers and other fine detail will return only after the segment adapter
                // can place them correctly for every character.
                if (IsSourceSkinRenderer(renderer) || IsDeferredAnatomyRenderer(renderer))
                {
                    renderer.enabled = false;
                    continue;
                }

                renderer.sharedMaterial = _anatomyMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            // The pack is our mesh library. Its authored rig has different body proportions, so
            // hide it and place only the large rigid bone meshes directly from the suspect joints.
            foreach (Renderer renderer in _importedAnatomy.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;

            AddDirectMeshSegment("LeftUpLeg", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 1f, 1f, true);
            AddDirectMeshSegment("LefLeg", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 1f, 1f, true);
            AddDirectMeshSegment("RightUpLeg", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 1f, 1f, true);
            AddDirectMeshSegment("RightLeg", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 1f, 1f, true);
            AddArmMesh("LeftArm", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm);
            AddArmMesh("LeftForeArm", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            AddArmMesh("RightArm", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
            AddArmMesh("RightForeArm", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            AddClavicleMesh("clavicle_l", HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm);
            AddClavicleMesh("clavicle_r", HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm);
            AddHeadMeshAnchor("Skull");
            AddShoulderBladeMesh("LeftShoulder", "rig:LeftShoulder", HumanBodyBones.LeftShoulder);
            AddShoulderBladeMesh("RightShoulder", "rig:RightShoulder", HumanBodyBones.RightShoulder);
            AddPelvisMesh();
            AddFootMesh("LeftFoot", HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            AddFootMesh("RightFoot", HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            BuildImportedThorax();
            return true;
        }

        private void AddDirectMeshSegment(
            string sourceMeshName,
            HumanBodyBones fromBone,
            HumanBodyBones toBone,
            float thicknessMultiplier = 1f,
            float lengthMultiplier = 1f,
            bool alignSecondaryAxisToBodyUp = false)
        {
            Mesh sourceMesh = null;
            Transform sourceTransform = null;
            foreach (MeshFilter candidate in _importedAnatomy.GetComponentsInChildren<MeshFilter>(true))
            {
                if (candidate.name == sourceMeshName)
                {
                    sourceMesh = candidate.sharedMesh;
                    sourceTransform = candidate.transform;
                    break;
                }
            }

            if (sourceMesh == null)
            {
                foreach (SkinnedMeshRenderer candidate in _importedAnatomy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (candidate.name == sourceMeshName)
                    {
                        sourceMesh = candidate.sharedMesh;
                        sourceTransform = candidate.transform;
                        break;
                    }
                }
            }

            Transform from = Bone(fromBone);
            Transform to = Bone(toBone);
            if (sourceMesh == null || sourceTransform == null || from == null || to == null)
                return;

            Vector3 sourceLongAxis = GetLongestMeshAxis(sourceMesh.bounds.size, out float length);
            if (length < 0.001f)
                return;

            Vector3 sourceSecondaryAxis = Vector3.zero;
            if (alignSecondaryAxisToBodyUp)
            {
                Transform sourceHips = FindDescendant(_importedAnatomy.transform, "rig:Hips");
                Transform sourceChest = FindDescendant(_importedAnatomy.transform, "rig:Spine2")
                    ?? FindDescendant(_importedAnatomy.transform, "rig:Spine1");
                Transform sourceLeftArm = FindDescendant(_importedAnatomy.transform, "rig:LeftArm");
                Transform sourceRightArm = FindDescendant(_importedAnatomy.transform, "rig:RightArm");
                if (sourceHips != null && sourceChest != null && sourceLeftArm != null && sourceRightArm != null)
                {
                    Quaternion sourceFrame = BuildAnatomicalFrame(sourceHips, sourceChest, sourceLeftArm, sourceRightArm);
                    sourceSecondaryAxis = sourceTransform.InverseTransformDirection(sourceFrame * Vector3.up);
                }
            }

            GameObject visual = new GameObject($"XRay {sourceMeshName}") { hideFlags = HideFlags.DontSave };
            visual.transform.SetParent(_anatomyRoot.transform, false);
            // Source foot meshes use an ankle pivot rather than their geometric center. Every
            // segment is positioned at the midpoint of two Humanoid joints, so center a private
            // runtime copy first; otherwise feet appear offset even when their transforms are
            // correctly attached to the ankle and toes.
            visual.AddComponent<MeshFilter>().sharedMesh = CreateCenteredMesh(sourceMesh, $"XRay {sourceMeshName}");
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _anatomyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _directMeshSegments.Add(new DirectMeshSegment
            {
                Visual = visual.transform,
                From = from,
                To = to,
                NativeLength = length,
                SourceLongAxis = sourceLongAxis,
                SourceSecondaryAxis = sourceSecondaryAxis,
                ThicknessMultiplier = thicknessMultiplier,
                LengthMultiplier = lengthMultiplier,
                AlignSecondaryAxisToBodyUp = alignSecondaryAxisToBodyUp
            });
        }

        private void AddArmMesh(string sourceMeshName, HumanBodyBones fromBone, HumanBodyBones toBone)
        {
            // Imported limb meshes end slightly before their raw bounds. A small overlap at the
            // elbow/wrist avoids visible gaps on broad or long-armed characters.
            AddDirectMeshSegment(sourceMeshName, fromBone, toBone, 1f, 1.08f, true);
        }

        private static Mesh CreateCenteredMesh(Mesh sourceMesh, string meshName)
        {
            Mesh mesh = Instantiate(sourceMesh);
            mesh.name = meshName;
            Vector3 center = mesh.bounds.center;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] -= center;
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 GetLongestMeshAxis(Vector3 size, out float length)
        {
            if (size.x >= size.y && size.x >= size.z)
            {
                length = size.x;
                return Vector3.right;
            }
            if (size.y >= size.z)
            {
                length = size.y;
                return Vector3.up;
            }

            length = size.z;
            return Vector3.forward;
        }

        private void AddFootMesh(string sourceMeshName, HumanBodyBones footBone, HumanBodyBones toeBone)
        {
            // Feet are not anchors: their visible length and forward direction come from the
            // ankle-to-toes segment, exactly like arms and legs. This also adapts shoe/foot size
            // to tall and short suspects.
            if (Bone(toeBone) != null)
                AddDirectMeshSegment(sourceMeshName, footBone, toeBone, 0.78f, 1f, true);
            else
                AddDirectMeshAnchor(sourceMeshName, footBone, 1f);
        }

        private void AddClavicleMesh(string sourceMeshName, HumanBodyBones shoulderBone, HumanBodyBones armBone)
        {
            // The clavicle ends at Shoulder, not at UpperArm. Using UpperArm made the bone span
            // the entire shoulder slope and produced the oversized V shape seen in the preview.
            HumanBodyBones endBone = Bone(shoulderBone) != null ? shoulderBone : armBone;
            HumanBodyBones startBone = Bone(HumanBodyBones.UpperChest) != null
                ? HumanBodyBones.UpperChest
                : HumanBodyBones.Chest;
            AddDirectMeshSegment(sourceMeshName, startBone, endBone, 0.85f);
        }

        private void AddHeadMeshAnchor(string sourceMeshName)
        {
            MeshFilter source = null;
            foreach (MeshFilter candidate in _importedAnatomy.GetComponentsInChildren<MeshFilter>(true))
            {
                if (candidate.name == sourceMeshName)
                {
                    source = candidate;
                    break;
                }
            }

            Transform targetHead = Bone(HumanBodyBones.Head);
            Transform targetNeck = Bone(HumanBodyBones.Neck);
            Transform sourceHead = FindDescendant(_importedAnatomy.transform, "rig:Head");
            Transform sourceNeck = FindDescendant(_importedAnatomy.transform, "rig:Neck");
            Transform sourceHips = FindDescendant(_importedAnatomy.transform, "rig:Hips");
            Transform sourceChest = FindDescendant(_importedAnatomy.transform, "rig:Spine2")
                ?? FindDescendant(_importedAnatomy.transform, "rig:Spine1");
            Transform sourceLeftArm = FindDescendant(_importedAnatomy.transform, "rig:LeftArm");
            Transform sourceRightArm = FindDescendant(_importedAnatomy.transform, "rig:RightArm");
            if (source == null || source.sharedMesh == null || targetHead == null || targetNeck == null
                || sourceHead == null || sourceNeck == null || sourceHips == null || sourceChest == null
                || sourceLeftArm == null || sourceRightArm == null)
                return;

            Quaternion sourceBodyFrame = BuildAnatomicalFrame(sourceHips, sourceChest, sourceLeftArm, sourceRightArm);
            Quaternion sourceHeadFrame = BuildHeadFrame(sourceNeck, sourceHead, sourceBodyFrame * Vector3.forward);
            Mesh mesh = Instantiate(source.sharedMesh);
            mesh.name = $"XRay {sourceMeshName}";
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldVertex = source.transform.TransformPoint(vertices[i]);
                vertices[i] = Quaternion.Inverse(sourceHeadFrame) * (worldVertex - sourceHead.position);
            }
            mesh.vertices = vertices;

            Vector3[] normals = mesh.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = (Quaternion.Inverse(sourceHeadFrame) * source.transform.TransformDirection(normals[i])).normalized;
                mesh.normals = normals;
            }
            mesh.RecalculateBounds();

            GameObject visual = new GameObject($"XRay {sourceMeshName}") { hideFlags = HideFlags.DontSave };
            visual.transform.SetParent(_anatomyRoot.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _anatomyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _headMeshAnchors.Add(new HeadMeshAnchor
            {
                Visual = visual.transform,
                Head = targetHead,
                Neck = targetNeck,
                SourceNeckHeadLength = Vector3.Distance(sourceNeck.position, sourceHead.position)
            });
        }

        private void AddShoulderBladeMesh(string sourceMeshName, string sourceBoneName, HumanBodyBones targetBone)
        {
            MeshFilter source = null;
            foreach (MeshFilter candidate in _importedAnatomy.GetComponentsInChildren<MeshFilter>(true))
            {
                if (candidate.name == sourceMeshName)
                {
                    source = candidate;
                    break;
                }
            }

            Transform sourceAnchor = FindDescendant(_importedAnatomy.transform, sourceBoneName);
            Transform sourceHips = FindDescendant(_importedAnatomy.transform, "rig:Hips");
            Transform sourceChest = FindDescendant(_importedAnatomy.transform, "rig:Spine2")
                ?? FindDescendant(_importedAnatomy.transform, "rig:Spine1");
            Transform sourceLeftArm = FindDescendant(_importedAnatomy.transform, "rig:LeftArm");
            Transform sourceRightArm = FindDescendant(_importedAnatomy.transform, "rig:RightArm");
            Transform targetShoulder = Bone(targetBone);
            Transform targetHips = Bone(HumanBodyBones.Hips);
            Transform targetChest = Bone(HumanBodyBones.Chest)
                ?? Bone(HumanBodyBones.UpperChest)
                ?? Bone(HumanBodyBones.Spine);
            Transform targetNeck = Bone(HumanBodyBones.Neck);
            Transform targetLeftArm = Bone(HumanBodyBones.LeftUpperArm);
            Transform targetRightArm = Bone(HumanBodyBones.RightUpperArm);
            if (source == null || source.sharedMesh == null || sourceAnchor == null || sourceHips == null || sourceChest == null
                || sourceLeftArm == null || sourceRightArm == null || targetShoulder == null || targetHips == null
                || targetChest == null || targetNeck == null || targetLeftArm == null || targetRightArm == null)
                return;

            Quaternion sourceFrame = BuildAnatomicalFrame(sourceHips, sourceChest, sourceLeftArm, sourceRightArm);
            Mesh mesh = Instantiate(source.sharedMesh);
            mesh.name = $"XRay {sourceMeshName}";
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldVertex = source.transform.TransformPoint(vertices[i]);
                vertices[i] = Quaternion.Inverse(sourceFrame) * (worldVertex - sourceAnchor.position);
            }
            mesh.vertices = vertices;

            Vector3[] normals = mesh.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = (Quaternion.Inverse(sourceFrame) * source.transform.TransformDirection(normals[i])).normalized;
                mesh.normals = normals;
            }
            mesh.RecalculateBounds();

            GameObject visual = new GameObject($"XRay {sourceMeshName}") { hideFlags = HideFlags.DontSave };
            visual.transform.SetParent(_anatomyRoot.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _anatomyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _shoulderMeshes.Add(new ShoulderMeshBinding
            {
                Visual = visual.transform,
                TargetShoulder = targetShoulder,
                TargetHips = targetHips,
                TargetChest = targetChest,
                TargetNeck = targetNeck,
                TargetLeftArm = targetLeftArm,
                TargetRightArm = targetRightArm,
                SourceWidth = Vector3.Distance(sourceRightArm.position, sourceLeftArm.position),
                SourceHeight = Vector3.Distance(sourceChest.position, sourceHips.position)
            });
        }

        private void AddPelvisMesh()
        {
            MeshFilter source = null;
            foreach (MeshFilter candidate in _importedAnatomy.GetComponentsInChildren<MeshFilter>(true))
            {
                if (candidate.name == "Hips")
                {
                    source = candidate;
                    break;
                }
            }

            Transform sourceHips = FindDescendant(_importedAnatomy.transform, "rig:Hips");
            Transform sourceChest = FindDescendant(_importedAnatomy.transform, "rig:Spine2")
                ?? FindDescendant(_importedAnatomy.transform, "rig:Spine1");
            Transform sourceLeftArm = FindDescendant(_importedAnatomy.transform, "rig:LeftArm");
            Transform sourceRightArm = FindDescendant(_importedAnatomy.transform, "rig:RightArm");
            Transform sourceLeftLeg = FindDescendant(_importedAnatomy.transform, "rig:LeftUpLeg");
            Transform sourceRightLeg = FindDescendant(_importedAnatomy.transform, "rig:RightUpLeg");
            Transform targetHips = Bone(HumanBodyBones.Hips);
            Transform targetChest = Bone(HumanBodyBones.Chest)
                ?? Bone(HumanBodyBones.UpperChest)
                ?? Bone(HumanBodyBones.Spine);
            Transform targetLeftArm = Bone(HumanBodyBones.LeftUpperArm);
            Transform targetRightArm = Bone(HumanBodyBones.RightUpperArm);
            Transform targetLeftLeg = Bone(HumanBodyBones.LeftUpperLeg);
            Transform targetRightLeg = Bone(HumanBodyBones.RightUpperLeg);
            if (source == null || source.sharedMesh == null || sourceHips == null || sourceChest == null
                || sourceLeftArm == null || sourceRightArm == null || sourceLeftLeg == null || sourceRightLeg == null
                || targetHips == null || targetChest == null || targetLeftArm == null || targetRightArm == null
                || targetLeftLeg == null || targetRightLeg == null)
                return;

            Quaternion sourceFrame = BuildAnatomicalFrame(sourceHips, sourceChest, sourceLeftArm, sourceRightArm);
            Mesh mesh = Instantiate(source.sharedMesh);
            mesh.name = "XRay Hips";
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldVertex = source.transform.TransformPoint(vertices[i]);
                vertices[i] = Quaternion.Inverse(sourceFrame) * (worldVertex - sourceHips.position);
            }
            mesh.vertices = vertices;

            Vector3[] normals = mesh.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = (Quaternion.Inverse(sourceFrame) * source.transform.TransformDirection(normals[i])).normalized;
                mesh.normals = normals;
            }
            mesh.RecalculateBounds();

            GameObject visual = new GameObject("XRay Hips") { hideFlags = HideFlags.DontSave };
            visual.transform.SetParent(_anatomyRoot.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _anatomyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _pelvisMeshes.Add(new PelvisMeshBinding
            {
                Visual = visual.transform,
                TargetHips = targetHips,
                TargetChest = targetChest,
                TargetLeftArm = targetLeftArm,
                TargetRightArm = targetRightArm,
                TargetLeftLeg = targetLeftLeg,
                TargetRightLeg = targetRightLeg,
                SourceLegWidth = Vector3.Distance(sourceRightLeg.position, sourceLeftLeg.position),
                SourceTorsoHeight = Vector3.Distance(sourceChest.position, sourceHips.position)
            });
        }

        private void BuildImportedThorax()
        {
            Transform sourceChest = FindDescendant(_importedAnatomy.transform, "rig:Spine2")
                ?? FindDescendant(_importedAnatomy.transform, "rig:Spine1");
            Transform sourceHips = FindDescendant(_importedAnatomy.transform, "rig:Hips");
            Transform targetChest = Bone(HumanBodyBones.Chest)
                ?? Bone(HumanBodyBones.UpperChest)
                ?? Bone(HumanBodyBones.Spine);
            Transform targetHips = Bone(HumanBodyBones.Hips);
            Transform sourceLeftArm = FindDescendant(_importedAnatomy.transform, "rig:LeftArm");
            Transform sourceRightArm = FindDescendant(_importedAnatomy.transform, "rig:RightArm");
            Transform targetLeftArm = Bone(HumanBodyBones.LeftUpperArm);
            Transform targetRightArm = Bone(HumanBodyBones.RightUpperArm);
            Transform targetNeck = Bone(HumanBodyBones.Neck);
            Transform targetHead = Bone(HumanBodyBones.Head);

            if (sourceChest == null || sourceHips == null || targetChest == null || targetHips == null
                || sourceLeftArm == null || sourceRightArm == null || targetLeftArm == null || targetRightArm == null)
            {
                Debug.LogWarning("[XRayAnatomyView] Imported thorax could not be bound to the Humanoid chest.", this);
                return;
            }

            foreach (SkinnedMeshRenderer renderer in _importedAnatomy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name != "Spine")
                    continue;

                Mesh baked = new Mesh { name = "XRay Imported Spine" };
                renderer.BakeMesh(baked);
                AddThoraxMesh(
                    baked, renderer.transform, sourceChest, sourceHips, sourceLeftArm, sourceRightArm,
                    targetChest, targetHips, targetLeftArm, targetRightArm, targetNeck, targetHead, "Spine", true);
                break;
            }

            foreach (MeshFilter filter in _importedAnatomy.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith("rib") || filter.name.EndsWith("_parent") || filter.sharedMesh == null)
                    continue;

                AddThoraxMesh(
                    filter.sharedMesh, filter.transform, sourceChest, sourceHips, sourceLeftArm, sourceRightArm,
                    targetChest, targetHips, targetLeftArm, targetRightArm, targetNeck, targetHead, filter.name, false);
            }
        }

        private void AddThoraxMesh(
            Mesh sourceMesh,
            Transform sourceMeshTransform,
            Transform sourceChest,
            Transform sourceHips,
            Transform sourceLeftArm,
            Transform sourceRightArm,
            Transform targetChest,
            Transform targetHips,
            Transform targetLeftArm,
            Transform targetRightArm,
            Transform targetNeck,
            Transform targetHead,
            string partName,
            bool isSpine)
        {
            if (sourceMesh == null || sourceMeshTransform == null)
                return;

            Quaternion sourceFrame = BuildAnatomicalFrame(sourceHips, sourceChest, sourceLeftArm, sourceRightArm);
            Mesh mesh = Instantiate(sourceMesh);
            mesh.name = $"XRay {partName}";
            Vector3[] vertices = mesh.vertices;
            float sourceMinY = float.PositiveInfinity;
            float sourceMaxY = float.NegativeInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldVertex = sourceMeshTransform.TransformPoint(vertices[i]);
                vertices[i] = Quaternion.Inverse(sourceFrame) * (worldVertex - sourceChest.position);
                sourceMinY = Mathf.Min(sourceMinY, vertices[i].y);
                sourceMaxY = Mathf.Max(sourceMaxY, vertices[i].y);
            }
            mesh.vertices = vertices;

            Vector3[] normals = mesh.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                {
                    Vector3 worldNormal = sourceMeshTransform.TransformDirection(normals[i]);
                    normals[i] = (Quaternion.Inverse(sourceFrame) * worldNormal).normalized;
                }
                mesh.normals = normals;
            }
            mesh.RecalculateBounds();

            GameObject visual = new GameObject($"XRay {partName}") { hideFlags = HideFlags.DontSave };
            visual.transform.SetParent(_anatomyRoot.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _anatomyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _thoraxMeshes.Add(new ThoraxMeshBinding
            {
                Visual = visual.transform,
                TargetChest = targetChest,
                TargetHips = targetHips,
                TargetLeftArm = targetLeftArm,
                TargetRightArm = targetRightArm,
                TargetNeck = targetNeck,
                TargetHead = targetHead,
                SourceWidth = Vector3.Distance(sourceRightArm.position, sourceLeftArm.position),
                SourceHeight = Vector3.Distance(sourceChest.position, sourceHips.position),
                SourceMinY = sourceMinY,
                SourceMaxY = sourceMaxY,
                IsSpine = isSpine
            });
        }

        private static Quaternion BuildAnatomicalFrame(Transform hips, Transform chest, Transform leftArm, Transform rightArm)
        {
            Vector3 up = chest.position - hips.position;
            Vector3 right = rightArm.position - leftArm.position;
            if (up.sqrMagnitude < 0.0001f || right.sqrMagnitude < 0.0001f)
                return chest.rotation;

            up.Normalize();
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, up).normalized;
            if (forward.sqrMagnitude < 0.0001f)
                return chest.rotation;

            // Cross product has two valid signs. Keep the one that agrees with the authored
            // chest forward axis so front/back ribs and the spine retain their intended side.
            if (Vector3.Dot(forward, chest.forward) < 0f)
                forward = -forward;

            Vector3 correctedRight = Vector3.Cross(up, forward).normalized;
            return Quaternion.LookRotation(forward, Vector3.Cross(forward, correctedRight).normalized);
        }

        private static Quaternion BuildHeadFrame(Transform neck, Transform head, Vector3 bodyForward)
        {
            Vector3 up = head.position - neck.position;
            if (up.sqrMagnitude < 0.0001f)
                return head.rotation;

            up.Normalize();
            Vector3 forward = Vector3.ProjectOnPlane(bodyForward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(head.forward, up);
            return forward.sqrMagnitude < 0.0001f
                ? head.rotation
                : Quaternion.LookRotation(forward.normalized, up);
        }

        private void AddBakedMeshAnchor(string sourceRendererName, HumanBodyBones bone, float scale)
        {
            SkinnedMeshRenderer source = null;
            foreach (SkinnedMeshRenderer candidate in _importedAnatomy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.name == sourceRendererName)
                {
                    source = candidate;
                    break;
                }
            }

            Transform target = Bone(bone);
            if (source == null || target == null)
                return;

            Mesh baked = new Mesh { name = $"XRay Baked {sourceRendererName}" };
            source.BakeMesh(baked);
            GameObject visual = new GameObject($"XRay {sourceRendererName}") { hideFlags = HideFlags.DontSave };
            visual.transform.SetParent(_anatomyRoot.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = baked;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _anatomyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _directMeshAnchors.Add(new DirectMeshAnchor { Visual = visual.transform, Bone = target, Scale = scale, LocalRotation = Quaternion.identity });
        }

        private void AddDirectMeshAnchor(string sourceMeshName, HumanBodyBones bone, float scale)
        {
            MeshFilter source = null;
            foreach (MeshFilter candidate in _importedAnatomy.GetComponentsInChildren<MeshFilter>(true))
            {
                if (candidate.name == sourceMeshName)
                {
                    source = candidate;
                    break;
                }
            }

            Transform target = Bone(bone);
            if (source == null || source.sharedMesh == null || target == null)
                return;

            GameObject visual = new GameObject($"XRay {sourceMeshName}") { hideFlags = HideFlags.DontSave };
            visual.transform.SetParent(_anatomyRoot.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _anatomyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Quaternion localRotation = source.transform.parent == null
                ? Quaternion.identity
                : Quaternion.Inverse(source.transform.parent.rotation) * source.transform.rotation;
            _directMeshAnchors.Add(new DirectMeshAnchor { Visual = visual.transform, Bone = target, Scale = scale, LocalRotation = localRotation });
        }

        private bool ConfigureImportedAnimator()
        {
            if (_importedAnimator == null || !_importedAnimator.isHuman || _animator == null || _animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning("[XRayAnatomyView] Imported anatomy or suspect has no compatible Humanoid Animator.", this);
                return false;
            }

            // Keep the imported model's Avatar, but evaluate exactly the same state machine as the
            // suspect. Mecanim then retargets the Humanoid pose to the anatomy rig automatically.
            _importedAnimator.runtimeAnimatorController = _animator.runtimeAnimatorController;
            _importedAnimator.applyRootMotion = false;
            _importedAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _importedAnimator.updateMode = _animator.updateMode;
            _importedAnimator.enabled = true;

            try
            {
                _sourcePoseHandler = new HumanPoseHandler(_animator.avatar, _animator.transform);
                _importedPoseHandler = new HumanPoseHandler(_importedAnimator.avatar, _importedAnimator.transform);
            }
            catch (System.ArgumentException exception)
            {
                DisposePoseHandlers();
                Debug.LogWarning($"[XRayAnatomyView] Could not create Humanoid pose transfer: {exception.Message}", this);
            }

            return true;
        }

        private void SyncImportedAnimator()
        {
            if (_importedAnimator == null || _animator == null || !_importedAnimator.enabled)
                return;

            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float:
                        _importedAnimator.SetFloat(parameter.nameHash, _animator.GetFloat(parameter.nameHash));
                        break;
                    case AnimatorControllerParameterType.Int:
                        _importedAnimator.SetInteger(parameter.nameHash, _animator.GetInteger(parameter.nameHash));
                        break;
                    case AnimatorControllerParameterType.Bool:
                        _importedAnimator.SetBool(parameter.nameHash, _animator.GetBool(parameter.nameHash));
                        break;
                }
            }

            int layerCount = Mathf.Min(_animator.layerCount, _importedAnimator.layerCount);
            for (int layer = 0; layer < layerCount; layer++)
            {
                AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(layer);
                if (state.fullPathHash != 0)
                    _importedAnimator.Play(state.fullPathHash, layer, state.normalizedTime);
            }
            _importedAnimator.Update(0f);

            // Suspect clips are authored against their individual Transform hierarchies, so
            // assigning that controller to another Avatar does not reliably retarget every limb.
            // Copy the evaluated Humanoid muscle pose after the Animator update instead: this
            // preserves the imported anatomy Avatar's proportions while matching the suspect's
            // actual animated pose (including clips with generic Transform bindings).
            if (_sourcePoseHandler != null && _importedPoseHandler != null)
            {
                _sourcePoseHandler.GetHumanPose(ref _humanPose);
                _importedPoseHandler.SetHumanPose(ref _humanPose);
            }
        }

        private void DisposePoseHandlers()
        {
            _sourcePoseHandler?.Dispose();
            _importedPoseHandler?.Dispose();
            _sourcePoseHandler = null;
            _importedPoseHandler = null;
        }

        private static bool IsSourceSkinRenderer(Renderer renderer)
        {
            // CharacterRigSkin is a full demonstrator character. These renderers are its ordinary
            // skin/clothing, not anatomy. Keeping any one of them makes it look as if a second
            // person in a T-pose is standing inside the suspect.
            return renderer.name.Contains("Character")
                || renderer.name == "Body"
                || renderer.name == "Eyelashes"
                || renderer.name == "Shirt"
                || renderer.name == "Pants"
                || renderer.name == "Sneakers"
                || renderer.name == "LeftKnee"
                || renderer.name == "RightKnee";
        }

        private static bool IsDeferredAnatomyRenderer(Renderer renderer)
        {
            string name = renderer.name;
            return name.Contains("HandIndex")
                || name.Contains("HandMiddle")
                || name.Contains("HandPinky")
                || name.Contains("HandRing")
                || name.Contains("HandThumb")
                || name == "teeth"
                || name == "glands"
                || name == "heart"
                || name == "diaphragm"
                || name == "l_lung"
                || name == "r_lung"
                || name == "gallbladder"
                || name == "liver"
                || name == "pancreas"
                || name == "stomach"
                || name == "spleen"
                || name == "trachea"
                || name == "kidneys"
                || name == "ureter"
                || name == "small_intestine"
                || name == "colon"
                || name == "bladder";
        }

        private void AddImportedRigBinding(string modelBoneName, HumanBodyBones targetBone, string modelScaleReferenceName, HumanBodyBones targetScaleReference)
        {
            Transform modelBone = FindDescendant(_importedAnatomy.transform, modelBoneName);
            Transform target = Bone(targetBone);
            if (modelBone == null || target == null)
                return;

            Transform modelReference = string.IsNullOrEmpty(modelScaleReferenceName)
                ? null
                : FindDescendant(_importedAnatomy.transform, modelScaleReferenceName);
            Transform targetReference = targetScaleReference == HumanBodyBones.LastBone
                ? null
                : Bone(targetScaleReference);
            float sourceLength = modelReference == null ? 0f : Vector3.Distance(modelBone.position, modelReference.position);

            _importedRigBindings.Add(new ImportedRigBinding
            {
                ModelBone = modelBone,
                TargetBone = target,
                ModelScaleReference = modelReference,
                TargetScaleReference = targetReference,
                SourceLength = sourceLength
            });
        }

        private static Transform FindDescendant(Transform root, string targetName)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == targetName)
                    return transform;
            }

            return null;
        }

        private void BuildSkeleton()
        {
            CreateBoneBetween("Pelvis", HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, 0.11f);
            CreateBoneBetween("Spine", HumanBodyBones.Hips, HumanBodyBones.Spine, 0.075f);
            CreateBoneBetween("Chest", HumanBodyBones.Spine, HumanBodyBones.Chest, 0.08f);
            CreateBoneBetween("UpperChest", HumanBodyBones.Chest, HumanBodyBones.UpperChest, 0.075f);
            CreateBoneBetween("Neck", HumanBodyBones.UpperChest, HumanBodyBones.Neck, 0.05f);
            CreateBoneBetween("Head", HumanBodyBones.Neck, HumanBodyBones.Head, 0.09f);

            CreateLimb("LeftArm", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            CreateLimb("RightArm", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            CreateLimb("LeftLeg", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            CreateLimb("RightLeg", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);

            Transform chest = Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.UpperChest);
            if (chest == null)
                return;

            Transform chestAnchor = CreateBoneAnchor("Chest Anatomy Anchor", chest);

            for (int i = 0; i < 4; i++)
            {
                float y = 0.04f - i * 0.09f;
                CreateLocalCylinder($"Rib_{i + 1}", chestAnchor, new Vector3(0f, y, 0.01f), Quaternion.Euler(0f, 0f, 90f), 0.32f - i * 0.025f, 0.025f);
            }
        }

        private void BuildOrgans()
        {
            Transform chest = Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.UpperChest) ?? Bone(HumanBodyBones.Spine);
            if (chest == null)
                return;

            Transform chestAnchor = CreateBoneAnchor("Organ Anatomy Anchor", chest);

            CreateLocalPrimitive("Heart", PrimitiveType.Sphere, chestAnchor, new Vector3(-0.08f, -0.02f, 0.10f), new Vector3(0.19f, 0.24f, 0.16f), _anatomyMaterial);
            CreateLocalPrimitive("LeftLung", PrimitiveType.Sphere, chestAnchor, new Vector3(-0.16f, 0.06f, 0.06f), new Vector3(0.25f, 0.34f, 0.16f), _anatomyMaterial);
            CreateLocalPrimitive("RightLung", PrimitiveType.Sphere, chestAnchor, new Vector3(0.16f, 0.06f, 0.06f), new Vector3(0.25f, 0.34f, 0.16f), _anatomyMaterial);
            CreateLocalPrimitive("Liver", PrimitiveType.Sphere, chestAnchor, new Vector3(0.12f, -0.18f, 0.08f), new Vector3(0.38f, 0.16f, 0.19f), _anatomyMaterial);
            CreateLocalPrimitive("Stomach", PrimitiveType.Sphere, chestAnchor, new Vector3(-0.1f, -0.2f, 0.08f), new Vector3(0.27f, 0.19f, 0.18f), _anatomyMaterial);
            CreateLocalPrimitive("Anomalous Organ", PrimitiveType.Sphere, chestAnchor, new Vector3(0.02f, -0.06f, 0.16f), new Vector3(0.24f, 0.24f, 0.2f), _anomalyMaterial);
        }

        private void CreateLimb(string namePrefix, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones handOrFoot)
        {
            CreateBoneBetween($"{namePrefix}_Upper", upper, lower, 0.055f);
            CreateBoneBetween($"{namePrefix}_Lower", lower, handOrFoot, 0.045f);
        }

        private void CreateBoneBetween(string partName, HumanBodyBones fromBone, HumanBodyBones toBone, float radius)
        {
            Transform from = Bone(fromBone);
            Transform to = Bone(toBone);
            if (from == null || to == null)
                return;

            Vector3 direction = to.position - from.position;
            float length = direction.magnitude;
            if (length < 0.001f)
                return;

            GameObject part = CreatePrimitive(partName, PrimitiveType.Cylinder, _anatomyRoot.transform, _anatomyMaterial);
            Transform partTransform = part.transform;
            partTransform.position = Vector3.Lerp(from.position, to.position, 0.5f);
            partTransform.rotation = Quaternion.FromToRotation(Vector3.up, direction);
            partTransform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            _boneSegments.Add(new BoneSegment(partTransform, from, to, radius));
        }

        private void CreateLocalCylinder(string partName, Transform parent, Vector3 localPosition, Quaternion localRotation, float width, float radius)
        {
            GameObject part = CreatePrimitive(partName, PrimitiveType.Cylinder, parent, _anatomyMaterial);
            Transform partTransform = part.transform;
            partTransform.localPosition = localPosition;
            partTransform.localRotation = localRotation;
            partTransform.localScale = new Vector3(radius * 2f, width * 0.5f, radius * 2f);
        }

        private void CreateLocalPrimitive(string partName, PrimitiveType primitiveType, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = CreatePrimitive(partName, primitiveType, parent, material);
            Transform partTransform = part.transform;
            partTransform.localPosition = localPosition;
            partTransform.localRotation = Quaternion.identity;
            partTransform.localScale = localScale;
        }

        private Transform CreateBoneAnchor(string anchorName, Transform bone)
        {
            GameObject anchor = new GameObject(anchorName)
            {
                hideFlags = HideFlags.DontSave
            };
            Transform anchorTransform = anchor.transform;
            anchorTransform.SetParent(_anatomyRoot.transform, false);
            anchorTransform.SetPositionAndRotation(bone.position, bone.rotation);
            _boneAnchors.Add(new BoneAnchor(anchorTransform, bone));
            return anchorTransform;
        }

        private static GameObject CreatePrimitive(string partName, PrimitiveType primitiveType, Transform parent, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.name = partName;
            part.hideFlags = HideFlags.DontSave;
            part.transform.SetParent(parent, false);

            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part;
        }

        private Transform Bone(HumanBodyBones bone)
        {
            return _animator != null ? _animator.GetBoneTransform(bone) : null;
        }

        private void ApplyBodyMaterial()
        {
            if (_originalMaterials.Count > 0)
                return;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.transform.IsChildOf(_anatomyRoot.transform))
                    continue;

                if (renderer is not SkinnedMeshRenderer && renderer is not MeshRenderer)
                    continue;

                Material[] originals = renderer.sharedMaterials;
                if (originals == null || originals.Length == 0)
                    continue;

                _originalMaterials.Add(renderer, originals);
                Material[] xRayMaterials = new Material[originals.Length];
                for (int i = 0; i < xRayMaterials.Length; i++)
                    xRayMaterials[i] = _bodyMaterial;
                renderer.sharedMaterials = xRayMaterials;
            }
        }

        private void RestoreBodyMaterials()
        {
            foreach (KeyValuePair<Renderer, Material[]> entry in _originalMaterials)
            {
                if (entry.Key != null)
                    entry.Key.sharedMaterials = entry.Value;
            }
            _originalMaterials.Clear();
        }

        private void CaptureAndSetLayers(int isolatedLayer)
        {
            _layerStates.Clear();
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child == null)
                    continue;

                _layerStates.Add(new GameObjectLayerState(child.gameObject, child.gameObject.layer));
                child.gameObject.layer = isolatedLayer;
            }
        }

        private void RestoreLayers()
        {
            foreach (GameObjectLayerState state in _layerStates)
            {
                if (state.GameObject != null)
                    state.GameObject.layer = state.Layer;
            }
            _layerStates.Clear();
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material == null)
                return;

            Destroy(material);
            material = null;
        }
    }
}
