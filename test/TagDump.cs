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
  // --tagwrite file key=value ... (cover=path, nocover)
  public static int Write(string[] args) {
    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
    var e = new TagEdit();
    foreach (var a in args.Skip(1)) {
      int eq = a.IndexOf('='); string k = eq < 0 ? a : a.Substring(0, eq), v = eq < 0 ? "" : a.Substring(eq + 1);
      switch (k) { case "title": e.Title = v; break; case "artist": e.Artist = v; break; case "album": e.Album = v; break; case "albumartist": e.AlbumArtist = v; break;
        case "year": e.Year = v; break; case "genre": e.Genre = v; break; case "track": e.Track = v; break; case "cover": e.Picture = File.ReadAllBytes(v); break; case "nocover": e.RemovePicture = true; break; }
    }
    try { TagWriter.Write(args[0], e); Console.WriteLine("OK " + Path.GetFileName(args[0])); return 0; }
    catch (Exception x) { Console.WriteLine("ERR " + Path.GetFileName(args[0]) + ": " + x.Message); return 1; }
  }
  public static int Sniff(string[] files) { foreach (var p in files) Console.WriteLine(Path.GetFileName(p) + "\t" + (AudioKind.Detect(p) ?? "?") + "\t" + AudioKind.Matches(p, AudioKind.Detect(p))); return 0; }
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
