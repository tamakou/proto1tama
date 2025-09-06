using System.Collections;
using UnityEngine;
using Fusion;
using MagicLeap.Utilities;

namespace MagicLeap.NetworkedSpatialAnchors
{
    /// <summary>
    /// 空間アンカーベースの共有参照点
    /// SharedReferencePointを拡張して空間アンカーによる座標統一を実現
    /// </summary>
    public class SharedSpatialReferencePoint : Singleton<SharedSpatialReferencePoint>, INetworkBehaviour
    {
        [Header("Reference Anchor Configuration")]
        [SerializeField] private string referenceAnchorId = "shared_reference_anchor";
        [SerializeField] private bool createReferenceAnchorOnHost = true;
        [SerializeField] private Vector3 fallbackPosition = Vector3.zero;
        [SerializeField] private float anchorSearchRadius = 20f;

        [Header("Synchronization")]
        [SerializeField] private float positionUpdateInterval = 0.1f;
        [SerializeField] private float rotationUpdateInterval = 0.1f;

        // ネットワーク同期されるデータ
        [Networked] public Vector3 NetworkPosition { get; set; }
        [Networked] public Quaternion NetworkRotation { get; set; }
        [Networked] public bool IsReferenceAnchored { get; set; }
        [Networked] public NetworkString<32> ReferenceAnchorId { get; set; }

        // NetworkBehaviour実装用のプロパティ
        public NetworkRunner Runner { get; set; }
        public NetworkObject Object { get; set; }

        // 内部状態
        private NetworkedSpatialAnchorManager anchorManager;
        private bool isInitialized = false;
        private Coroutine positionUpdateCoroutine;
        private Vector3 lastPosition;
        private Quaternion lastRotation;

        // イベント
        public System.Action<Vector3, Quaternion> OnReferencePointUpdated;
        public System.Action OnReferenceAnchorEstablished;
        public System.Action OnReferenceAnchorLost;

        protected override void Awake()
        {
            base.Awake();
            
            // 初期値設定
            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }

        public void Spawned()
        {
            Debug.Log("SharedSpatialReferencePoint spawned");
            
            // NetworkedSpatialAnchorManagerとの連携
            anchorManager = FindObjectOfType<NetworkedSpatialAnchorManager>();
            if (anchorManager == null)
            {
                Debug.LogError("NetworkedSpatialAnchorManager not found!");
                return;
            }

            // アンカーマネージャーのイベント購読
            anchorManager.OnNetworkAnchorCreated += OnNetworkAnchorCreated;
            anchorManager.OnNetworkAnchorRemoved += OnNetworkAnchorRemoved;
            anchorManager.OnAnchorSynchronized += OnAnchorSynchronized;

            // ホストの場合は参照アンカーを作成または検索
            if (Object.HasStateAuthority)
            {
                StartCoroutine(InitializeReferenceAnchor());
            }
            else
            {
                // クライアントの場合はホストの参照アンカーを待機
                StartCoroutine(WaitForReferenceAnchor());
            }

            // 位置更新開始
            if (positionUpdateCoroutine == null)
            {
                positionUpdateCoroutine = StartCoroutine(UpdatePositionCoroutine());
            }

            isInitialized = true;
        }

        public void Despawned(NetworkRunner runner, bool hasState)
        {
            if (anchorManager != null)
            {
                anchorManager.OnNetworkAnchorCreated -= OnNetworkAnchorCreated;
                anchorManager.OnNetworkAnchorRemoved -= OnNetworkAnchorRemoved;
                anchorManager.OnAnchorSynchronized -= OnAnchorSynchronized;
            }

            if (positionUpdateCoroutine != null)
            {
                StopCoroutine(positionUpdateCoroutine);
                positionUpdateCoroutine = null;
            }
        }

        private IEnumerator InitializeReferenceAnchor()
        {
            yield return new WaitForSeconds(1f); // システムの初期化を待機

            // 既存の参照アンカーを検索
            var existingAnchors = anchorManager.GetAllNetworkAnchors();
            foreach (var anchor in existingAnchors)
            {
                if (anchor.Id.ToString() == referenceAnchorId)
                {
                    EstablishReferenceFromAnchor(anchor);
                    yield break;
                }
            }

            // 既存のアンカーが見つからない場合、新しく作成
            if (createReferenceAnchorOnHost)
            {
                Vector3 referencePosition = Camera.main != null ? Camera.main.transform.position : fallbackPosition;
                Quaternion referenceRotation = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;
                
                CreateReferenceAnchor(referencePosition, referenceRotation);
            }
        }

