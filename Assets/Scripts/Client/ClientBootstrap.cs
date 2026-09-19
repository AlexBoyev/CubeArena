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
        private ClientConfig _config;
        private AuthClient _auth;
        private SessionClient _session;

        private Canvas _canvas;
        private GameObject _loginPanel;
        private GameObject _characterSelectPanel;
        private GameObject _connectingPanel;
        private GameObject _hudPanel;
        private Text _loginStatus;
        private Text _selectStatus;
        private Text _hudText;
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
            _auth = new AuthClient(_config.BackendUrl);
            _session = new SessionClient(_config.BackendUrl);

            ArenaBuilder.Build();
            BuildUi();

            // Subscribed unconditionally (not after connecting) so there's no race
            // between the transport "connected" event and the player object's spawn
            // message, which can arrive in either order.
            PlayerController.LocalPlayerSpawned += OnLocalPlayerSpawned;

            if (_config.AutoTestEnabled)
            {
                _ = RunAutoTestAsync();
            }
        }

        private void OnDestroy()
        {
            PlayerController.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        }

        private void BuildUi()
        {
            _canvas = UiFactory.CreateCanvas();
            BuildLoginPanel();
            BuildCharacterSelectPanel();
            BuildConnectingPanel();
            BuildHud();
            ShowOnly(_loginPanel);
        }

        private void BuildLoginPanel()
        {
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(380, 320));
            _loginPanel = panelRect.gameObject;
            UiFactory.CreateText(panelRect, "Cube Arena", 28, new Vector2(0, 120), new Vector2(300, 40));
            _emailField = UiFactory.CreateInputField(panelRect, "email", new Vector2(0, 50));
            _passwordField = UiFactory.CreateInputField(panelRect, "password", new Vector2(0, 0), isPassword: true);
            UiFactory.CreateButton(panelRect, "Register", new Vector2(-95, -55), OnRegisterClicked);
            UiFactory.CreateButton(panelRect, "Login", new Vector2(95, -55), OnLoginClicked);
            _loginStatus = UiFactory.CreateText(panelRect, "", 14, new Vector2(0, -110), new Vector2(340, 40));
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
            var panelRect = UiFactory.CreatePanel(_canvas.transform, new Vector2(260, 60));
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(16, -16);
            _hudPanel = panelRect.gameObject;
            _hudText = UiFactory.CreateText(panelRect, "", 18, Vector2.zero, new Vector2(240, 50));
        }

        private void ShowOnly(GameObject panel)
        {
            _loginPanel.SetActive(panel == _loginPanel);
            _characterSelectPanel.SetActive(panel == _characterSelectPanel);
            _connectingPanel.SetActive(panel == _connectingPanel);
            _hudPanel.SetActive(panel == _hudPanel);
        }

        private async void OnRegisterClicked()
        {
            _loginStatus.text = "Registering...";
            var (success, error) = await _auth.RegisterAsync(_emailField.text, _passwordField.text);
            _loginStatus.text = success ? "Registered — now log in." : $"Register failed: {error}";
        }

        private async void OnLoginClicked()
        {
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
            playerTemplate.SetActive(false);

            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.ConnectionApproval = true; // must match the server (see ServerBootstrap.cs)
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
