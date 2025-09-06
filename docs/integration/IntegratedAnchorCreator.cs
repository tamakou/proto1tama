using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using LearnXR.Core;
using Fusion;

namespace MagicLeap.NetworkedSpatialAnchors
{
    /// <summary>
    /// 統合されたアンカー作成システム
    /// AnchorCreatorを拡張してネットワーク機能を追加
    /// </summary>
    public class IntegratedAnchorCreator : Singleton<IntegratedAnchorCreator>
    {
        [Header("Input Configuration")]
        [SerializeField] private InputActionProperty createAnchorAction;
        [SerializeField] private InputActionProperty deleteAnchorAction;
        [SerializeField] private InputActionProperty menuAction;
        [SerializeField] private Transform controllerTransform;

        [Header("Anchor Creation Settings")]
        [SerializeField] private GameObject anchorPrefab;
        [SerializeField] private float previewUpdateRate = 30f;
        [SerializeField] private Color previewColor = Color.gray;
        [SerializeField] private Color creatingColor = Color.yellow;
        [SerializeField] private Color completedColor = Color.green;

        [Header("Network Settings")]
        [SerializeField] private bool createPersistentAnchors = true;
        [SerializeField] private bool requireNetworkAuthority = true;
        [SerializeField] private float deletionRadius = 2f;

        [Header("UI Integration")]
        [SerializeField] private IntegratedAnchorControlPanel controlPanel;

        // システム参照
        private NetworkedSpatialAnchorManager networkAnchorManager;
        private SharedSpatialReferencePoint sharedReferencePoint;
        private Camera mainCamera;

        // 作成プレビュー
        private GameObject previewAnchor;
        private Coroutine previewUpdateCoroutine;
        private bool isCreatingAnchor = false;

        // 統計
        private int localAnchorsCreated = 0;
        private int networkAnchorsCreated = 0;
        private int anchorsDeleted = 0;

        public string Status => $"Created: {networkAnchorsCreated}, Deleted: {anchorsDeleted}, Network Active: {networkAnchorManager?.GetNetworkAnchorCount() ?? 0}";

        protected override void Awake()
        {
            base.Awake();
            mainCamera = Camera.main;
        }

        private void Start()
        {
            StartCoroutine(InitializeSystem());
        }

        private IEnumerator InitializeSystem()
        {
            // システムの初期化を待機
            yield return new WaitUntil(() => NetworkRunner.Instances.Count > 0);
            yield return new WaitForSeconds(1f);

            // 必要なシステムコンポーネントを取得
            networkAnchorManager = FindObjectOfType<NetworkedSpatialAnchorManager>();
            sharedReferencePoint = FindObjectOfType<SharedSpatialReferencePoint>();

            if (networkAnchorManager == null)
            {
                Debug.LogError("NetworkedSpatialAnchorManager not found! IntegratedAnchorCreator requires this component.");
                return;
            }

            if (sharedReferencePoint == null)
            {
                Debug.LogError("SharedSpatialReferencePoint not found! IntegratedAnchorCreator requires this component.");
                return;
            }

            // 入力アクション設定
            SetupInputActions();

            // ネットワークイベント購読
            networkAnchorManager.OnNetworkAnchorCreated += OnNetworkAnchorCreated;
            networkAnchorManager.OnNetworkAnchorRemoved += OnNetworkAnchorRemoved;

            // コントロールパネル統合
            if (controlPanel == null)
            {
                controlPanel = FindObjectOfType<IntegratedAnchorControlPanel>();
            }

            if (controlPanel != null)
            {
                controlPanel.onCreateAnchorAtCamera.AddListener(CreateAnchorAtCamera);
                controlPanel.onClearAllAnchors.AddListener(ClearAllNetworkAnchors);
                controlPanel.onRestoreAnchors.AddListener(RestoreNetworkAnchors);
            }

            Debug.Log("IntegratedAnchorCreator initialized successfully");
        }

        private void SetupInputActions()
        {
            createAnchorAction.action.Enable();
            deleteAnchorAction.action.Enable();
            menuAction.action.Enable();

            createAnchorAction.action.started += OnCreateAnchorStarted;
            createAnchorAction.action.canceled += OnCreateAnchorCanceled;
            deleteAnchorAction.action.performed += OnDeleteAnchorPerformed;
            menuAction.action.performed += OnMenuActionPerformed;
        }

