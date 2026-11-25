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
- **Search** — songs, artists and albums, including file names — and a line
  of the lyrics finds its song. Narrow it down with `artist:`, `album:`,
  `era:`, `prod:`, `year:>=2019`, `bpm:>140`, `key:8A`, `rating:>=4`,
  `plays:>10`, `"an exact phrase"`, `-leave-this-out`, and `noart`,
  `nolyrics`, `unrated`, `fav`, `versioned`, `unplayed`.
- **Ratings and smart playlists** — 1–5 stars (keys 1–5, or the stars in Now
  Playing); Most played, Recently played, Recently added and Top rated build
  themselves in the sidebar.
- **Eras** — group songs by era or session (right-click → More → Set era, or
  Edit info); each era gets a page with its cover.
- **History** — every song you played, newest first.
- **Drag and drop** — drag songs onto a playlist, Favorites or Up next in the
  sidebar, or out to a folder.
- **A–Z** — long song lists sorted by name get a letter bar on the right.
- **Between songs** — a short gap, no gap, or a 3–10 second crossfade.
- **Even out loudness** — loud songs are turned down to match the rest.
- **Radio** — when Up next runs out, it keeps going with songs like the last
  one (same artist, era, tempo, and your favorites).
- **Sleep timer** — the moon in the player bar: stop in 15–90 minutes or at the
  end of the song (it fades out).
- **Speed** — 0.75× to 1.5× (Now Playing).
- **Bookmarks** — press B (or the Bookmark button) to mark a moment; they show
  on the waveform — click to jump, right-click to remove.
- **Discord status** — "Listening to" on your Discord profile, with the
  song's cover (Settings → Discord status; needs a free Discord app's
  Application ID). Turn on "Show my own covers" to see the covers from AURA
  itself: each one is uploaded once to catbox.moe so Discord can load it.
  Otherwise it's the Apple Music cover when it's the very same song and
  picture, or the AURA logo.
- **Years** — songs without a year get one looked up on MusicBrainz, Apple
  Music and Genius (only when the title and artist match; the earliest year
  wins). It's written into the file, and Undo puts it back. Settings → Years.
- **Undo** — Ctrl+Z puts back the last tag change, era or tracker import;
  Settings → Undo lists the last few.
- **Fan trackers** — Missing info → Import a fan tracker: a .csv, .xlsx or
  Google Sheets link. Matches songs by title (and their other names), shows
  what would change, then fills in titles, eras, years and notes (producer,
  recorded and leaked dates).
- **Now in full** — when a song you only had snippets of shows up in full,
  AURA says so (and Versions lists it).
- **Player** — shuffle, repeat all / repeat one, seek, volume, and the
  keyboard's media keys even when AURA isn't the window in front.
- **Colors follow the song** — the accent color comes from the playing cover.
- **Now Playing and lyrics** — click the cover in the player bar (or the
  microphone, or Ctrl+L). Timed lyrics follow the song and light up word by
  word when the song has word timings; click a line to jump there.
- **Full-screen lyrics** — deep space: black, with twinkling stars, shooting
  stars and soft splashes of violet, pink and blue (the same for every song).
  The cover sits big in the middle with a ring that pulses with the music; the
  lyrics are on the right (the current line glows and fills in word by word;
  click a line to jump there), what plays next is on the left (click a card to
  play it), and a glass player bar at the bottom slides away when the mouse
  rests. A new song brings a short warp. F11 or K to open; Esc to leave; Space,
  ←/→, N/P work there.
- **Smooth** — the lyrics (in Now Playing and full screen) and the space scene
  are drawn by the graphics card (WPF, built into Windows), so they glide
  instead of stuttering. If that can't start, AURA falls back to its older
  drawing by itself. Covers are prepared once per size, so lists scroll smoothly.
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
- **Find a cover** — searches Apple Music, Deezer, Genius (which has art for many unreleased songs) and MusicBrainz; use it for one song or
  the whole album, or choose a picture file.
- **Fill in blanks from file and folder names** — Music/Artist/Album (2020)/05 -
  Song.mp3 fills in the artist, album, year, number and title. Also: "03 - Juice WRLD - Robbery (v2).mp3"
  gives track 3, artist and title. Only empty boxes are filled.
- **Rename files** — from a pattern like {artist} - {title}; plays, playlists
  and favorites stay with each song.
- **Missing info** — songs with no artist, title, cover, album, year or lyrics.
- **Versions** — songs that come in more than one version (v2, CDQ, snippet…);
  "maybe versions" with the artist written differently to group in one click;
  right-click one to write clear titles (Song (v2), Song (Snippet)) or to split
  it up. Select songs → More → Group as versions to group them by hand.
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
  downloads once (base 148 MB, small 488 MB, or bigger). It can **take the
  vocals out first** (the same MDX-Net model as the first AURA, run with
  onnxruntime; 65 MB, downloads once), which hears much better over loud
  beats. After writing lyrics down it **looks online for the real words** and
  times those instead. Check the result before saving: click a line to hear
  it, − / + to move it, double-click to fix the words. Settings can also
  **time every song by itself** in the background.
- **Lyrics by hand** — right-click → Edit lyrics, or Time the lyrics by hand
  (press Space as each line starts).
- **Windows media controls** — the pop-up that shows with the volume keys
  and the lock screen show the song and cover, with play/pause, next and
  previous.
- **Taskbar buttons** — previous, play/pause and next under AURA's taskbar
  thumbnail.
- **Mini player** — a small window that stays on top (Ctrl+M or the button
  next to the microphone).
- **Stats** — listening time, plays by day or month, top songs and artists.
- **Themes** — Midnight, Neon, Sunset, Ocean, Light and Mono, or Match
  Windows (Light when Windows is set to light).
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

Not here yet: an equalizer.

## Keys

| Key | Does |
| --- | --- |
| Space | play / pause |
| Ctrl+→ / Ctrl+← | next / previous song |
| Shift+→ / Shift+← | jump 10 seconds |
| Ctrl+↑ / Ctrl+↓ | volume |
| N or Ctrl+L | Now Playing |
| K or F11 | full-screen lyrics |
| M or Ctrl+M | mini player |
| Q | Up next |
| F | favorite the song that's playing |
| 1–5, 0 | rate it, clear the rating |
| B | bookmark this moment |
| [ ] \\ | loop start, loop end, loop off |
| / or Ctrl+F | search |
| Ctrl+Z | undo the last change |
| Alt+← / Alt+→ | back / forward |
| Enter | play the selected song |
| Delete | remove from a playlist or Up next |

The single-letter keys work when you're not typing in a box.
