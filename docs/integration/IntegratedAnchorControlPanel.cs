using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using LearnXR.Core;

namespace MagicLeap.NetworkedSpatialAnchors
{
    /// <summary>
    /// 統合されたアンカーコントロールパネル
    /// AnchorControlPanelを拡張してネットワーク機能を追加
    /// </summary>
    public class IntegratedAnchorControlPanel : Singleton<IntegratedAnchorControlPanel>
    {
        [Header("UI Components")]
        [SerializeField] private Canvas panelCanvas;
        [SerializeField] private Button createAnchorButton;
        [SerializeField] private Button clearAllAnchorsButton;
        [SerializeField] private Button restoreAnchorsButton;
        [SerializeField] private Button toggleVisibilityButton;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI networkStatusText;
        [SerializeField] private TextMeshProUGUI referencePointText;

        [Header("Panel Positioning")]
        [SerializeField] private float distanceFromCamera = 1.0f;
        [SerializeField] private Vector3 positionOffset = new Vector3(0, -0.3f, 0);
        [SerializeField] private bool followCamera = false;
        [SerializeField] private float followSmoothness = 2f;

        [Header("Display Settings")]
        [SerializeField] private float refreshRate = 0.5f;
        [SerializeField] private bool showDetailedNetworkInfo = true;
        [SerializeField] private bool showReferencePointInfo = true;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color warningColor = Color.yellow;
        [SerializeField] private Color errorColor = Color.red;

        [Header("Auto-Hide Settings")]
        [SerializeField] private bool enableAutoHide = true;
        [SerializeField] private float autoHideDelay = 10f;
        
        // Events
        [System.Serializable]
        public class AnchorControlEvents
        {
            public UnityEvent onCreateAnchorAtCamera = new UnityEvent();
            public UnityEvent onClearAllAnchors = new UnityEvent();
            public UnityEvent onRestoreAnchors = new UnityEvent();
            public UnityEvent onToggleVisibility = new UnityEvent();
        }

        public AnchorControlEvents Events = new AnchorControlEvents();
        
        // 外部からアクセス可能なイベント
        public UnityEvent onCreateAnchorAtCamera => Events.onCreateAnchorAtCamera;
        public UnityEvent onClearAllAnchors => Events.onClearAllAnchors;
        public UnityEvent onRestoreAnchors => Events.onRestoreAnchors;

        // システム参照
        private IntegratedAnchorCreator anchorCreator;
        private NetworkedSpatialAnchorManager networkAnchorManager;
        private SharedSpatialReferencePoint sharedReferencePoint;
        private Camera mainCamera;

        // 状態管理
        private bool isPanelVisible = true;
        private Coroutine statusUpdateCoroutine;
        private Coroutine followCameraCoroutine;
        private Coroutine autoHideCoroutine;
        private float lastInteractionTime;

        protected override void Awake()
        {
            base.Awake();
            mainCamera = Camera.main;
            lastInteractionTime = Time.time;
        }

        private void Start()
        {
            StartCoroutine(InitializePanel());
        }

        private IEnumerator InitializePanel()
        {
            // システムコンポーネントの初期化を待機
            yield return new WaitUntil(() => IntegratedAnchorCreator.Instance != null);
            yield return new WaitForSeconds(0.5f);

            // システム参照を取得
            anchorCreator = IntegratedAnchorCreator.Instance;
            networkAnchorManager = FindObjectOfType<NetworkedSpatialAnchorManager>();
            sharedReferencePoint = FindObjectOfType<SharedSpatialReferencePoint>();

            // UI設定
            SetupUIComponents();

            // 初期位置設定
            ResetPosition();

            // 更新コルーチン開始
            if (statusUpdateCoroutine == null)
            {
                statusUpdateCoroutine = StartCoroutine(UpdateStatusDisplay());
            }

            // カメラフォロー設定
            if (followCamera && followCameraCoroutine == null)
            {
                followCameraCoroutine = StartCoroutine(FollowCameraCoroutine());
            }

            // 自動非表示設定
            if (enableAutoHide && autoHideCoroutine == null)
            {
                autoHideCoroutine = StartCoroutine(AutoHideCoroutine());
            }

            Debug.Log("IntegratedAnchorControlPanel initialized");
        }

