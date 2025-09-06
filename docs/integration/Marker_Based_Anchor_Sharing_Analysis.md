# マーカーベースColocationでのアンカー共有 - 技術的分析

## 質問への回答

**部分的にYES - ただし「真の永続アンカー共有」ではなく「相対位置ベースのアンカー再現」が可能です**

## マーカーベースColocationの仕組み

### 1. 基本原理

```csharp
// VirtualFiducialMarker.cs:149-154 での基本動作
private void OnUpdated(MarkerPose data)
{
    // 物理AprilTagマーカーの位置・回転を取得
    transform.position = data.Position;      // マーカーのワールド座標
    transform.rotation = data.Rotation;      // マーカーの回転
    UpdateTracking(data);                    // 位置フィルタリング処理
}
```

### 2. SharedReferencePoint統合

**MarkerColocation.unityシーンでの設定:**
- VirtualFiducialMarkerがSharedReferencePointとして機能
- 物理マーカー = 共有座標系の原点
- 両デバイスが同じ物理マーカーを基準にする

```csharp
// MarkerColocation.unity:1485, 1495 での設定確認
propertyPath: SharedReferencePoint
objectReference: {fileID: 2128075853}  // VirtualFiducialMarkerオブジェクト
```

## マーカーベースでのアンカー共有メカニズム

### シナリオ: ARCloudなしでのアンカー共有

#### **ローカルプレイヤー（DeviceA）の動作**

```
1. 物理AprilTagマーカーをスキャン
   └─ VirtualFiducialMarker.transform = マーカー位置
   └─ SharedReferencePoint = マーカー位置

2. マーカーに対する相対位置でアンカー作成
   └─ Anchor位置 = マーカー位置 + 相対オフセット
   └─ 例: マーカーから右に2m、前に1mの位置

3. Photon Fusion経由でアンカー情報送信
   └─ 送信データ: 相対位置、回転、その他メタデータ
```

#### **リモートプレイヤー（DeviceB）の動作**

```
1. 同じ物理AprilTagマーカーをスキャン  
   └─ VirtualFiducialMarker.transform = マーカー位置（DeviceAと同一物理位置）
   └─ SharedReferencePoint = マーカー位置（DeviceAと統一）

2. ネットワーク経由でアンカー情報受信
   └─ 受信データ: DeviceAが送信した相対位置情報

3. 受信した相対位置を基にローカルアンカー作成
   └─ Anchor位置 = 自分のマーカー位置 + 受信した相対オフセット
   └─ DeviceAと物理的に同じ位置にアンカー表示
```

## 技術的実現可能性

### ✅ **可能な部分**

1. **Space作成不要**: 
   - 物理マーカーが共有座標系を提供
   - Magic Leap Spacesアプリ不使用

2. **正確なColocation**:
   - サブセンチメートル精度でのアンカー位置共有
   - リアルタイム位置同期

3. **オフライン動作**:
   - インターネット接続不要
   -完全ローカルネットワークでの動作

### ❌ **制限事項**

1. **真の永続化なし**:
   ```csharp
   // 永続アンカーではなく、セッション限定の相対位置再現
   // アプリ終了後は情報が失われる
   // デバイス固有の永続ストレージには保存されない
   ```

2. **マーカー依存**:
   ```csharp
   // 物理マーカーが見えなくなると座標系が失われる
   // マーカーの可視性維持が必要
   // 屋外や広範囲での使用に制限
   ```

## 実装に必要な拡張

現在のMagicLeapPhotonFusionExampleを拡張する必要があります：

### 1. マーカーベースアンカー管理システム

