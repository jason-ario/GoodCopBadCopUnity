# Data and Content Map

Last deep survey: 2026-07-04.

## When To Read

Read this before changing ScriptableObjects, prefabs, scenes, authored content, suspect data, dialogue, environments, weapons, pickups, or content folder structure.

## Source-of-Truth Folders

Primary product folders:

- `Assets/_GoodCopBadCop/_Scenes`
- `Assets/_GoodCopBadCop/_Scripts`
- `Assets/_GoodCopBadCop/_Data`
- `Assets/_GoodCopBadCop/_Prefabs`
- `Assets/_GoodCopBadCop/_Settings`
- `Assets/_GoodCopBadCop/Timelines`
- `Assets/_GoodCopBadCop/Signals`

Do not treat `Assets/_Recovery` as canonical gameplay content unless the task explicitly asks.

## Data Folder Layout

Observed `_Data` directories:

- `Dialogue`
- `Doppelgangers`
- `Enemies`
- `Environments`
- `Ink Refill Actions`
- `Mutant NPCs`
- `Newspaper Date Contents`
- `Pickups`
- `Shop Actions`
- `Suspects`
- `Utility`
- `Weapons`
- `Words Lists`

Top-level important asset:

- `Assets/_GoodCopBadCop/_Data/CampaignData.asset`

## Prefab Folder Layout

Observed `_Prefabs` directories:

- `Characters`
- `Environment`
- `Equipment`
- `Game Systems`
- `Graffiti`
- `Guards`
- `Interactables`
- `Junk`
- `Particles`
- `Rat`
- `Shop`
- `Tasks`
- `Timelines`
- `UI`
- `Weapons`
- `Writings on Walls`

## Scenes

Observed `_Scenes` files:

- `Main.unity`
- `Cutscenes.unity`
- `Cutscenes 2.unity`
- `Cutscenes 3.unity`
- `Main (Clone).unity`
- `Mugshots.unity`
- `Neighborhood.unity`
- `TempUI.unity`

Build settings from snapshot:

- Main build scene: `Assets/_GoodCopBadCop/_Scenes/Main.unity`.
- `Assets/_GoodCopBadCop/_Scenes/Cutscenes.unity` exists as a disabled build scene entry.

Before editing scenes:

- Prefer code/data changes when possible.
- If scene references must change, inspect serialized diff carefully.
- Verify in Unity, not only with text search.

## ScriptableObject Types

Important content data classes:

- `SuspectData`: suspect identity, infection settings, dialogue pools, first-encounter dialogue, question responses, idle barks, ID data, and character prefab.
- `SuspectSet`: lists suspect pools for day/shift selection.
- `ScriptedDialogue`: authored monologue/choice sequence for `ScriptedDialogueRunner`.
- `PickableItemData`: pickup item definitions.
- `MeleeWeaponData`: melee weapon tuning.
- `MutantLineupSet`: mutant intruder prefab pool.
- `MutantIntruderData`: mutant lineup behavior/config data.
- `MutantEnemyData`: roaming mutant enemy tuning.
- `DoppelgangerData`: target suspect and doppelganger anomaly/visual config.
- `DoppelgangerLineupSet`: doppelganger pool and spawn chance.
- `Environment`: environment/weather/atmosphere data.
- `NewspaperContentScriptable`: newspaper date content.
- `WordsListScriptable`: word/reason lists.
- `TMPWobbleProfile`: subtitle wobble styling.
- `ShopPurchaseAction` and `RefillInkShopAction`: shop action assets.

## Suspect Content

Primary folders/files:

