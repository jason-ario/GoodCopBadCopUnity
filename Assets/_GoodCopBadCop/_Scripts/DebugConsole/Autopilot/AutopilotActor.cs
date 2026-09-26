#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem.LowLevel;

namespace GoodCopBadCop.Autopilot
{
    /// <summary>Outcome of one actor step. Coroutines fill it in; callers read it after yielding.</summary>
    public class ActionResult
    {
        public bool Success;
        public string Failure;

        public ActionResult Fail(string reason)
        {
            Success = false;
            Failure = reason;
            return this;
        }

        public ActionResult Ok()
        {
            Success = true;
            Failure = null;
            return this;
        }
    }

    /// <summary>
    /// The bot's "hands". Every action goes through the same entry points the player uses:
    /// <list type="bullet">
    /// <item><b>Flow</b> mode calls Interactable.Interact / InteractWithItem and commits placement through
    /// ObjectPlacer + PlayerPickupController.DropObject — the exact calls PlayerInteractionController makes,
    /// minus aiming and walking.</item>
    /// <item><b>Input</b> mode walks (NavMesh path), aims (closed-loop right stick), verifies the interaction
    /// ray resolves the target, then presses the gamepad button. If the physical path fails the failure is
    /// reported as a StepFailed finding and the step is retried in Flow style (reported as an Assist).</item>
    /// </list>
    /// </summary>
    public class AutopilotActor
    {
        private const float PreconditionTimeout = 6f;

        private readonly AutopilotConfig _config;
        private readonly AutopilotReport _report;
        private readonly AutopilotInputDriver _input;
        private float _pitchSign = 1f;

        public AutopilotActor(AutopilotConfig config, AutopilotReport report, AutopilotInputDriver input)
        {
            _config = config;
            _report = report;
            _input = input;
        }

        private bool UseInput => _config.mode == AutopilotMode.Input && _input != null && _input.IsEnabled;

        // ── Public steps ────────────────────────────────────────────────────────

        /// <summary>Primary interaction with a world object (LMB when empty-handed, E while holding).</summary>
        public IEnumerator Interact(Interactable target, string label, ActionResult result)
        {
            yield return Preconditions(target, label, result);
            if (!result.Success) yield break;

            if (UseInput)
            {
                yield return InputInteract(target, useHeld: false, label, result);
                if (result.Success) yield break;
                ReportInputFailure(label, result.Failure);
            }

            DirectInteract(target, label, result);
        }

        /// <summary>Uses the held item on a target (LMB while holding) — stamping, filing, returning a stamp.</summary>
        public IEnumerator UseHeldOn(Interactable target, string label, ActionResult result)
        {
            yield return Preconditions(target, label, result);
            if (!result.Success) yield break;

            if (AutopilotGame.Held == null)
            {
                result.Fail($"{label}: nothing is held.");
                yield break;
            }

            if (UseInput)
            {
                yield return InputInteract(target, useHeld: true, label, result);
                if (result.Success) yield break;
                ReportInputFailure(label, result.Failure);
            }

            DirectUseHeld(target, label, result);
        }

        /// <summary>Places the held item at a surface point, or onto a PlacementBoard (e.g. the window hand-off).</summary>
        public IEnumerator PlaceHeld(Vector3 point, Vector3 surfaceNormal, PlacementBoard board, string label, ActionResult result)
        {
            PickableObject held = AutopilotGame.Held;
            if (held == null)
            {
                result.Fail($"{label}: nothing is held.");
                yield break;
            }

            if (UseInput)
            {
                yield return InputPlace(point, board, held, label, result);
                if (result.Success) yield break;
                ReportInputFailure(label, result.Failure);
            }

            DirectPlace(point, surfaceNormal, board, held, label, result);
        }

