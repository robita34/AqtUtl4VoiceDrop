using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;

namespace AqT_Utl.Component
{
    //ローカル専用のHTTP API。Claude等から台本を流し込み、VoiceDrop用のwav+txtを一括生成するためのもの
    //  GET  /            … 動作確認
    //  GET  /help        … 使い方（エンドポイント一覧と例）
    //  GET  /profiles    … プロファイル一覧
    //  POST /synthesis   … {"profile":"名前かId","text":"字幕","kana":"発音(省略時は字幕と同じ)","folder":"出力先(省略可)","filename":"名前(省略可)"} → wav+txtを保存
    //  POST /batch       … {"folder":"全行の出力先(省略可)","lines":[{...},{...}]} → 上から順に保存
    //  どちらも "encoding":"utf-8|shift_jis" でtxtの文字コードを指定可。エラーは {"code":..,"error":..}
    //  POST /play        … /synthesisと同じ形式。試聴のみでファイルは残さない
    internal class ApiServer
    {
        HttpListener listener;
        readonly AquesTalkPlayerManager playerManager;
        readonly Func<List<SerifProfile>> getProfiles;     //UIスレッドからプロファイルのコピーを取得する

        static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,     //HTMLに埋め込まないので日本語をそのまま返す
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public ApiServer(AquesTalkPlayerManager playerManager, Func<List<SerifProfile>> getProfiles)
        {
            this.playerManager = playerManager;
            this.getProfiles = getProfiles;
        }

        public void Start(int port)
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:" + port + "/");
            listener.Start();
            Task.Run(Loop);
        }

        public void Stop()
        {
            try { listener?.Close(); } catch { }
        }

