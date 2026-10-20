# AqtUtl4VoiceDrop (v2.7.0.0)

AviUtl2用の音声・字幕自動配置プラグイン **「Voice Drop」** に完全最適化させた、AqtUtlのフロントエンドカスタムビルドです。
（本家リポジトリ：ChibiBouningen/AqtUtl をベースに改造）

## カスタム（変更）内容の事実
1. **VOICEVOX互換のファイル命名規則の実装**
   - 生成時、字幕テキストの先頭から「最大7文字」を自動で切り出し。
   - ファイル名に使えない禁止文字や改行を自動除去。
   - `プロファイル名_セリフ7文字_タイムスタンプ.{wav,txt}` の形式で出力。
2. **字幕テキストファイルの同時ペア出力**
   - WAVファイルの書き出しと同時に、同名の字幕テキストファイルを全自動生成。
   - 文字コードは v2.7.0.0 から既定で UTF-8（BOMなし）。`setting.json` の `txt_encoding` を `"shift_jis"` にすると従来の Shift-JIS（UI・API共通。APIはリクエストごとに `encoding` でも指定可）。
3. **不要な旧形式ロジックの完全廃止**
   - 不要な `.exo` ファイルの物理的な生成およびハードディスクへの書き出しロジックを100%カット。
4. **誤操作防止のためのD&D機能フリーズ**
   - 画面右側のグレーエリアのドラッグ＆ドロップ動作（DoDragDrop）を内部コードから完全無効化（「D&D機能は廃止しました」へUIテキストを変更）。
5. **改行を挟まない快適なショートカットキー変更**
   - 音声生成ショートカットを `Ctrl + Insert` から `Ctrl + Enter` へお引越し。
   - テキストエリア内での不要な改行入力を強制キャンセル（遮断）。
6. **不要な旧連携オプションの物理ロック**
   - AviUtl2環境では動作しない「ごちゃまぜドロップスと連携」チェックボックスを `Setting.cs` 内で強制的にチェックオフ＆グレーアウト化（無効化）。
7. **環境およびメタデータのクリーンアップ**
   - プロセス名・製品名を `AqtUtl_VoiceDrop`、バージョンを `v2.3.0.0` へ正規書き換え。
   - Windows側から再コンパイルが走る自動化スクリプト `build.bat` を追加。

## UIのファイル出力切り替え
生成エリア左上の「ファイルを出力」をオフにすると、生成（クリック / `Ctrl + Enter`）は試聴のみになり、wav/txtを出力しません（VoiceDropに取り込まれない）。
状態は `setting.json` の `ui_output` に保存されます。APIからの生成には影響しません。

## ローカルAPI
起動中は `http://localhost:47121/` でAPIを待ち受けます（タイトルバーに `API:47121` と表示）。
ポートは `setting.json` の `api_port` で変更、`0` で無効化。49152以上のポートはWindowsが予約して使えなくなることがあるので避けてください（v2.5.0.0以前の既定 `50121` は起動時に自動で `47121` へ移行）。生成ファイルは既定ではGUIと同じ命名・出力フォルダで、`folder` / `filename` を指定すると変更できます（v2.6.0.0〜）。txtの文字コードは `encoding` で指定できます（v2.7.0.0〜）。
### ※本APIは同一PC内のローカル連携専用です

| メソッド | パス | 内容 |
|---|---|---|
| GET | `/` | 動作確認 |
| GET | `/help` | 使い方（エンドポイント一覧と例。`/usage` でも可） |
| GET | `/profiles` | プロファイル一覧 |
| POST | `/synthesis` | `{"profile":"名前","text":"字幕","kana":"発音(省略可)","folder":"出力先(省略可)","filename":"ファイル名(省略可)"}` → wav+txtを保存 |
| POST | `/batch` | `{"folder":"全行の出力先(省略可)","encoding":"全行の文字コード(省略可)","lines":[{...},{...}],"interval_ms":500}` → 上から順に1件ずつ保存（`interval_ms` は行間の待ち時間・省略可。同名衝突時は秒が進むまで待機） |
| POST | `/play` | `/synthesis` と同形式。試聴のみ（`folder`/`filename`/`encoding` は無視） |

- `folder`: `C:\～` の絶対パス、または出力フォルダからの相対パス（例 `第3話`）。無ければ作成。`/batch` では行ごとの指定が優先
- `filename`: 拡張子なしの名前（`.wav` を付けても可）。同名の wav/txt が既にあれば上書きせずエラー（`code: "exists"`）
- `encoding`: txtの文字コード。`utf-8`（BOMなし）か `shift_jis`。省略時は `setting.json` の `txt_encoding`（未設定なら `utf-8`）。`/batch` では行ごとの指定が優先
- VoiceDropに取り込ませるには、`folder` をVoiceDropの監視対象のフォルダにしてください

### エラー
エラー応答は `{"code":"識別子","error":"日本語の説明"}` です。スクリプトは `code` で判定してください（一覧は `GET /help` の `errors`）。

- `/synthesis`: 同名が既にあれば HTTP 409（`code: "exists"`）、その他の入力エラーは 400
- `/batch`: 応答の `ok` / `skipped` / `failed` が件数。`results` の失敗行には `index`・`status`・`code`・`error` が入り、`code: "exists"` の行は `status: "skipped"`（飛ばした）、それ以外は `status: "failed"`。全行成功なら 200、それ以外は 207

主な `code`: `exists` / `invalid_filename` / `invalid_folder` / `invalid_encoding` / `empty_text` / `missing_profile` / `profile_not_found` / `generate_failed` / `player_not_found` / `invalid_output_folder` / `invalid_json` / `empty_lines` / `not_found` / `internal`

PowerShellからの例（日本語はUTF-8バイト列で送る）:
```powershell
$body = @{ folder = "第3話"; lines = @(
  @{ profile = "ゆっくりれいむ"; text = "こんにちは"; filename = "001_挨拶" },
  @{ profile = "ゆっくりれいむ"; text = "今日は晴れ"; kana = "きょうははれ"; filename = "002_天気" }
) } | ConvertTo-Json -Depth 3
Invoke-RestMethod -Method Post http://localhost:47121/batch -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

## ビルド方法
WindowsのPowerShellまたはエクスプローラーから、ルートにある `build.bat` を実行（ダブルクリック）するだけで、`bin/Release/` 配下に最新のカスタム版exeが出荷されます。

## ライセンス
- GPL v3
