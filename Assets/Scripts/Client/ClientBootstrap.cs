using System.Text;
using System.Threading.Tasks;
using CubeArena.Shared;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

namespace CubeArena.Client
{
    // Character-select flow per section 6: pick a display name, quick-play into a
    // session, see the server-assigned colour. All UI is built from code — see
    // UiFactory.cs and CLAUDE.md's rule against hand-editing scenes/prefabs.
    public class ClientBootstrap : MonoBehaviour
    {
        private const string BackendUrlPrefKey = "BackendUrl";

        private ClientConfig _config;
        private AuthClient _auth;
        private SessionClient _session;
        private string _currentBackendUrl;

        private Canvas _canvas;
        private GameObject _mainMenuPanel;
        private GameObject _optionsPanel;
        private GameObject _aboutPanel;
        private GameObject _loginPanel;
        private GameObject _characterSelectPanel;
        private GameObject _connectingPanel;
        private GameObject _hudPanel;
        private GameObject _minimap;
        private Text _loginStatus;
        private Text _selectStatus;
        private Text _hudText;
        private InputField _serverField;
        private InputField _emailField;
        private InputField _passwordField;
        private InputField _displayNameField;

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
            BuildMainMenuPanel();
            BuildOptionsPanel();
            BuildAboutPanel();
            BuildLoginPanel();
            BuildCharacterSelectPanel();
            BuildConnectingPanel();
            BuildHud();
            ShowOnly(_mainMenuPanel);
        }

        private void BuildMainMenuPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(340, 380));
            _mainMenuPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Cube Arena", 32, new Vector2(0, 150), new Vector2(300, 44));

            var buttonSize = new Vector2(280, 50);
            UiFactory.CreateButton(panelRect, "Start Game (Online/LAN)", new Vector2(0, 65), OnStartGameClicked, buttonSize);
            UiFactory.CreateButton(panelRect, "Options", new Vector2(0, 0), OnOptionsClicked, buttonSize);
            UiFactory.CreateButton(panelRect, "About", new Vector2(0, -65), OnAboutClicked, buttonSize);
            UiFactory.CreateButton(panelRect, "Exit", new Vector2(0, -130), OnExitClicked, buttonSize);
        }

        private void BuildOptionsPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(340, 220));
            _optionsPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Options", 24, new Vector2(0, 70), new Vector2(300, 40));
            UiFactory.CreateText(panelRect, "Work in progress", 18, new Vector2(0, 0), new Vector2(300, 40));
            UiFactory.CreateButton(panelRect, "Back", new Vector2(0, -75), OnBackToMenuClicked);
        }

        private void BuildAboutPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(380, 260));
            _aboutPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "About", 24, new Vector2(0, 95), new Vector2(300, 40));
            UiFactory.CreateText(panelRect,
                "Cube Arena\nA 4-player multiplayer prototype.\n\n" +
                "Server-authoritative movement, signed connect\ntickets, and a real dedicated game server.",
                14, new Vector2(0, 10), new Vector2(340, 110));
            UiFactory.CreateButton(panelRect, "Back", new Vector2(0, -95), OnBackToMenuClicked);
        }

        private void BuildLoginPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(380, 420));
            _loginPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Cube Arena", 28, new Vector2(0, 165), new Vector2(300, 40));

            // Editable so the same client build can point at a cloud-hosted backend or
            // a LAN host's local IP without rebuilding — CUBEARENA_BACKEND_URL only sets
            // the initial value here, and whatever's typed is remembered for next launch.
            _serverField = UiFactory.CreateInputField(panelRect, "server address", new Vector2(0, 110));
            _serverField.text = PlayerPrefs.GetString(BackendUrlPrefKey, _config.BackendUrl);

            _emailField = UiFactory.CreateInputField(panelRect, "email", new Vector2(0, 55));
            _passwordField = UiFactory.CreateInputField(panelRect, "password", new Vector2(0, 5), isPassword: true);
            UiFactory.CreateButton(panelRect, "Register", new Vector2(-95, -50), OnRegisterClicked);
            UiFactory.CreateButton(panelRect, "Login", new Vector2(95, -50), OnLoginClicked);
            _loginStatus = UiFactory.CreateText(panelRect, "", 14, new Vector2(0, -115), new Vector2(340, 50));
            UiFactory.CreateButton(panelRect, "Back", new Vector2(0, -180), OnBackToMenuClicked, new Vector2(120, 36));
        }

        // (Re)creates the auth/session clients if the server-address field has changed
        // since the last call, and remembers the value for next launch.
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
            PlayerPrefs.SetString(BackendUrlPrefKey, url);
        }

        private void BuildCharacterSelectPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(380, 260));
            _characterSelectPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Character Select", 24, new Vector2(0, 90), new Vector2(300, 40));
            _displayNameField = UiFactory.CreateInputField(panelRect, "display name", new Vector2(0, 30));
            UiFactory.CreateButton(panelRect, "Quick Play", new Vector2(0, -30), OnQuickPlayClicked);
            _selectStatus = UiFactory.CreateText(panelRect, "", 14, new Vector2(0, -90), new Vector2(340, 40));
        }

        private void BuildConnectingPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(300, 120));
            _connectingPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Connecting...", 20, Vector2.zero, new Vector2(260, 60));
        }

        private void BuildHud()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(260, 130));
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(16, -16);
            _hudPanel = panelRect.gameObject;
            _hudText = UiFactory.CreateText(panelRect, "", 18, new Vector2(0, 30), new Vector2(240, 50));
            UiFactory.CreateButton(panelRect, "Leave", new Vector2(0, -35), OnLeaveClicked);

            _minimap = Minimap.Create(_canvas.transform).gameObject;
        }

        private void ShowOnly(GameObject panel)
        {
            _mainMenuPanel.SetActive(panel == _mainMenuPanel);
            _optionsPanel.SetActive(panel == _optionsPanel);
            _aboutPanel.SetActive(panel == _aboutPanel);
            _loginPanel.SetActive(panel == _loginPanel);
            _characterSelectPanel.SetActive(panel == _characterSelectPanel);
            _connectingPanel.SetActive(panel == _connectingPanel);
            _hudPanel.SetActive(panel == _hudPanel);
            _minimap.SetActive(panel == _hudPanel);
        }

        private void OnStartGameClicked() => ShowOnly(_loginPanel);

        private void OnOptionsClicked() => ShowOnly(_optionsPanel);

        private void OnAboutClicked() => ShowOnly(_aboutPanel);

        private void OnBackToMenuClicked() => ShowOnly(_mainMenuPanel);

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
            EnsureClientsForServerField();
            _loginStatus.text = "Logging in...";
            var (success, error) = await _auth.LoginAsync(_emailField.text, _passwordField.text);
            if (success)
            {
                ShowOnly(_characterSelectPanel);
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

            var playerTemplate = PlayerController.CreateTemplate();

            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.ConnectionApproval = true; // must match the server (see ServerBootstrap.cs)
            networkManager.NetworkConfig.EnableSceneManagement = false; // must match the server (see ServerBootstrap.cs)
            networkManager.AddNetworkPrefab(playerTemplate);
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
            }

            _hudText.text = $"Slot {player.SlotIndex}";
            _hudText.color = PlayerColors.Get(player.SlotIndex);
            ShowOnly(_hudPanel);
        }

        private void OnDisconnected(ulong clientId)
        {
            var reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : null;
            _selectStatus.text = string.IsNullOrEmpty(reason) ? "Disconnected." : $"Disconnected: {reason}";
            ShowOnly(_characterSelectPanel);
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
