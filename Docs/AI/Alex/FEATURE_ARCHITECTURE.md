# Feature Architecture

Last updated: 2026-07-05.

This is Alex's target architecture for new significant gameplay/UI/networking features.
Apply it to new modules first. Do not refactor old systems into this shape unless the current task needs it.

## Summary

Use this feature shape:

```text
FeatureModel
  Observable/read API plus mutable reactive internals.

FeatureService
  Commands, rules, validation, and mutations of FeatureModel.

Presenter / View / Adapter
  Unity scene, UI, input, animation, and Netcode integration.

Config / Data
  ScriptableObject or static content data, not runtime state.
```

The short rule:

```text
Model = observable state.
Service = commands and rules.
Presenter/Adapter = Unity/Netcode/UI bridge.
R3 = state and events.
UniTask = async flows.
DOTween = simple tween animations.
DI = controls who can see concrete implementations.
```

Selected dependencies:

- R3: `com.cysharp.r3` via OpenUPM.
- UniTask: `com.cysharp.unitask` via OpenUPM.
- VContainer: `jp.hadashikick.vcontainer` via OpenUPM.
- DOTween/DOTween Pro: asset-based dependency already present under `Assets/Plugins/Demigiant`, not a UPM dependency.
- Odin Inspector: asset-based editor/tooling dependency under `Assets/Plugins/Sirenix`.

## Public API

For each significant feature, prefer:

```text
IFeatureModel
IFeatureService
FeatureModel
FeatureService
FeaturePresenter / FeatureView / FeatureAdapter
```

## Namespace Rules

Use `GoodCopBadCop.*` namespaces for new architecture/framework code and new significant features.

Recommended namespace shape:

```text
GoodCopBadCop.FeatureName
GoodCopBadCop.Infrastructure.Persistence
GoodCopBadCop.Settings
GoodCopBadCop.EnvironmentSystem
GoodCopBadCop.Editor
GoodCopBadCop.Tests.Editor
```

Rules:

- New feature code should use namespaces from the start.
- New reusable infrastructure should live under `GoodCopBadCop.Infrastructure.*`.
- New tests should live under `GoodCopBadCop.Tests.*`.
- Keep legacy global-namespace code as-is unless the current task substantially rewrites that subsystem.
- Avoid mass namespace-only refactors across prefab-heavy or scene-heavy legacy scripts.
- When a legacy global class needs to consume namespaced new code, add an explicit `using` rather than moving the legacy class just for style.

## File And Naming Rules

Keep interfaces close to their only implementation:

```text
FeatureModel.cs
  IFeatureModel
  FeatureModel

FeatureService.cs
  IFeatureService
  FeatureService
```

Rules:

- If an interface has exactly one implementation, place the interface in the implementation file above the concrete class.
- Split an interface into its own file only when there are multiple real implementations, the file becomes too large, or a clear ownership boundary requires it.
- Name concrete classes by their architecture role: `FeatureModel`, `FeatureService`, `FeaturePresenter`, `FeatureView`, `FeatureAdapter`.
- Prefix enum type names with `E` so the entity kind is clear at call sites, for example `ESettingsMenuTab`.
- MonoBehaviour views should include the role keyword in the class name. Use `SettingsMenuView : ISettingsMenuView`, not `SettingsMenu : ISettingsMenuView`.
- When renaming a Unity component script, preserve the old `.meta` GUID by renaming the `.meta` file with the script. Otherwise prefabs and scenes can lose their component reference.

`IFeatureModel` contains state and read-side observable events:

```csharp
public interface IShiftModel
{
    ReadOnlyReactiveProperty<bool> IsActive { get; }
    ReadOnlyReactiveProperty<float> RemainingSeconds { get; }
    Observable<ShiftEnded> ShiftEnded { get; }
}
```

`IFeatureService` contains actions only:

```csharp
public interface IShiftService
{
    void StartShift(float duration);
    void Pause();
    void Resume();
    void EndShift();
}
```

Do not put current state into the service interface. State belongs to the model.

## Concrete Model

The concrete model is a public implementation class used by the feature service and DI wiring. It exposes mutable reactive fields directly for the service, while the public model interface stays read-only for regular consumers.

