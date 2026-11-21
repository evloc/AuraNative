// test only: prints what the tag reader sees
using System; using System.IO; using System.Linq;
namespace Aura {
static class TagDump {
  public static int Run(string[] files) {
    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
    foreach (var p in files) {
      var t = Tags.Read(p, true);
      Console.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8:0.000}\t{9}\t{10}", Path.GetFileName(p), t.Title, t.Artist, t.Album, t.AlbumArtist, t.Year, t.Genre, t.Track, t.Duration, t.HasPicture ? 1 : 0, t.Picture == null ? 0 : t.Picture.Length);
    }
    return 0;
  }
  public static int Lyrics(string[] files) {
    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
    foreach (var p in files) {
      var t = Tags.Read(p);
      var l = Aura.Lyrics.From(t.LyricsText, t.Synced, t.WordsRaw, "file");
      if (l == null) { Console.WriteLine(Path.GetFileName(p) + "\tnone"); continue; }
      var w = l.Lines?.FirstOrDefault(x => x.Words != null);
      Console.WriteLine("{0}\tlines={1}\ttimed={2}\tbyword={3}\tfirst={4}\tt1={5:0.00}\twords={6}\tend={7:0.00}\tplain={8}", Path.GetFileName(p),
        l.Lines?.Count ?? 0, l.Timed, l.Lines?.Count(x => x.Words != null) ?? 0, l.Lines?.FirstOrDefault()?.Text ?? l.Plain.Split('\n')[0],
        l.Lines?.FirstOrDefault()?.T ?? -1, w == null ? "" : string.Join("|", w.Words.Select(x => x.w + "@" + x.t.ToString("0.00"))), w?.End ?? -1, l.Plain.Replace("\n", "/"));
    }
    return 0;
  }
}}