        private void OnDestroy()
        {
            // イベント購読解除
            if (networkAnchorManager != null)
            {
                networkAnchorManager.OnNetworkAnchorCreated -= OnNetworkAnchorCreated;
                networkAnchorManager.OnNetworkAnchorRemoved -= OnNetworkAnchorRemoved;
            }

            // 入力アクション解除
            createAnchorAction.action.started -= OnCreateAnchorStarted;
            createAnchorAction.action.canceled -= OnCreateAnchorCanceled;
            deleteAnchorAction.action.performed -= OnDeleteAnchorPerformed;
            menuAction.action.performed -= OnMenuActionPerformed;

            // コルーチン停止
            if (previewUpdateCoroutine != null)
            {
                StopCoroutine(previewUpdateCoroutine);
            }
        }

        #region Input Event Handlers

        private void OnCreateAnchorStarted(InputAction.CallbackContext context)
        {
            if (!CanCreateAnchor())
                return;

            StartAnchorPreview();
        }

        private void OnCreateAnchorCanceled(InputAction.CallbackContext context)
        {
            if (previewAnchor != null && !isCreatingAnchor)
            {
                CompleteAnchorCreation();
            }
        }

        private void OnDeleteAnchorPerformed(InputAction.CallbackContext context)
        {
            DeleteNearestAnchor();
        }

        private void OnMenuActionPerformed(InputAction.CallbackContext context)
        {
            if (controlPanel != null)
            {
                controlPanel.ToggleVisibility();
            }
        }

        #endregion

        #region Anchor Creation

        private bool CanCreateAnchor()
        {
            if (requireNetworkAuthority)
            {
                var networkObject = networkAnchorManager.GetComponent<NetworkObject>();
                if (networkObject != null && !networkObject.HasStateAuthority)
                {
                    Debug.LogWarning("Cannot create anchor: No network authority");
                    return false;
                }
            }

            if (isCreatingAnchor)
            {
                Debug.LogWarning("Anchor creation already in progress");
                return false;
            }

            return networkAnchorManager != null && controllerTransform != null;
        }

        private void StartAnchorPreview()
        {
            if (previewAnchor == null)
            {
                previewAnchor = Instantiate(anchorPrefab, controllerTransform.position, controllerTransform.rotation);
                SetAnchorColor(previewAnchor, previewColor);
                
                // プレビュー更新開始
                if (previewUpdateCoroutine == null)
                {
                    previewUpdateCoroutine = StartCoroutine(UpdatePreviewPosition());
                }
            }

            Debug.Log("Anchor preview started");
        }

        private IEnumerator UpdatePreviewPosition()
        {
            while (previewAnchor != null && !isCreatingAnchor)
            {
                previewAnchor.transform.position = controllerTransform.position;
                previewAnchor.transform.rotation = controllerTransform.rotation;
                yield return new WaitForSeconds(1f / previewUpdateRate);
            }
            
            previewUpdateCoroutine = null;
        }

        private void CompleteAnchorCreation()
        {
            if (previewAnchor == null) return;

            isCreatingAnchor = true;
            SetAnchorColor(previewAnchor, creatingColor);

            Vector3 finalPosition = previewAnchor.transform.position;
            Quaternion finalRotation = previewAnchor.transform.rotation;

            // プレビューオブジェクトを削除
            Destroy(previewAnchor);
            previewAnchor = null;

            // ネットワークアンカーを作成
            CreateNetworkAnchor(finalPosition, finalRotation);
        }

        private void CreateNetworkAnchor(Vector3 position, Quaternion rotation)
        {
            // 共有座標系での相対位置に変換
            Vector3 relativePosition = sharedReferencePoint.InverseTransformPoint(position);
            Quaternion relativeRotation = Quaternion.Inverse(sharedReferencePoint.transform.rotation) * rotation;

            // ネットワークアンカー作成
            networkAnchorManager.CreateNetworkAnchor(position, rotation, createPersistentAnchors);
            
            localAnchorsCreated++;
            Debug.Log($"Network anchor creation requested at position: {position}");
        }

        public void CreateAnchorAtCamera()
        {
            if (!CanCreateAnchor()) return;

            if (mainCamera != null)
            {
                Vector3 position = mainCamera.transform.position + mainCamera.transform.forward * 2f;
                Quaternion rotation = mainCamera.transform.rotation;
                CreateNetworkAnchor(position, rotation);
            }
        }

        #endregion

        #region Anchor Deletion