        private IEnumerator WaitForReferenceAnchor()
        {
            float waitTime = 0f;
            const float maxWaitTime = 30f; // 30秒でタイムアウト

            while (!IsReferenceAnchored && waitTime < maxWaitTime)
            {
                yield return new WaitForSeconds(0.5f);
                waitTime += 0.5f;

                // 参照アンカーが作成されたかチェック
                if (!ReferenceAnchorId.IsEmpty)
                {
                    var existingAnchors = anchorManager.GetAllNetworkAnchors();
                    foreach (var anchor in existingAnchors)
                    {
                        if (anchor.Id.ToString() == ReferenceAnchorId.ToString())
                        {
                            EstablishReferenceFromAnchor(anchor);
                            yield break;
                        }
                    }
                }
            }

            if (!IsReferenceAnchored)
            {
                Debug.LogWarning("Failed to establish reference anchor within timeout period");
                // フォールバック位置を使用
                SetFallbackReference();
            }
        }

        private void CreateReferenceAnchor(Vector3 position, Quaternion rotation)
        {
            Debug.Log($"Creating reference anchor at position: {position}");
            
            // 参照アンカーを作成
            anchorManager.CreateNetworkAnchor(position, rotation, true);
            
            // 作成したアンカーが参照アンカーとして設定されるのを待つ
            StartCoroutine(WaitForReferenceAnchorCreation());
        }

        private IEnumerator WaitForReferenceAnchorCreation()
        {
            float waitTime = 0f;
            const float maxWaitTime = 10f;

            while (waitTime < maxWaitTime)
            {
                yield return new WaitForSeconds(0.1f);
                waitTime += 0.1f;

                var anchors = anchorManager.GetAllNetworkAnchors();
                foreach (var anchor in anchors)
                {
                    if (Vector3.Distance(anchor.Position, transform.position) < 1f) // 近い位置のアンカーを参照アンカーとして設定
                    {
                        EstablishReferenceFromAnchor(anchor);
                        yield break;
                    }
                }
            }

            Debug.LogWarning("Reference anchor creation timeout");
            SetFallbackReference();
        }

        private void EstablishReferenceFromAnchor(NetworkAnchorData anchorData)
        {
            if (Object.HasStateAuthority)
            {
                NetworkPosition = anchorData.Position;
                NetworkRotation = anchorData.Rotation;
                IsReferenceAnchored = true;
                ReferenceAnchorId = anchorData.Id;
            }

            // ローカルのTransformを更新
            transform.position = anchorData.Position;
            transform.rotation = anchorData.Rotation;

            lastPosition = anchorData.Position;
            lastRotation = anchorData.Rotation;

            Debug.Log($"Reference point established using anchor: {anchorData.Id}");
            OnReferenceAnchorEstablished?.Invoke();
        }

        private void SetFallbackReference()
        {
            if (Object.HasStateAuthority)
            {
                NetworkPosition = fallbackPosition;
                NetworkRotation = Quaternion.identity;
                IsReferenceAnchored = false;
                ReferenceAnchorId = "";
            }

            transform.position = fallbackPosition;
            transform.rotation = Quaternion.identity;

            Debug.Log("Using fallback reference position");
        }

        private IEnumerator UpdatePositionCoroutine()
        {
            while (isInitialized)
            {
                yield return new WaitForSeconds(positionUpdateInterval);

                if (!Object.HasStateAuthority)
                {
                    // クライアント: ネットワークから受信した位置に更新
                    if (Vector3.Distance(transform.position, NetworkPosition) > 0.01f ||
                        Quaternion.Angle(transform.rotation, NetworkRotation) > 0.1f)
                    {
                        transform.position = Vector3.Lerp(transform.position, NetworkPosition, Time.deltaTime * 5f);
                        transform.rotation = Quaternion.Slerp(transform.rotation, NetworkRotation, Time.deltaTime * 5f);
                        
                        OnReferencePointUpdated?.Invoke(transform.position, transform.rotation);
                    }
                }
                else if (IsReferenceAnchored)
                {
                    // ホスト: 参照アンカーが存在する場合はその位置を維持
                    // 参照アンカーの位置が更新された場合の処理
                    var anchors = anchorManager.GetAllNetworkAnchors();
                    foreach (var anchor in anchors)
                    {
                        if (anchor.Id.ToString() == ReferenceAnchorId.ToString())
                        {
                            if (Vector3.Distance(NetworkPosition, anchor.Position) > 0.01f ||
                                Quaternion.Angle(NetworkRotation, anchor.Rotation) > 0.1f)
                            {
                                NetworkPosition = anchor.Position;
                                NetworkRotation = anchor.Rotation;
                                transform.position = anchor.Position;
                                transform.rotation = anchor.Rotation;
                                
                                OnReferencePointUpdated?.Invoke(transform.position, transform.rotation);
                            }
                            break;
                        }
                    }
                }
            }
        }