        async Task Loop()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    return;     //Stop()された
                }

                //生成は直列化されるので、リクエストも1件ずつ順番に処理する
                try
                {
                    Handle(context);
                }
                catch (InvalidDataException ex)
                {
                    TryRespond(context.Response, 400, new { code = "invalid_json", error = ex.Message });
                }
                catch (Exception ex)
                {
                    TryRespond(context.Response, 500, new { code = "internal", error = ex.Message });
                }
            }
        }

        void Handle(HttpListenerContext context)
        {
            HttpListenerRequest req = context.Request;
            string path = req.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            string method = req.HttpMethod;

            if (method == "GET" && path == "")
            {
                Respond(context.Response, 200, new
                {
                    app = "AqtUtl_VoiceDrop",
                    version = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    playerFound = playerManager.IsRegistered,
                    help = "GET /help で使い方を表示"
                });
                return;
            }

            if (method == "GET" && (path == "/help" || path == "/usage"))
            {
                Respond(context.Response, 200, Help());
                return;
            }

            if (method == "GET" && path == "/profiles")
            {
                var list = getProfiles().Select(p => new { id = p.Id, name = p.ProfileName, preset = p.UsePreset });
                Respond(context.Response, 200, list);
                return;
            }

            if (method == "POST" && (path == "/synthesis" || path == "/play"))
            {
                LineRequest line = ReadBody<LineRequest>(req);
                List<SerifProfile> profiles = getProfiles();
                bool play = path == "/play";
                try
                {
                    Respond(context.Response, 200, Generate(line, profiles, play));
                }
                catch (VoiceGenerateException ex)
                {
                    Respond(context.Response, ex.Code == "exists" ? 409 : 400, new { code = ex.Code, error = ex.Message });
                }
                return;
            }

            if (method == "POST" && path == "/batch")
            {
                BatchRequest batch = ReadBody<BatchRequest>(req);
                if (batch?.Lines == null || batch.Lines.Count == 0)
                {
                    Respond(context.Response, 400, new { code = "empty_lines", error = "linesが空です" });
                    return;
                }

                List<SerifProfile> profiles = getProfiles();
                var results = new List<object>();
                int okCount = 0, skippedCount = 0;
                for (int i = 0; i < batch.Lines.Count; i++)
                {
                    //VoiceDropの取り込みが追いつくよう、行間に任意の待ち時間を入れる
                    if (i > 0 && batch.Interval_ms > 0) System.Threading.Thread.Sleep(batch.Interval_ms);

                    //行ごとの folder/encoding が無ければ、バッチ全体の指定を使う
                    LineRequest line = batch.Lines[i];
                    if (line != null && string.IsNullOrWhiteSpace(line.Folder)) line.Folder = batch.Folder;
                    if (line != null && string.IsNullOrWhiteSpace(line.Encoding)) line.Encoding = batch.Encoding;

                    try
                    {
                        results.Add(Generate(line, profiles, false));
                        okCount++;
                    }
                    catch (VoiceGenerateException ex)
                    {
                        //同名が既にある行は「飛ばした」、それ以外は「失敗」として数える
                        bool skipped = ex.Code == "exists";
                        if (skipped) skippedCount++;
                        results.Add(new { index = i, status = skipped ? "skipped" : "failed", code = ex.Code, error = ex.Message });
                    }
                }
                int failedCount = batch.Lines.Count - okCount - skippedCount;
                Respond(context.Response, okCount == batch.Lines.Count ? 200 : 207, new { ok = okCount, skipped = skippedCount, failed = failedCount, results });
                return;
            }

            Respond(context.Response, 404, new { code = "not_found", error = "不明なエンドポイントです: " + method + " " + req.Url.AbsolutePath, help = "GET /help で使い方を表示" });
        }

        object Help()
        {
            return new
            {
                description = "AquesTalkで音声を生成し、出力フォルダに「プロファイル名_セリフ先頭7文字_タイムスタンプ.wav/.txt」を保存するローカルAPI。VoiceDropがこのフォルダを監視してAviUtl2に自動配置する。",
                version = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                request_encoding = "リクエストのボディはUTF-8のJSON（Content-Type: application/json; charset=utf-8）。出力txtの文字コードは各エンドポイントの encoding で指定",
                endpoints = new object[]
                {
                    new { method = "GET",  path = "/",         description = "動作確認（バージョン、AquesTalkPlayerの有無）" },
                    new { method = "GET",  path = "/help",     description = "この使い方（/usage でも可）" },
                    new { method = "GET",  path = "/profiles", description = "プロファイル（キャラ）一覧。profile にはここの name を指定する" },
                    new
                    {
                        method = "POST", path = "/synthesis",
                        description = "1行を生成してwav+txtを保存する",
                        body = new
                        {
                            profile = "プロファイル名（必須）",
                            text = "字幕テキスト（必須、txtに保存される）",
                            kana = "AquesTalkに読ませる発音（省略時はtextを読む）",
                            folder = "出力先（省略時は設定の出力フォルダ）。C:\\～ の絶対パス、または出力フォルダからの相対パス。無ければ作成",
                            filename = "ファイル名・拡張子なし（省略時は「プロファイル名_セリフ先頭7文字_タイムスタンプ」）。同名が既にあれば上書きせず code=exists（HTTP 409）",
                            encoding = "txtの文字コード。utf-8（BOMなし）か shift_jis（省略時は setting.json の txt_encoding、未設定なら utf-8）"
                        },
                        example = new { profile = "ゆっくりれいむ", text = "今日は晴れ", kana = "きょうははれ", folder = "第3話", filename = "003_れいむ_天気" }
                    },
                    new
                    {
                        method = "POST", path = "/batch",
                        description = "複数行を上から順に1件ずつ生成する。1行失敗しても残りは続行する。全行成功なら200、それ以外は207。応答の ok/skipped/failed が件数で、results の失敗行には index・status（code=exists なら skipped、それ以外は failed）・code・error が入る",
                        body = new { lines = "/synthesis と同じ形式の配列（必須）", folder = "全行の出力先（省略可。行ごとの folder が優先）", encoding = "全行のtxt文字コード（省略可。行ごとの encoding が優先）", interval_ms = "行間の待ち時間ミリ秒（省略時0）" },
                        example = new
                        {
                            folder = "第3話",
                            lines = new object[]
                            {
                                new { profile = "ゆっくりれいむ", text = "こんにちは", filename = "001_挨拶" },
                                new { profile = "ゆっくりれいむ", text = "今日は晴れ", kana = "きょうははれ", filename = "002_天気" }
                            }
                        }
                    },
                    new
                    {
                        method = "POST", path = "/play",
                        description = "試聴のみ（音が鳴る。ファイルは残らない）。ボディは /synthesis と同じ（folder/filename/encoding は無視）",
                        example = new { profile = "ゆっくりれいむ", text = "テスト" }
                    }
                },
                notes = new[]
                {
                    "難読漢字・固有名詞・英字は kana にひらがなで読みを指定すると確実",
                    "生成はUI操作と合わせて直列化される。filename省略時に同名ファイルになる場合はタイムスタンプが進むまで待つ",
                    "VoiceDropに取り込ませるには、folder をVoiceDropの監視フォルダ（またはその配下、VoiceDrop側の設定による）にする",
                    "ポートは setting.json の api_port で変更（0で無効）"
                },
                errors = new
                {
                    format = "エラー応答は {\"code\":\"識別子\",\"error\":\"日本語の説明\"}。スクリプトは code で判定する",
                    codes = new
                    {
                        exists = "同名の wav/txt が既にある（上書きしない）。/synthesis は409、/batch では skipped",
                        invalid_filename = "filename に使えない文字がある",
                        invalid_folder = "folder を作成・使用できない",
                        invalid_encoding = "encoding が utf-8 / shift_jis 以外",
                        empty_text = "text が空",
                        missing_profile = "profile が未指定",
                        profile_not_found = "profile が見つからない",
                        generate_failed = "AquesTalkPlayer で音声を作れなかった",
                        player_not_found = "AquesTalkPlayer.exe が無い",
                        invalid_output_folder = "設定の出力フォルダが不正",
                        invalid_json = "リクエストのJSONが不正",
                        empty_lines = "/batch の lines が空",
                        not_found = "不明なエンドポイント",
                        @internal = "想定外のエラー（HTTP 500）"
                    }
                }
            };
        }

        object Generate(LineRequest line, List<SerifProfile> profiles, bool play)
        {
            if (line == null || string.IsNullOrEmpty(line.Text)) throw new VoiceGenerateException("empty_text", "textが空です");

            SerifProfile p = FindProfile(line.Profile, profiles);
            string kana = string.IsNullOrEmpty(line.Kana) ? line.Text : line.Kana;

            if (play)
            {
                VoiceGenerateResult pr = playerManager.Synthesize(kana, line.Text, p, true, true);
                return new { profile = p.ProfileName, seconds = Math.Round((decimal)pr.Seconds, 3) };
            }

            //wavを作ってからtxtで失敗しないよう、文字コードは先に確認する
            string encodingName = string.IsNullOrWhiteSpace(line.Encoding) ? SettingLoader.TxtEncodingName : line.Encoding;
            Encoding txtEncoding = TxtEncoding.ParseOrThrow(encodingName);

            VoiceGenerateResult r = playerManager.Synthesize(kana, line.Text, p, false, false, line.Folder, line.Filename, txtEncoding);
            decimal seconds = Math.Round((decimal)r.Seconds, 3);     //doubleのままだと 0.26200000000000001 のように出力される
            return new { profile = p.ProfileName, text = line.Text, wav = r.WavPath, txt = r.TxtPath, encoding = txtEncoding.CodePage == 932 ? "shift_jis" : "utf-8", seconds, frames = r.FrameCount };
        }

        static SerifProfile FindProfile(string key, List<SerifProfile> profiles)
        {
            if (string.IsNullOrEmpty(key)) throw new VoiceGenerateException("missing_profile", "profileが指定されていません");

            SerifProfile p = profiles.FirstOrDefault(x => x.ProfileName == key);
            if (p == null && int.TryParse(key, out int id)) p = profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) throw new VoiceGenerateException("profile_not_found", "プロファイル「" + key + "」が見つかりません");
            return p;
        }

        static T ReadBody<T>(HttpListenerRequest req)
        {
            using (var reader = new StreamReader(req.InputStream, new UTF8Encoding(false)))
            {
                string body = reader.ReadToEnd();
                try
                {
                    return JsonSerializer.Deserialize<T>(body, jsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException("JSONが不正です: " + ex.Message);
                }
            }
        }

        static void Respond(HttpListenerResponse res, int status, object body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, jsonOptions));
            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        static void TryRespond(HttpListenerResponse res, int status, object body)
        {
            try { Respond(res, status, body); } catch { }
        }

        public class LineRequest
        {
            public string Profile { get; set; }
            public string Text { get; set; }
            public string Kana { get; set; }
            public string Folder { get; set; }
            public string Filename { get; set; }
            public string Encoding { get; set; }
        }

        public class BatchRequest
        {
            public List<LineRequest> Lines { get; set; }
            public string Folder { get; set; }
            public string Encoding { get; set; }
            public int Interval_ms { get; set; }
        }
    }
}