        private void DeleteNearestAnchor()
        {
            if (requireNetworkAuthority)
            {
                var networkObject = networkAnchorManager.GetComponent<NetworkObject>();
                if (networkObject != null && !networkObject.HasStateAuthority)
                {
                    Debug.LogWarning("Cannot delete anchor: No network authority");
                    return;
                }
            }

            var allAnchors = networkAnchorManager.GetAllNetworkAnchors();
            NetworkAnchorData? nearestAnchor = null;
            float nearestDistance = float.MaxValue;

            foreach (var anchor in allAnchors)
            {
                float distance = Vector3.Distance(controllerTransform.position, anchor.Position);
                if (distance <= deletionRadius && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestAnchor = anchor;
                }
            }

            if (nearestAnchor.HasValue)
            {
                networkAnchorManager.RemoveNetworkAnchor(nearestAnchor.Value.Id.ToString());
                Debug.Log($"Requested deletion of nearest anchor: {nearestAnchor.Value.Id}");
            }
            else
            {
                Debug.Log("No anchors found within deletion radius");
            }
        }

        public void ClearAllNetworkAnchors()
        {
            if (requireNetworkAuthority)
            {
                var networkObject = networkAnchorManager.GetComponent<NetworkObject>();
                if (networkObject != null && !networkObject.HasStateAuthority)
                {
                    Debug.LogWarning("Cannot clear anchors: No network authority");
                    return;
                }
            }

            var allAnchors = networkAnchorManager.GetAllNetworkAnchors();
            foreach (var anchor in allAnchors)
            {
                networkAnchorManager.RemoveNetworkAnchor(anchor.Id.ToString());
            }

            Debug.Log($"Requested clearing of {allAnchors.Count} network anchors");
        }

        #endregion

        #region Network Event Handlers

        private void OnNetworkAnchorCreated(NetworkAnchorData anchorData)
        {
            Debug.Log($"Network anchor created: {anchorData.Id}");
            networkAnchorsCreated++;
            isCreatingAnchor = false;

            // 作成完了の視覚フィードバック
            StartCoroutine(ShowAnchorCreatedFeedback(anchorData.Position));
        }

        private void OnNetworkAnchorRemoved(string anchorId)
        {
            Debug.Log($"Network anchor removed: {anchorId}");
            anchorsDeleted++;

            // 削除完了の視覚フィードバック
            StartCoroutine(ShowAnchorDeletedFeedback());
        }

        private IEnumerator ShowAnchorCreatedFeedback(Vector3 position)
        {
            GameObject feedbackObject = Instantiate(anchorPrefab, position, Quaternion.identity);
            SetAnchorColor(feedbackObject, completedColor);
            
            // 3秒間表示後に削除
            yield return new WaitForSeconds(3f);
            
            if (feedbackObject != null)
                Destroy(feedbackObject);
        }

        private IEnumerator ShowAnchorDeletedFeedback()
        {
            // 簡単な視覚フィードバック（実装可能）
            yield return null;
        }

        #endregion

        #region Anchor Restoration

        public void RestoreNetworkAnchors()
        {
            // 既存のアンカーの復元は NetworkedSpatialAnchorManager が自動的に処理
            Debug.Log("Anchor restoration requested");
        }

        #endregion

        #region Utility Methods

        private void SetAnchorColor(GameObject anchorObject, Color color)
        {
            var renderer = anchorObject.GetComponentInChildren<Renderer>();
            if (renderer != null && renderer.material != null)
            {
                renderer.material.color = color;
            }
        }

        /// <summary>
        /// 現在のネットワーク権限状態を取得
        /// </summary>
        public bool HasNetworkAuthority()
        {
            if (!requireNetworkAuthority) return true;
            
            var networkObject = networkAnchorManager?.GetComponent<NetworkObject>();
            return networkObject != null && networkObject.HasStateAuthority;
        }

        /// <summary>
        /// 指定位置に最も近いアンカーを取得
        /// </summary>
        public NetworkAnchorData? GetNearestAnchor(Vector3 position, float maxDistance = float.MaxValue)
        {
            var allAnchors = networkAnchorManager.GetAllNetworkAnchors();
            NetworkAnchorData? nearestAnchor = null;
            float nearestDistance = maxDistance;

            foreach (var anchor in allAnchors)
            {
                float distance = Vector3.Distance(position, anchor.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestAnchor = anchor;
                }
            }

            return nearestAnchor;
        }

        /// <summary>
        /// 指定範囲内のアンカーを取得
        /// </summary>
        public List<NetworkAnchorData> GetAnchorsInRange(Vector3 center, float radius)
        {
            var anchorsInRange = new List<NetworkAnchorData>();
            var allAnchors = networkAnchorManager.GetAllNetworkAnchors();

            foreach (var anchor in allAnchors)
            {
                if (Vector3.Distance(center, anchor.Position) <= radius)
                {
                    anchorsInRange.Add(anchor);
                }
            }

            return anchorsInRange;
        }

        #endregion
    }
}