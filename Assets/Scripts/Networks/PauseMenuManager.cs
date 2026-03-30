using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenuManager : MonoBehaviour
{
    [Header("Pause Panel")]
    [SerializeField] GameObject pausePanel;

    [Header("Buttons")]
    [SerializeField] Button inviteFriendButton;
    [SerializeField] Button quitButton;

    [Header("Invite")]
    [SerializeField] TMP_Text inviteCodeText;

    [Header("Scene")]
    [SerializeField] string mainMenuSceneName = "Main menu";

    const string PauseAction = "Pause";
    bool _isPaused;

    void Start()
    {
        pausePanel.SetActive(false);
        inviteFriendButton.onClick.AddListener(OnInviteFriendClicked);
        quitButton.onClick.AddListener(OnQuitClicked);
    }

    void Update()
    {
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePause();
        }
    }

    void TogglePause()
    {
        _isPaused = !_isPaused;
        IsPaused = _isPaused;

        pausePanel.SetActive(_isPaused);
        Time.timeScale = _isPaused ? 0f : 1f;

        Cursor.lockState = _isPaused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = _isPaused;

        if (_isPaused)
        {
            RefreshInviteCode();
        }
    }


    void OnInviteFriendClicked()
    {
        NetworkRunner runner = FindFirstObjectByType<NetworkRunner>();

        if (runner == null || runner.SessionInfo == null)
        {
            return;
        }

        string sessionName = runner.SessionInfo.Name;
        GUIUtility.systemCopyBuffer = sessionName;

        if (inviteCodeText != null)
        {
            inviteCodeText.text = $"Code copied: {sessionName}";
        }
    }

    async void OnQuitClicked()
    {
        Time.timeScale = 1f;

        NetworkRunner runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null)
        {
            await runner.Shutdown();
            Destroy(runner.gameObject);
        }

        SceneManager.LoadScene(mainMenuSceneName);
    }

    void RefreshInviteCode()
    {
        if (inviteCodeText == null)
        {
            return;
        }

        NetworkRunner runner = FindFirstObjectByType<NetworkRunner>();
        inviteCodeText.text = runner?.SessionInfo?.Name ?? "No session";
    }

    public static bool IsPaused { get; private set; }
}
