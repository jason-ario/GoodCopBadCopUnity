# Known Risks

## Worktree Hygiene

- Always run `git status --short` before edits.
- The deep project survey on 2026-07-04 started from a clean worktree.
- Do not revert user changes, even if they appear while an AI session is running.

## Serialized Unity Assets

- Scene, prefab, material, `.asset`, Timeline, and `.meta` files can produce large diffs.
- Always inspect serialized diffs carefully.
- Avoid broad automated rewrites of Unity assets.

## Vendor Assets

- The repo contains many top-level vendor/imported asset folders.
- Do not modify vendor assets unless the task explicitly targets them.

## Product Code Organization

- Product scripts currently appear to live mostly in `Assembly-CSharp`, with no product asmdef found in the initial survey.
- Adding asmdefs would be a large architectural change and should not happen opportunistically.
- Most core systems are scene-singleton and event coupled; changing initialization order can break runtime behavior without compile errors.

## Netcode Complexity

- Many systems use `NetworkBehaviour`, `ServerRpc`, and `ClientRpc`.
- Changes must account for host, server, client, ownership, and late-join paths.
- Runtime-spawned gameplay prefabs generally need `NetworkObject` and NetworkManager registration.
- `NetworkObjectReference` resolution can be delayed on clients.
- `PlayerInstance.Instance` is local-player state, not a reliable server reference to any arbitrary player.

## Campaign and Shift Coupling

- `CampaignManager`, `ShiftManager`, `DailySuspectManager`, and day scripts communicate through events and singletons.
- In the current source, `CampaignManager` subscribes to `ShiftManager.OnShiftEnd` in both `Start` and `OnEnable`; check for double subscription behavior before touching day advance.
- `ShiftManager` comments and method names still mix older night/task language with the current systemic threat model.
- `Day_01` is a custom scripted flow and can override normal suspect scheduling, verdict hand-off, stamps, notebooks, and shift timing.

## Static Flags and Event Subscriptions

- `SuspectController` has many static debug/force/intercept flags. If they are not consumed/reset, the next suspect can be wrong.
- `HandOffPoint.BlockVerdict` is static and must be cleared after deferred verdict flows.
- Day scripts subscribe to static events like `SuspectController.OnSuspectArrived`, `FolderController.OnAnyFolderStamped`, and `ExamNotebook` events. Missing unsubscribe can create duplicate behavior across days.

## Persistence Split

- Save slots live in `SaveDataManager` JSON.
- First suspect encounters live in `PlayerPrefs`.
- Suspect infection/killed/quarantine records live in `SuspectRunRecords` runtime memory.
- Before adding progression state, decide which persistence layer owns it.

## Systemic Threat Legacy Area

- `ISystemicThreat` is the active between-shift model.
- `IBetweenShiftTask` is obsolete but old task scripts still exist.
- Some files still use "task" names for threat-like behavior, such as `TakeOutTrashTask`.
- Verify actual scene wiring before extending, deleting, or renaming old task code.

## Recovery and Demo Content

- `Assets/_Recovery` contains many recovery scenes and should not be treated as canonical gameplay content.
- Imported demo scenes and sample scripts exist in vendor folders.

## Shallow Missing-Reference Signals

- Initial text search found missing-script-like markers in a project volume profile and missing-prefab text in UMotion assets.
- Verify in Unity before attempting repairs; some matches may be harmless serialized text or plugin-specific data.
