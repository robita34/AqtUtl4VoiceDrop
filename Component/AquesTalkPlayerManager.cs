using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using NAudio.Wave;

namespace AqT_Utl
{
    //音声生成の失敗を表す例外（メッセージはそのままユーザー/APIに返す）
    internal class VoiceGenerateException : Exception
    {
        public VoiceGenerateException(string message) : base(message) { }
    }

    //音声生成の結果
    internal class VoiceGenerateResult
    {
        public string WavPath { get; set; }
        public string TxtPath { get; set; }
        public double Seconds { get; set; }
        public int FrameCount { get; set; }
    }

    internal class AquesTalkPlayerManager
    {
        string PlayerPath;

        //UIとAPIから同時に生成されてもタイムスタンプ順・ファイル名が崩れないよう直列化する
        static readonly object generateLock = new object();

        public AquesTalkPlayerManager()
        {

        }

        public bool IsRegistered { get { return PlayerPath != null; } }

        public int RegistPlayer()
        {
            if(File.Exists("aquestalkplayer/AquesTalkPlayer.exe"))
            {
                PlayerPath = Path.GetFullPath("aquestalkplayer/AquesTalkPlayer.exe");
                return 0;
            }
            else
            {
                //MessageBox.Show("なし");
                return 1;
            }

        }

        public void StartupPlayer()
        {
            try
            {
                Process.Start(PlayerPath);
            }
            catch(Exception ex)
            {
                MessageBox.Show("aquestalkplayer/AquesTalkPlayer.exeが見つかりません");
            }

        }

        public string VoiceGenerate(string hatsuon, string jimaku, SerifProfile p, int fps, bool interim, bool OffSound)   //音声を生成（UI用）
        {
            try
            {
                VoiceGenerateResult result = Synthesize(hatsuon, jimaku, p, interim, !OffSound);
                if (interim) return "delete_success";
                return Path.ChangeExtension(result.WavPath, ".exo");
            }
            catch (VoiceGenerateException ex)
            {
                MessageBox.Show(ex.Message);
                return "err";
            }
        }

