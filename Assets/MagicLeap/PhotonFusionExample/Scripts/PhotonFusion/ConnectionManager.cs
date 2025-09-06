using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace MagicLeap.Networking
{
    /// <summary>
    /// Modified version of script from:
    /// https://doc.photonengine.com/fusion/current/technical-samples/fusion-vr-shared
    /// </summary>
    public class ConnectionManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        public string roomName = "SampleFusion-MagicLeap2";
        public bool connectOnStart = true;

        // Inspector から切替えたい場合は初期値 Host に
        public GameMode gameMode = GameMode.Shared;

        public Action<string> ConnectionFailed;
        public Action DisconnectedFromServer;
        public Action ConnectedToServer;

        [Header("Fusion settings")]
        [Tooltip("Fusion runner. Automatically created if not set")]
        public NetworkRunner runner;

        public INetworkSceneManager sceneManager;

        [Header("Local user spawner")]
        public NetworkObject userPrefab;

        [Header("Event")]
        public UnityEvent onWillConnect = new();

        private void Awake()
        {
            if (runner == null) runner = GetComponent<NetworkRunner>();
            if (runner == null) runner = gameObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = true;
        }

        private async void Start()
        {
            if (connectOnStart)
            {
                await Connect();
            }
        }

        public async Task Connect()
        {
            if (sceneManager == null)
                sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

            onWillConnect?.Invoke();

            var args = new StartGameArgs
            {
                GameMode     = gameMode,          // ← Host/Client/Shared/Single をここで選ぶ
                SessionName  = roomName,
                Scene        = SceneManager.GetActiveScene().buildIndex,
                SceneManager = sceneManager
            };

            await runner.StartGame(args);
        }

        #region INetworkRunnerCallbacks

        public void OnPlayerJoined(NetworkRunner playerRunner, PlayerRef player)
        {
            if (player == playerRunner.LocalPlayer)
            {
                Debug.Log($"OnPlayerJoined: {playerRunner.UserId}");
                playerRunner.Spawn(userPrefab, transform.position, transform.rotation, player,
                    (r, obj) => { });
            }
        }

        public void OnConnectedToServer(NetworkRunner runner)
        {
            Debug.Log("[Fusion] ConnectedToServer");
            ConnectedToServer?.Invoke();
        }

        public void OnDisconnectedFromServer(NetworkRunner runner)
        {
            Debug.Log("[Fusion] DisconnectedFromServer");
            DisconnectedFromServer?.Invoke();
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            Debug.LogError($"[Fusion] ConnectFailed: {reason}");
            ConnectionFailed?.Invoke($"Connection Failed: {reason}");
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.LogWarning($"[Fusion] Shutdown: {shutdownReason}");
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ArraySegment<byte> data) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }

        #endregion
    }
}
