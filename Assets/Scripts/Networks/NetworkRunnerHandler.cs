using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkRunnerHandler : MonoBehaviour
{
    [SerializeField] NetworkRunner networkRunnerPrefab;

    NetworkRunner networkRunner;
    public NetworkRunner NetworkRunner => networkRunner;

    const string LobbyName = "OurLobbyID";
    const string FixedRegion = "asia";
    const string FixedAppVersion = "1.0";

    void Awake()
    {
        networkRunner = FindFirstObjectByType<NetworkRunner>();
    }

    void Start()
    {
#if UNITY_EDITOR
        // Optional fallback only for direct testing in editor.
        // Remove this too if you want absolutely no auto session.
        if (networkRunner == null)
        {
            networkRunner = Instantiate(networkRunnerPrefab);
            networkRunner.name = "Network runner";

            _ = InitializeNetworkRunner(
                networkRunner,
                GameMode.AutoHostOrClient,
                "Test",
                NetAddress.Any(),
                SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
                null
            );

            Utils.DebugLog("NetworkRunnerHandler: started fallback session for editor testing.");
        }
#endif
    }

    INetworkSceneManager GetSceneManager(NetworkRunner runner)
    {
        INetworkSceneManager sceneManager =
            runner.GetComponents<MonoBehaviour>().OfType<INetworkSceneManager>().FirstOrDefault();

        if (sceneManager == null)
            sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        return sceneManager;
    }

    protected virtual Task InitializeNetworkRunner(
        NetworkRunner runner,
        GameMode gameMode,
        string sessionName,
        NetAddress address,
        SceneRef scene,
        Action initialized)
    {
        INetworkSceneManager sceneManager = GetSceneManager(runner);

        runner.ProvideInput = true;

        FusionAppSettings customSettings = BuildCustomAppSettings();

        return runner.StartGame(new StartGameArgs
        {
            GameMode = gameMode,
            Address = address,
            Scene = scene,
            SessionName = sessionName,
            CustomLobbyName = LobbyName,
            SceneManager = sceneManager,
            CustomPhotonAppSettings = customSettings
        });
    }

    FusionAppSettings BuildCustomAppSettings()
    {
        FusionAppSettings appSettings = PhotonAppSettings.Global.AppSettings.GetCopy();

        appSettings.UseNameServer = true;
        appSettings.AppVersion = FixedAppVersion;
        appSettings.FixedRegion = FixedRegion;

        return appSettings;
    }
}