- `Assets/_GoodCopBadCop/_Data/Suspects`
- `Assets/_GoodCopBadCop/_Data/Suspects/Suspect Datas`
- `Assets/_GoodCopBadCop/_Scripts/Data/SuspectData.cs`
- `Assets/_GoodCopBadCop/_Scripts/Data/SuspectSet.cs`
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/DailySuspectManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/SuspectRunRecords.cs`

To add or change a suspect, check:

- `SuspectData.CharacterPrefab` has a valid `SuspectCharacter` prefab with `NetworkObject`.
- Dialogue arrays have entries for early/mid/final day bands where used.
- `introDialogue` is optional but, if assigned, is first-encounter persistent through `PlayerPrefs`.
- Infection values: `startingInfectionScore` and `dailyInfectionProgression`.
- Whether the suspect is included in the correct `SuspectSet`.
- Whether the suspect has anomaly components and `AnomalyController` category lists configured on prefab.
- Whether doppelganger or Day 1 scripted flows target this suspect.

## Day Content

Primary files:

- `Game Systems/CampaignManager.cs`
- `Game Systems/DayBase.cs`
- `Game Systems/Days/*.cs`
- Scene child objects under `CampaignManager`.

Day content lives mostly in scene objects with `DayBase` subclasses, not only in ScriptableObjects.

DayBase fields to inspect:

- `DayNumber`
- `IntroCutscene`
- `SuspectSet`
- `LockDoorDuringShift`
- `TutorialStepsToFire`
- `HasSupplyBoxDelivery`
- `SupplyBoxItemPrefabs`
- `CanFollowTrailEvent`

To add or change a day:

- Verify the day object is a child of `CampaignManager` so `CollectDays` can find it.
- Assign correct `DayNumber`.
- Assign or update `SuspectSet`.
- Check whether door locking should happen.
- Add any custom subclass logic with clean event unsubscribe in `DayDeactivated` and `OnDestroy`.
- Check `CampaignManager.AdvanceDay` and save-slot current day behavior.

## Anomaly and Checklist Content

Primary files:

- `Anomalies/Anomaly.cs`
- `Anomalies/AnomalyController.cs`
- `Tools/AnomalyCategory.cs`
- `Checklist/*`
- `Interactables/FolderController.cs`
- `Gameplay/SuspectController.cs`

Five scoring categories:

- Documentation -> `DocumentationAnomaly`
- Vitals -> `VitalsAnomaly`
- Behavior -> `BehaviorAnomaly`
- Mutations -> `MutationAnomaly`
- Supernatural -> `SupernaturalAnomaly`

Important rule:

- Scoring currently depends on C# type-name strings. Adding a category is not just adding an enum. It touches checklist UI/data, `AnomalyCategory`, `AnomalyController`, `FolderController`, and `SuspectController.CalculateCategoryScores`.

## Dialogue Content

Primary files/folders:

- `Assets/_GoodCopBadCop/_Data/Dialogue`
- `DialogueSystem/ScriptedDialogue.cs`
- `DialogueSystem/ScriptedDialogueRunner.cs`
- `DialogueSystem/DialogueManager.cs`
- `Data/SuspectData.cs`

Types of dialogue:

- Suspect entry/exit/random barks from `SuspectData`.
- First-encounter scripted dialogue from `SuspectData.introDialogue`.
- Day-specific scripted dialogue, such as `Day_01` Vlad/Ivan/Soldier/megaphone content.
- Megaphone dialogue through `ScriptedDialogueRunner.PlayMegaphoneDialogue`.

When changing dialogue:

- Check day-band arrays for early/mid/final days.
- Check uncanny overrides for fully mutated suspects.
- Check story mismatch answers for `StoryMismatchAnomaly`.
- Check `ScriptedDialogueRunner` camera keys and wobble profiles for scripted sequences.

## Tools, Shop, and Economy

Primary files/folders:

- `Assets/_GoodCopBadCop/_Data/Pickups`
- `Assets/_GoodCopBadCop/_Data/Weapons`
- `Assets/_GoodCopBadCop/_Data/Ink Refill Actions`
- `Assets/_GoodCopBadCop/_Data/Shop Actions`
- `Tool Locker/*`
- `Equipment System/PlayerPickupController.cs`
- `Gameplay/GlobalHostVariables.cs`
- `Interactables/ATM.cs`
- `Game Systems/SaveDataManager.cs`

Main concepts:

- Money/coupons are shared through `GlobalHostVariables.money`.
- Coupons can be physical pickups spawned by `ATM`.
- Purchases route through `PlayerPickupController` and/or locker/world shop interactables.
- Unlocks persist through `SaveDataManager.UnlockedShopItems`.
- Ink refill actions can use custom `ShopPurchaseAction` assets.

When changing shop content:

- Check price and unlock gating.
- Check server money deduction and refund paths.
- Check spawned item prefab has `NetworkObject` and `PickableObject`.
- Check save unlocks if item availability should persist.

## Night Threat Content

Primary files/folders:

- `Game Systems/BetweenShiftTaskManager.cs`
- `Game Systems/ISystemicThreat.cs`
- `Game Systems/Tasks/*`
- `Guidebook/TaskRegistry.cs`
- `_Prefabs/Tasks`

Current model:

- Prefer `ISystemicThreat` for new night/outside pressure systems.
- Register threat components in `BetweenShiftTaskManager._threatBehaviours`.
- Threat level should be a server-written NetworkVariable.
- Threats should provide `ThreatName`, `ThreatDescription`, `ThreatLevel`, and `ScoreWeight`.
- `TaskRegistry` displays threats in HUD/guidebook.

Legacy:

- `IBetweenShiftTask` exists but is marked obsolete.
- Old task scripts can still exist. Check scene wiring before using or deleting them.

## Persistence Map

Persistent or semi-persistent state:

- Save slots: `SaveDataManager` JSON file named `savedata.json` under `Application.persistentDataPath`.
- First encounters: `PlayerPrefs` keys prefixed with `Encountered_`.
- Run infection/killed/quarantine state: `SuspectRunRecords` runtime records initialized on start.
- Unlocked shop items and locks: arrays on `SaveSlot`.

When adding persistence:

- Decide whether the value belongs to save slots, runtime run records, PlayerPrefs, or scene state.
- Add migration/backward compatibility for existing JSON if needed.
- Avoid making content assets hold mutable runtime state.
