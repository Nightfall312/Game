using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;


public class NetworkRunnerHandler : MonoBehaviour
{
    [SerializeField] NetworkRunner networkRunnerPrefab;

    NetworkRunner networkRunner;

    public NetworkRunner NetworkRunner => networkRunner;

    void Awake()
    {
        networkRunner = FindFirstObjectByType<NetworkRunner>();
    }

    void Start()
    {
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

            Utils.DebugLog("NetworkRunnerHandler: started fallback session for direct scene testing.");
        }

        // If a runner already exists, do nothing.
    }

    INetworkSceneManager GetSceneManager(NetworkRunner runner)
    {
        INetworkSceneManager sceneManager = runner
            .GetComponents(typeof(MonoBehaviour))
            .OfType<INetworkSceneManager>()
            .FirstOrDefault();

        if (sceneManager == null)
        {
            sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        }

        return sceneManager;
    }

    protected virtual Task InitializeNetworkRunner(
        NetworkRunner runner,
        GameMode gameMode,
        string sessionName,
        NetAddress address,
        SceneRef scene,
        Action<NetworkRunner> initialized)
    {
        INetworkSceneManager sceneManager = GetSceneManager(runner);

        runner.ProvideInput = true;

        return runner.StartGame(new StartGameArgs
        {
            GameMode = gameMode,
            Address = address,
            Scene = scene,
            SessionName = sessionName,
            CustomLobbyName = "OurLobbyID",
            SceneManager = sceneManager,
        });
    }
}