```csharp
public class MarkerBasedAnchorManager : NetworkBehaviour
{
    [Header("Marker-based Anchor Settings")]
    public VirtualFiducialMarker referenceMarker;
    public GameObject anchorPrefab;
    
    // ネットワーク同期されるアンカーデータ
    [Networked, Capacity(50)]
    public NetworkDictionary<NetworkString<32>, MarkerAnchorData> MarkerAnchors => default;
    
    public struct MarkerAnchorData : INetworkStruct
    {
        public Vector3 RelativePosition;    // マーカーからの相対位置
        public Quaternion RelativeRotation; // マーカーからの相対回転
        public NetworkString<32> AnchorId;  // アンカー識別子
        public float CreationTime;          // 作成時刻
    }
    
    public void CreateMarkerBasedAnchor(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (!HasStateAuthority) return;
        
        // ワールド座標をマーカー相対座標に変換
        Vector3 relativePos = referenceMarker.transform.InverseTransformPoint(worldPosition);
        Quaternion relativeRot = Quaternion.Inverse(referenceMarker.transform.rotation) * worldRotation;
        
        // ネットワーク同期
        var anchorData = new MarkerAnchorData
        {
            RelativePosition = relativePos,
            RelativeRotation = relativeRot,
            AnchorId = System.Guid.NewGuid().ToString(),
            CreationTime = (float)NetworkTime.Time
        };
        
        MarkerAnchors.Add(anchorData.AnchorId, anchorData);
    }
    
    public override void FixedUpdateNetwork()
    {
        // 全クライアントでマーカー相対アンカーを更新
        foreach (var kvp in MarkerAnchors)
        {
            var anchorData = kvp.Value;
            
            // マーカー相対位置をワールド座標に変換
            Vector3 worldPos = referenceMarker.transform.TransformPoint(anchorData.RelativePosition);
            Quaternion worldRot = referenceMarker.transform.rotation * anchorData.RelativeRotation;
            
            // ローカルアンカーオブジェクトの位置更新
            UpdateLocalAnchor(anchorData.AnchorId.ToString(), worldPos, worldRot);
        }
    }
}
```

### 2. セッション永続化システム

```csharp
public class SessionAnchorPersistence : MonoBehaviour
{
    [Header("Session Persistence")]
    public string sessionDataFileName = "marker_anchors_session.json";
    
    [System.Serializable]
    public class SessionAnchorData
    {
        public List<MarkerAnchor> anchors = new List<MarkerAnchor>();
    }
    
    [System.Serializable]
    public class MarkerAnchor
    {
        public Vector3 relativePosition;
        public Quaternion relativeRotation;
        public string anchorId;
        public float creationTime;
    }
    
    public void SaveSessionData()
    {
        // セッション終了時にローカルファイルに保存
        var sessionData = new SessionAnchorData();
        // MarkerBasedAnchorManager からデータ取得してJSON保存
        
        string jsonData = JsonUtility.ToJson(sessionData, true);
        string filePath = Path.Combine(Application.persistentDataPath, sessionDataFileName);
        File.WriteAllText(filePath, jsonData);
    }
    
    public void LoadSessionData()
    {
        // セッション開始時にローカルファイルから復元
        string filePath = Path.Combine(Application.persistentDataPath, sessionDataFileName);
        if (File.Exists(filePath))
        {
            string jsonData = File.ReadAllText(filePath);
            var sessionData = JsonUtility.FromJson<SessionAnchorData>(jsonData);
            // MarkerBasedAnchorManager にデータを復元
        }
    }
}
```

## 実用的なワークフロー

### **準備フェーズ**
```
1. 物理AprilTagマーカーを印刷・配置
   └─ Tag36h11 family, ID 0（デフォルト）
   └─ 安定した位置に固定設置

2. MarkerColocation.unity シーンを使用
   └─ VirtualFiducialMarker が SharedReferencePoint として動作
```

### **セッション開始**
```
1. 両プレイヤーが同じ物理マーカーをスキャン
   └─ 共通の座標系原点確立

2. ローカルプレイヤーがアンカー作成
   └─ マーカー相対位置で保存
   └─ Photon経由で相対座標送信

3. リモートプレイヤーがアンカー受信・表示
   └─ 同じ物理位置にアンカー表示
   └─ Space作成は不要
```

### **制限事項**
```
1. セッション限定の共有（アプリ終了で消失）
2. マーカー可視性への依存
3. 永続ストレージへの自動保存なし
```

## 結論

### ✅ **可能な部分**
- **Space作成不要**: リモートプレイヤーは自身のSpaceを作成する必要なし
- **アンカー位置共有**: マーカー相対座標による正確な位置共有
- **オフライン動作**: ARCloud不使用での完全動作

### ⚠️ **制限事項**  
- **セッション限定**: 真の永続化ではなく、セッション内での共有
- **マーカー依存**: 物理マーカーの可視性が必要
- **追加実装必要**: 現在のコードには拡張が必要

### 📝 **推奨実装**
1. `MarkerBasedAnchorManager`の実装
2. ネットワーク同期された相対座標管理
3. セッション永続化機能（オプション）

**最終回答**: マーカーベースColocationにより、ARCloudを使わずリモートプレイヤーのSpace作成なしでアンカー共有は可能ですが、真の永続アンカーではなく「セッション内での相対位置再現」となります。実用性は高いですが、追加実装が必要です。