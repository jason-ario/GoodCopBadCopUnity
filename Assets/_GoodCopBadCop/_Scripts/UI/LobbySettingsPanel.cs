using System;
using System.Collections.Generic;
using System.Text;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Pause menu "Lobby Settings" screen: who's in the session, who hosts it, the join code and
/// whether it's public. The host also gets the Public/Private toggle and a kick button on every
/// other player's row. Opened and closed by <see cref="PauseMenuController"/>.
/// </summary>
public class LobbySettingsPanel : MonoBehaviour
{
    [SerializeField] private PlayerInfoPanel rowPrefab;
    [SerializeField] private Transform rowsContainer;
    [SerializeField] private TMP_Text hostText;
    [SerializeField] private TMP_Text codeText;
    [SerializeField] private TMP_Text playerCountText;
    [Tooltip("Read-only visibility line, shown when the toggle is hidden (clients).")]
    [SerializeField] private TMP_Text visibilityText;
    [Tooltip("Host-only Public/Private toggle.")]
    [SerializeField] private LobbyVisibilityToggle visibilityToggle;
    [SerializeField] private Button backButton;

    // English source text; render-time localization translates code-set labels.
    private static readonly string HostFormat = Loc.N("HOST: {0}");
    private static readonly string CodeFormat = Loc.N("LOBBY CODE: {0}");
    private static readonly string PlayersFormat = Loc.N("PLAYERS: {0}/{1}");
    private static readonly string PublicText = Loc.N("Lobby: Public");
    private static readonly string PrivateText = Loc.N("Lobby: Private");
    private static readonly string HostStatus = Loc.N("HOST");
    private static readonly string YouStatus = Loc.N("YOU");

    private const string InviteSlotKey = "ui.start_campaign_screen.start_campaign_screen.startcampaignscreen.inviteslotlabel";
    private const string OpenSlotKey = "ui.start_campaign_screen.start_campaign_screen.startcampaignscreen.openslotlabel";
    private const string OverlayUnavailableKey = "ui.start_campaign_screen.start_campaign_screen.startcampaignscreen.overlayunavailablecopiedmessage";

    private static readonly Color HostStatusColor = new Color(0.72f, 0.52f, 0.05f, 1f);
    private static readonly Color YouStatusColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    /// <summary>Back button, gamepad B, or the lobby going away.</summary>
    public event Action BackRequested;

    private readonly List<PlayerInfoPanel> _rows = new List<PlayerInfoPanel>();
    private string _rosterKey;
    private int _openedFrame;

    private void Awake()
    {
        if (backButton != null)
            backButton.onClick.AddListener(RequestBack);
    }