```csharp
public sealed class ShiftModel : IShiftModel
{
    public readonly ReactiveProperty<bool> IsActiveMutable = new(false);
    public readonly ReactiveProperty<float> RemainingSecondsMutable = new(0);
    public readonly Subject<ShiftEnded> ShiftEndedSubject = new();

    public ReadOnlyReactiveProperty<bool> IsActive => IsActiveMutable;
    public ReadOnlyReactiveProperty<float> RemainingSeconds => RemainingSecondsMutable;
    public Observable<ShiftEnded> ShiftEnded => ShiftEndedSubject;
}
```

Rules:

- Mutable fields inside the concrete model are allowed to be `public readonly`.
- The concrete model class is allowed to be `public`.
- External code should resolve only `IFeatureModel`, not `FeatureModel`.
- External code must never receive `ReactiveProperty<T>` or `Subject<T>`.
- Public model API exposes only `ReadOnlyReactiveProperty<T>` and `Observable<T>`.

## Service

The service owns gameplay rules and is the only intended mutator of the concrete model.

```csharp
public sealed class ShiftService : IShiftService
{
    private readonly ShiftModel _model;

    public ShiftService(ShiftModel model)
    {
        _model = model;
    }

    public void EndShift()
    {
        if (!_model.IsActive.CurrentValue)
            return;

        _model.IsActiveMutable.Value = false;
        _model.RemainingSecondsMutable.Value = 0;
        _model.ShiftEndedSubject.OnNext(new ShiftEnded());
    }
}
```

Service rules:

- Services contain commands, validation, transitions, and side effects.
- Services may use concrete models directly.
- Other systems should call `IFeatureService`, not mutate a model.
- Avoid command streams such as `Subject<Command>`. Use explicit service methods.

## Reactive Rule

Use this distinction:

```text
ReactiveProperty = current state.
Observable/Subject = one-shot fact.
Service method = command.
```

Examples of state:

```text
CurrentSuspect
RemainingSeconds
IsInterrogationActive
SelectedDialogue
CurrentShiftPhase
```

Examples of one-shot events:

```text
ShiftEnded
SuspectArrived
DialogueCompleted
VerdictSubmitted
ThreatTriggered
```

If a fact can be represented as current state, prefer state. Use an event only when the moment itself matters.

Good:

```csharp
shiftModel.RemainingSeconds.Subscribe(view.SetTimer);
shiftService.EndShift();
```

Avoid:

```csharp
commandSubject.OnNext(new EndShiftCommand());
```

## Persistent Reactive Properties

For simple local settings/preferences, use `PersistentReactiveProperty<T>` when a value should behave like an R3 `ReactiveProperty<T>` and also persist itself.

Use it for independent settings values:

```csharp
public sealed class SettingsModel : ISettingsModel
{
    public readonly PersistentReactiveProperty<float> MouseSensitivityMutable =
        new("settings.mouseSensitivity", 1f);

    public ReadOnlyReactiveProperty<float> MouseSensitivity => MouseSensitivityMutable;
}
```

Rules:

- `PersistentReactiveProperty<T>` inherits from `ReactiveProperty<T>`.
- Writing `Value` or calling `OnNext` updates the runtime value and saves it through `IPersistentPropertyStorage`.
- The default storage is `PlayerPrefsPersistentPropertyStorage`.
- Supported PlayerPrefs-backed types: `int`, `long`, `float`, `string`, `bool`, and `enum`.
- Use explicit default values for settings. Avoid relying on `default(T)` unless `0`, `false`, or empty string is really correct.
- Call `Flush()` when the settings menu closes, on application pause, or on application quit. Do not force `PlayerPrefs.Save()` on every slider tick.
- Expose persistent properties through model interfaces as `ReadOnlyReactiveProperty<T>`, not as mutable persistent properties.

Example service mutation:

```csharp
public void SetMouseSensitivity(float value)
{
    _model.MouseSensitivityMutable.Value = value;
}
```

Storage can be swapped later:

```csharp
new PersistentReactiveProperty<float>(
    "settings.mouseSensitivity",
    1f,
    jsonSettingsStorage);
```