        private void SetupUIComponents()
        {
            // ボタンイベント設定
            if (createAnchorButton != null)
            {
                createAnchorButton.onClick.AddListener(() => {
                    Events.onCreateAnchorAtCamera.Invoke();
                    OnUserInteraction();
                });
            }

            if (clearAllAnchorsButton != null)
            {
                clearAllAnchorsButton.onClick.AddListener(() => {
                    Events.onClearAllAnchors.Invoke();
                    OnUserInteraction();
                });
            }

            if (restoreAnchorsButton != null)
            {
                restoreAnchorsButton.onClick.AddListener(() => {
                    Events.onRestoreAnchors.Invoke();
                    OnUserInteraction();
                });
            }

            if (toggleVisibilityButton != null)
            {
                toggleVisibilityButton.onClick.AddListener(() => {
                    ToggleVisibility();
                    OnUserInteraction();
                });
            }

            // 初期表示設定
            if (panelCanvas != null)
            {
                panelCanvas.gameObject.SetActive(isPanelVisible);
            }
        }

        #region Position Management

        public void ResetPosition()
        {
            if (mainCamera == null) return;

            Vector3 cameraForward = mainCamera.transform.forward;
            Vector3 newPosition = mainCamera.transform.position + cameraForward * distanceFromCamera + positionOffset;
            
            transform.position = newPosition;
            
            // カメラの方向を向く
            Vector3 lookDirection = mainCamera.transform.position - transform.position;
            if (lookDirection != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(-lookDirection);
            }

            OnUserInteraction();
        }

        private IEnumerator FollowCameraCoroutine()
        {
            while (followCamera)
            {
                if (mainCamera != null && isPanelVisible)
                {
                    Vector3 targetPosition = mainCamera.transform.position + 
                                           mainCamera.transform.forward * distanceFromCamera + 
                                           positionOffset;

                    transform.position = Vector3.Lerp(transform.position, targetPosition, 
                                                    Time.deltaTime * followSmoothness);

                    // 緩やかにカメラの方向を向く
                    Vector3 lookDirection = mainCamera.transform.position - transform.position;
                    if (lookDirection != Vector3.zero)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(-lookDirection);
                        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 
                                                            Time.deltaTime * followSmoothness);
                    }
                }