        #region NetworkedSpatialAnchorManager Event Handlers

        private void OnNetworkAnchorCreated(NetworkAnchorData anchorData)
        {
            // 参照アンカーが未設定で、この新しいアンカーが参照アンカー候補の場合
            if (!IsReferenceAnchored && Object.HasStateAuthority)
            {
                if (anchorData.Id.ToString() == referenceAnchorId || 
                    Vector3.Distance(anchorData.Position, transform.position) < 1f)
                {
                    EstablishReferenceFromAnchor(anchorData);
                }
            }
        }

        private void OnNetworkAnchorRemoved(string anchorId)
        {
            // 参照アンカーが削除された場合
            if (IsReferenceAnchored && ReferenceAnchorId.ToString() == anchorId)
            {
                Debug.LogWarning("Reference anchor was removed!");
                
                if (Object.HasStateAuthority)
                {
                    IsReferenceAnchored = false;
                    ReferenceAnchorId = "";
                    
                    // 新しい参照アンカーを探すか作成
                    StartCoroutine(ReestablishReferenceAnchor());
                }
                
                OnReferenceAnchorLost?.Invoke();
            }
        }

        private void OnAnchorSynchronized(string anchorId)
        {
            // 同期されたアンカーが参照アンカーの候補かチェック
            if (!IsReferenceAnchored)
            {
                var anchors = anchorManager.GetAllNetworkAnchors();
                foreach (var anchor in anchors)
                {
                    if (anchor.Id.ToString() == anchorId && anchor.Id.ToString() == referenceAnchorId)
                    {
                        EstablishReferenceFromAnchor(anchor);
                        break;
                    }
                }
            }
        }

        private IEnumerator ReestablishReferenceAnchor()
        {
            yield return new WaitForSeconds(1f);

            // 他の利用可能なアンカーから参照アンカーを選択
            var anchors = anchorManager.GetAllNetworkAnchors();
            if (anchors.Count > 0)
            {
                // 最も近いアンカーを選択
                NetworkAnchorData closestAnchor = anchors[0];
                float closestDistance = Vector3.Distance(transform.position, anchors[0].Position);

                foreach (var anchor in anchors)
                {
                    float distance = Vector3.Distance(transform.position, anchor.Position);
                    if (distance < closestDistance)
                    {
                        closestAnchor = anchor;
                        closestDistance = distance;
                    }
                }

                EstablishReferenceFromAnchor(closestAnchor);
            }
            else
            {
                // アンカーがない場合は新しく作成
                CreateReferenceAnchor(transform.position, transform.rotation);
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 指定した座標を共有座標系に変換
        /// </summary>
        public Vector3 TransformPoint(Vector3 localPoint)
        {
            return transform.TransformPoint(localPoint);
        }

        /// <summary>
        /// 指定した方向を共有座標系に変換
        /// </summary>
        public Vector3 TransformDirection(Vector3 localDirection)
        {
            return transform.TransformDirection(localDirection);
        }

        /// <summary>
        /// 共有座標系の座標をローカル座標系に変換
        /// </summary>
        public Vector3 InverseTransformPoint(Vector3 worldPoint)
        {
            return transform.InverseTransformPoint(worldPoint);
        }

        /// <summary>
        /// 参照アンカーを手動で設定
        /// </summary>
        public void SetReferenceAnchor(string anchorId)
        {
            if (!Object.HasStateAuthority)
            {
                Debug.LogWarning("Only the host can set reference anchor");
                return;
            }

            var anchors = anchorManager.GetAllNetworkAnchors();
            foreach (var anchor in anchors)
            {
                if (anchor.Id.ToString() == anchorId)
                {
                    EstablishReferenceFromAnchor(anchor);
                    break;
                }
            }
        }

        /// <summary>
        /// 現在の参照アンカー情報を取得
        /// </summary>
        public (bool isAnchored, string anchorId, Vector3 position, Quaternion rotation) GetReferenceInfo()
        {
            return (IsReferenceAnchored, ReferenceAnchorId.ToString(), NetworkPosition, NetworkRotation);
        }

        #endregion

        public void FixedUpdateNetwork()
        {
            // Fusion NetworkBehaviour requirement
        }

        public void Render()
        {
            // Fusion NetworkBehaviour requirement
        }
    }
}