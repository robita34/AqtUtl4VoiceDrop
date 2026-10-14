# AqtUtl4VoiceDrop (v2.3.0.0)

AviUtl2用の音声・字幕自動配置プラグイン **「Voice Drop」** に完全最適化させた、AqtUtlのフロントエンドカスタムビルドです。
（本家リポジトリ：ChibiBouningen/AqtUtl をベースに改造）

## カスタム（変更）内容の事実
1. **VOICEVOX互換のファイル命名規則の実装**
   - 生成時、字幕テキストの先頭から「最大7文字」を自動で切り出し。
   - ファイル名に使えない禁止文字や改行を自動除去。
   - `プロファイル名_セリフ7文字_タイムスタンプ.{wav,txt}` の形式で出力。
2. **Shift-JISテキストファイルの同時ペア出力**
   - WAVファイルの書き出しと同時に、同名の字幕テキストファイル（Shift-JIS）を全自動生成。
3. **不要な旧形式ロジックの完全廃止**
   - 不要な `.exo` ファイルの物理的な生成およびハードディスクへの書き出しロジックを100%カット。
4. **誤操作防止のためのD&D機能フリーズ**
   - 画面右側のグレーエリアのドラッグ＆ドロップ動作（DoDragDrop）を内部コードから完全無効化（「D&D機能は廃止しました」へUIテキストを変更）。
5. **改行を挟まない快適なショートカットキー変更**
   - 音声生成ショートカットを `Ctrl + Insert` から `Ctrl + Enter` へお引越し。
   - テキストエリア内での不要な改行入力を `ProcessCmdKey` で最優先で強制キャンセル（遮断）。
6. **不要な旧連携オプションの物理ロック**
   - AviUtl2環境では動作しない「ごちゃまぜドロップスと連携」チェックボックスを `Setting.cs` 内で強制的にチェックオフ＆グレーアウト化（無効化）。
7. **環境およびメタデータのクリーンアップ**
   - プロセス名・製品名を `AqtUtl_VoiceDrop`、バージョンをあなたの指定通り `v2.3.0.0` へ正規書き換え。
   - ダブルクリックだけでいつでも一発でWindows側から再コンパイルが走る自動化スクリプト `build.bat` を追加。

## ローカルAPI
起動中は `http://localhost:50121/` でAPIを待ち受けます（タイトルバーに `API:50121` と表示）。
ポートは `setting.json` の `api_port` で変更、`0` で無効化。生成ファイルはGUIと同じ命名・出力フォルダです。

| メソッド | パス | 内容 |
|---|---|---|
| GET | `/` | 動作確認 |
| GET | `/profiles` | プロファイル一覧 |
| POST | `/synthesis` | `{"profile":"名前","text":"字幕","kana":"発音(省略可)"}` → wav+txtを保存 |
| POST | `/batch` | `{"lines":[{...},{...}],"interval_ms":500}` → 上から順に1件ずつ保存（`interval_ms` は行間の待ち時間・省略可。同名衝突時は秒が進むまで待機） |
| POST | `/play` | `/synthesis` と同形式。試聴のみ |

PowerShellからの例（日本語はUTF-8バイト列で送る）:
```powershell
$body = @{ lines = @(
  @{ profile = "ゆっくりれいむ"; text = "こんにちは" },
  @{ profile = "ゆっくりれいむ"; text = "今日は晴れ"; kana = "きょうははれ" }
) } | ConvertTo-Json -Depth 3
Invoke-RestMethod -Method Post http://localhost:50121/batch -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

## ビルド方法
WindowsのPowerShellまたはエクスプローラーから、ルートにある `build.bat` を実行（ダブルクリック）するだけで、`bin/Release/` 配下に最新のカスタム版exeが出荷されます。

## ライセンス
- GPL v3