        public static IEnumerator WaitFor(Func<bool> condition, float timeout, ActionResult result, string failure)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end)
            {
                bool ok;
                try { ok = condition(); } catch { ok = false; }
                if (ok) { result.Ok(); yield break; }
                yield return null;
            }
            result.Fail(failure);
        }

        public static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        // ── Preconditions ───────────────────────────────────────────────────────

        private IEnumerator Preconditions(Interactable target, string label, ActionResult result)
        {
            if (target == null)
            {
                result.Fail($"{label}: target not found.");
                yield break;
            }

            yield return WaitFor(() => AutopilotGame.Interaction != null && AutopilotGame.CanInteract && !AutopilotGame.Paused,
                PreconditionTimeout, result, $"{label}: player could not interact (CanInteract stayed false).");
            if (!result.Success) yield break;

            PlayerInteractionController pic = AutopilotGame.Interaction;
            if (pic.onlyAllowedInteractable != null && pic.onlyAllowedInteractable != target)
            {
                result.Fail($"{label}: interaction is restricted to '{pic.onlyAllowedInteractable.name}'.");
                yield break;
            }

            if (!target.IsInteractable)
                result.Fail($"{label}: '{target.name}' is not interactable.");
        }

        private void ReportInputFailure(string label, string reason)
        {
            _report.Add(FindingKind.StepFailed, "medium", $"Input path failed: {label}", reason + "\n\n" + AutopilotGame.Snapshot());
            _report.Add(FindingKind.Assist, "low", $"Fell back to logic-level action for: {label}", screenshot: false);
        }

        // ── Direct (logic-level) implementations ────────────────────────────────

        private static void DirectInteract(Interactable target, string label, ActionResult result)
        {
            PlayerInteractionController pic = AutopilotGame.Interaction;
            try
            {
                if (AutopilotGame.Held != null) target.InteractAlternate(pic);
                else target.Interact(pic);
                result.Ok();
            }
            catch (Exception e)
            {
                result.Fail($"{label}: {e.GetType().Name} while interacting with '{target.name}': {e.Message}");
            }
        }

        private static void DirectUseHeld(Interactable target, string label, ActionResult result)
        {
            PlayerInteractionController pic = AutopilotGame.Interaction;
            PickableObject held = AutopilotGame.Held;
            bool compatible = target.itemsThatCanInteractWith != null && target.itemsThatCanInteractWith.Contains(held.ItemData);

            try
            {
                if (compatible)
                {
                    target.InteractWithItem(pic, held);
                    AutopilotGame.Pickup.TryUseObject();
                    result.Ok();
                }
                else if (target is IHeldItemPassthrough)
                {
                    target.Interact(pic);
                    result.Ok();
                }
                else
                {
                    result.Fail($"{label}: held '{held.ItemData?.name}' is not in '{target.name}'.itemsThatCanInteractWith " +
                                "(a player pressing LMB would get 'not compatible').");
                }
            }
            catch (Exception e)
            {
                result.Fail($"{label}: {e.GetType().Name} while using '{held.name}' on '{target.name}': {e.Message}");
            }
        }

        /// <summary>Mirrors PlayerInteractionController.CheckActivatePlacer followed by the RMB-release drop.</summary>
        private static void DirectPlace(Vector3 point, Vector3 normal, PlacementBoard board, PickableObject held, string label, ActionResult result)
        {
            ObjectPlacer placer = ObjectPlacer.Instance;
            PlayerPickupController pickup = AutopilotGame.Pickup;
            if (placer == null || pickup == null)
            {
                result.Fail($"{label}: ObjectPlacer or PlayerPickupController missing.");
                return;
            }

            try
            {
                PlacementSlot slot = board as PlacementSlot;
                Quaternion rotation = slot != null ? slot.SnapPoint.rotation
                    : board != null ? board.transform.rotation
                    : Quaternion.FromToRotation(Vector3.up, normal == Vector3.zero ? Vector3.up : normal);
                Vector3 position = slot != null ? slot.SnapPoint.position : point;

                if (placer.IsActive) placer.DeactivatePlacer();
                placer.SetPendingTargetRotation(rotation);
                placer.SetItem(held.ItemData);
                placer.transform.SetPositionAndRotation(position, rotation);
                placer.ActivatePlacer(board);
                placer.SetInRange(true);
                pickup.DropObject();
                result.Ok();
            }
            catch (Exception e)
            {
                result.Fail($"{label}: {e.GetType().Name} while placing '{held.name}': {e.Message}");
            }
        }

        // ── Input (virtual gamepad) implementations ─────────────────────────────

        private IEnumerator InputInteract(Interactable target, bool useHeld, string label, ActionResult result)
        {
            PlayerInteractionController pic = AutopilotGame.Interaction;
            float reach = Mathf.Max(1f, pic.interactDistance * 0.7f);

            yield return MoveTo(target.transform.position, reach, 25f, result);
            if (!result.Success) { result.Fail($"{label}: could not walk to '{target.name}' ({result.Failure})."); yield break; }

            bool aimed = false;
            foreach (Vector3 aimPoint in AimPoints(target))
            {
                yield return LookAt(aimPoint, 2.5f, 3f);
                if (RayResolvesTarget(target, out _)) { aimed = true; break; }
            }

            if (!aimed)
            {
                RayResolvesTarget(target, out string blocker);
                result.Fail($"{label}: interaction ray never resolved '{target.name}' (hit: {blocker}).");
                yield break;
            }

            bool holding = AutopilotGame.Held != null;
            if (useHeld || !holding) _input.PressRightTrigger();
            else _input.Press(GamepadButton.West);

            yield return null;
            yield return null;
            yield return null;
            result.Ok();
        }

        private IEnumerator InputPlace(Vector3 point, PlacementBoard board, PickableObject held, string label, ActionResult result)
        {
            Vector3 aim = board != null ? board.transform.position : point;
            yield return MoveTo(aim, 1.4f, 25f, result);
            if (!result.Success) { result.Fail($"{label}: could not walk to placement point ({result.Failure})."); yield break; }

            yield return LookAt(aim, 2f, 3f);
            _input.HoldLeftTrigger(true);

            float end = Time.realtimeSinceStartup + 2f;
            bool valid = false;
            while (Time.realtimeSinceStartup < end)
            {
                AimStep(aim);
                ObjectPlacer placer = ObjectPlacer.Instance;
                if (placer != null && placer.IsActive && placer.IsInRange &&
                    (board == null || placer.PlacementBoard == board))
                {
                    valid = true;
                    break;
                }
                yield return null;
            }

            yield return WaitSeconds(0.15f);
            _input.SetLook(Vector2.zero);
            _input.HoldLeftTrigger(false);

            if (!valid)
            {
                result.Fail($"{label}: placement ghost never became valid" + (board != null ? $" on '{board.name}'." : "."));
                yield break;
            }

            yield return WaitFor(() => AutopilotGame.Held != held, 2f, result, $"{label}: item was still held after releasing place.");
        }

        private IEnumerable<Vector3> AimPoints(Interactable target)
        {
            int mask = AutopilotGame.Interaction != null ? AutopilotGame.Interaction.interactLayer.value : ~0;
            var points = new List<Vector3>();
            foreach (Collider c in target.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || ((1 << c.gameObject.layer) & mask) == 0) continue;
                points.Add(c.bounds.center);
            }
            points.Add(target.transform.position);
            return points.Take(6);
        }

        /// <summary>Replicates PlayerInteractionController.TryGetNearestInteractHit for verification.</summary>
        private static bool RayResolvesTarget(Interactable target, out string blocker)
        {
            blocker = "nothing";
            PlayerInteractionController pic = AutopilotGame.Interaction;
            if (pic == null || pic.cam == null) return false;

            Ray ray = new Ray(pic.cam.transform.position, pic.cam.transform.forward);
            RaycastHit[] hits = Physics.RaycastAll(ray, 10f, pic.interactLayer);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                Collider col = hit.collider;
                Interactable candidate = col.GetComponent<Interactable>();
                InteractableCollider ic = col.GetComponent<InteractableCollider>();
                if (ic != null && ic.enabled) candidate = ic.Interactable;

                Interactable owner = candidate != null ? candidate : col.GetComponentInParent<Interactable>();
                if (owner != null && owner.PassesInteractionRayThrough) continue;

                if (candidate != null && candidate.IsInteractable)
                {
                    blocker = $"{candidate.name} @ {hit.distance:0.0}m";
                    return candidate == target && hit.distance <= pic.interactDistance;
                }

                if (!col.isTrigger)
                {
                    if (col.GetComponentInParent<BreakableGlassController>() != null) continue;
                    blocker = $"solid '{col.name}' @ {hit.distance:0.0}m";
                    return false;
                }
            }
            return false;
        }

        // ── Locomotion & aiming ─────────────────────────────────────────────────

        private IEnumerator MoveTo(Vector3 destination, float stopDistance, float timeout, ActionResult result)
        {
            PlayerInstance player = AutopilotGame.Player;
            if (player == null) { result.Fail("no player"); yield break; }

            List<Vector3> corners = PlanPath(player.transform.position, destination);
            int cornerIndex = 0;
            float end = Time.realtimeSinceStartup + timeout;
            Vector3 lastCheckPos = player.transform.position;
            float nextStuckCheck = Time.realtimeSinceStartup + 1.5f;
            int stuckCount = 0;
            float sidestepUntil = 0f;
            float sidestepDir = 1f;

            while (Time.realtimeSinceStartup < end)
            {
                player = AutopilotGame.Player;
                if (player == null) break;

                Vector3 pos = player.transform.position;
                if (Flat(destination - pos).magnitude <= stopDistance)
                {
                    StopMoving();
                    result.Ok();
                    yield break;
                }

                if (!AutopilotGame.CanControl)
                {
                    StopMoving();
                    nextStuckCheck = Time.realtimeSinceStartup + 1.5f;
                    yield return null;
                    continue;
                }

                while (cornerIndex < corners.Count - 1 && Flat(corners[cornerIndex] - pos).magnitude < 0.6f)
                    cornerIndex++;
                Vector3 corner = corners[Mathf.Min(cornerIndex, corners.Count - 1)];

                float yawErr = YawError(corner);
                float look = Mathf.Clamp(yawErr / 35f, -1f, 1f);
                if (Mathf.Abs(yawErr) > 3f && Mathf.Abs(look) < 0.15f) look = Mathf.Sign(look) * 0.15f;
                float pitchLook = PitchStick(-8f);
                _input.SetLook(new Vector2(look, pitchLook));

                bool sidestepping = Time.realtimeSinceStartup < sidestepUntil;
                float forward = Mathf.Abs(yawErr) < 40f ? 1f : 0.2f;
                _input.SetMove(new Vector2(sidestepping ? sidestepDir : 0f, sidestepping ? 0.3f : forward));

                if (Time.realtimeSinceStartup >= nextStuckCheck)
                {
                    if ((pos - lastCheckPos).magnitude < 0.25f)
                    {
                        stuckCount++;
                        if (stuckCount >= 4)
                        {
                            StopMoving();
                            result.Fail($"stuck near {pos:F1} heading to {corner:F1}");
                            yield break;
                        }
                        _input.Press(GamepadButton.South, 3);
                        sidestepDir = -sidestepDir;
                        sidestepUntil = Time.realtimeSinceStartup + 0.6f;
                    }
                    lastCheckPos = pos;
                    nextStuckCheck = Time.realtimeSinceStartup + 1.5f;
                }

                yield return null;
            }

            StopMoving();
            result.Fail("timed out walking");
        }

        private static List<Vector3> PlanPath(Vector3 from, Vector3 to)
        {
            var corners = new List<Vector3>();
            if (NavMesh.SamplePosition(from, out NavMeshHit a, 2f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(to, out NavMeshHit b, 3f, NavMesh.AllAreas))
            {
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status != NavMeshPathStatus.PathInvalid)
                    corners.AddRange(path.corners.Skip(1));
            }
            corners.Add(to);
            return corners;
        }

        private IEnumerator LookAt(Vector3 point, float tolerance, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            int settled = 0;
            while (Time.realtimeSinceStartup < end && settled < 3)
            {
                settled = AimStep(point) < tolerance ? settled + 1 : 0;
                yield return null;
            }
            _input.SetLook(Vector2.zero);
            yield return null;
        }

        /// <summary>One closed-loop aiming step. Returns the remaining angular error in degrees.</summary>
        private float AimStep(Vector3 point)
        {
            PlayerInteractionController pic = AutopilotGame.Interaction;
            if (pic == null || pic.cam == null) return 0f;

            Transform cam = pic.cam.transform;
            Vector3 dir = point - cam.position;
            float yawErr = YawError(point);
            float targetElev = Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;

            float x = Mathf.Clamp(yawErr / 25f, -1f, 1f);
            if (Mathf.Abs(yawErr) > 1f && Mathf.Abs(x) < 0.06f) x = Mathf.Sign(x) * 0.06f;
            _input.SetLook(new Vector2(x, PitchStick(targetElev)));

            float elevErr = targetElev - CurrentElevation();
            return Mathf.Max(Mathf.Abs(yawErr), Mathf.Abs(elevErr));
        }

        private float _lastElev;
        private float _lastPitchInput;
        private int _pitchWrongFrames;

        /// <summary>Right-stick Y needed to reach a target elevation; self-calibrates for inverted Y.</summary>
        private float PitchStick(float targetElevation)
        {
            float elev = CurrentElevation();

            if (Mathf.Abs(_lastPitchInput) > 0.3f)
            {
                float moved = elev - _lastElev;
                if (Mathf.Abs(moved) > 0.05f && Mathf.Sign(moved) != Mathf.Sign(_lastPitchInput * _pitchSign))
                {
                    if (++_pitchWrongFrames >= 5) { _pitchSign = -_pitchSign; _pitchWrongFrames = 0; }
                }
                else _pitchWrongFrames = 0;
            }

            float err = targetElevation - elev;
            float y = Mathf.Clamp(err / 25f, -1f, 1f);
            if (Mathf.Abs(err) > 1f && Mathf.Abs(y) < 0.06f) y = Mathf.Sign(y) * 0.06f;
            y *= _pitchSign;

            _lastElev = elev;
            _lastPitchInput = y;
            return y;
        }

        private static float CurrentElevation()
        {
            PlayerInteractionController pic = AutopilotGame.Interaction;
            if (pic == null || pic.cam == null) return 0f;
            return Mathf.Asin(Mathf.Clamp(pic.cam.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private static float YawError(Vector3 point)
        {
            PlayerInteractionController pic = AutopilotGame.Interaction;
            Transform reference = pic != null && pic.cam != null ? pic.cam.transform : AutopilotGame.Player.transform;
            Vector3 fwd = Flat(reference.forward);
            Vector3 to = Flat(point - reference.position);
            if (fwd.sqrMagnitude < 1e-4f || to.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.SignedAngle(fwd, to, Vector3.up);
        }

        private void StopMoving()
        {
            _input.SetMove(Vector2.zero);
            _input.SetLook(Vector2.zero);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
#endif
