using System;
using System.Collections.Generic;
using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.MagicLeapSupport;
using UnityEngine.XR.MagicLeap;

namespace MagicLeap.NetworkedSpatialAnchors
{
    /// <summary>
    /// ネットワーク化された空間アンカーマネージャー
    /// PhotonFusionとSpatialAnchorsを統合して空間アンカーのマルチユーザー共有を実現
    /// </summary>
    public class NetworkedSpatialAnchorManager : NetworkBehaviour
    {
        [Header("Anchor Configuration")]
        [SerializeField] private GameObject anchorPrefab;
        [SerializeField] private float queryRadius = 10.0f;
        
        [Header("Network Anchor Management")]
        [Networked, Capacity(50)]
        public NetworkDictionary<NetworkString<32>, NetworkAnchorData> NetworkAnchors => default;
        
        // 統合されたアンカーストレージ
        private MagicLeapSpatialAnchorsStorageFeature openXRStorage;
        private MLXrAnchorSubsystem activeSubsystem;
        
        // 各APIのアンカー管理
        private Dictionary<string, ARAnchor> arAnchors = new Dictionary<string, ARAnchor>();
        private Dictionary<string, MLAnchors.Anchor> mlAnchors = new Dictionary<string, MLAnchors.Anchor>();
        
        // イベント
        public event Action<NetworkAnchorData> OnNetworkAnchorCreated;
        public event Action<string> OnNetworkAnchorRemoved;
        public event Action<string> OnAnchorSynchronized;

        public override void Spawned()
        {
            base.Spawned();
            InitializeAnchorSystems();
            
            if (HasStateAuthority)
            {
                // ホストとして既存のアンカーを同期
                StartCoroutine(SynchronizeExistingAnchors());
            }
        }

        private void InitializeAnchorSystems()
        {
            // OpenXR Storageの初期化
            openXRStorage = OpenXRSettings.Instance.GetFeature<MagicLeapSpatialAnchorsStorageFeature>();
            if (openXRStorage != null)
            {
                openXRStorage.OnCreationCompleteFromStorage += OnOpenXRAnchorCreated;
                openXRStorage.OnPublishComplete += OnOpenXRAnchorPublished;
                openXRStorage.OnDeletedComplete += OnOpenXRAnchorDeleted;
                openXRStorage.OnQueryComplete += OnOpenXRQueryComplete;
            }

            // ML Anchor Subsystemの初期化
            activeSubsystem = UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.activeLoader.GetLoadedSubsystem<XRAnchorSubsystem>() as MLXrAnchorSubsystem;
            
            Debug.Log($"NetworkedSpatialAnchorManager initialized - OpenXR: {openXRStorage != null}, ML: {activeSubsystem != null}");
        }