Do not use `PersistentReactiveProperty<T>` for campaign/progression/save-slot data. Those systems need a real save layer with versioning, migrations, validation, and atomic writes.

## Observable Events

Events can live in `IFeatureModel` if they are part of the feature's read-side API.

```csharp
public interface ISuspectModel
{
    ReadOnlyReactiveProperty<SuspectData?> CurrentSuspect { get; }
    Observable<SuspectArrived> SuspectArrived { get; }
}
```

Use state for durable truth:

```text
CurrentSuspect = the suspect currently being handled.
```

Use event streams for impulses:

```text
SuspectArrived = play entrance animation, show toast, log analytics.
```

Never expose `Subject<T>` outside the concrete model.

## Unity Layer

MonoBehaviours should not own gameplay rules.

```text
View
  Holds Unity references and visual methods.

Presenter
  Subscribes to IFeatureModel and updates View.

Input/UI Handler
  Calls IFeatureService.

Adapter
  Bridges Unity, Netcode, save system, content, or legacy systems.
```

Views should look like this:

```csharp
public sealed class ShiftView : MonoBehaviour
{
    public void SetTimer(float seconds)
    {
        // Update TMP text, animator, or UI widgets.
    }
}
```

Presenters should subscribe and dispose subscriptions:

```csharp
public sealed class ShiftPresenter : IDisposable
{
    private readonly DisposableBag _disposables;

    public ShiftPresenter(IShiftModel model, ShiftView view)
    {
        model.RemainingSeconds.Subscribe(view.SetTimer).AddTo(ref _disposables);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
```

## Inspector And Editor Interfaces

Use Odin Inspector for project-specific inspector interfaces and debug tooling when it reduces custom editor code.

Good Odin use cases:

- Editor-only debug windows, for example next/previous environment.
- Read-only runtime state display in the Inspector.
- Safer ScriptableObject authoring with validation, grouping, dropdowns, and inline previews.
- Small internal tools that would otherwise need a one-off `CustomEditor`.

Rules:

- Odin attributes are UI/tooling only. They must not contain gameplay rules.
- Buttons should call existing services or adapter methods, not duplicate feature logic.
- Keep the same architecture boundary: model owns state, service owns commands, presenter/adapter/view bridges Unity.
- Prefer Odin for inspector tooling; use a hand-written `CustomEditor` only when Odin cannot express the workflow cleanly.
- Do not use Odin as runtime UI. Player-facing UI still belongs in normal Unity UI views and presenters.
- Avoid putting Odin dependencies into pure domain classes unless the class is intentionally an inspected Unity type such as a `MonoBehaviour` or `ScriptableObject`.

Debug editor tools:

- Prefer `OdinEditorWindow` under `Assets/_GoodCopBadCop/_Scripts/Editor` over scene debug objects.
- Use `EditorConstants.RootMenuPath` for project tools, currently `Tools/_GoodCopBadCop/`.
- Use `EditorConstants.RootMenuPriority` for project menu items so the project tool group stays near the top of Unity's `Tools` menu.
- Runtime debug windows may resolve services from the active `MainSceneLifetimeScope.Container` in Play Mode.
- Do not apply gameplay/rendering changes directly from the editor window; call the feature service.
- Do not run scene searches or DI resolves from Odin-drawn properties, `EnableIf`, or other draw-path methods.
- Cache resolved services/models and subscribe to model events when the window needs live state.
- Clear cached runtime references and subscriptions on Play Mode exit and window destroy.

Example:

```csharp
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;

public sealed class EnvironmentDebugWindow : OdinEditorWindow
{
    private IEnvironmentService service;

    [MenuItem(EditorConstants.EnvironmentDebugMenuPath)]
    private static void Open()
    {
        GetWindow<EnvironmentDebugWindow>("Environment Debug").Show();
    }

    [Button]
    private void ApplyNextEnvironment()
    {
        service?.ApplyNext();
    }
}
```

## Animation Rule

Use DOTween for simple tween-style animations:

- UI fade, scale, move, pulse, shake.
- Small feedback animations.
- Simple object movement or rotation.
- Short sequencing that does not need Animator state machines.

