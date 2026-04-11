using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class Spawner : SimulationBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] NetworkPlayer networkPlayerPrefab;
    [SerializeField] NetworkPlayer drunkNetworkPlayerPrefab;
    [Tooltip("The transform players spawn at. Assign grade_1 (or a child SpawnPoint on it) here.")]
    [SerializeField] Transform spawnPoint;
    [Tooltip("Vertical offset above the spawn point to avoid spawning inside geometry.")]
    [SerializeField] float spawnHeightOffset = 1f;

    readonly Dictionary<PlayerRef, NetworkPlayer> _spawnedPlayers =
        new Dictionary<PlayerRef, NetworkPlayer>();

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            Utils.DebugLog("OnPlayerJoined this is the server/host, spawning network player");

            bool isDrunk = false;

            NetworkPlayer prefab = isDrunk && drunkNetworkPlayerPrefab != null
                ? drunkNetworkPlayerPrefab
                : networkPlayerPrefab;

            Vector3 spawnPos = spawnPoint != null
                ? spawnPoint.position + Vector3.up * spawnHeightOffset
                : Vector3.up * spawnHeightOffset;

            NetworkPlayer playerObject = runner.Spawn(
                prefab,
                spawnPos,
                spawnPoint != null ? spawnPoint.rotation : Quaternion.identity,
                player
            );

            _spawnedPlayers[player] = playerObject;
        }
        else
        {
            Utils.DebugLog("OnPlayerJoined this is the client");
        }
    }


    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer)
        {
            return;
        }

        if (_spawnedPlayers.TryGetValue(player, out NetworkPlayer playerObject))
        {
            runner.Despawn(playerObject.Object);
            _spawnedPlayers.Remove(player);
            Utils.DebugLog($"OnPlayerLeft: despawned player {player}");
        }
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        if (NetworkPlayer.Local != null)
        {
            input.Set(NetworkPlayer.Local.GetNetworkInput());
        }
    }

    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        NetworkPlayer.ClearLocal();
        Utils.DebugLog($"OnDisconnectedFromServer: {reason}");
    }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        foreach (NetworkPlayer player in _spawnedPlayers.Values)
        {
            if (player != null)
            {
                UnityEngine.Object.Destroy(player.gameObject);
            }
        }

        _spawnedPlayers.Clear();
        Utils.DebugLog($"OnShutdown: destroyed all player objects. Reason: {shutdownReason}");
    }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
}
