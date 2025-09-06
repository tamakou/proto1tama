# Magic Leap 2 Spatial Anchors と Space依存性 - 詳細分析

## 重要な結論

**永続化されたアンカー情報にはSpace情報が含まれており、アンカーをロードする前にSpaceを再作成する必要はありません。**

ただし、**同じSpaceにローカライズされていることが前提条件**です。

## 1. アンカーとSpace情報の関係

### アンカーに含まれる空間情報

```csharp
// AnchorCreator.cs:188-210 でのアンカー作成完了時
private void OnCompletedCreation(Pose pose, ulong anchorId, 
    string anchorMapPositionId, XrResult result)
{
    // anchorMapPositionId: Space内での位置を特定するID
    StoredAnchor newStoredAnchor = new StoredAnchor
    {
        AnchorId = anchorId,                    // ML2固有のアンカーID
        AnchorMapPositionId = anchorMapPositionId,  // Spaceマップ位置ID
        AnchorObject = newAnchorComponent 
    };
}
```

### 重要なデータ構造

**StoredAnchor構造体:**
- `AnchorId`: Magic Leap 2固有のアンカー識別子
- `AnchorMapPositionId`: **Space内でのマップ位置ID（Space情報含有）**
- `AnchorObject`: Unity ARFoundationのARAnchorオブジェクト

## 2. Space依存性の技術的詳細

### 必要なローカライゼーション条件

```csharp
// AnchorCreator.cs:248-252 でのローカライゼーション確認
private bool IsMagicLeapAnchorSubsystemsLoaded()
{
    if (XRGeneralSettings.Instance?.Manager?.activeLoader == null) return false;
    ActiveSubsystem = XRGeneralSettings.Instance.Manager.activeLoader
        .GetLoadedSubsystem<XRAnchorSubsystem>() as MLXrAnchorSubsystem;
    return ActiveSubsystem != null;
}
```

### アンカークエリの空間制約

```csharp
// AnchorCreator.cs:133-143 でのアンカークエリ
private void QueryAnchors()
{
    // 現在位置から半径内のアンカーを検索
    // **重要**: 現在ローカライズされているSpaceのアンカーのみが返される
    if (!storage.QueryStoredSpatialAnchors(controllerTransform.transform.position, queryAnchorRadius))
    {
        Logger.Instance.LogError("There was a problem querying stored anchors");
    }
}
```

## 3. Space情報が含まれる証拠

### 1. AnchorMapPositionId の役割

**MagicLeapSpatialAnchorsStorageFeature** は `anchorMapPositionId` を使用してアンカーを管理：

```csharp
// OnQueryCompleted での処理
private void OnQueryCompleted(List<string> anchorMapPositionIds)
{
    foreach (var anchorMapPositionId in anchorMapPositionIds)
    {
        // anchorMapPositionId にはSpace情報が含まれている
        // 同じSpaceにローカライズされている場合のみ取得可能
        if (!storage.CreateSpatialAnchorsFromStorage(new List<string>() { anchorMapPositionId }))
        {
            Logger.Instance.LogError($"Couldn't create spatial anchor: {anchorMapPositionId} from storage");
        }
    }
}
```

### 2. OpenXR Storage Feature の設定

**OpenXR Package Settings.asset** での確認：
```yaml
m_Name: MagicLeapSpatialAnchorsStorageFeature Android
nameUi: Magic Leap 2 Spatial Anchors Storage
m_enabled: 1
```

このFeatureは**Space依存のストレージ**を提供します。

## 4. 実際の動作シナリオ

### シナリオ A: 正常な場合

```
1. Space "Office" を作成
2. Office内でアンカーA, B, Cを作成・永続化
   → AnchorMapPositionId: "Office_Anchor_A_12345", "Office_Anchor_B_67890", etc.
3. アプリ再起動
4. Office Spaceにローカライゼーション成功
5. QueryAnchors() 実行
   → Office内のアンカーA, B, Cが正常に取得される
```

