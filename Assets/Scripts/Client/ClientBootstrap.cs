using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CubeArena.Shared;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CubeArena.Client
{
    // Character-select flow per section 6: pick a display name, quick-play into a
    // session, see the server-assigned colour. All UI is built from code — see
    // UiFactory.cs and CLAUDE.md's rule against hand-editing scenes/prefabs.
    public class ClientBootstrap : MonoBehaviour
    {
        private const string OnlineBackendUrlPrefKey = "BackendUrl_Online";
        private const string LanBackendUrlPrefKey = "BackendUrl_Lan";
        private const string RememberMePrefKey = "RememberMe";
        private const string RememberedEmailPrefKey = "RememberedEmail";

        private ClientConfig _config;
        private AuthClient _auth;
        private SessionClient _session;
        private string _currentBackendUrl;

        private Canvas _canvas;
        private GameObject _mainMenuPanel;
        private GameObject _startGamePanel;
        private GameObject _optionsPanel;
        private GameObject _aboutPanel;
        private GameObject _loginPanel;
        private GameObject _characterSelectPanel;
        private GameObject _connectingPanel;
        private GameObject _hudPanel;
        private GameObject _matchHudPanel;
        private GameObject _pausePanel;
        private GameObject _lobbyPanel;
        private GameObject _tabScoreboardPanel;
        private GameObject _gameOverPanel;
        private GameObject _voteEndPanel;
        private GameObject _minimap;
        private PlayerController _localPlayer;
        private CameraFollow _cameraFollow;
        private bool _isPaused;
        private bool _canRejoin;
        private bool _gameOverToMainMenu;
        private Text _lobbyStatusText;
        private Text _voteEndStatusText;
        private Button _startMatchButton;
        private Text _loginStatus;
        private Text _selectStatus;
        private Text _timerText;
        private Text _tabNamesText;
        private Text _tabScoresText;
        private Text _gameOverResultText;
        private Text _gameOverNamesText;
        private Text _gameOverScoresText;
        private Image _hpBarFill;
        private Text _hpText;
        private Image _manaBarFill;
        private Text _manaText;
        private Image _staminaBarFill;
        private Text _staminaText;
        private Text _quickPlayButtonText;
        private Image _menuBackground;
        private CanvasGroup _fadeGroup;
        private Coroutine _fadeCoroutine;
        private float _matchHudRefreshTimer;
        // At most 4 entries, refreshed every UpdateMatchHud tick (250ms) — kept around so
        // OnDisconnected can show a final scoreboard on the Game Over screen even after
        // every PlayerController has already been despawned by the disconnect itself.
        private readonly List<(string Name, int Score)> _lastKnownScoreboard = new();
        private InputField _serverField;
        private InputField _emailField;
        private InputField _passwordField;
        private InputField _displayNameField;
        private Toggle _rememberMeToggle;
        private bool _isLanMode;
        private GameObject _currentPanel;
        private readonly Stack<GameObject> _panelHistory = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
#if !UNITY_SERVER
            var go = new GameObject(nameof(ClientBootstrap));
            DontDestroyOnLoad(go);
            go.AddComponent<ClientBootstrap>();
#endif
        }

        private void Start()
        {
            _config = ClientConfig.FromEnvironment();

            ArenaBuilder.Build();
            BuildUi();

            // Subscribed unconditionally (not after connecting) so there's no race
            // between the transport "connected" event and the player object's spawn
            // message, which can arrive in either order.
            PlayerController.LocalPlayerSpawned += OnLocalPlayerSpawned;

            if (_config.AutoTestEnabled)
            {
                // Auto-test always targets CUBEARENA_BACKEND_URL directly, bypassing
                // whatever's typed into the server-address field.
                _currentBackendUrl = _config.BackendUrl;
                _auth = new AuthClient(_currentBackendUrl);
                _session = new SessionClient(_currentBackendUrl);
                _ = RunAutoTestAsync();
            }
        }

        private void OnDestroy()
        {
            PlayerController.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        }

        // Escape mirrors the on-screen Back buttons (GoBack) in the menus. While actually
        // playing it instead opens/closes a *local* pause overlay (TogglePause) — the
        // match itself can't pause (other players keep going regardless), so this only
        // ever freezes this client's own input/camera and gives a way back to the same
        // session, same as most multiplayer games' Escape menu. Leaving is still its own
        // explicit choice, from a button in that overlay.
        private void Update()
        {
            if (_hudPanel.activeSelf)
            {
                UpdateMatchHud();
                UpdateLobby();
                UpdateVoteEndPanel();

                if (_localPlayer != null && _hpBarFill != null)
                {
                    var hpFraction = _localPlayer.Health / PlayerController.MaxHealth;
                    _hpBarFill.fillAmount = hpFraction;
                    _hpText.text = $"{Mathf.RoundToInt(hpFraction * 100f)}%";
                }

                if (_localPlayer != null && _manaBarFill != null)
                {
                    var manaFraction = _localPlayer.Mana / PlayerController.MaxMana;
                    _manaBarFill.fillAmount = manaFraction;
                    _manaText.text = $"{Mathf.RoundToInt(manaFraction * 100f)}%";
                }

                // Every frame, not throttled like UpdateMatchHud — it drains/regens fast
                // enough (see MovementConstants.Stamina*) that a 4x/second update would
                // visibly stair-step.
                if (_localPlayer != null && _staminaBarFill != null)
                {
                    var staminaFraction = _localPlayer.Stamina / MovementConstants.StaminaMax;
                    _staminaBarFill.fillAmount = staminaFraction;
                    _staminaText.text = $"{Mathf.RoundToInt(staminaFraction * 100f)}%";
                }

                // Hold Tab for the full scoreboard — suppressed while paused so it doesn't
                // stack visually with the pause overlay.
                var showScoreboard = !_isPaused && Keyboard.current != null && Keyboard.current.tabKey.isPressed;
                if (_tabScoreboardPanel.activeSelf != showScoreboard)
                {
                    _tabScoreboardPanel.SetActive(showScoreboard);
                }
            }
            else if (_tabScoreboardPanel.activeSelf)
            {
                _tabScoreboardPanel.SetActive(false);
            }

            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            if (_hudPanel.activeSelf)
            {
                TogglePause();
            }
            else if (!_connectingPanel.activeSelf)
            {
                GoBack();
            }
        }

        private void TogglePause()
        {
            _isPaused = !_isPaused;
            _pausePanel.SetActive(_isPaused);
            _localPlayer?.SetInputPaused(_isPaused);
            RefreshCursorState();
        }

        private void OnResumeClicked() => TogglePause();

        // Single source of truth for whether the cursor/camera should be locked for
        // gameplay right now — true only once the match has actually started AND the
        // player isn't paused. Called every frame the HUD is active (see UpdateLobby)
        // so it's always current, not just recomputed at specific transition points —
        // that's what "during lobby i can move and collect, no mouse control to click
        // start" turned out to be: the cursor was hard-locked from the moment the
        // player spawned, with nothing accounting for the lobby needing it free to
        // click Start Match at all.
        private void RefreshCursorState()
        {
            var matchActive = MatchManager.Instance == null || MatchManager.Instance.MatchStarted;
            var shouldLock = matchActive && !_isPaused;
            var wasLocked = Cursor.lockState == CursorLockMode.Locked;

            if (_cameraFollow != null)
            {
                // Whenever the cursor is free for UI interaction — paused, or still in
                // the lobby — the camera needs to stop reading mouse-look too, or moving
                // the mouse to click a button (Resume, Start Match, whatever) silently
                // spins the camera in the background the same way the pause-only version
                // of this bug did.
                _cameraFollow.Paused = !shouldLock;
                if (shouldLock && !wasLocked)
                {
                    // Locking snaps the OS cursor back to center, which can register as a
                    // single huge synthetic delta on the next read — discard it so
                    // locking back in doesn't also snap the view to a random angle.
                    _cameraFollow.NotifyResumed();
                }
            }

            Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !shouldLock;
        }

        // Throttled to 4x/second — plenty for a countdown and scoreboard, and cheaper
        // than a FindObjectsByType scan every single frame. Everything read here
        // (MatchManager.Instance.TimeRemaining, each PlayerController's Score/
        // SlotIndex/DisplayName) is already replicated to every client via
        // NetworkVariables, so no extra networking is needed just to show it.
        private void UpdateMatchHud()
        {
            _matchHudRefreshTimer -= Time.deltaTime;
            if (_matchHudRefreshTimer > 0f)
            {
                return;
            }

            _matchHudRefreshTimer = 0.25f;

            if (MatchManager.Instance != null)
            {
                var remaining = Mathf.Max(0, Mathf.CeilToInt(MatchManager.Instance.TimeRemaining));
                _timerText.text = $"{remaining / 60}:{remaining % 60:D2}";
                // A plain countdown doesn't communicate urgency on its own — turning
                // reddish under a minute left does, without needing to read the number.
                _timerText.color = remaining <= 60 ? new Color(0.95f, 0.3f, 0.3f) : Color.white;
            }

            // FindObjectsByType also picks up the local, never-spawned player template
            // GameObject that ConnectToGameServer keeps around for NGO's network-prefab
            // registration (see PlayerController.CreateTemplate) — filter to only actually
            // spawned (i.e. real, connected) players.
            var players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            Array.Sort(players, (a, b) => b.Score.CompareTo(a.Score));
            var names = new StringBuilder();
            var scores = new StringBuilder();
            _lastKnownScoreboard.Clear();
            foreach (var player in players)
            {
                if (!player.IsSpawned)
                {
                    continue;
                }

                // Falls back to the slot's color name rather than "Slot N" — matches the
                // same default ClientBootstrap now submits when a player left the name
                // field blank at character select.
                var name = string.IsNullOrEmpty(player.DisplayName) ? PlayerColors.GetName(player.SlotIndex) : player.DisplayName;
                names.AppendLine(name);
                scores.AppendLine(player.Score.ToString());
                _lastKnownScoreboard.Add((name, player.Score));
            }

            _tabNamesText.text = names.ToString();
            _tabScoresText.text = scores.ToString();
        }

        // Shows/hides the lobby overlay based on MatchManager.Instance.MatchStarted, and
        // keeps the status text / Start button current for who's connected and who's
        // host. Every frame, not throttled like UpdateMatchHud — the moment the host
        // actually starts the match, this needs to disappear immediately, not up to
        // 250ms late.
        private void UpdateLobby()
        {
            RefreshCursorState();

            var matchManager = MatchManager.Instance;
            var networkManager = NetworkManager.Singleton;
            if (matchManager == null || networkManager == null)
            {
                return;
            }

            if (matchManager.MatchStarted)
            {
                _lobbyPanel.SetActive(false);
                return;
            }

            _lobbyPanel.SetActive(true);

            var isHost = IsLobbyHost(networkManager);
            var count = networkManager.ConnectedClientsIds.Count;
            _lobbyStatusText.text = isHost
                ? $"{count}/6 connected — start when everyone's ready."
                : $"{count}/6 connected — waiting for the host to start...";
            _startMatchButton.gameObject.SetActive(isHost);
        }

        // Shows the vote-to-end popup to everyone the instant anyone casts a vote
        // (MatchManager.EndMatchVoteCount > 0), not just whoever opens the pause menu —
        // that's the "pop up a poll for players" part. Hides again once the match has
        // actually started ending (votes reset) or if it somehow isn't running.
        private void UpdateVoteEndPanel()
        {
            var matchManager = MatchManager.Instance;
            var networkManager = NetworkManager.Singleton;
            if (matchManager == null || networkManager == null || !matchManager.MatchStarted)
            {
                _voteEndPanel.SetActive(false);
                return;
            }

            var votes = matchManager.EndMatchVoteCount;
            if (votes <= 0)
            {
                _voteEndPanel.SetActive(false);
                return;
            }

            _voteEndPanel.SetActive(true);
            var connected = networkManager.ConnectedClientsIds.Count;
            var needed = connected / 2 + 1;
            _voteEndStatusText.text = $"{votes}/{connected} voted to end the match\n({needed} needed for majority)";
        }

        private void OnVoteEndMatchClicked() => MatchManager.Instance?.CastEndMatchVote();

        // "Host" = whoever's been connected longest (the lowest client id) — the same
        // rule MatchManager.RequestStartMatchServerRpc enforces server-side, so the
        // button being visible here is never a false promise.
        private static bool IsLobbyHost(NetworkManager networkManager)
        {
            var min = ulong.MaxValue;
            foreach (var id in networkManager.ConnectedClientsIds)
            {
                if (id < min)
                {
                    min = id;
                }
            }

            return networkManager.LocalClientId == min;
        }

        private void OnStartMatchClicked() => MatchManager.Instance?.RequestStart();

        // Graceful leave (section 6): shut the connection down cleanly instead of just
        // letting the process die, so the server's disconnect callback (and therefore
        // the backend's release-slot call) fires immediately rather than waiting for a
        // transport timeout.
        private void OnApplicationQuit()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        private void BuildUi()
        {
            _canvas = UiFactory.CreateCanvas();
            _menuBackground = UiFactory.CreateBackground(_canvas.transform);
            BuildMainMenuPanel();
            BuildStartGamePanel();
            BuildOptionsPanel();
            BuildAboutPanel();
            BuildLoginPanel();
            BuildCharacterSelectPanel();
            BuildConnectingPanel();
            BuildGameOverPanel();
            BuildHud();
            BuildPausePanel();
            BuildLobbyPanel();
            BuildVoteEndPanel();
            BuildTabScoreboardPanel();
            BuildFadeOverlay(); // built last so it's the topmost sibling, drawing over every panel above
            ShowOnly(_mainMenuPanel);
        }

        // A brief black flash on every screen change (ShowOnly) — most noticeable
        // leaving an actual match back to character select, but applies uniformly to
        // every panel switch rather than special-casing which ones "deserve" it.
        private void BuildFadeOverlay()
        {
            var go = new GameObject("FadeOverlay", typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(_canvas.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = Color.black;

            _fadeGroup = go.GetComponent<CanvasGroup>();
            _fadeGroup.alpha = 0f;
            _fadeGroup.blocksRaycasts = false;
            _fadeGroup.interactable = false;
        }

        // A local-only overlay (see Update's TogglePause) — not part of the exclusive
        // ShowOnly panel set, since it sits on top of the HUD rather than replacing it.
        private void BuildPausePanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(300, 380));
            _pausePanel = panelRect.gameObject;
            _pausePanel.SetActive(false);
            UiFactory.CreateText(panelRect, "Paused", 28, new Vector2(0, 150), new Vector2(260, 40));
            UiFactory.CreateButton(panelRect, "Resume", new Vector2(0, 80), OnResumeClicked, new Vector2(220, 50));
            UiFactory.CreateButton(panelRect, "Options", new Vector2(0, 15), OnPauseOptionsClicked, new Vector2(220, 50));
            // Casts this player's vote — doesn't end the match on its own, needs a
            // majority of everyone currently connected (see MatchManager.
            // CastEndMatchVoteServerRpc). The live tally is its own popup
            // (BuildVoteEndPanel), visible to everyone the moment anyone votes, not just
            // whoever opens the pause menu.
            UiFactory.CreateButton(panelRect, "Vote to End Match", new Vector2(0, -50), OnVoteEndMatchClicked, new Vector2(220, 50));
            UiFactory.CreateButton(panelRect, "Leave Match", new Vector2(0, -115), OnLeaveClicked, new Vector2(220, 50), danger: true);
        }

        // Shown on top of the HUD from the moment a player spawns until
        // MatchManager.Instance.MatchStarted goes true (see Update's UpdateLobby).
        // Movement is frozen while this is up (PlayerController.IsMatchActive) — a lobby
        // that still lets you walk around and collect pickups isn't really a lobby.
        // Everyone sees the same panel; only whoever's currently "host" (lowest
        // connected client id) sees an enabled Start button instead of a "waiting"
        // message.
        private void BuildLobbyPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(320, 200));
            _lobbyPanel = panelRect.gameObject;
            _lobbyPanel.SetActive(false);
            UiFactory.CreateText(panelRect, "Lobby", 28, new Vector2(0, 70), new Vector2(280, 40));
            _lobbyStatusText = UiFactory.CreateText(panelRect, "", 16, new Vector2(0, 20), new Vector2(280, 50));
            _startMatchButton = UiFactory.CreateButton(panelRect, "Start Match", new Vector2(0, -55), OnStartMatchClicked, new Vector2(220, 50));
        }

        // A live poll, visible to every connected player the moment anyone casts a vote
        // to end the match (MatchManager.EndMatchVoteCount > 0) — not tucked away inside
        // the pause menu, so it actually "pops up" the way a poll should rather than only
        // being discoverable by whoever happens to open Esc. Resolves itself: once a
        // majority is reached the match ends and everyone gets disconnected, which hides
        // this along with the rest of the HUD.
        private void BuildVoteEndPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(320, 180));
            _voteEndPanel = panelRect.gameObject;
            _voteEndPanel.SetActive(false);
            UiFactory.CreateText(panelRect, "Vote to End Match", 22, new Vector2(0, 60), new Vector2(280, 36));
            _voteEndStatusText = UiFactory.CreateText(panelRect, "", 16, new Vector2(0, 10), new Vector2(280, 40));
            UiFactory.CreateButton(panelRect, "Vote Yes", new Vector2(0, -45), OnVoteEndMatchClicked, new Vector2(180, 46));
        }

        private void BuildMainMenuPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(340, 420));
            _mainMenuPanel = panelRect.gameObject;
            var title = UiFactory.CreateText(panelRect, "Cube Arena", 34, new Vector2(0, 165), new Vector2(300, 44));
            title.fontStyle = FontStyle.Bold;

            var buttonSize = new Vector2(280, 50);
            UiFactory.CreateButton(panelRect, "New Game", new Vector2(0, 80), OnStartGameClicked, buttonSize);
            UiFactory.CreateButton(panelRect, "Options", new Vector2(0, 15), OnOptionsClicked, buttonSize);
            UiFactory.CreateButton(panelRect, "About", new Vector2(0, -50), OnAboutClicked, buttonSize);
            UiFactory.CreateButton(panelRect, "Exit", new Vector2(0, -115), OnExitClicked, buttonSize, danger: true);
        }

        // Sub-menu for the two ways to play (section 6 / Tier-0 LAN hosting): Online
        // targets whatever backend the client is configured with by default, LAN targets
        // a host's local IP (see Start-CubeArena-Host.bat / docs/HOSTING.md). Both paths
        // land on the same login panel — the only difference is what's pre-filled into
        // the server-address field, which stays freely editable either way.
        private void BuildStartGamePanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(340, 260));
            _startGamePanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "New Game", 28, new Vector2(0, 85), new Vector2(300, 40));

            var buttonSize = new Vector2(280, 50);
            UiFactory.CreateButton(panelRect, "Online", new Vector2(0, 15), OnStartOnlineClicked, buttonSize);
            UiFactory.CreateButton(panelRect, "LAN", new Vector2(0, -45), OnStartLanClicked, buttonSize);
            UiFactory.CreateButton(panelRect, "Back", new Vector2(0, -110), OnBackClicked, new Vector2(120, 36));
        }

        // Doubles as "How to Play" — reachable both from the main menu and, via the pause
        // overlay's own Options button, from mid-game/mid-lobby too (ESC).
        private void BuildOptionsPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(400, 300));
            _optionsPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Options", 26, new Vector2(0, 120), new Vector2(340, 40));
            UiFactory.CreateText(panelRect, "How to play", 18, new Vector2(0, 80), new Vector2(340, 30));
            UiFactory.CreateText(panelRect,
                "WASD — move\nSpace — jump\nCtrl — crouch (fits under low tunnels)\n" +
                "C — crawl (for the lowest crawl tunnels)\nShift — sprint (costs mana, recharges over time)\n" +
                "Esc — pause\n\nCollect the gold pickups for points. Whoever's\n" +
                "connected longest hosts the lobby and starts the match.",
                14, new Vector2(0, -30), new Vector2(360, 190));
            UiFactory.CreateButton(panelRect, "Back", new Vector2(0, -135), OnBackClicked);
        }

        private void BuildAboutPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(380, 260));
            _aboutPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "About", 26, new Vector2(0, 95), new Vector2(300, 40));
            UiFactory.CreateText(panelRect,
                "Cube Arena\nA 6-player multiplayer prototype.\n\n" +
                "Server-authoritative movement, signed connect\ntickets, and a real dedicated game server.",
                14, new Vector2(0, 10), new Vector2(340, 110));
            UiFactory.CreateButton(panelRect, "Back", new Vector2(0, -95), OnBackClicked);
        }

        private void BuildLoginPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(380, 480));
            _loginPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Cube Arena", 28, new Vector2(0, 165), new Vector2(300, 40));

            // Editable so the same client build can point at a cloud-hosted backend or
            // a LAN host's local IP without rebuilding. Pre-filled by OnStartOnlineClicked
            // /OnStartLanClicked depending on which Start Game sub-menu option was picked;
            // whatever's typed is remembered — along with the email below — only if
            // "Remember me" is checked (see ApplyRememberedLoginFields/SaveRememberedLoginFields).
            _serverField = UiFactory.CreateInputField(panelRect, "server address", new Vector2(0, 110));

            _emailField = UiFactory.CreateInputField(panelRect, "email", new Vector2(0, 55));
            _passwordField = UiFactory.CreateInputField(panelRect, "password", new Vector2(0, 5), isPassword: true);
            panelRect.gameObject.AddComponent<TabNavigation>().SetFields(_serverField, _emailField, _passwordField);
            _rememberMeToggle = UiFactory.CreateToggle(panelRect, "Remember me", new Vector2(0, -35));
            UiFactory.CreateButton(panelRect, "Register", new Vector2(-95, -85), OnRegisterClicked);
            UiFactory.CreateButton(panelRect, "Login", new Vector2(95, -85), OnLoginClicked);
            _loginStatus = UiFactory.CreateText(panelRect, "", 14, new Vector2(0, -150), new Vector2(340, 50));
            UiFactory.CreateButton(panelRect, "Back", new Vector2(0, -215), OnBackClicked, new Vector2(120, 36));
        }

        // (Re)creates the auth/session clients if the server-address field has changed
        // since the last call. Persisting it for next launch happens separately, only if
        // "Remember me" is checked — see SaveRememberedLoginFields.
        private void EnsureClientsForServerField()
        {
            var url = string.IsNullOrWhiteSpace(_serverField.text) ? _config.BackendUrl : _serverField.text.Trim();
            if (_auth != null && _currentBackendUrl == url)
            {
                return;
            }

            _currentBackendUrl = url;
            _auth = new AuthClient(url);
            _session = new SessionClient(url);
        }

        private void BuildCharacterSelectPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(380, 300));
            _characterSelectPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Character Select", 24, new Vector2(0, 90), new Vector2(300, 40));
            _displayNameField = UiFactory.CreateInputField(panelRect, "display name", new Vector2(0, 30));
            var quickPlayButton = UiFactory.CreateButton(panelRect, "Quick Play", new Vector2(0, -30), OnQuickPlayClicked);
            _quickPlayButtonText = quickPlayButton.GetComponentInChildren<Text>();
            _selectStatus = UiFactory.CreateText(panelRect, "", 14, new Vector2(0, -90), new Vector2(340, 40));
            UiFactory.CreateButton(panelRect, "Back", new Vector2(0, -125), OnBackClicked, new Vector2(120, 36));
        }

        private void BuildConnectingPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(300, 120));
            _connectingPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Connecting...", 20, Vector2.zero, new Vector2(260, 60));
        }

        private void BuildHud()
        {
            // Own name is already on the nameplate above the character, and Leave is one
            // click away via Esc -> Pause -> Leave Match — this corner used to duplicate
            // both, which was pure clutter. HP/Mana/Stamina are the only things worth a
            // permanent glance mid-play. Three evenly-spaced rows, same label/bar/text
            // column layout throughout so they line up cleanly.
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(250, 150));
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(16, -16);
            _hudPanel = panelRect.gameObject;

            // HP and Mana have no gameplay behind them yet (nothing damages a player or
            // spends Mana) — this is scaffolding for whenever combat/abilities get
            // designed, always showing full for now. Stamina is the real one, driving
            // sprint.
            UiFactory.CreateText(panelRect, "HP", 12, new Vector2(-90, 55), new Vector2(40, 20));
            _hpBarFill = UiFactory.CreateBar(panelRect, new Vector2(10, 55), new Vector2(120, 16), new Color(0.85f, 0.2f, 0.2f));
            _hpText = UiFactory.CreateText(panelRect, "100%", 12, new Vector2(95, 55), new Vector2(50, 20));

            UiFactory.CreateText(panelRect, "MP", 12, new Vector2(-90, 5), new Vector2(40, 20));
            _manaBarFill = UiFactory.CreateBar(panelRect, new Vector2(10, 5), new Vector2(120, 16), new Color(0.2f, 0.45f, 0.9f));
            _manaText = UiFactory.CreateText(panelRect, "100%", 12, new Vector2(95, 5), new Vector2(50, 20));

            UiFactory.CreateText(panelRect, "SP", 12, new Vector2(-90, -45), new Vector2(40, 20));
            _staminaBarFill = UiFactory.CreateBar(panelRect, new Vector2(10, -45), new Vector2(120, 16), new Color(0.25f, 0.8f, 0.3f));
            // A numeric readout alongside the bar, not just for players — it's also the
            // easiest way to tell "stamina isn't draining" (a real gameplay bug) apart
            // from "the bar just isn't rendering the fill" (a UI-only one).
            _staminaText = UiFactory.CreateText(panelRect, "100%", 12, new Vector2(95, -45), new Vector2(50, 20));

            _minimap = Minimap.Create(_canvas.transform).gameObject;

            // Top-center: just the match clock now — the scoreboard moved to a Hold-Tab
            // overlay (BuildTabScoreboardPanel) instead of sitting on screen permanently.
            var matchPanelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(180, 70));
            matchPanelRect.anchorMin = matchPanelRect.anchorMax = new Vector2(0.5f, 1f);
            matchPanelRect.pivot = new Vector2(0.5f, 1f);
            matchPanelRect.anchoredPosition = new Vector2(0, -16);
            _matchHudPanel = matchPanelRect.gameObject;
            _timerText = UiFactory.CreateText(matchPanelRect, "5:00", 28, Vector2.zero, new Vector2(160, 50));
        }

        // Hold Tab to see the full scoreboard as a table (Player | Score) — moved out of
        // the always-on top-center panel, which now just shows the clock. A local-only
        // overlay like Pause/Lobby, not part of ShowOnly's exclusive set.
        private void BuildTabScoreboardPanel()
        {
            // Grown from the original 4-player sizing (320x260, 160-tall columns) to fit
            // up to 6 rows comfortably without clipping.
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(320, 340));
            _tabScoreboardPanel = panelRect.gameObject;
            _tabScoreboardPanel.SetActive(false);
            UiFactory.CreateText(panelRect, "Scoreboard", 24, new Vector2(0, 140), new Vector2(280, 36));

            _tabNamesText = UiFactory.CreateText(panelRect, "", 16, new Vector2(-70, 0), new Vector2(160, 220));
            _tabNamesText.alignment = TextAnchor.UpperLeft;

            _tabScoresText = UiFactory.CreateText(panelRect, "", 16, new Vector2(90, 0), new Vector2(80, 220));
            _tabScoresText.alignment = TextAnchor.UpperRight;
        }

        // Shown instead of going straight to character select when the disconnect reason
        // is the match timer running out (see OnDisconnected) — a real result screen
        // instead of just a status line, with the final scoreboard (from
        // _lastKnownScoreboard, snapshotted just before everyone got disconnected, since
        // every PlayerController is gone by the time this shows).
        private void BuildGameOverPanel()
        {
            // Grown from the original 4-player sizing (380x380, 160-tall columns) to fit
            // up to 6 rows comfortably without clipping.
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(380, 480));
            _gameOverPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Match Over", 28, new Vector2(0, 200), new Vector2(320, 40));
            _gameOverResultText = UiFactory.CreateText(panelRect, "", 16, new Vector2(0, 150), new Vector2(340, 70));

            UiFactory.CreateText(panelRect, "Final Scores", 18, new Vector2(0, 95), new Vector2(300, 30));
            _gameOverNamesText = UiFactory.CreateText(panelRect, "", 16, new Vector2(-70, -30), new Vector2(160, 220));
            _gameOverNamesText.alignment = TextAnchor.UpperLeft;
            _gameOverScoresText = UiFactory.CreateText(panelRect, "", 16, new Vector2(90, -30), new Vector2(80, 220));
            _gameOverScoresText.alignment = TextAnchor.UpperRight;

            UiFactory.CreateButton(panelRect, "Continue", new Vector2(0, -200), OnGameOverContinueClicked, new Vector2(200, 50));
        }

        private void ShowOnly(GameObject panel)
        {
            // Skip on the very first call (startup, nothing to transition from) and on a
            // same-panel no-op — only an actual screen change gets the flash.
            var isRealTransition = _currentPanel != null && _currentPanel != panel;

            _mainMenuPanel.SetActive(panel == _mainMenuPanel);
            _startGamePanel.SetActive(panel == _startGamePanel);
            _optionsPanel.SetActive(panel == _optionsPanel);
            _aboutPanel.SetActive(panel == _aboutPanel);
            _loginPanel.SetActive(panel == _loginPanel);
            _characterSelectPanel.SetActive(panel == _characterSelectPanel);
            _connectingPanel.SetActive(panel == _connectingPanel);
            _gameOverPanel.SetActive(panel == _gameOverPanel);
            _hudPanel.SetActive(panel == _hudPanel);
            _minimap.SetActive(panel == _hudPanel);
            _matchHudPanel.SetActive(panel == _hudPanel);
            // The gradient backdrop is the menu flow's "world" — visible everywhere
            // except the HUD, where the real 3D arena behind the canvas takes over.
            _menuBackground.gameObject.SetActive(panel != _hudPanel);
            _currentPanel = panel;

            if (isRealTransition)
            {
                PlayScreenTransition();
            }
        }

        private const float ScreenFadeDuration = 0.18f;

        private void PlayScreenTransition()
        {
            if (_fadeGroup == null)
            {
                return;
            }

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }

            _fadeCoroutine = StartCoroutine(FadeOverlayRoutine());
        }

        private IEnumerator FadeOverlayRoutine()
        {
            _fadeGroup.alpha = 1f;
            var t = 0f;
            while (t < ScreenFadeDuration)
            {
                t += Time.deltaTime;
                _fadeGroup.alpha = 1f - Mathf.Clamp01(t / ScreenFadeDuration);
                yield return null;
            }

            _fadeGroup.alpha = 0f;
            _fadeCoroutine = null;
        }

        // Pushes the panel showing now onto history before switching — use this for
        // "forward" transitions the player can Back/Esc back out of (GoBack). Plain
        // ShowOnly is for resets that aren't really "back"-able, e.g. returning to
        // character select on disconnect.
        private void NavigateTo(GameObject panel)
        {
            if (_currentPanel != null && _currentPanel != panel)
            {
                _panelHistory.Push(_currentPanel);
            }

            ShowOnly(panel);
        }

        private void GoBack()
        {
            if (_panelHistory.Count == 0)
            {
                return;
            }

            var previous = _panelHistory.Pop();
            ShowOnly(previous);

            // Landing back on the HUD while still logically paused (reached Options via
            // the pause overlay's Options button, which hides it first so it doesn't
            // visually overlap) — restore the pause overlay rather than leaving the
            // player stuck with no way back to Resume/Leave short of pressing Esc again.
            if (previous == _hudPanel && _isPaused)
            {
                _pausePanel.SetActive(true);
            }
        }

        private void OnStartGameClicked() => NavigateTo(_startGamePanel);

        private void OnStartOnlineClicked()
        {
            _isLanMode = false;
            ApplyRememberedLoginFields(OnlineBackendUrlPrefKey, _config.BackendUrl);
            ((Text)_serverField.placeholder).text = "server address";
            NavigateTo(_loginPanel);
        }

        private void OnStartLanClicked()
        {
            _isLanMode = true;
            ApplyRememberedLoginFields(LanBackendUrlPrefKey, "");
            // Full example (http://192.168.1.20:8080) doesn't fit the 300px-wide field
            // without wrapping past its visible height, hence the short placeholder.
            ((Text)_serverField.placeholder).text = "LAN host address";
            NavigateTo(_loginPanel);
        }

        // "Remember me" gates both the email and the server address together — if it was
        // unchecked last time (or never checked), both fields start blank/default instead
        // of silently carrying over from a previous session.
        private void ApplyRememberedLoginFields(string hostPrefKey, string defaultHost)
        {
            var remember = PlayerPrefs.GetInt(RememberMePrefKey, 0) == 1;
            _rememberMeToggle.isOn = remember;
            _serverField.text = remember ? PlayerPrefs.GetString(hostPrefKey, defaultHost) : defaultHost;
            _emailField.text = remember ? PlayerPrefs.GetString(RememberedEmailPrefKey, "") : "";
        }

        private void SaveRememberedLoginFields()
        {
            var remember = _rememberMeToggle.isOn;
            PlayerPrefs.SetInt(RememberMePrefKey, remember ? 1 : 0);
            if (remember)
            {
                PlayerPrefs.SetString(RememberedEmailPrefKey, _emailField.text);
                PlayerPrefs.SetString(_isLanMode ? LanBackendUrlPrefKey : OnlineBackendUrlPrefKey, _currentBackendUrl);
            }
            else
            {
                PlayerPrefs.DeleteKey(RememberedEmailPrefKey);
                PlayerPrefs.DeleteKey(OnlineBackendUrlPrefKey);
                PlayerPrefs.DeleteKey(LanBackendUrlPrefKey);
            }
        }

        private void OnOptionsClicked() => NavigateTo(_optionsPanel);

        private void OnAboutClicked() => NavigateTo(_aboutPanel);

        // From the pause overlay specifically: hide it first (rather than calling
        // TogglePause, which would resume gameplay/relock the cursor) so it doesn't
        // visually overlap the full-screen Options panel underneath — GoBack restores it
        // once the player backs out, since _isPaused stays true the whole time.
        private void OnPauseOptionsClicked()
        {
            _pausePanel.SetActive(false);
            NavigateTo(_optionsPanel);
        }

        private void OnBackClicked() => GoBack();

        private void OnExitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private async void OnRegisterClicked()
        {
            EnsureClientsForServerField();
            _loginStatus.text = "Registering...";
            var (success, error) = await _auth.RegisterAsync(_emailField.text, _passwordField.text);
            _loginStatus.text = success ? "Registered — now log in." : $"Register failed: {error}";
        }

        private async void OnLoginClicked()
        {
            // "Remember me" only ever restores the email, never the password (see
            // ApplyRememberedLoginFields) — clicking Login right after relaunch with a
            // still-empty password field was reaching the server and, until the backend
            // fix, crashing it outright instead of just failing normally. Caught here too
            // so it's an instant local message either way.
            if (string.IsNullOrEmpty(_passwordField.text))
            {
                _loginStatus.text = "Enter your password.";
                return;
            }

            EnsureClientsForServerField();
            _loginStatus.text = "Logging in...";
            var (success, error) = await _auth.LoginAsync(_emailField.text, _passwordField.text);
            if (success)
            {
                SaveRememberedLoginFields();
                NavigateTo(_characterSelectPanel);
            }
            else
            {
                _loginStatus.text = $"Login failed: {error}";
            }
        }

        private async void OnQuickPlayClicked()
        {
            // Cosmetic only in this prototype — not sent to the backend (no endpoint
            // accepts a display-name override yet). See docs/ROADMAP.md Phase 5 notes.
            PlayerPrefs.SetString("DisplayName", _displayNameField.text);

            _selectStatus.text = "Finding a match...";
            var result = await _session.QuickplayAsync(_auth.AccessToken);

            if (!result.Success)
            {
                _selectStatus.text = $"Quickplay failed: {result.Error}";
                return;
            }

            ShowOnly(_connectingPanel);
            ConnectToGameServer(result);
        }

        private void ConnectToGameServer(QuickplayResult result)
        {
            var networkManager = gameObject.GetComponent<NetworkManager>() ?? gameObject.AddComponent<NetworkManager>();
            var transport = gameObject.GetComponent<UnityTransport>() ?? gameObject.AddComponent<UnityTransport>();
            // Match ServerBootstrap's shortened timeout (see its comment) — keeps both
            // sides agreeing on how quickly a dead connection gets declared dead.
            transport.DisconnectTimeoutMS = 5000;

            var playerTemplate = PlayerController.CreateTemplate();

            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.ConnectionApproval = true; // must match the server (see ServerBootstrap.cs)
            networkManager.NetworkConfig.EnableSceneManagement = false; // must match the server (see ServerBootstrap.cs)
            networkManager.AddNetworkPrefab(playerTemplate);
            // Every network prefab the server can spawn must also be registered here with
            // the same GlobalObjectIdHash, or NGO can't resolve the spawn message — see
            // PlayerController.CreateTemplate's comment for why runtime-only prefabs need
            // that hash assigned manually at all.
            networkManager.AddNetworkPrefab(PickupController.CreateTemplate());
            networkManager.AddNetworkPrefab(MatchManager.CreateTemplate());
            networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(result.Ticket);

            transport.SetConnectionData(result.Host, (ushort)result.Port);

            networkManager.OnClientDisconnectCallback += OnDisconnected;
            networkManager.StartClient();
        }

        private void OnLocalPlayerSpawned(PlayerController player)
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var follow = mainCamera.GetComponent<CameraFollow>() ?? mainCamera.gameObject.AddComponent<CameraFollow>();
                follow.Target = player.transform;
                _cameraFollow = follow;
            }

            // Chosen at Character Select but never sent anywhere before now — see
            // PlayerController.SubmitDisplayName. FixedString32Bytes can hold at most 29
            // UTF-8 bytes (32 minus its own length header), so this is trimmed well under
            // that even for names full of multi-byte characters. Falls back to the
            // slot's color name ("Red"/"Blue"/"Green"/"Yellow") instead of "Slot N" —
            // still unique per player, but legible without knowing what a "slot" is.
            var displayName = _displayNameField.text?.Trim();
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = PlayerColors.GetName(player.SlotIndex);
            }
            else if (displayName.Length > 16)
            {
                displayName = displayName[..16];
            }

            player.SubmitDisplayName(displayName);

            _localPlayer = player;
            ShowOnly(_hudPanel);

            // Starts unlocked — RefreshCursorState (driven every frame by UpdateLobby)
            // takes over from here, and a freshly spawned player always lands in the
            // lobby first (match not started yet), which needs the cursor free to click
            // Start Match.
            RefreshCursorState();
        }

        // The reason strings ServerBootstrap.BuildMatchEndReason/BuildVoteEndReason send
        // always start with one of these — used to tell "the match actually ended" (timer
        // or vote) apart from every other disconnect cause (deliberate Leave, timeout,
        // crash), which is what decides Game Over vs. plain character select.
        private const string MatchEndedReasonPrefix = "Match ended";
        private const string VoteEndedReasonPrefix = "Vote ended";

        private void OnDisconnected(ulong clientId)
        {
            var reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : null;
            var isTimerEnd = !string.IsNullOrEmpty(reason) && reason.StartsWith(MatchEndedReasonPrefix);
            var isVoteEnd = !string.IsNullOrEmpty(reason) && reason.StartsWith(VoteEndedReasonPrefix);
            var isGameOver = isTimerEnd || isVoteEnd;

            // A timer-based end starts a fresh lobby server-side (see ServerBootstrap.
            // EndMatch/MatchManager.ResetForNewRound) — quick-playing again is genuinely a
            // rejoin into that fresh lobby, so the button reads "Rejoin Match". A vote end
            // means everyone agreed to stop — no rejoin offered, and Game Over's Continue
            // sends everyone to the main menu instead of character select. Any other
            // disconnect (Leave Match, a timeout, a crash) leaves the existing confirm/
            // release/rejoin grace period intact, so quick-playing again is also a rejoin.
            _canRejoin = !isGameOver;
            _gameOverToMainMenu = isVoteEnd;
            _quickPlayButtonText.text = _canRejoin ? "Rejoin Match" : "Quick Play";

            _localPlayer = null;
            _cameraFollow = null;
            _isPaused = false;
            _pausePanel.SetActive(false);
            _lobbyPanel.SetActive(false);
            _voteEndPanel.SetActive(false);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (isGameOver)
            {
                ShowGameOver(reason);
            }
            else
            {
                _selectStatus.text = string.IsNullOrEmpty(reason) ? "Disconnected." : $"Disconnected: {reason}";
                ShowOnly(_characterSelectPanel);
            }
        }

        private void ShowGameOver(string reason)
        {
            // The trailing "quick play again..." hint is redundant here — there's a
            // dedicated Continue button right below it.
            const string trailingHint = " — quick play again to start a new match.";
            var resultLine = reason.EndsWith(trailingHint) ? reason[..^trailingHint.Length] : reason;
            _gameOverResultText.text = resultLine;

            var names = new StringBuilder();
            var scores = new StringBuilder();
            foreach (var (name, score) in _lastKnownScoreboard)
            {
                names.AppendLine(name);
                scores.AppendLine(score.ToString());
            }

            _gameOverNamesText.text = names.ToString();
            _gameOverScoresText.text = scores.ToString();

            ShowOnly(_gameOverPanel);
        }

        private void OnGameOverContinueClicked()
        {
            _selectStatus.text = "";
            // Vote-ended matches send everyone to the main menu — the game is meant to
            // actually end there, not just loop back into a fresh lobby. A normal timer
            // end goes to character select instead, since that's the natural place to
            // start another round (Quick Play just says "Quick Play" here either way —
            // _canRejoin is false for both game-over paths).
            ShowOnly(_gameOverToMainMenu ? _mainMenuPanel : _characterSelectPanel);
        }

        private void OnLeaveClicked()
        {
            // Triggers the same OnClientDisconnectCallback path as a timeout or crash —
            // OnDisconnected handles returning to character select either way.
            NetworkManager.Singleton?.Shutdown();
        }

        private async Task RunAutoTestAsync()
        {
            Debug.Log("[ClientBootstrap] Auto-test mode: logging in automatically.");
            await _auth.RegisterAsync(_config.AutoTestEmail, _config.AutoTestPassword);
            var (success, error) = await _auth.LoginAsync(_config.AutoTestEmail, _config.AutoTestPassword);
            if (!success)
            {
                Debug.LogError($"[ClientBootstrap] Auto-test login failed: {error}");
                return;
            }

            var result = await _session.QuickplayAsync(_auth.AccessToken);
            if (!result.Success)
            {
                Debug.LogError($"[ClientBootstrap] Auto-test quickplay failed: {result.Error}");
                return;
            }

            Debug.Log($"[ClientBootstrap] Auto-test connecting to {result.Host}:{result.Port}, slot {result.SlotIndex}");
            ShowOnly(_connectingPanel);
            ConnectToGameServer(result);
        }
    }
}
