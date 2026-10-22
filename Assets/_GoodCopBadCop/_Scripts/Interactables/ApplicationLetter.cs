using GoodCopBadCop.SuspectPaperwork;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class ApplicationLetter : FolderItem
{
    [SerializeField] private TextMeshPro nameText;
    [SerializeField] private TextMeshPro birthDateText;
    [SerializeField] private TextMeshPro sexText;
    [SerializeField] private TextMeshPro reasonForEntryText;
    [SerializeField] private TextMeshPro idNumberText;
    [SerializeField] private TextMeshPro expirationDateText;

    [Tooltip("Optional child object holding a hand-drawn entry reason (e.g. a scribbled doodle) " +
             "shown instead of reasonForEntryText. Toggled via SetEntryReasonDrawing — used by " +
             "one-off scripted encounters. Leave unassigned for normal suspects.")]
    [SerializeField] private GameObject entryReasonDrawing;

    private readonly NetworkVariable<FixedString512Bytes> syncedFullName = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<FixedString512Bytes> syncedBirthDate = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<FixedString512Bytes> syncedSex = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<FixedString512Bytes> syncedIdNumber = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<FixedString512Bytes> syncedExpirationDate = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<FixedString512Bytes> syncedEntryReason = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<bool> syncedVisible = new(true);
    private readonly NetworkVariable<bool> syncedShowDrawing = new(false);
    // Language-neutral paperwork description: each client rebuilds the form (including its anomalies) in its own language.
    private readonly NetworkVariable<PaperworkSpec> syncedSpec = new();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        syncedFullName.OnValueChanged += OnFullNameChanged;
        syncedBirthDate.OnValueChanged += OnBirthDateChanged;
        syncedSex.OnValueChanged += OnSexChanged;
        syncedIdNumber.OnValueChanged += OnIdNumberChanged;
        syncedExpirationDate.OnValueChanged += OnExpirationDateChanged;
        syncedEntryReason.OnValueChanged += OnEntryReasonChanged;
        syncedVisible.OnValueChanged += OnVisibleChanged;
        syncedShowDrawing.OnValueChanged += OnShowDrawingChanged;
        syncedSpec.OnValueChanged += OnSpecChanged;
        Loc.LanguageChanged += ApplySyncedState;

        ApplySyncedState();
    }

    public override void OnNetworkDespawn()
    {
        syncedFullName.OnValueChanged -= OnFullNameChanged;
        syncedBirthDate.OnValueChanged -= OnBirthDateChanged;
        syncedSex.OnValueChanged -= OnSexChanged;
        syncedIdNumber.OnValueChanged -= OnIdNumberChanged;
        syncedExpirationDate.OnValueChanged -= OnExpirationDateChanged;
        syncedEntryReason.OnValueChanged -= OnEntryReasonChanged;
        syncedVisible.OnValueChanged -= OnVisibleChanged;
        syncedShowDrawing.OnValueChanged -= OnShowDrawingChanged;
        syncedSpec.OnValueChanged -= OnSpecChanged;
        Loc.LanguageChanged -= ApplySyncedState;

        base.OnNetworkDespawn();
    }

    public void SetInfo(SuspectCharacter suspectCharacter)
    {
        SuspectData suspectData = suspectCharacter.Data;
        SetPaperworkState(
            new SuspectPaperworkState(
                suspectData.FirstName + " " + suspectData.LastName,
                suspectData.DateOfBirth,
                suspectData.Sex,
                suspectData.IDNumber,
                suspectData.FirstName + " " + suspectData.LastName,
                suspectData.DateOfBirth,
                suspectData.Sex,
                suspectData.IDNumber,
                suspectData.EntryPermitExpiryDate,
                string.Empty,
                suspectData.EntryPermitExpiryDate,
                suspectData.IsResident,
                true,
                true,
                false,
                suspectData.IDPhoto),
            suspectData);
    }

    public void SetPaperworkState(SuspectPaperworkState state, SuspectData suspectData, PaperworkSpec spec = default)
    {
        if (!IsServer)
            return;

        syncedFullName.Value = ToFixedString(state.ApplicationFullName);
        syncedBirthDate.Value = ToFixedString(state.ApplicationBirthDate);
        syncedSex.Value = ToFixedString(state.ApplicationSex);
        syncedIdNumber.Value = ToFixedString(state.ApplicationIdNumber);
        syncedExpirationDate.Value = ToFixedString(state.ApplicationExpirationDate);
        syncedEntryReason.Value = ToFixedString(state.EntryReason);
        syncedVisible.Value = state.ApplicationVisible;
        syncedSpec.Value = spec;

        ApplySyncedState();
    }

    public void ApplyPreviewState(SuspectPaperworkState state, SuspectData suspectData)
    {
        ApplyState(state);
    }

    /// <summary>
    /// Server-only. Toggles whether a hand-drawn <see cref="entryReasonDrawing"/> child is shown
    /// instead of <see cref="reasonForEntryText"/>. Used by one-off scripted encounters (e.g.
    /// Ocho's fake application) where the entry reason should be a visual doodle rather than text.
    /// </summary>
    public void SetEntryReasonDrawing(bool showDrawing)
    {
        if (!IsServer) return;
        syncedShowDrawing.Value = showDrawing;
        ApplyEntryReasonVisual(showDrawing);
    }

    /// <summary>
    /// With a replicated <see cref="PaperworkSpec"/>, rebuilds the form in the local language (misspelled name in the
    /// local alphabet, translated months/sex/entry reason, same anomalies as every other client). Visibility always
    /// comes from the replicated flag. Without a spec, the replicated English strings are shown.
    /// </summary>
    private void ApplySyncedState()
    {
        PaperworkSpec spec = syncedSpec.Value;
        SuspectData data = spec.IsValid ? SuspectDataLookup.Find(spec.DataName.ToString()) : null;
        if (data != null)
        {
            SuspectPaperworkState local = SuspectPaperworkService.BuildFromSpec(data, spec);
            ApplyState(new SuspectPaperworkState(
                local.ApplicationFullName, local.ApplicationBirthDate, local.ApplicationSex, local.ApplicationIdNumber,
                local.ApplicationFullName, local.ApplicationBirthDate, local.ApplicationSex, local.ApplicationIdNumber,
                local.ApplicationExpirationDate, local.EntryReason, string.Empty, false,
                syncedVisible.Value, syncedVisible.Value, false, null));
            return;
        }

        ApplyState(new SuspectPaperworkState(
            syncedFullName.Value.ToString(),
            syncedBirthDate.Value.ToString(),
            syncedSex.Value.ToString(),
            syncedIdNumber.Value.ToString(),
            syncedFullName.Value.ToString(),
            syncedBirthDate.Value.ToString(),
            syncedSex.Value.ToString(),
            syncedIdNumber.Value.ToString(),
            syncedExpirationDate.Value.ToString(),
            syncedEntryReason.Value.ToString(),
            string.Empty,
            false,
            syncedVisible.Value,
            syncedVisible.Value,
            false,
            null));
    }

    private void ApplyState(SuspectPaperworkState state)
    {
        nameText.text = state.ApplicationFullName;
        birthDateText.text = state.ApplicationBirthDate;
        sexText.text = state.ApplicationSex;
        idNumberText.text = state.ApplicationIdNumber;
        SetText(expirationDateText, state.ApplicationExpirationDate);
        reasonForEntryText.text = state.EntryReason;
        SetDocumentVisible(state.ApplicationVisible);
        ApplyEntryReasonVisual(syncedShowDrawing.Value);
    }

    private void ApplyEntryReasonVisual(bool showDrawing)
    {
        if (entryReasonDrawing != null)
            entryReasonDrawing.SetActive(showDrawing);

        if (reasonForEntryText != null)
            reasonForEntryText.gameObject.SetActive(!showDrawing);
    }

    private void OnSpecChanged(PaperworkSpec previous, PaperworkSpec current) => ApplySyncedState();
    private void OnFullNameChanged(FixedString512Bytes previous, FixedString512Bytes current) => ApplySyncedState();
    private void OnBirthDateChanged(FixedString512Bytes previous, FixedString512Bytes current) => ApplySyncedState();
    private void OnSexChanged(FixedString512Bytes previous, FixedString512Bytes current) => ApplySyncedState();
    private void OnIdNumberChanged(FixedString512Bytes previous, FixedString512Bytes current) => ApplySyncedState();
    private void OnExpirationDateChanged(FixedString512Bytes previous, FixedString512Bytes current) => ApplySyncedState();
    private void OnEntryReasonChanged(FixedString512Bytes previous, FixedString512Bytes current) => ApplySyncedState();
    private void OnShowDrawingChanged(bool previous, bool current) => ApplyEntryReasonVisual(current);
    private void OnVisibleChanged(bool previous, bool current)
    {
        SetDocumentVisible(current);
    }

    private void SetDocumentVisible(bool visible)
    {
        foreach (Renderer documentRenderer in GetComponentsInChildren<Renderer>(true))
        {
            documentRenderer.enabled = visible;
        }

        SetInteractable(visible);
    }

    private static FixedString512Bytes ToFixedString(string value)
    {
        const int maxCharacters = 120;
        string safeValue = value ?? string.Empty;
        if (safeValue.Length > maxCharacters)
            safeValue = safeValue.Substring(0, maxCharacters);

        return new FixedString512Bytes(safeValue);
    }

    private static void SetText(TextMeshPro target, string value)
    {
        if (target != null)
            target.text = value;
    }
    
    public void SetInsideFolder(FolderController folder)
    {
        insideThisFolder = folder;
    }
    
    public override void OnEquipped(PlayerPickupController player)
    {
        base.OnEquipped(player);
      
        if (insideThisFolder)
        {
            insideThisFolder.RemoveDocument(this, player);
        }
    }

    // LMB inspect, same as the newspaper / daily fax (Application.asset uses the
    // HoldingNewspaper pose so it shares the Reading Newspaper animation).
    // Zoom mode "holds LMB" through the same path (see HeldDocumentInspection).
    /// <summary>LMB reads the letter up close.</summary>
    public override string GetHeldUseVerb() => Loc.N("Read");

    public override void OnStartUse()
    {
        base.OnStartUse();
        HeldDocumentInspection.BeginFromUse(playerPickupController);
    }

    public override void OnStopUse()
    {
        base.OnStopUse();
        HeldDocumentInspection.EndFromUse(playerPickupController);
    }
}