        //音声を生成する本体。失敗時は VoiceGenerateException を投げる（UIを出さないのでAPIからも呼べる）
        //folder/fileName はAPI用。省略時は設定の出力フォルダと既定の命名ルールを使う
        public VoiceGenerateResult Synthesize(string hatsuon, string jimaku, SerifProfile p, bool interim, bool playSound, string folder = null, string fileName = null)
        {
            if (PlayerPath == null) throw new VoiceGenerateException("aquestalkplayer/AquesTalkPlayer.exeが見つかりません");

            lock (generateLock)
            {
                int fps = Properties.Settings.Default.fps_AviUtl;
                string output_folder = ResolveOutputFolder();
                if (!string.IsNullOrWhiteSpace(folder)) output_folder = ResolveCustomFolder(folder, output_folder);
                if (!string.IsNullOrWhiteSpace(fileName)) fileName = ValidateFileName(fileName, output_folder);

                if (hatsuon == null) hatsuon = "";
                if (jimaku == null) jimaku = "";
                hatsuon = hatsuon.Replace("\"", ""); //コマンドライン引数が壊れないよう"を除去
                if (hatsuon == "") hatsuon = "_"; //発音なしの場合発音しない適当な文字に置換

                // ① ファイル名に使えない禁止文字を正規表現で一括削除
                string safeJimaku = System.Text.RegularExpressions.Regex.Replace(jimaku, @"[\\/:*?""<>|\s\t\n\r]", "");

                // ② 切り出す文字数を最大7文字に制限（安全な切り出し）
                if (safeJimaku.Length > 7)
                {
                    safeJimaku = safeJimaku.Substring(0, 7);
                }

                // ③ 記号や改行のみで文字が消滅した場合、OSのエラーを防ぐためのフォールバック
                if (string.IsNullOrEmpty(safeJimaku))
                {
                    safeJimaku = "voice";
                }

                // ④ 理想の命名ルールでベース名を作成
                //    連続生成で同名になる場合は、命名ルールを変えずにタイムスタンプが進むまで待つ
                string filename = fileName;
                while (filename == null)
                {
                    string timeStamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    string candidate = p.ProfileName + "_" + safeJimaku + "_" + timeStamp;
                    if (!File.Exists(output_folder + "\\" + candidate + ".wav")) { filename = candidate; break; }
                    Thread.Sleep(100);
                }

                // 最終的なWAVファイルのフルパス
                string filepath = output_folder + "\\" + filename + ".wav";

                // 最終的なTXTファイルのフルパス
                string txtFilepath = output_folder + "\\" + filename + ".txt";

                string 引数 = "/T \"" + hatsuon + "\" " + "/P \"" + p.UsePreset + "\" " + "/W \"" + filepath + "\"";


                ProcessStartInfo GenerateProcessStartInfo = new ProcessStartInfo();
                GenerateProcessStartInfo.FileName = PlayerPath;
                GenerateProcessStartInfo.Arguments = 引数;

                ProcessStartInfo ListenProcessStartInfo = new ProcessStartInfo();
                ListenProcessStartInfo.FileName = PlayerPath;
                ListenProcessStartInfo.Arguments = "/T \"" + hatsuon + "\"" + "/P \"" + p.UsePreset + "\"";

                Process voiceGenerate = Process.Start(GenerateProcessStartInfo);
                if (playSound)
                {
                    Process voiceListen = Process.Start(ListenProcessStartInfo);
                }

                voiceGenerate.WaitForExit();



                //フレーム数を求める
                double duration;
                int frameCount = 0;
                try
                {
                    using (var reader = new WaveFileReader(filepath))
                    {
                        duration = reader.TotalTime.TotalSeconds;
                        frameCount = (int)(duration * fps);
                        Console.WriteLine("wavファイルの長さ: {0:F2}秒", duration);
                        Console.WriteLine("動画編集ソフトに読み込む際に必要なフレーム数: {0}", frameCount);
                    }
                }
                catch
                {
                    throw new VoiceGenerateException("音声ファイルの生成に失敗しました。");
                }

                if (interim)    //仮モード有効時、生成した音声ファイルを削除し終了する
                {
                    File.Delete(filepath);
                    return new VoiceGenerateResult { Seconds = duration, FrameCount = frameCount };
                }

                try
                {
                    // Windowsの標準文字コード「Shift-JIS (CodePage: 932)」を指定
                    System.Text.Encoding sjis = System.Text.Encoding.GetEncoding(932);

                    // 引数で渡ってきた字幕文字列（jimaku）をファイルに上書き保存
                    System.IO.File.WriteAllText(txtFilepath, jimaku, sjis);
                }
                catch (System.Exception ex)
                {
                    // 万が一テキストの書き込みに失敗してもログを出すだけで全体の処理は止めない
                    System.Console.WriteLine("TXTファイルの生成に失敗しました: " + ex.Message);
                }

                return new VoiceGenerateResult { WavPath = filepath, TxtPath = txtFilepath, Seconds = duration, FrameCount = frameCount };
            }
        }

        string ResolveOutputFolder()
        {
            string output_folder = Properties.Settings.Default.output_folder;

            if (output_folder.StartsWith("\\"))     //出力フォルダ設定の最初が\だった場合、相対パスと判断する
            {
                output_folder = Directory.GetCurrentDirectory() + output_folder;

                if (Directory.Exists(output_folder) == false)
                {
                    Directory.CreateDirectory(output_folder);
                }
                return output_folder;
            }

            if (Directory.Exists(output_folder)) return output_folder;

            throw new VoiceGenerateException("ボイス生成フォルダの設定が正しくありません。");
        }

        //API指定の出力先。「C:\～」やUNCはそのまま、それ以外は既定の出力フォルダからの相対パス。無ければ作成する
        static string ResolveCustomFolder(string folder, string defaultFolder)
        {
            folder = folder.Trim().Replace('/', '\\');
            bool absolute = (folder.Length >= 3 && folder[1] == ':' && folder[2] == '\\') || folder.StartsWith("\\\\");
            string path = absolute ? folder : Path.Combine(defaultFolder, folder.TrimStart('\\'));

            try
            {
                path = Path.GetFullPath(path).TrimEnd('\\');
                Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                throw new VoiceGenerateException("出力先フォルダ「" + folder + "」を使えません: " + ex.Message);
            }
            return path;
        }

        //API指定のファイル名（拡張子なし）。使えない文字があればエラー、既存ファイルは上書きせずエラー
        static string ValidateFileName(string fileName, string folder)
        {
            fileName = fileName.Trim();
            if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - 4);

            if (fileName == "" || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new VoiceGenerateException("ファイル名「" + fileName + "」に使えない文字が含まれています（\\ / : * ? \" < > | など）");

            if (File.Exists(Path.Combine(folder, fileName + ".wav")) || File.Exists(Path.Combine(folder, fileName + ".txt")))
                throw new VoiceGenerateException("「" + fileName + "」は出力先に既にあります（上書きはしません）");

            return fileName;
        }

    }
}
