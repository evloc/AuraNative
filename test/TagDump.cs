// test only: prints what the tag reader sees, as JSON lines
using System; using System.IO;
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
}}