Use Animator, Timeline, or authored animation clips when the animation is stateful, character-driven, cinematic, heavily authored, or needs animation blending.

Rules:

- Do not put gameplay rules in tweens.
- Views may own DOTween calls for visual feedback.
- Presenters may trigger view animation methods in response to model state/events.
- Services should not depend on DOTween directly unless the feature is explicitly animation/service oriented.
- Async flows may await animation completion through UniTask only when sequencing matters.

## Netcode Rule

Reactive model state is local process state. It is not network replication by itself.

For networked features:

```text
Client input
  -> IFeatureService or NetworkBridge
  -> ServerRpc
  -> server FeatureService mutates authoritative model
  -> NetworkVariable/ClientRpc replicates
  -> client adapter updates local model projection
  -> UI reacts through subscriptions
```

Rules:

- Server owns authoritative gameplay state.
- Client models are local projections unless explicitly authoritative.
- Do not assume `ReactiveProperty<T>` is synced.
- Keep Netcode-specific code in adapters/bridges, not in the core service when possible.

## DI Rule

When adding DI, prefer controlled resolution over global access.

For this project, default to one scene-level master scope instead of a small `LifetimeScope` per feature:

```text
MainSceneLifetimeScope / GameLifetimeScope
  MonoBehaviour in Main.unity.
  Registers scene-wide services and feature models/services/presenters.
  Registers scene components from hierarchy when there is exactly one scene instance.
  Holds serialized refs to scene components/assets only when explicit references are clearer than hierarchy search.
```

Reason:

- The current Unity scene is already singleton/event-heavy.
- The environment/UI systems are still small.
- A `LifetimeScope` per small feature would add Unity object noise and make initialization order harder to read.
- Use feature/local child scopes only when there is a real lifetime boundary, repeated prefab instance boundary, or multiple independent copies of the same feature.

For scene views, prefer VContainer scene lookup when the feature has a single view instance:

```csharp
builder.RegisterComponentInHierarchy<ShiftView>().As<IShiftView>();
```

Expected registration shape:

```csharp
builder.Register<ShiftModel>(Lifetime.Singleton);
builder.Register<IShiftModel>(resolver => resolver.Resolve<ShiftModel>(), Lifetime.Singleton);
builder.Register<IShiftService, ShiftService>(Lifetime.Singleton);
```

Rules:

- External systems request `IFeatureModel` and `IFeatureService`.
- The feature service receives concrete `FeatureModel`.
- Do not register concrete models as public dependencies for unrelated systems.
- Prefer feature/lifetime scopes for large systems.

VContainer is the selected DI container for new feature architecture. Do not introduce Zenject unless Alex explicitly changes this decision.

## UniTask Rule

Use UniTask for Unity async flows that need clearer cancellation and sequencing than coroutines.

Good use cases:

- Dialogue sequences.
- Timed gameplay steps.
- Waiting for DOTween animation, Animator state, or scene operations.
- Async UI flows.
- Network or loading waits.

Rules:

- Every async flow should have a cancellation token from scene/object/feature lifetime.
- Avoid `async void`.
- Use fire-and-forget only for explicit entry points with exception handling.

## Boundaries

Minimum boundary for now:

```text
Concrete model is public.
Mutable fields are public readonly inside the concrete model.
External access goes through IFeatureModel.
Mutation goes through IFeatureService.
Code review rejects MonoBehaviour direct model writes.
```

Stronger boundary for large future features:

```text
GoodCopBadCop.FeatureName asmdef
  public FeatureModel
  public FeatureService
  public IFeatureModel
  public IFeatureService
```

Assembly boundaries are still useful for dependency control and compile-time separation, but this standard does not rely on hidden concrete classes. The boundary is enforced by DI registration, interface-based consumption, and review.

## When Not To Use This Pattern

Do not force this pattern onto:

- Tiny one-off UI scripts.
- Pure data assets/configs.
- Old systems unless the task already touches them.
- Very hot per-frame paths where a direct method call is clearer.
- Network replication primitives themselves.

Use it where a feature has meaningful state, commands, UI reactions, async flow, or cross-system interaction.
