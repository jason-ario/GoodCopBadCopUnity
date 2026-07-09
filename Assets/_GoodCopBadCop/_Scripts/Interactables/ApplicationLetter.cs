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

    private readonly NetworkVariable<FixedString512Bytes> syncedFullName = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<FixedString512Bytes> syncedBirthDate = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<FixedString512Bytes> syncedSex = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<FixedString512Bytes> syncedIdNumber = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<FixedString512Bytes> syncedExpirationDate = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<FixedString512Bytes> syncedEntryReason = new(new FixedString512Bytes(string.Empty));
    private readonly NetworkVariable<bool> syncedVisible = new(true);

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
                false,
                suspectData.IDPhoto),
            suspectData);
    }

    public void SetPaperworkState(SuspectPaperworkState state, SuspectData suspectData)
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

        ApplySyncedState();
        ApplyFonts(suspectData);
    }

    public void ApplyPreviewState(SuspectPaperworkState state, SuspectData suspectData)
    {
        ApplyState(state);
        ApplyFonts(suspectData);
    }

    private void ApplySyncedState()
    {
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
    }

    private void ApplyFonts(SuspectData suspectData)
    {
        if (suspectData == null || suspectData.handwritingFont == null)
            return;

        nameText.font = suspectData.handwritingFont;
        reasonForEntryText.font = suspectData.handwritingFont;
        birthDateText.font = suspectData.handwritingFont;
        sexText.font = suspectData.handwritingFont;
        if (expirationDateText != null)
            expirationDateText.font = suspectData.handwritingFont;
    }

    private void OnFullNameChanged(FixedString512Bytes previous, FixedString512Bytes current) => nameText.text = current.ToString();
    private void OnBirthDateChanged(FixedString512Bytes previous, FixedString512Bytes current) => birthDateText.text = current.ToString();
    private void OnSexChanged(FixedString512Bytes previous, FixedString512Bytes current) => sexText.text = current.ToString();
    private void OnIdNumberChanged(FixedString512Bytes previous, FixedString512Bytes current) => idNumberText.text = current.ToString();
    private void OnExpirationDateChanged(FixedString512Bytes previous, FixedString512Bytes current) => SetText(expirationDateText, current.ToString());
    private void OnEntryReasonChanged(FixedString512Bytes previous, FixedString512Bytes current) => reasonForEntryText.text = current.ToString();
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
}