                yield return new WaitForSeconds(1f / 30f); // 30 FPS
            }
        }

        #endregion

        #region Visibility Management

        public void ToggleVisibility()
        {
            isPanelVisible = !isPanelVisible;
            
            if (panelCanvas != null)
            {
                panelCanvas.gameObject.SetActive(isPanelVisible);
            }

            Events.onToggleVisibility.Invoke();
            
            Debug.Log($"Panel visibility toggled: {isPanelVisible}");
        }

        public void SetVisibility(bool visible)
        {
            isPanelVisible = visible;
            
            if (panelCanvas != null)
            {
                panelCanvas.gameObject.SetActive(isPanelVisible);
            }
        }

        private IEnumerator AutoHideCoroutine()
        {
            while (enableAutoHide)
            {
                if (Time.time - lastInteractionTime > autoHideDelay && isPanelVisible)
                {
                    SetVisibility(false);
                }

                yield return new WaitForSeconds(1f);
            }
        }

        private void OnUserInteraction()
        {
            lastInteractionTime = Time.time;
            
            if (!isPanelVisible && enableAutoHide)
            {
                SetVisibility(true);
            }
        }

        #endregion

        #region Status Display

        private IEnumerator UpdateStatusDisplay()
        {
            while (true)
            {
                yield return new WaitForSeconds(refreshRate);
                
                if (isPanelVisible)
                {
                    UpdateAnchorStatus();
                    UpdateNetworkStatus();
                    UpdateReferencePointStatus();
                }
            }
        }

        private void UpdateAnchorStatus()
        {
            if (statusText == null || anchorCreator == null) return;

            string status = anchorCreator.Status;
            bool hasNetworkAuthority = anchorCreator.HasNetworkAuthority();

            statusText.text = $"Anchor Status:\n{status}\n" +
                             $"Authority: {(hasNetworkAuthority ? "Host" : "Client")}";

            // 権限に応じて色を変更
            statusText.color = hasNetworkAuthority ? normalColor : warningColor;
        }

        private void UpdateNetworkStatus()
        {
            if (networkStatusText == null || !showDetailedNetworkInfo) return;

            string networkStatus = "Network: Disconnected";
            Color statusColor = errorColor;

            if (networkAnchorManager != null)
            {
                int anchorCount = networkAnchorManager.GetNetworkAnchorCount();
                var networkObject = networkAnchorManager.GetComponent<Fusion.NetworkObject>();
                
                if (networkObject != null && networkObject.Runner != null)
                {
                    bool isHost = networkObject.HasStateAuthority;
                    int playerCount = networkObject.Runner.ActivePlayers.Count;
                    
                    networkStatus = $"Network: Connected\n" +
                                   $"Players: {playerCount}\n" +
                                   $"Network Anchors: {anchorCount}\n" +
                                   $"Role: {(isHost ? "Host" : "Client")}";
                    statusColor = normalColor;
                }
                else
                {
                    networkStatus = "Network: Initializing...";
                    statusColor = warningColor;
                }
            }

            networkStatusText.text = networkStatus;
            networkStatusText.color = statusColor;
        }

        private void UpdateReferencePointStatus()
        {
            if (referencePointText == null || !showReferencePointInfo) return;

            string referenceStatus = "Reference: Not Set";
            Color statusColor = warningColor;

            if (sharedReferencePoint != null)
            {
                var (isAnchored, anchorId, position, rotation) = sharedReferencePoint.GetReferenceInfo();
                
                if (isAnchored)
                {
                    referenceStatus = $"Reference: Anchored\n" +
                                     $"Anchor ID: {anchorId.Substring(0, Mathf.Min(8, anchorId.Length))}...\n" +
                                     $"Position: {position:F2}";
                    statusColor = normalColor;
                }
                else
                {
                    referenceStatus = $"Reference: Fallback\n" +
                                     $"Position: {position:F2}";
                    statusColor = warningColor;
                }
            }

            referencePointText.text = referenceStatus;
            referencePointText.color = statusColor;
        }

        #endregion

        #region Button State Management

        private void Update()
        {
            // ボタンの有効/無効状態を更新
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            bool hasAuthority = anchorCreator?.HasNetworkAuthority() ?? false;
            bool systemReady = networkAnchorManager != null && anchorCreator != null;

            // 権限が必要なボタンの状態更新
            if (createAnchorButton != null)
                createAnchorButton.interactable = systemReady && hasAuthority;

            if (clearAllAnchorsButton != null)
                clearAllAnchorsButton.interactable = systemReady && hasAuthority;

            if (restoreAnchorsButton != null)
                restoreAnchorsButton.interactable = systemReady;
        }

        #endregion

        #region Public API

        /// <summary>
        /// パネルの自動フォローを設定
        /// </summary>
        public void SetFollowCamera(bool follow)
        {
            followCamera = follow;
            
            if (follow && followCameraCoroutine == null)
            {
                followCameraCoroutine = StartCoroutine(FollowCameraCoroutine());
            }
            else if (!follow && followCameraCoroutine != null)
            {
                StopCoroutine(followCameraCoroutine);
                followCameraCoroutine = null;
            }
        }

        /// <summary>
        /// 自動非表示機能を設定
        /// </summary>
        public void SetAutoHide(bool autoHide, float delay = 10f)
        {
            enableAutoHide = autoHide;
            autoHideDelay = delay;
            
            if (autoHide && autoHideCoroutine == null)
            {
                autoHideCoroutine = StartCoroutine(AutoHideCoroutine());
            }
            else if (!autoHide && autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
                autoHideCoroutine = null;
            }
        }

        /// <summary>
        /// パネルの位置オフセットを設定
        /// </summary>
        public void SetPositionOffset(Vector3 offset)
        {
            positionOffset = offset;
        }

        /// <summary>
        /// カメラからの距離を設定
        /// </summary>
        public void SetDistanceFromCamera(float distance)
        {
            distanceFromCamera = distance;
        }

        #endregion

        private void OnDestroy()
        {
            // コルーチン停止
            if (statusUpdateCoroutine != null)
                StopCoroutine(statusUpdateCoroutine);
            
            if (followCameraCoroutine != null)
                StopCoroutine(followCameraCoroutine);
            
            if (autoHideCoroutine != null)
                StopCoroutine(autoHideCoroutine);

            // イベントクリア
            Events.onCreateAnchorAtCamera.RemoveAllListeners();
            Events.onClearAllAnchors.RemoveAllListeners();
            Events.onRestoreAnchors.RemoveAllListeners();
            Events.onToggleVisibility.RemoveAllListeners();
        }
    }
}