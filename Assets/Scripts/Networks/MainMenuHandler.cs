using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuUIManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject joinPanel;
    [SerializeField] GameObject loadPanel;

    [Header("Main Panel Buttons")]
    [SerializeField] Button newGameButton;
    [SerializeField] Button loadGameButton;
    [SerializeField] Button joinGameButton;
    [SerializeField] Button quitButton;

    [Header("Join Panel")]
    [SerializeField] TMP_InputField roomCodeInput;
    [SerializeField] Button confirmJoinButton;
    [SerializeField] Button cancelJoinButton;

    [Header("Load Panel - one entry per slot")]
    [SerializeField] Button[] slotButtons;
    [SerializeField] TMP_Text[] slotLabels;
    [SerializeField] Button closeLoadButton;

    [Header("Status")]
    [SerializeField] TMP_Text statusText;

    [Header("Network")]
    [SerializeField] NetworkRunner networkRunnerPrefab;

    [Header("Scene")]
    [SerializeField] string gameSceneName = "SampleScene";

    [Header("New Game Panel")]
    [SerializeField] GameObject newGamePanel;
    [SerializeField] TMP_InputField gameNameInput;
    [SerializeField] Button createButton;
    [SerializeField] Button cancelNewGameButton;

    const string DefaultRoom = "TestRoom";
    const string LobbyName = "OurLobbyID";
    const string FixedRegion = "asia";
    const string FixedAppVersion = "1.0";

    int _selectedSlot = -1;

    void Start()
    {
        newGameButton.onClick.AddListener(OnNewGameClicked);
        joinGameButton.onClick.AddListener(OnJoinGameClicked);
        quitButton.onClick.AddListener(OnQuitClicked);
        confirmJoinButton.onClick.AddListener(OnConfirmJoinClicked);
        cancelJoinButton.onClick.AddListener(OnCancelJoinClicked);

        if (loadGameButton != null)
            loadGameButton.onClick.AddListener(OnLoadGameClicked);

        if (closeLoadButton != null)
            closeLoadButton.onClick.AddListener(OnCloseLoadClicked);

        if (slotButtons != null)
        {
            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slot = i;
                if (slotButtons[i] != null)
                    slotButtons[i].onClick.AddListener(() => OnSlotClicked(slot));
            }
        }

        if (createButton != null)
            createButton.onClick.AddListener(OnCreateClicked);

        if (cancelNewGameButton != null)
            cancelNewGameButton.onClick.AddListener(OnCancelNewGameClicked);

        ShowMainPanel();
        SetStatus(string.Empty);
    }

    void OnNewGameClicked()
    {
        _selectedSlot = -1;
        SetButtonsInteractable(false);
        SetStatus("Creating session...");
        _ = StartSession(GameMode.Host, DefaultRoom);
    }

    void OnCreateClicked()
    {
        string gameName = gameNameInput != null ? gameNameInput.text.Trim() : string.Empty;

        if (string.IsNullOrEmpty(gameName))
        {
            SetStatus("Enter a game name.");
            return;
        }

        SetButtonsInteractable(false);
        SetStatus("Creating session...");
        _ = StartSession(GameMode.Host, gameName);
    }

    void OnCancelNewGameClicked()
    {
        ShowMainPanel();
        SetStatus(string.Empty);
    }

    void OnLoadGameClicked()
    {
        RefreshSlotLabels();
        mainPanel.SetActive(false);

        if (loadPanel != null)
            loadPanel.SetActive(true);
    }

    void OnJoinGameClicked()
    {
        roomCodeInput.text = string.Empty;
        mainPanel.SetActive(false);
        joinPanel.SetActive(true);
    }

    void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void RefreshSlotLabels()
    {
        if (slotButtons == null)
            return;

        for (int i = 0; i < slotButtons.Length; i++)
        {
            SaveData data = SaveManager.Load(i);

            if (slotLabels != null && i < slotLabels.Length && slotLabels[i] != null)
                slotLabels[i].text = data.GetSummary();
        }
    }

    void OnSlotClicked(int slot)
    {
        _selectedSlot = slot;
        SetButtonsInteractable(false);
        SetStatus("Loading slot " + (slot + 1) + "...");
        _ = StartSession(GameMode.Host, DefaultRoom);
    }

    void OnCloseLoadClicked()
    {
        ShowMainPanel();
    }

    void OnConfirmJoinClicked()
    {
        string room = roomCodeInput.text.Trim();

        if (string.IsNullOrEmpty(room))
        {
            SetStatus("Paste an invite code.");
            return;
        }

        SetButtonsInteractable(false);
        SetStatus("Joining session...");
        _ = StartSession(GameMode.Client, room);
    }

    async Task StartSession(GameMode mode, string roomName)
    {
        NetworkRunner existing = FindFirstObjectByType<NetworkRunner>();
        if (existing != null)
            Destroy(existing.gameObject);

        NetworkRunner runner = Instantiate(networkRunnerPrefab);
        runner.name = "Network Runner";
        runner.ProvideInput = true;

        INetworkSceneManager sceneManager =
            runner.GetComponents<MonoBehaviour>().OfType<INetworkSceneManager>().FirstOrDefault()
            ?? runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        int sceneIndex = SceneUtility.GetBuildIndexByScenePath(
            $"Assets/Scenes/{gameSceneName}.unity"
        );

        FusionAppSettings customSettings = BuildCustomAppSettings();

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = mode,
            SessionName = roomName,
            CustomLobbyName = LobbyName,
            Scene = SceneRef.FromIndex(sceneIndex),
            SceneManager = sceneManager,
            CustomPhotonAppSettings = customSettings
        });

        if (result.Ok)
        {
            PlayerPrefs.SetInt("ActiveSaveSlot", _selectedSlot);
            PlayerPrefs.Save();
            SetStatus("Connected! Loading...");
        }
        else
        {
            SetStatus($"Failed: {result.ShutdownReason}");
            SetButtonsInteractable(true);
            Destroy(runner.gameObject);
        }
    }

    FusionAppSettings BuildCustomAppSettings()
    {
        FusionAppSettings appSettings = PhotonAppSettings.Global.AppSettings.GetCopy();

        appSettings.UseNameServer = true;
        appSettings.AppVersion = FixedAppVersion;
        appSettings.FixedRegion = FixedRegion;

        return appSettings;
    }

    void ShowMainPanel()
    {
        mainPanel.SetActive(true);
        joinPanel.SetActive(false);

        if (loadPanel != null)
            loadPanel.SetActive(false);

        if (newGamePanel != null)
            newGamePanel.SetActive(false);
    }

    void SetButtonsInteractable(bool value)
    {
        newGameButton.interactable = value;
        joinGameButton.interactable = value;
        confirmJoinButton.interactable = value;

        if (loadGameButton != null)
            loadGameButton.interactable = value;
    }

    void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    void OnCancelJoinClicked()
    {
        ShowMainPanel();
        SetStatus(string.Empty);
    }
}