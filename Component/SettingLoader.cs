using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AqT_Utl.Component
{
    internal class SettingLoader
    {
        //49152以上はWindows(WinNAT/Hyper-V)が起動時に予約することがあり、使えなくなるので避ける
        public const int DefaultApiPort = 47121;

        //v2.5.1.0より前の既定ポート。setting.jsonに残っていたら新しい既定へ移行する
        const int OldDefaultApiPort = 50121;

        //APIの待ち受けポート。0以下でAPI無効（setting.jsonの api_port で変更）
        public static int ApiPort = DefaultApiPort;

        //UI操作でファイルを出力するか（falseなら生成ボタンは試聴のみ。APIには影響しない）
        public static bool UiOutput = true;

        //字幕txtの文字コード（"utf-8" か "shift_jis"。UIとAPIの既定。APIはリクエストごとに変更可）
        public static string TxtEncodingName = TxtEncoding.Default;

        public void Load()
        {
            string json;
            try
            {
                json = File.ReadAllText("setting.json");
            }
            catch (Exception ex)
            {
                Save();
                json = File.ReadAllText("setting.json");
            }

            SettingJson setting = JsonSerializer.Deserialize<SettingJson>(json);

            Properties.Settings.Default.output_folder = setting.output_folder;
            Properties.Settings.Default.jimakuCopy_startup = setting.jimakuCopy_startup;
            Properties.Settings.Default.fps_AviUtl = setting.fps_AviUtl;
            Properties.Settings.Default.useGCMZ = setting.useGCMZ;
            Properties.Settings.Default.TopMost = setting.TopMost;
            ApiPort = setting.api_port ?? DefaultApiPort;     //旧バージョンのsetting.jsonには無いので既定値
            if (ApiPort == OldDefaultApiPort && (setting.last_version == null || setting.last_version < new Version(2, 5, 1, 0)))
                ApiPort = DefaultApiPort;
            UiOutput = setting.ui_output ?? true;
            TxtEncodingName = TxtEncoding.Parse(setting.txt_encoding) != null ? setting.txt_encoding : TxtEncoding.Default;     //未設定・不正ならUTF-8

            if (setting.last_version != Assembly.GetExecutingAssembly().GetName().Version)      //最後に起動したときのバージョンが今のバージョンと異なる場合に処理
            {
                VersionChangedConvert versionChangedConvert = new VersionChangedConvert(setting.last_version);
            }

            Properties.Settings.Default.Save();


        }

        public void Save()
        {
            SettingJson setting = new SettingJson();
            setting.last_version = Assembly.GetExecutingAssembly().GetName().Version;
            setting.output_folder = Properties.Settings.Default.output_folder;
            setting.jimakuCopy_startup = Properties.Settings.Default.jimakuCopy_startup;
            setting.fps_AviUtl = Properties.Settings.Default.fps_AviUtl;
            setting.useGCMZ = Properties.Settings.Default.useGCMZ;
            setting.TopMost = Properties.Settings.Default.TopMost;
            setting.api_port = ApiPort;
            setting.ui_output = UiOutput;
            setting.txt_encoding = TxtEncodingName;

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(setting, options);
            File.WriteAllText("setting.json", json);


        }
    }

    public class SettingJson
    {
        public Version last_version { get; set; }
        public string output_folder { get; set; }
        public bool jimakuCopy_startup { get; set; }
        public int fps_AviUtl { get; set; }
        public bool useGCMZ { get; set; }
        public bool TopMost { get; set; }
        public int? api_port { get; set; }
        public bool? ui_output { get; set; }
        public string txt_encoding { get; set; }
    }
}
