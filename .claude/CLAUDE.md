# Leeyez Kai - プロジェクト指示書

## プロジェクト概要
WinForms (.NET 8) 製の画像/動画ビューア。taureader (Tauri/React) のネイティブ版。

## ビルドとテスト
```bash
# ビルド
dotnet build

# テスト実行（変更後は必ず実行）
cd Tests && dotnet test

# 実行
dotnet run
```

## 回帰テストチェックリスト
コード変更後、以下を確認すること：

### 必須確認（毎回）
- [ ] `dotnet build` がエラーなし
- [ ] `dotnet test` が全テスト合格
- [ ] アプリが起動する（クラッシュしない）

### 機能変更時の確認
- [ ] 書庫ファイルをシングルクリック → 画像が表示される
- [ ] 書庫ファイルをシングルクリック → ファイルリストに画像一覧が表示される
- [ ] ホイールで画像切替 → ファイルリストのハイライトも追従する
- [ ] 前回の状態が復元される（パス、画像、ウィンドウ位置）
- [ ] ツリーのオートリビールが動作する
- [ ] 見開き表示で2ファイルがハイライトされる

### UI変更時の確認
- [ ] ナビゲーションバーのアイコンが表示される
- [ ] ビューアーツールバーのボタンが正しい位置にある
- [ ] スケールモード/表示モードのハイライトが正しい

## アーキテクチャ
- `MainForm.cs` — フィールド定義 + Setup（partial class）
- `MainForm.InitializeComponent.cs` — UI構築
- `MainForm.Navigation.cs` — ナビゲーション
- `MainForm.Archive.cs` — 書庫処理
- `MainForm.Viewer.cs` — 画像表示・プリフェッチ・ズーム
- `MainForm.Input.cs` — キーボード・全画面・コンテキストメニュー
- `MainForm.State.cs` — 状態保存・復元
- `Constants.cs` — FileExtensions (HashSet) + AppConstants
- `Services/ImageDecoder.cs` — デコード専用（GDI+ / SkiaSharp）
- `Services/NativeMethods*.cs` — Win32 API（Shell / FileOp）

## 注意事項
- WebPアニメーションは大容量（10MB+）のためプリフェッチしない
- 書庫内のStreamはSeek不可の場合があるため、必ずMemoryStreamにコピーしてからSKCodecに渡す
- FileExtensionsはHashSet (OrdinalIgnoreCase) で定義済み
- TODO.mdに未実装機能・既知のバグを管理
