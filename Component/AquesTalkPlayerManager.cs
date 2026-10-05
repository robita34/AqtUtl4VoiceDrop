using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using NAudio.Wave;

namespace AqT_Utl
{
    internal class AquesTalkPlayerManager
    {
        string PlayerPath;
        public AquesTalkPlayerManager()
        {

        }

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

        public string VoiceGenerate(string hatsuon, string jimaku, SerifProfile p, int fps, bool interim, bool OffSound)   //音声を生成
        {

            fps = Properties.Settings.Default.fps_AviUtl;
            string output_folder = Properties.Settings.Default.output_folder;

            if (hatsuon == "") hatsuon = "_"; //発音なしの場合発音しない適当な文字に置換

            if (output_folder.StartsWith("\\"))     //出力フォルダ設定の最初が\だった場合、相対パスと判断する
            {
                string currentDirectory = Directory.GetCurrentDirectory();
                output_folder = currentDirectory + output_folder;

                if(Directory.Exists(output_folder) == false)
                {
                    Directory.CreateDirectory(output_folder);
                }

            }
            else
            {
                if(Directory.Exists(Properties.Settings.Default.output_folder))
                {
                    output_folder = Properties.Settings.Default.output_folder;
                }
                else
                {
                    MessageBox.Show("ボイス生成フォルダの設定が正しくありません。");
                    return "err";
                }
            }

            string timeStamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            
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
            string filename = p.ProfileName + "_" + safeJimaku + "_" + timeStamp;

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
            if (OffSound == false)
            {
                Process voiceListen = Process.Start(ListenProcessStartInfo);
            }

            voiceGenerate.WaitForExit();



            //フレーム数を求める
            int frameCount = 0;
            try
            {
                using (var reader = new WaveFileReader(filepath))
                {
                    double duration = reader.TotalTime.TotalSeconds;
                    frameCount = (int)(duration * fps);
                    Console.WriteLine("wavファイルの長さ: {0:F2}秒", duration);
                    Console.WriteLine("動画編集ソフトに読み込む際に必要なフレーム数: {0}", frameCount);
                }
            }
            catch
            {
                MessageBox.Show("音声ファイルの生成に失敗しました。");
                return "err";
            }


            try
            {
                // Windowsの標準文字コード「Shift-JIS (CodePage: 932)」を指定
                System.Text.Encoding sjis = System.Text.Encoding.GetEncoding(932);
                
                // 引数で渡ってきた字幕文字列（jimaku）をファイルに上書き保存
                // Javaの Files.writeString や PHPの file_put_contents と同じ挙動です
                System.IO.File.WriteAllText(txtFilepath, jimaku, sjis);
            }
            catch (System.Exception ex)
            {
                // 万が一テキストの書き込みに失敗してもログを出すだけで全体の処理は止めない
                System.Console.WriteLine("TXTファイルの生成に失敗しました: " + ex.Message);
            }

            if (interim)    //仮モード有効時、生成した音声ファイルを削除し終了する
            {
                string wavfilepath = output_folder + "\\" + filename + ".wav";
                File.Delete(wavfilepath);
                return "delete_success";
            }

            return output_folder + "\\" + filename + ".exo";
        }

    }
}
