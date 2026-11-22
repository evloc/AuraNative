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
- **Listens to your songs** — once, slowly, in the background (lowest
  priority; it can be turned off in Settings). From that it draws each song's
  **waveform** in Now Playing, finds its **tempo (BPM)** and **key** (with the
  Camelot number), lines up **versions** so switching between them lands on
  the same part of the song (even when one has a longer intro), and spots
  duplicates that have different names.
- **Waveform and A–B loop** — in Now Playing, click the waveform to jump, or
  drag across it to loop a part (or use the A and B buttons).
- **Duplicates** — finds the same song saved more than once: exact copies,
  the same song in another file (WAV and MP3, CDQ and LQ…), and copies that
  sound the same whatever they're called. AURA keeps the best copy (best
  sound, then the most complete tags — you can pick another) and moves the
  others into a `_duplicates` folder next to them. Nothing is deleted, and the
  plays, playlists and favorites of the extra copies move to the one that's
  kept. Different versions (v1, v2, snippets, remixes, sped up…) never count.
- **Convert to MP3** — right-click → More → Convert to MP3, with Windows' own
  MP3 encoder (128–320 kbps). Tags, cover and lyrics (with their timings) come
  along, the song keeps its plays, playlists and favorite, and the original
  goes into an `_originals` folder next to it.
- **Lyrics in time** — right-click → More → Write lyrics in time (or the
  waveform button in Now Playing): **auto-sync** times the lyrics a song has
  to the music, word by word; **transcribe** writes them down by listening.
  It uses Whisper (whisper.cpp) on your PC — nothing is uploaded. The model
  downloads once (base 148 MB, small 488 MB, or bigger).
- **Windows media controls** — the pop-up that shows with the volume keys
  and the lock screen show the song and cover, with play/pause, next and
  previous.
- **Taskbar buttons** — previous, play/pause and next under AURA's taskbar
  thumbnail.
- **Mini player** — a small window that stays on top (Ctrl+M or the button
  next to the microphone).
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

Not here yet: vocal isolation before transcribing (the first AURA's MDX-Net
model). Whisper listens to the full mix.

## Keys

Space play/pause · Ctrl+L Now Playing · F11 full-screen lyrics · Ctrl+M mini player · Ctrl+→ / Ctrl+← next / previous · Ctrl+↑ / Ctrl+↓ volume ·
Ctrl+F search · Alt+← / Alt+→ back / forward · Enter plays the selected song ·
Delete removes from a playlist or up next.

## Releasing

    git tag v0.1.0
    git push origin v0.1.0

GitHub builds it on Windows and puts `AURA-Native-Setup.exe` and
`AURA-Native-portable.zip` on the release page.

To build it yourself on Windows, install the .NET SDK and run
`dotnet build AuraNative.csproj -c Release`; the app ends up in `bin\Release`.
The speech part (`aura_whisper.dll`) is built from `native/` with CMake and
whisper.cpp v1.7.6 — see the release workflow for the exact commands.
