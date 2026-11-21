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