    private void OnEnable()
    {
        _openedFrame = Time.frameCount;
        if (LobbyManager.Instance != null)
            LobbyManager.Instance.OnLobbyUpdated += Refresh;
        Loc.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    private void OnDisable()
    {
        if (LobbyManager.Instance != null)
            LobbyManager.Instance.OnLobbyUpdated -= Refresh;
        Loc.LanguageChanged -= OnLanguageChanged;
        ClearRows();
    }

    private void Update()
    {
        // B closes this screen (not the whole pause menu); skip the frame it opened on.
        if (Time.frameCount == _openedFrame) return;
        if (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false)
            RequestBack();
    }

    private void RequestBack() => BackRequested?.Invoke();

    private void OnLanguageChanged()
    {
        _rosterKey = null;
        Refresh();
    }

    /// <summary>Rebuilds the screen from the current Steam lobby.</summary>
    public void Refresh()
    {
        LobbyManager lobby = LobbyManager.Instance;
        if (lobby == null || lobby.CurrentLobby.Id == 0)
        {
            // Session ended underneath us (host left, kicked...): nothing to manage any more.
            ClearRows();
            if (isActiveAndEnabled)
                RequestBack();
            return;
        }

        Friend[] members = lobby.GetMembersSnapshot();
        Friend owner = lobby.CurrentLobby.Owner;

        // Steam names and the join code are shown verbatim, never localized.
        if (hostText != null)
            hostText.text = Loc.Verbatim(Loc.T(HostFormat, owner.Name));
        if (codeText != null)
            codeText.text = Loc.Verbatim(Loc.T(CodeFormat, lobby.CurrentJoinCode));
        if (playerCountText != null)
            playerCountText.text = Loc.T(PlayersFormat, members.Length, DevSpectatorConfig.MaxPlayers);

        if (visibilityToggle != null)
            visibilityToggle.Refresh();
        bool toggleShown = visibilityToggle != null && visibilityToggle.gameObject.activeSelf;
        if (visibilityText != null)
        {
            visibilityText.gameObject.SetActive(!toggleShown);
            visibilityText.text = lobby.IsLobbyPublic ? PublicText : PrivateText;
        }

        string key = BuildRosterKey(lobby, members, owner);
        if (key == _rosterKey && _rows.Count == Mathf.Max(members.Length, DevSpectatorConfig.MaxPlayers))
            return;

        ClearRows();
        _rosterKey = key;

        if (rowPrefab == null || rowsContainer == null)
            return;

        ulong localId = SteamClient.IsValid ? SteamClient.SteamId.Value : 0;
        foreach (Friend member in members)
        {
            bool isHost = member.Id == owner.Id;
            bool isLocal = member.Id.Value == localId;

            PlayerInfoPanel row = Instantiate(rowPrefab, rowsContainer);
            row.gameObject.SetActive(true);
            row.PopulateInfo(member.Name, false, isHost);

            if (isHost) row.SetStatus(HostStatus, HostStatusColor);
            else if (isLocal) row.SetStatus(YouStatus, YouStatusColor);
            else row.SetStatus(string.Empty, Color.clear);

            if (lobby.CanKick(member))
            {
                Friend target = member;
                row.SetKickAction(() => LobbyManager.Instance.KickPlayer(target));
            }
            else
            {
                row.SetKickAction(null);
            }

            _rows.Add(row);
        }

        SpawnInviteSlots(members.Length);

        if (rowsContainer is RectTransform rect)
            LayoutRebuilder.MarkLayoutForRebuild(rect);
    }

    /// <summary>
    /// Fills free seats with the same "Invite a Friend" / "Open Slot" rows as the main-menu lobby,
    /// so the card always shows every seat. Mid-game joins are supported, so the invite is live.
    /// </summary>
    private void SpawnInviteSlots(int occupiedSeats)
    {
        int openSeats = DevSpectatorConfig.MaxPlayers - occupiedSeats;
        if (openSeats <= 0) return;

        // Same sheet entries as StartCampaignScreen's serialized labels, so translations stay shared.
        string inviteLabel = LocOrDefault(InviteSlotKey, "Invite a Friend");
        string openLabel = LocOrDefault(OpenSlotKey, "Open Slot");

        for (int i = 0; i < openSeats; i++)
        {
            PlayerInfoPanel slot = Instantiate(rowPrefab, rowsContainer);
            slot.name = "Invite Slot";
            slot.gameObject.SetActive(true);
            slot.PopulateInviteSlot(inviteLabel, openLabel, InviteFriend);
            _rows.Add(slot);
        }
    }

    /// <summary>
    /// Same flow as <see cref="StartCampaignScreen.InviteFriend"/>: open Steam's friend-invite
    /// overlay for this lobby, or, when the overlay can't open (Steam not running, overlay
    /// disabled, Editor), copy the join code and say so.
    /// </summary>
    private static void InviteFriend()
    {
        LobbyManager lobby = LobbyManager.Instance;
        if (lobby == null) return;

        if (!IsSteamOverlayAvailable())
        {
            string code = lobby.CurrentJoinCode;
            if (string.IsNullOrEmpty(code)) return;
            GUIUtility.systemCopyBuffer = code;
            GoodCopBadCop.Networking.LobbyToast.Show(
                LocOrDefault(OverlayUnavailableKey, "Steam overlay unavailable - code copied, send it to a friend"));
            return;
        }

        lobby.OpenInviteFriendsPopup();
    }

    private static bool IsSteamOverlayAvailable()
    {
        if (!SteamClient.IsValid) return false;
        try { return SteamUtils.IsOverlayEnabled; }
        catch (Exception) { return false; }
    }

    private static string LocOrDefault(string key, string english) =>
        Loc.TryGet(key, out string text) && !string.IsNullOrEmpty(text) ? text : english;

    private static string BuildRosterKey(LobbyManager lobby, Friend[] members, Friend owner)
    {
        var sb = new StringBuilder();
        sb.Append(Loc.CurrentLanguage).Append('|').Append(owner.Id.Value).Append('|')
          .Append(lobby.CanKickPlayers ? 'K' : '-').Append('|');
        foreach (Friend member in members)
            sb.Append(member.Id.Value).Append(':').Append(member.Name).Append(';');
        return sb.ToString();
    }

    private void ClearRows()
    {
        foreach (PlayerInfoPanel row in _rows)
        {
            if (row != null)
                Destroy(row.gameObject);
        }
        _rows.Clear();
        _rosterKey = null;
    }
}
