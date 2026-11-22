using System;
using System.Collections.Generic;
using System.Drawing;

namespace Aura
{
    sealed class Route
    {
        public string Kind;        // home, songs, artists, albums, album, artist, playlist, favorites, queue, search, settings, now
        public string Key = "";    // album/artist key, search text
        public long Id;            // playlist id
        public float Scroll;       // remembered when going back
        public Route(string kind, string key = "", long id = 0) { Kind = kind; Key = key ?? ""; Id = id; }
        public bool Same(Route o) => o != null && o.Kind == Kind && o.Key == Key && o.Id == Id;
    }

    interface IShell
    {
        Library Lib { get; }
        Player Player { get; }
        Settings Prefs { get; }
        LyricsStore Lyrics { get; }
        SoundStore Sound { get; }
        void ConvertToMp3(IList<Track> tracks);
        void MergeDuplicates(IList<(DupGroup g, Track keep)> groups);
        void ToggleMini();
        void Transcribe(Track t);
        void FullScreenLyrics();
        void FindLyrics(Track t);
        void EditInfo(IList<Track> tracks);
        void FindCover(IList<Track> tracks);
        void FillFromNames(IList<Track> tracks);
        void RenameFiles(IList<Track> tracks);
        void Go(Route r);
        void Back();
        void Forward();
        bool CanBack { get; }
        bool CanForward { get; }
        Route Current { get; }
        void Toast(string msg);
        void TrackMenu(IList<Track> tracks, Point screen, TrackContext ctx);
        void AddFolder();
        void NewPlaylist(IEnumerable<long> ids = null);
        void Rescan();
    }

    // where a song menu was opened from, so it can offer the right extras
    sealed class TrackContext
    {
        public Playlist Playlist;           // "Remove from this playlist"
        public bool Queue;                  // "Remove from up next"
        public List<int> QueueIndexes;
        public Action Removed;
    }
}
