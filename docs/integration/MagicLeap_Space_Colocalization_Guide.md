# Magic Leap 2 - Space共有によるコロケーション詳細手順ガイド

## 概要

MagicLeapPhotonFusionExampleで複数のMagic Leap 2デバイス間でSpaceベースのコロケーションを実現するための詳細手順を説明します。

## 前提条件

### ハードウェア要件
- Magic Leap 2 ヘッドセット（各プレイヤー1台ずつ）
- 同じ物理空間（部屋）内での使用
- インターネット接続（ARCloud使用時）

### ソフトウェア要件
- Magic Leap Spacesアプリ（各デバイスにインストール済み）
- MagicLeapPhotonFusionExampleアプリ（ビルド済み）
- Spatial Anchors権限が有効

## Space共有の仕組み

### コロケーションの原理
1. **Space原点の統一**: 各デバイスが同じSpaceにローカライズすることで、共通の座標原点を持つ
2. **相対座標同期**: `SharedReferencePoint`がSpace原点に設定され、全てのネットワークオブジェクトがこの点に対して相対位置で同期
3. **ネットワーク通信**: Photon Fusionを通じて各プレイヤーの相対位置がリアルタイム共有

## 詳細手順

### Phase 1: 事前準備（最初の1回のみ）

#### Step 1.1: Space作成者（プレイヤーA）の作業

1. **Magic Leap Spacesアプリ起動**
   ```
   デバイス: Magic Leap 2 (プレイヤーA)
   操作: アプリドロワー → "Spaces"アプリを選択
   ```

2. **新しいSpaceの作成**
   ```
   Spacesアプリ内:
   - "Create New Space" または "+" ボタンを選択
   - Space名前を設定（例: "PhotonFusionDemo"）
   - マッピングモードを選択:
     - OnDevice: ローカルストレージのみ
     - ARCloud: クラウド同期有効
   ```

3. **物理空間のスキャン**
   ```
   操作:
   - "Start Mapping" ボタンを押下
   - 部屋全体をゆっくりと歩き回る
   - 壁、床、天井、家具を見渡してスキャン
   - スキャン品質が十分になるまで継続（通常2-3分）
   - "Complete Mapping" でスキャン終了
   ```

4. **Space保存**
   ```
   操作:
   - Space名前を確認/修正
   - "Save Space" でSpace保存
   - Space IDを確認・記録（共有用）
   ```

#### Step 1.2: 他のプレイヤーへの共有準備

**ARCloudモードの場合:**
```
自動共有: Space IDまたはQRコードで共有可能
他のデバイス: 同じMagic Leapアカウントでサインインすることで自動的に利用可能
```

**OnDeviceモードの場合:**
```
制限: 作成したデバイスのみで利用可能
対策: 各デバイスで個別にSpace作成が必要（同じ物理空間で）
```

### Phase 2: 各セッション開始時の手順

#### Step 2.1: プレイヤーA（ホスト）の準備

1. **Magic Leap Spacesでローカライゼーション**
   ```
   操作:
   - Spacesアプリを起動
   - 作成済みSpace "PhotonFusionDemo" を選択
   - "Localize to Space" を選択
   - デバイスを持って部屋を見回し、ローカライゼーション待機
   - "Localized" 表示を確認
   ```

2. **PhotonFusionExampleアプリ起動**
   ```
   操作:
   - アプリドロワー → MagicLeapPhotonFusionExampleを起動
   - MapColocation.unity シーンが読み込まれることを確認
   - 権限確認ダイアログで"Allow"を選択
   ```

3. **ローカライゼーション状態確認**
   ```
   アプリ内表示:
   - "You are localized" メッセージが表示される
   - UIパネルに現在のSpace情報が表示される
   - SharedReferencePointがSpace原点に設定される
   ```

4. **ネットワークセッション作成**
   ```
   操作:
   - ConnectionManagerが自動的にPhoton Fusionセッション作成
   - ルーム名: "SampleFusion-MagicLeap2"（デフォルト）
   - Hostとしてセッション開始
   ```

#### Step 2.2: プレイヤーB（クライアント）の準備

1. **Magic Leap Spacesでローカライゼーション**
   ```
   ARCloudモードの場合:
   - Spacesアプリ起動
   - "Available Spaces" から "PhotonFusionDemo" を選択
   - または、Space IDを入力して検索
   - "Localize to Space" を選択
   - ローカライゼーション完了まで待機
   
   OnDeviceモードの場合:
   - プレイヤーAと同じ手順でSpaceを個別作成
   - または既存の共有Spaceがあれば選択
   ```

2. **重要: 同じ物理位置でのローカライゼーション**
   ```
   注意点:
   - プレイヤーAがSpace作成時にいた同じ部屋にいる必要
   - 部屋の配置（家具位置等）が大きく変わっていない
   - 照明条件が極端に異なってない（昼/夜の差は通常OK）
   ```

3. **PhotonFusionExampleアプリ起動**
   ```
   操作:
   - MagicLeapPhotonFusionExampleを起動
   - 権限確認で"Allow"を選択
   - ローカライゼーション状態確認
   ```