        /// <summary>
        /// ネットワーク化されたアンカーを作成
        /// </summary>
        public void CreateNetworkAnchor(Vector3 position, Quaternion rotation, bool persistent = true)
        {
            if (!HasStateAuthority)
            {
                Debug.LogWarning("Only the host can create network anchors");
                return;
            }

            string anchorId = System.Guid.NewGuid().ToString();
            CreateNetworkAnchorRPC(anchorId, position, rotation, persistent);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void CreateNetworkAnchorRPC(NetworkString<32> anchorId, Vector3 position, Quaternion rotation, bool persistent)
        {
            NetworkAnchorData anchorData = new NetworkAnchorData
            {
                Id = anchorId,
                Position = position,
                Rotation = rotation,
                IsPersistent = persistent,
                CreationTime = (float)NetworkTime.Time
            };

            NetworkAnchors.Add(anchorId, anchorData);
            StartCoroutine(CreateLocalAnchor(anchorData));
        }

        private IEnumerator CreateLocalAnchor(NetworkAnchorData networkAnchor)
        {
            // ARアンカーとしてローカルで作成
            GameObject anchorObject = Instantiate(anchorPrefab, networkAnchor.Position, networkAnchor.Rotation);
            ARAnchor arAnchor = anchorObject.AddComponent<ARAnchor>();
            
            // トラッキングが安定するまで待機
            yield return new WaitUntil(() => arAnchor.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.Tracking);
            
            arAnchors[networkAnchor.Id.ToString()] = arAnchor;

            // 永続化が必要な場合
            if (networkAnchor.IsPersistent && HasStateAuthority)
            {
                yield return StartCoroutine(PersistAnchor(networkAnchor.Id.ToString(), arAnchor));
            }

            OnNetworkAnchorCreated?.Invoke(networkAnchor);
            Debug.Log($"Created local anchor for network anchor: {networkAnchor.Id}");
        }

        private IEnumerator PersistAnchor(string anchorId, ARAnchor arAnchor)
        {
            // OpenXR Storageでの永続化
            if (openXRStorage != null)
            {
                openXRStorage.PublishSpatialAnchorsToStorage(new List<ARAnchor> { arAnchor }, 0);
                yield return null;
            }

            // Magic Leap Anchorsでの永続化
            if (activeSubsystem != null)
            {
                var pose = new Pose(arAnchor.transform.position, arAnchor.transform.rotation);
                yield return new WaitForEndOfFrame(); // ML API呼び出し前の待機
                
                // ML Anchorの作成は非同期で処理
                CreateMLAnchor(anchorId, pose);
            }
        }

        private void CreateMLAnchor(string anchorId, Pose pose)
        {
            // MLAnchorsAsyncを使用してML Anchorを作成
            var mlAnchorsAsync = MLAnchorsAsync.Instance;
            if (mlAnchorsAsync != null)
            {
                mlAnchorsAsync.Create(pose.position, pose.rotation, 0, (anchor) => {
                    mlAnchors[anchorId] = anchor;
                    Debug.Log($"ML Anchor created for network anchor: {anchorId}");
                    
                    // 公開
                    mlAnchorsAsync.Publish(anchor, (publishedAnchor) => {
                        Debug.Log($"ML Anchor published for network anchor: {anchorId}");
                    });
                });
            }
        }

        /// <summary>
        /// 既存のアンカーをネットワークに同期
        /// </summary>
        private IEnumerator SynchronizeExistingAnchors()
        {
            yield return new WaitForSeconds(1f); // システムの初期化を待機

            // OpenXRからのクエリ
            if (openXRStorage != null)
            {
                openXRStorage.QueryStoredSpatialAnchors(transform.position, queryRadius);
            }

            // ML Anchorsからのクエリ
            var mlAnchorsAsync = MLAnchorsAsync.Instance;
            if (mlAnchorsAsync != null)
            {
                mlAnchorsAsync.QueryAnchors(
                    (updated) => Debug.Log($"Updated ML anchors: {updated.Count}"),
                    (added) => SyncMLAnchorsToNetwork(added),
                    (removed) => Debug.Log($"Removed ML anchors: {removed.Count}")
                );
            }
        }

        private void SyncMLAnchorsToNetwork(List<MLAnchors.Anchor> mlAnchorList)
        {
            foreach (var mlAnchor in mlAnchorList)
            {
                if (!NetworkAnchors.ContainsKey(mlAnchor.Id))
                {
                    NetworkAnchorData anchorData = new NetworkAnchorData
                    {
                        Id = mlAnchor.Id,
                        Position = mlAnchor.Pose.position,
                        Rotation = mlAnchor.Pose.rotation,
                        IsPersistent = true,
                        CreationTime = (float)NetworkTime.Time
                    };
                    
                    NetworkAnchors.Add(mlAnchor.Id, anchorData);
                    mlAnchors[mlAnchor.Id] = mlAnchor;
                    
                    OnAnchorSynchronized?.Invoke(mlAnchor.Id);
                }
            }
        }

        /// <summary>
        /// ネットワークアンカーを削除
        /// </summary>
        public void RemoveNetworkAnchor(string anchorId)
        {
            if (!HasStateAuthority)
            {
                Debug.LogWarning("Only the host can remove network anchors");
                return;
            }

            RemoveNetworkAnchorRPC(anchorId);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RemoveNetworkAnchorRPC(NetworkString<32> anchorId)
        {
            string id = anchorId.ToString();
            
            if (NetworkAnchors.ContainsKey(anchorId))
            {
                NetworkAnchors.Remove(anchorId);
            }

            // ローカルアンカーの削除
            if (arAnchors.ContainsKey(id))
            {
                if (arAnchors[id] != null)
                    Destroy(arAnchors[id].gameObject);
                arAnchors.Remove(id);
            }

            if (mlAnchors.ContainsKey(id))
            {
                // ML Anchorの削除
                var mlAnchor = mlAnchors[id];
                mlAnchor.Delete();
                mlAnchors.Remove(id);
            }

            OnNetworkAnchorRemoved?.Invoke(id);
            Debug.Log($"Removed network anchor: {id}");
        }

        #region OpenXR Storage Callbacks
        
        private void OnOpenXRAnchorCreated(Pose pose, ulong anchorId, string anchorMapPositionId, UnityEngine.XR.OpenXR.NativeTypes.XrResult result)
        {
            if (result == UnityEngine.XR.OpenXR.NativeTypes.XrResult.Success)
            {
                Debug.Log($"OpenXR Anchor created: {anchorMapPositionId}");
                // ネットワークアンカーとして登録
                if (HasStateAuthority && !NetworkAnchors.ContainsKey(anchorMapPositionId))
                {
                    SyncOpenXRAnchorToNetwork(pose, anchorMapPositionId);
                }
            }
        }

        private void OnOpenXRAnchorPublished(ulong anchorId, string anchorMapPositionId)
        {
            Debug.Log($"OpenXR Anchor published: {anchorMapPositionId}");
        }

        private void OnOpenXRAnchorDeleted(List<string> anchorMapPositionIds)
        {
            foreach (var id in anchorMapPositionIds)
            {
                Debug.Log($"OpenXR Anchor deleted: {id}");
                if (HasStateAuthority && NetworkAnchors.ContainsKey(id))
                {
                    RemoveNetworkAnchor(id);
                }
            }
        }

        private void OnOpenXRQueryComplete(List<string> anchorMapPositionIds)
        {
            foreach (var id in anchorMapPositionIds)
            {
                if (!NetworkAnchors.ContainsKey(id))
                {
                    openXRStorage.CreateSpatialAnchorsFromStorage(new List<string> { id });
                }
            }
        }

        private void SyncOpenXRAnchorToNetwork(Pose pose, string anchorId)
        {
            if (!NetworkAnchors.ContainsKey(anchorId))
            {
                NetworkAnchorData anchorData = new NetworkAnchorData
                {
                    Id = anchorId,
                    Position = pose.position,
                    Rotation = pose.rotation,
                    IsPersistent = true,
                    CreationTime = (float)NetworkTime.Time
                };
                
                NetworkAnchors.Add(anchorId, anchorData);
                OnAnchorSynchronized?.Invoke(anchorId);
            }
        }

        #endregion

        public int GetNetworkAnchorCount()
        {
            return NetworkAnchors.Count;
        }

        public List<NetworkAnchorData> GetAllNetworkAnchors()
        {
            List<NetworkAnchorData> anchors = new List<NetworkAnchorData>();
            foreach (var kvp in NetworkAnchors)
            {
                anchors.Add(kvp.Value);
            }
            return anchors;
        }
    }

    /// <summary>
    /// ネットワーク同期されるアンカーデータ構造
    /// </summary>
    [System.Serializable]
    public struct NetworkAnchorData : INetworkStruct
    {
        public NetworkString<32> Id;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool IsPersistent;
        public float CreationTime;
    }
}