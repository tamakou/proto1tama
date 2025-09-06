# ARCloudを使わない場合のSpace共有メカニズム - 詳細分析

## 重要な結論

**ARCloudを使わない（OnDeviceモード）場合、Spaceは実際には「共有」されません。**

代わりに、**各デバイスが独立してローカライズし、ネットワーク経由で相対位置を同期する**仕組みを使用します。

## OnDevice Space の制限と実装

### 1. OnDevice Spaceの根本的制限

```csharp
// MapLocalizer.cs:119-127 でモード不一致をチェック
if (localizationInfo.MappingMode == MLAnchors.MappingMode.ARCloud && mapMode == MapMode.OnDevice)
{
    _localizationText.text = "You are localized to a map on AR Cloud, but the app is using local maps to co-locate. Please change your Space.";
}
else if (localizationInfo.MappingMode != MLAnchors.MappingMode.ARCloud && mapMode == MapMode.ARCloud)
{
    _localizationText.text = "You are localized to a local ma, but the app is using AR Cloud. Please change your Space.";
}
```

**OnDevice Spaceの特徴:**
- **ローカルストレージ**: デバイス内部ストレージのみに保存
- **非共有**: 他のデバイスからは直接アクセス不可
- **デバイス固有**: 作成したデバイスのみで利用可能

### 2. OnDeviceモードでのColocation実現方法

#### Method 1: 同一デバイスでの事前Space作成

**プロセス:**
1. **デバイスA**で物理空間をスキャンしてSpace作成
2. **デバイスB**を**デバイスA**として一時的に使用してSpace作成
3. 各デバイスに同じ物理空間の個別Spaceが保存される

**具体的手順:**
```
Step 1: プライマリデバイス（デバイスA）での作業
- Magic Leap Spacesアプリ起動
- 物理空間全体をスキャン
- Space "Room_A" として保存 → デバイスAのローカルストレージに保存

Step 2: セカンダリデバイス（デバイスB）での作業  
- デバイスBを同じ物理空間に持参
- Magic Leap Spacesアプリ起動
- 同じ物理空間を独立してスキャン
- Space "Room_A_Copy" として保存 → デバイスBのローカルストレージに保存

Step 3: アプリでのローカライゼーション
- 両デバイスが各自のSpaceにローカライズ
- 各デバイスが独自にSpace原点を特定
- SharedReferencePointが各デバイスで設定される
```

#### Method 2: 物理的Space共有（推奨されない）

**理論的手順:**
```
1. デバイスAでSpace作成
2. デバイスA内部ストレージからSpaceファイルを抽出
3. デバイスBにSpaceファイルをコピー
4. 両デバイスで同じSpaceファイルを使用
```

**問題点:**
- Magic LeapのSpace APIは直接的なファイル共有をサポートしていない
- デバイス固有の較正データが含まれる可能性
- セキュリティ上の制限

### 3. 実際の共有メカニズム: ネットワーク同期

#### Photon Fusionによる相対位置同期

```csharp
// NetworkRig.cs での同期メカニズム
public override void FixedUpdateNetwork()
{
    if (GetInput<RigInput>(out var input))
    {
        ApplyInputToRigParts(input);
        Controller.controllerInputData = input.ControllerInput;
    }
}

private void ApplyRelativeWorldPosition(Vector3 rigPosition, Quaternion rigRotation, 
                                        Vector3 controllerPosition, Quaternion controllerRotation, 
                                        Vector3 headsetPosition, Quaternion headsetRotation)
{
    // SharedReferencePointに対する相対位置で同期
    transform.position = _referencePoint.transform.TransformPoint(rigPosition);
    transform.rotation = _referencePoint.transform.rotation * rigRotation;
}
```

#### 同期の流れ

```
Device A:
1. OnDevice Space "Room_A" にローカライズ
2. Space原点 = (0, 0, 0, Quaternion.identity)
3. SharedReferencePoint.transform = Space原点
4. プレイヤー位置 = SharedReferencePoint からの相対位置
5. ネットワーク送信: 相対位置データ

Device B:  
1. OnDevice Space "Room_B" にローカライズ
2. Space原点 = (X, Y, Z, 異なるQuaternion)
3. SharedReferencePoint.transform = Space原点（Aと異なる）
4. ネットワーク受信: デバイスAの相対位置データ  
5. 変換: Aの相対位置 → Bの座標系での絶対位置
```

### 4. OnDeviceモードの実用的制限

#### 位置ずれの根本原因

```csharp
// 各デバイスが異なるSpace原点を持つため
Device A: SharedReferencePoint = SpaceOrigin_A (1.2, 0.1, -0.5)
Device B: SharedReferencePoint = SpaceOrigin_B (0.8, -0.2, 0.3)

// 結果: 同じ相対位置でも異なる絶対位置
Player A position (relative): (2.0, 1.5, 3.0)
Player B sees A at: SpaceOrigin_B + (2.0, 1.5, 3.0) ≠ 実際の物理位置
```

#### 精度の問題

**Space作成時の個体差:**
- スキャン開始位置の違い
- スキャン経路の違い  
- デバイス較正の個体差
- 物理環境認識の差異

## 実用的な解決策

### 推奨Approach 1: マーカーベースColocation

OnDeviceモードでも確実にColocateするために：

```csharp
// MarkerColocation.unity シーンを使用
// VirtualFiducialMarker が物理マーカーに基づいてSharedReferencePointを設定
// 両デバイスが同じ物理マーカーを基準にする
```

**利点:**
- デバイス間の座標系の差を排除
- 物理マーカーが共通の基準点
- OnDeviceでも正確なColocation実現

### 推奨Approach 2: 手動較正システム

**実装案:**
```csharp
public class ManualCalibrationSystem : MonoBehaviour
{
    [SerializeField] private float calibrationDistance = 2.0f;
    
    public void StartCalibration()
    {
        // 両プレイヤーが同じ物理位置（例：テーブルの角）を指す
        // ネットワーク経由で位置データを比較
        // オフセット計算して SharedReferencePoint を調整
    }
    
    private void AdjustReferencePoint(Vector3 offset, Quaternion rotationOffset)
    {
        SharedReferencePoint.Instance.transform.position += offset;
        SharedReferencePoint.Instance.transform.rotation *= rotationOffset;
    }
}
```

### 推奨Approach 3: ARCloud使用（根本的解決）

**最も確実な方法:**
```csharp
// MapLocalizer.cs でARCloudモード有効化
public MapMode mapMode = MapMode.ARCloud;
```

**ARCloudの利点:**
- クラウド上の統一されたSpace
- デバイス間での自動的な座標系統一
- 較正不要の正確なColocation

## まとめ

### OnDeviceモードの現実

**Space共有は実際には行われません。** 代わりに：

1. **個別ローカライゼーション**: 各デバイスが独自にSpaceにローカライズ
2. **相対位置同期**: ネットワーク経由で相対位置データを共有
3. **座標変換**: 受信した相対位置を各デバイスの座標系で解釈

### 実用的な結論

**OnDeviceモードでの確実なColocation:**

1. **マーカーベース使用** （推奨）
   - MarkerColocation.unity シーン使用
   - 物理AprilTagマーカーで共通基準点確立

2. **手動較正実装** 
   - アプリ内で位置合わせ機能追加
   - 物理的基準点での較正

3. **ARCloud移行**（最適解）
   - MapMode.ARCloud に変更
   - クラウドベースの統一座標系使用

**結論**: OnDeviceモードでは真のSpace共有は不可能であり、代替手法による座標系統一が必要です。