# AURA Native

The new AURA: a plain Windows app written in C#. No Python and no web engine —
just one small `AURA Native.exe` that runs on the .NET Framework already built
into Windows 10 and 11. Nothing else to install.

It is a separate app from the first AURA (github.com/evloc/Aura). Both can be
installed side by side, and each keeps its own library:

- AURA Native: `%APPDATA%\AURA Native`
- the first AURA: `%APPDATA%\Unreleased`

## What it does so far

- **Your music, where it is.** Reads MP3, M4A/AAC, FLAC, Ogg, Opus, WAV and
  AIFF straight from your folders (tags, covers and song lengths). Notices new,
  changed and deleted songs by itself.
- **Home** — Shuffle everything, quick picks (Favorites, playlists, albums),
  Jump back in, Recently added, On repeat, New songs, Your artists.
- **Songs** — sortable by title, artist, album, date added, length or most
  played; multi-select with Ctrl and Shift; right-click for everything.
- **Artists and Albums** — cover grids, and pages for each with their songs.
- **Playlists** — make, rename, delete, add songs from any right-click menu,
  drag songs to reorder.
- **Favorites** — the heart on any song.
- **Up next** — Play next, Add to up next, drag to reorder, remove, clear.
- **Search** — songs, artists and albums, including file names.
- **Player** — shuffle, repeat all / repeat one, seek, volume, and the
  keyboard's media keys even when AURA isn't the window in front.
- **Colors follow the song** — the accent color comes from the playing cover.
- **Now Playing and lyrics** — click the cover in the player bar (or the
  microphone, or Ctrl+L). Timed lyrics follow the song and light up word by
  word when the song has word timings; click a line to jump there.
- **Full-screen lyrics** — over a slow starry space scene colored by the
  cover (F11; Esc to leave).
- **Lyrics from everywhere** — lyrics already in your songs (including the
  timings and word timings the first AURA saves), a `.lrc` file next to the
  song, or found online. AURA looks on LRCLIB, Genius and lyrics.ovh by itself for songs
  without lyrics and only keeps a match when the title and artist both agree;
  **Find lyrics** lets you search and pick yourself. Found lyrics are saved in
  AURA — your music files aren't changed.
- **Edit info** — right-click songs → Edit info to change the title, artist,
  album, album artist, year, genre, track number and cover, for one song or
  many at once (only the boxes you change are saved). Works for MP3, FLAC,
  Ogg, Opus, M4A, WAV and AIFF. AURA writes a new copy, reads it back to check
  it, and only then swaps it in, so a failed save never damages a song. A song
  that's playing keeps playing.
- **Find a cover** — searches Apple Music and Deezer; use it for one song or
  the whole album, or choose a picture file.
- **Fill in blanks from file names** — "03 - Juice WRLD - Robbery (v2).mp3"
  gives track 3, artist and title. Only empty boxes are filled.
- **Rename files** — from a pattern like {artist} - {title}; plays, playlists
  and favorites stay with each song.
- **Missing info** — songs with no artist, title, cover, album, year or lyrics.
- **Versions** — songs that come in more than one version (v2, CDQ, snippet…)
  side by side; switch versions in Now Playing without losing your place.
- **Stats** — listening time, plays by day or month, top songs and artists.
- **Themes** — Midnight, Neon, Sunset, Ocean and Mono.
- **Plays with Windows' own Media Foundation engine** (the one behind Edge and
  the Media Player app), with Windows Media Player's engine and MCI as backups
  — so it doesn't need the old Windows Media Player installed.
- **Plays awkward files** — songs whose path is very long or has a folder
  ending in a space; files that are really M4A, Opus, FLAC or WAV but named
  .mp3 (right-click → More → Fix file type renames them properly); and if
  Windows' main player engine can't open a song, a second engine tries.
- **Follows converted songs** — when song.wav becomes song.mp3 (the first
  AURA's converter), it stays the same song with its plays and playlists.
- **Updates itself** — when a new version is on GitHub, an Update button shows
  at the top; AURA installs it and opens again.
- **Picks up where you left off** — same song, same place, same queue.
- **Brings over your first AURA** — on first start it takes your music
  folders, playlists, favorites and play counts from the first AURA
  (matched by file path; the first AURA is not changed).

Not here yet (still in the first AURA): auto-sync and transcribe (they need a
speech-recognition model), converting to MP3, and BPM/key detection.

## Keys

Space play/pause · Ctrl+L Now Playing · F11 full-screen lyrics · Ctrl+→ / Ctrl+← next / previous · Ctrl+↑ / Ctrl+↓ volume ·
Ctrl+F search · Alt+← / Alt+→ back / forward · Enter plays the selected song ·
Delete removes from a playlist or up next.

## Releasing

    git tag v0.1.0
    git push origin v0.1.0

GitHub builds it on Windows and puts `AURA-Native-Setup.exe` and
`AURA-Native-portable.zip` on the release page.

To build it yourself on Windows, install the .NET SDK and run
`dotnet build AuraNative.csproj -c Release`; the app ends up in `bin\Release`.