### シナリオ B: 異なるSpaceの場合

```
1. Space "Home" にローカライゼーション
2. QueryAnchors() 実行
   → Office Spaceで作成されたアンカーは取得されない
   → 空のリストまたはエラーが返される
```

### シナリオ C: ローカライゼーション失敗の場合

```csharp
// AnchorCreator.cs:44-48 での初期化
private IEnumerator Start()
{
    yield return new WaitUntil(IsMagicLeapAnchorSubsystemsLoaded);
    Logger.Instance.LogInfo("Magic Leap Subsystem Loaded");
    
    // ローカライゼーション失敗時は後続処理が実行されない
}
```

## 5. Magic Leap 2公式ドキュメントからの確認

### Space依存性の明確化

**Magic Leap Developer Documentation**より：

> "This feature also requires that the device is localized into a space that is recognized by the device."

> "If you are operating the headset inside a recognized space, you can also choose to publish the Anchors so they persist across multiple sessions."

### ローカライゼーションの重要性

> "Localization is a process by which the Magic Leap 2 device identifies its position in a space. Before localizing, Magic Leap 2 requires users to map the location and save it as a Space using the Spaces application."

## 6. 実装上の重要な注意点

### 1. 事前のSpace作成は不要

```csharp
// アンカーデータ自体にSpace情報が含まれているため、
// アプリ開発者が手動でSpaceを再作成する必要はない

// ただし、以下が必要：
// - 同じSpaceが既に存在している（Spacesアプリで作成済み）  
// - デバイスがそのSpaceにローカライズされている
```

### 2. 自動的なSpace認識

```csharp
// Magic Leap 2の自動ローカライゼーション
// デバイスが以前訪れたSpaceを認識すると自動的にローカライズ
// アンカーは自動的に利用可能になる
```

### 3. クロスSpace制約

```csharp
// 重要な制限：
// Space Aで作成されたアンカーは、Space Bでは取得できない
// これは技術仕様であり、設計上の制限
```

## 7. ベストプラクティス

### アンカーの確実な取得

```csharp
public void EnsureAnchorAvailability()
{
    // 1. ローカライゼーション状態確認
    if (!IsMagicLeapAnchorSubsystemsLoaded())
    {
        Logger.Instance.LogError("Anchor subsystem not loaded");
        return;
    }
    
    // 2. 適切なSpaceへのローカライゼーション確認
    // （MLLocalizationMapFeature などで確認）
    
    // 3. アンカークエリ実行
    QueryAnchors();
}
```

### エラー処理

```csharp
private void OnQueryCompleted(List<string> anchorMapPositionIds)
{
    if (anchorMapPositionIds.Count == 0)
    {
        Logger.Instance.LogWarning("No anchors found in current space");
        // 別のSpaceにローカライゼーションするか、
        // ユーザーに適切なSpaceに移動するよう指示
    }
}
```

## まとめ

### ✅ アンカー情報にはSpace情報が含まれている

- `AnchorMapPositionId`にSpace固有の位置情報が含まれる
- Magic Leap 2が自動的にSpace-アンカーの関連付けを管理

### ✅ Space再作成は不要

- アンカーデータ自体にSpace参照が含まれている
- Spacesアプリで作成済みのSpaceに自動ローカライズされれば利用可能

### ⚠️ 重要な前提条件

1. **適切なSpaceへのローカライゼーション**が必要
2. **同じSpace内でのみアンカー取得可能**
3. **異なるSpace間でのアンカー共有は不可**

### 📝 実用的な指針

- アンカー作成時のSpaceを記録しておく
- ローカライゼーション状態の監視機能を実装
- アンカー取得失敗時の適切なユーザーガイダンスを提供

この仕組みにより、Magic Leap 2は空間的コンテキストを保持しながら、確実にアンカーの永続化と復元を実現しています。