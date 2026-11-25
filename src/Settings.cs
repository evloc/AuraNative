using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Aura
{
    [DataContract]
    sealed class Settings
    {
        [DataMember] public List<string> Folders = new List<string>();
        [DataMember] public int Volume = 80;
        [DataMember] public bool Shuffle;
        [DataMember] public int Repeat;              // 0 off, 1 all, 2 one
        [DataMember] public long LastTrack;
        [DataMember] public double LastPosition;
        [DataMember] public List<long> Queue = new List<long>();
        [DataMember] public List<int> Order = new List<int>();
        [DataMember] public int QueuePos;
        [DataMember] public bool CoverColors = true;
        [DataMember] public string Accent = "#1688FF";
        [DataMember] public bool Compact;
        [DataMember] public int WinX = -1, WinY = -1, WinW = 1360, WinH = 860;
        [DataMember] public bool Maximized;
        [DataMember] public bool Imported;            // folders, playlists and favorites brought over from AURA
        [DataMember] public string SongsSort = "added";
        [DataMember] public bool SongsDesc = true;
        [DataMember] public bool AutoLyrics = true;      // look online by itself for songs without lyrics
        [DataMember] public bool CheckUpdates = true;
        [DataMember] public string RenamePattern = "{artist} - {title}";
        [DataMember] public string ThemeName = "Midnight";
        [DataMember] public bool Listen = true;           // learn each song's sound in the background (waveform, BPM, key, duplicates)
        [DataMember] public int Mp3Kbps = 320;
        [DataMember] public int MiniX = -1, MiniY = -1;
        [DataMember] public string WhisperModel = "base";
        [DataMember] public int Between;                 // 0 normal, -1 no gap, 2..12 crossfade seconds
        [DataMember] public bool Leveling;
        [DataMember] public bool Radio;
        [DataMember] public double Rate = 1;
        [DataMember] public bool Discord;
        [DataMember] public string DiscordApp = "";
        [DataMember] public bool DiscordOwnCovers;
        [DataMember] public bool AutoYears = true;
        [DataMember] public bool AutoSyncAll;           // time every song that has lyrics but no timings, in the background
        [DataMember] public string Separate = "transcribe";   // separate the vocals first: "transcribe", "always", "off"
        [DataMember] public string SongLanguage = "en";
        [DataMember] public bool LookOnlineAfterTranscribe = true;

        // settings saved by an older AURA don't have the newer ones: start those at their defaults
        [OnDeserializing]
        void Defaults(StreamingContext c)
        {
            Folders = new List<string>(); Volume = 80; Queue = new List<long>(); Order = new List<int>();
            CoverColors = true; Accent = "#1688FF"; WinX = -1; WinY = -1; WinW = 1360; WinH = 860;
            SongsSort = "added"; SongsDesc = true; AutoLyrics = true; CheckUpdates = true;
            RenamePattern = "{artist} - {title}"; ThemeName = "Midnight";
            Listen = true; Mp3Kbps = 320; MiniX = -1; MiniY = -1; WhisperModel = "base";
            Rate = 1; DiscordApp = ""; AutoYears = true; Separate = "transcribe"; SongLanguage = "en"; LookOnlineAfterTranscribe = true;
        }

        public static string Dir;
        static string FilePath => System.IO.Path.Combine(Dir, "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var f = File.OpenRead(FilePath))
                    {
                        var s = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(f);
                        if (s != null) { s.Fix(); return s; }
                    }
            }
            catch { }
            var d = new Settings(); d.Fix(); return d;
        }

        void Fix()
        {
            if (Folders == null) Folders = new List<string>();
            if (Queue == null) Queue = new List<long>();
            if (Order == null) Order = new List<int>();
            if (string.IsNullOrEmpty(Accent)) Accent = "#1688FF";
            if (string.IsNullOrEmpty(SongsSort)) SongsSort = "added";
            Volume = Math.Max(0, Math.Min(100, Volume));
            if (Array.IndexOf(Converter.Rates, Mp3Kbps) < 0) Mp3Kbps = 320;
            if (string.IsNullOrEmpty(WhisperModel)) WhisperModel = "base";
            if (Rate < 0.5 || Rate > 2) Rate = 1;
            if (Between < -1 || Between > 12) Between = 0;
            if (string.IsNullOrEmpty(Separate)) Separate = "transcribe";
            if (string.IsNullOrEmpty(SongLanguage)) SongLanguage = "en";
            if (DiscordApp == null) DiscordApp = "";
            if (string.IsNullOrEmpty(RenamePattern)) RenamePattern = "{artist} - {title}";
            if (string.IsNullOrEmpty(ThemeName)) ThemeName = "Midnight";
        }

        public void Save()
        {
            try
            {
                var tmp = FilePath + ".tmp";
                using (var f = File.Create(tmp)) new DataContractJsonSerializer(typeof(Settings)).WriteObject(f, this);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch { }
        }
    }

    // The old AURA's settings: only the music folders are read.
    [DataContract]
    sealed class OldConfig
    {
        [DataMember(Name = "folders")] public List<string> Folders;

        public static List<string> ReadFolders(string path)
        {
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var c = (OldConfig)new DataContractJsonSerializer(typeof(OldConfig)).ReadObject(f);
                    return c?.Folders ?? new List<string>();
                }
            }
            catch { return new List<string>(); }
        }
    }
}
