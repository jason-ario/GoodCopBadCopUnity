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

        private readonly Dictionary<Renderer, Material[]> _originalMaterials = new();
        private readonly List<BoneSegment> _boneSegments = new();
        private readonly List<BoneAnchor> _boneAnchors = new();
        private readonly List<GameObjectLayerState> _layerStates = new();

        private Animator _animator;
        private GameObject _anatomyRoot;
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
        }

        private void OnDestroy()
        {
            RestoreBodyMaterials();
            DestroyMaterial(ref _bodyMaterial);
            DestroyMaterial(ref _anatomyMaterial);
            DestroyMaterial(ref _anomalyMaterial);
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

            BuildSkeleton();
            BuildOrgans();
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
