# TMPJaFix

**Unexplored 2: The Wayfarer's Legacy** の日本語化に必要な TextMeshPro (TMP) フォント表示修正 BepInEx プラグインです。  
[XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator) と組み合わせて使用します。

## 解決する問題

| 問題 | 原因 | 対処 |
|---|---|---|
| 起動時クラッシュ (NullReferenceException) | `TMP_Settings.GetCharacters` に `null` が渡される | Prefix パッチで空の Dictionary を返す |
| 日本語テキストが □□□（豆腐）になる | ゲーム組み込みフォントに日本語グリフがない | 日本語フォントバンドルをフォールバックとして全 TMP フォントに注入 |
| 日本語テキストがダイアログからはみ出す | 日本語文字は欧文より幅が広くなりがち | `Rebuild` パッチで日本語検出時にフォントサイズを縮小 |

## 動作環境

- Unexplored 2: The Wayfarer's Legacy (Steam / Unity 2020.3.26f1 IL2CPP)
- [BepInEx 6.0.0-be.755 以降](https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-be.755)（IL2CPP 版）
- [XUnity.AutoTranslator 5.6.1](https://github.com/bbepis/XUnity.AutoTranslator/releases/tag/v5.6.1)

## インストール

1. [Releases](../../releases) から最新の `TMPJaFix.dll` をダウンロードします。
2. `BepInEx/plugins/` フォルダに配置します。
3. 日本語フォントバンドルをゲームフォルダのルートに配置します（後述）。

## 設定

初回起動後に `BepInEx/config/com.user.tmpjafix.cfg` が自動生成されます。

```ini
[Font]
## ゲームフォルダに置いたフォントバンドルのファイル名（拡張子なし）
BundleName = arialuni_sdf_u2019

## 日本語テキストのフォントサイズ倍率（1.0 = 等倍、0.8 = 80%）
JapaneseFontSizeScale = 0.8
```

### フォントバンドルについて

日本語グリフを含む TMP フォントアセットバンドルをゲームフォルダのルートに配置する必要があります。

- **簡単**: XUnity.AutoTranslator に付属の `arialuni_sdf_u2019` をデフォルトで使用できます。ただし全角記号（！？など U+FF00–FFEF）が欠けています。
- **推奨**: [こちらのブログ](https://ebith.hatenablog.jp/entry/2024/09/16/091531) から作成済みバンドルをダウンロードし、`BundleName` を合わせてください。

バンドルを自作する場合は [Unexplored 2 日本語化パッチ](../Unexplored2_JP) の README にある手順（Noto Sans JP / Unity Font Asset Creator）を参照してください。

## ビルド方法

### 前提条件

- .NET 6 SDK
- Unexplored 2: The Wayfarer's Legacy（BepInEx 6 + Interop DLL 生成済み）

### 手順

```bash
git clone https://github.com/<your-username>/TMPJaFix.git
cd TMPJaFix
```

`TMPJaFix.csproj` 内の `GamePath` プロパティをゲームのインストールパスに変更します。

```xml
<GamePath>C:\Program Files (x86)\Steam\steamapps\common\Unexplored 2 The Wayfarer's Legacy</GamePath>
```

または、リポジトリルートに `Directory.Build.props`（`.gitignore` 済み）を作成して上書きすることもできます。

```xml
<Project>
  <PropertyGroup>
    <GamePath>C:\your\path\to\Unexplored 2 The Wayfarer's Legacy</GamePath>
  </PropertyGroup>
</Project>
```

```bash
dotnet build -c Release
# → bin/Release/net6.0/TMPJaFix.dll
```

## 技術的な詳細

### なぜ `set_text` をパッチできないか

HarmonyX + Il2CppInterop 環境では、`TMP_Text.set_text`（`string` パラメータを持つプロパティセッター）に Prefix/Postfix いずれでもパッチを当てるとランタイムクラッシュします。  
IL2CPP ネイティブコードと managed コードの境界で string のマーシャリングが問題を起こすためです。

代わりに **`TextMeshProUGUI.Rebuild` / `TextMeshPro.Rebuild`** をパッチしています。このメソッドは `CanvasUpdate`（int 列挙型）しか受け取らず、string マーシャリングが発生しないため安全にパッチできます。TMP はテキスト内容が変化するたびにこのメソッドを呼び出してメッシュを再構築するので、再構築直前にフォントサイズを調整することができます。

### パッチ一覧

| パッチ | 対象メソッド | 種別 | 目的 |
|---|---|---|---|
| Patch 1 | `TMP_Settings.GetCharacters` | Prefix | `null` 引数による NRE クラッシュ防止 |
| Patch 2 | `TMP_FontAsset.Awake` | Postfix | ゲームプレイ中に動的ロードされるフォントへの注入 |
| Patch 3 | `TextMeshProUGUI.Awake` | Postfix | UI テキストのフォントへの日本語フォント注入 |
| Patch 4 | `TextMeshPro.Awake` | Postfix | ワールド空間テキストへの注入 |
| Patch 5a | `TextMeshProUGUI.Rebuild` | Prefix | 日本語テキスト検出時のフォントサイズ縮小 |
| Patch 5b | `TextMeshPro.Rebuild` | Prefix | 同上（ワールド空間テキスト） |

## ライセンス

[MIT License](LICENSE)
