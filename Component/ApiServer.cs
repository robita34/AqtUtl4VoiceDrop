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
    //  GET  /profiles    … プロファイル一覧
    //  POST /synthesis   … {"profile":"名前かId","text":"字幕","kana":"発音(省略時は字幕と同じ)"} → wav+txtを保存
    //  POST /batch       … {"lines":[{...},{...}]} → 上から順に保存
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
                catch (Exception ex)
                {
                    TryRespond(context.Response, 500, new { error = ex.Message });
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
                    playerFound = playerManager.IsRegistered
                });
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
                    Respond(context.Response, 400, new { error = ex.Message });
                }
                return;
            }

            if (method == "POST" && path == "/batch")
            {
                BatchRequest batch = ReadBody<BatchRequest>(req);
                if (batch?.Lines == null || batch.Lines.Count == 0)
                {
                    Respond(context.Response, 400, new { error = "linesが空です" });
                    return;
                }

                List<SerifProfile> profiles = getProfiles();
                var results = new List<object>();
                int okCount = 0;
                for (int i = 0; i < batch.Lines.Count; i++)
                {
                    //VoiceDropの取り込みが追いつくよう、行間に任意の待ち時間を入れる
                    if (i > 0 && batch.Interval_ms > 0) System.Threading.Thread.Sleep(batch.Interval_ms);

                    try
                    {
                        results.Add(Generate(batch.Lines[i], profiles, false));
                        okCount++;
                    }
                    catch (VoiceGenerateException ex)
                    {
                        results.Add(new { index = i, error = ex.Message });
                    }
                }
                Respond(context.Response, okCount == batch.Lines.Count ? 200 : 207, new { ok = okCount, failed = batch.Lines.Count - okCount, results });
                return;
            }

            Respond(context.Response, 404, new { error = "不明なエンドポイントです: " + method + " " + req.Url.AbsolutePath });
        }

        object Generate(LineRequest line, List<SerifProfile> profiles, bool play)
        {
            if (line == null || string.IsNullOrEmpty(line.Text)) throw new VoiceGenerateException("textが空です");

            SerifProfile p = FindProfile(line.Profile, profiles);
            string kana = string.IsNullOrEmpty(line.Kana) ? line.Text : line.Kana;

            VoiceGenerateResult r = playerManager.Synthesize(kana, line.Text, p, play, play);
            if (play) return new { profile = p.ProfileName, seconds = Math.Round(r.Seconds, 3) };
            return new { profile = p.ProfileName, text = line.Text, wav = r.WavPath, txt = r.TxtPath, seconds = Math.Round(r.Seconds, 3), frames = r.FrameCount };
        }

        static SerifProfile FindProfile(string key, List<SerifProfile> profiles)
        {
            if (string.IsNullOrEmpty(key)) throw new VoiceGenerateException("profileが指定されていません");

            SerifProfile p = profiles.FirstOrDefault(x => x.ProfileName == key);
            if (p == null && int.TryParse(key, out int id)) p = profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) throw new VoiceGenerateException("プロファイル「" + key + "」が見つかりません");
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
        }

        public class BatchRequest
        {
            public List<LineRequest> Lines { get; set; }
            public int Interval_ms { get; set; }
        }
    }
}