4. **ネットワーク参加**
   ```
   操作:
   - ConnectionManagerが自動的にセッション検索
   - 同じルーム名のセッションに参加
   - プレイヤーAのセッションにクライアントとして参加
   ```

### Phase 3: コロケーション確認と調整

#### Step 3.1: 初期位置確認

1. **互いの位置確認**
   ```
   確認方法:
   - 両プレイヤーのアバター（NetworkRig）が正しい位置に表示される
   - 物理的に手を振って、相手の仮想アバターの手が連動することを確認
   - 部屋の同じ物理オブジェクト（テーブル、椅子等）を指して位置合わせ確認
   ```

2. **座標系精度チェック**
   ```
   テスト方法:
   - 両プレイヤーが同じ物理位置（例：テーブルの角）を指す
   - 仮想オブジェクト（キューブなど）をその位置に配置
   - 両プレイヤーから見て同じ位置に表示されることを確認
   ```

#### Step 3.2: トラブルシューティング

**位置がずれている場合:**

1. **ローカライゼーション再実行**
   ```
   操作:
   - 一度アプリを終了
   - Spacesアプリでローカライゼーションをやり直し
   - より丁寧に部屋全体を見回してローカライゼーション
   ```

2. **Space品質確認**
   ```
   Spacesアプリ内:
   - Space詳細で"Quality"スコアを確認
   - スコアが低い場合は再スキャンを検討
   - 十分な特徴点（壁のテクスチャ、家具等）があることを確認
   ```

3. **モード不一致確認**
   ```csharp
   // アプリ内エラーメッセージ例:
   "You are localized to a map on AR Cloud, but the app is using local maps"
   
   対策: 
   - MapLocalizer.csのmapModeを確認
   - 両プレイヤーが同じマッピングモード（OnDevice/ARCloud）を使用
   ```

## 技術的詳細

### MapLocalizer.csの動作フロー

```csharp
// Step 1: ローカライゼーション状態取得
MLAnchors.GetLocalizationInfo(out var localizationInfo);

// Step 2: ローカライズ済みかチェック
var localized = localizationInfo.LocalizationStatus == MLAnchors.LocalizationStatus.Localized;

// Step 3: 有効なポーズかチェック
var validPose = localized && IsPoseValid(localizationInfo.SpaceOrigin);

// Step 4: SharedReferencePointにSpace原点設定
SharedReferencePoint.Instance.transform.SetPositionAndRotation(
    localizationInfo.SpaceOrigin.position, 
    localizationInfo.SpaceOrigin.rotation);
```

### ネットワーク同期の仕組み

```csharp
// NetworkRig.cs内で各プレイヤーの位置をSharedReferencePointに対して相対化
transform.position = _referencePoint.transform.TransformPoint(rigPosition);
transform.rotation = _referencePoint.transform.rotation * rigRotation;
```

## 実用的な使用シナリオ

### シナリオ1: オフィス会議室での利用

1. **事前準備**（1回のみ）
   - 会議室でSpaceを作成
   - "MeetingRoom_A" として保存
   - ARCloudにアップロード

2. **毎回の会議時**
   - 各参加者が会議室でローカライゼーション
   - 自動的に同じSpaceに接続
   - 共有ARコンテンツで会議開始

### シナリオ2: 自宅リビングでの利用

1. **OnDeviceモード推奨**
   - プライベート空間のためARCloud無し
   - 各デバイスで個別にSpaceをスキャン
   - 同じ物理空間での使用に限定

### シナリオ3: 展示会ブースでの利用

1. **ARCloudモード推奨**
   - 多数の来場者デバイスでの利用
   - 事前にブーススペースをスキャン・アップロード
   - QRコードでSpace共有

## よくあるトラブルと対策

### 1. ローカライゼーション失敗
```
症状: "You are not localized" メッセージが消えない
原因: 
- Space品質不足
- 照明条件の大幅変化
- 物理空間の配置変更

対策:
- より丁寧な部屋スキャン
- 十分な特徴点確保
- Spaceの再作成
```

### 2. 位置ずれ
```
症状: プレイヤー同士の位置が大幅にずれる
原因:
- 異なるSpaceにローカライズ
- ローカライゼーション精度不足
- マッピングモード不一致

対策:
- Space名確認
- ローカライゼーションやり直し
- 両デバイスで同じマッピングモード使用
```

### 3. ネットワーク接続問題
```
症状: 相手プレイヤーが表示されない
原因:
- Photon Fusionセッション問題
- ネットワーク権限不足
- ファイアウォール問題

対策:
- アプリ再起動
- ネットワーク権限確認
- 同じWiFiネットワーク使用確認
```

## 最適化のベストプラクティス

### Space作成時
1. **十分な時間をかけてスキャン**（最低2-3分）
2. **部屋全体の特徴を捉える**（壁、床、天井、家具）
3. **安定した照明下でスキャン**
4. **Space名前は識別しやすく**

### 日常使用時
1. **同じ照明条件でローカライゼーション**
2. **家具配置を大きく変えない**
3. **定期的なSpace更新**（月1回程度）
4. **バックアップSpaceの作成**

これらの手順に従うことで、Magic Leap 2デバイス間での安定したSpace共有とコロケーションが実現できます。