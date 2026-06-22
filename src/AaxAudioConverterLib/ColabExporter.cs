using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using audiamus.aux.ex;
using static audiamus.aux.Logging;

namespace audiamus.aaxconv.lib {

  // Instead of transcribing locally, packages a book's audio (down-mixed to 16 kHz mono wav)
  // plus a manifest.json into the Colab export folder. The user syncs that folder to Google
  // Drive; the Colab notebook (faster-whisper, GPU) reads the manifest, transcribes and writes
  // the Markdown back next to the audio. See tools/colab/.
  class ColabExporter {

    #region manifest DTOs
    class Manifest {
      public string schema = "aaxconv-colab/1";
      public string title;
      public string author;
      public string narrator;
      public string language;            // "en" | "ja"
      public string markdown;            // "perBook" | "perChapter"
      public bool filterBoilerplate;     // apply text-level Audible boilerplate filter in Colab
      public bool audioPreTrimmed;       // intro/outro seconds already removed from the wav files
      public uint skipIntroSec;          // informational (already applied when audioPreTrimmed)
      public uint skipOutroSec;          // informational (already applied when audioPreTrimmed)
      public List<ChapterEntry> chapters = new List<ChapterEntry> ();
    }

    class ChapterEntry {
      public uint number;
      public string title;
      public string audio;               // relative path, e.g. "audio/001.wav"
      public bool isFirst;
      public bool isLast;
    }
    #endregion

    const string AUDIO_SUBDIR = "audio";
    const string MANIFEST = "manifest.json";
    const string WAV_EXT = ".wav";

    private readonly Book _book;
    private readonly IConvSettings _settings;
    private readonly IResources _resources;
    private readonly Func<bool> _cancel;

    public ColabExporter (Book book, IConvSettings settings, IResources resources, Func<bool> cancel) {
      _book = book;
      _settings = settings;
      _resources = resources;
      _cancel = cancel;
    }

    public void Export (string ffmpegExePath, Func<Book, string> nameFunc) {
      string root = _settings.ColabExportDirectory;
      if (root.IsNullOrWhiteSpace () || !Directory.Exists (root)) {
        Log (1, this, () => $"Colab export directory not set or missing: \"{root.SubstitUser ()}\". Export skipped.");
        return;
      }

      var allTracks = _book.Parts.SelectMany (p => p.Tracks).ToList ();
      if (allTracks.Count == 0)
        return;

      string bookDir = Path.Combine (root, nameFunc (_book));
      string audioDir = Path.Combine (bookDir, AUDIO_SUBDIR);

      Log (3, this, () => $"\"{bookDir.SubstitUser ()}\", #tracks={allTracks.Count}");

      try {
        Directory.CreateDirectory (audioDir);
      } catch (Exception exc) {
        Log (1, this, () => exc.ToShortString ());
        return;
      }

      string lang = _settings.TranscriptionLanguage == ETranscriptionLanguage.japanese ? "ja" : "en";
      bool filter = _settings.TranscriptionFilterBoilerplate;
      var introSkip = TimeSpan.FromSeconds (_settings.TranscriptionSkipIntroSec);
      var outroSkip = TimeSpan.FromSeconds (_settings.TranscriptionSkipOutroSec);

      var afi = _book.Parts.FirstOrDefault ()?.AaxFileItem;
      var manifest = new Manifest {
        title = _book.TitleTag,
        author = _book.AuthorTag,
        narrator = afi?.Narrator,
        language = lang,
        markdown = _settings.TranscriptionMarkdown.ToString (),
        filterBoilerplate = filter,
        audioPreTrimmed = filter,
        skipIntroSec = _settings.TranscriptionSkipIntroSec,
        skipOutroSec = _settings.TranscriptionSkipOutroSec
      };

      var whisper = new Whisper ();
      Track firstTrack = allTracks.First ();
      Track lastTrack = allTracks.Last ();
      int index = 0;

      foreach (var part in _book.Parts) {
        foreach (var track in part.Tracks) {
          if (_cancel?.Invoke () ?? false)
            return;

          index++;
          string wavName = index.ToString ("D3") + WAV_EXT;
          string wavPath = Path.Combine (audioDir, wavName);

          TimeSpan? skipStart = null;
          TimeSpan? keepDuration = null;
          if (filter) {
            if (ReferenceEquals (track, firstTrack) && introSkip > TimeSpan.Zero)
              skipStart = introSkip;
            if (ReferenceEquals (track, lastTrack) && outroSkip > TimeSpan.Zero) {
              TimeSpan keep = track.Time.Duration - outroSkip - (skipStart ?? TimeSpan.Zero);
              if (keep > TimeSpan.Zero)
                keepDuration = keep;
            }
          }

          bool ok = whisper.MakeWav (ffmpegExePath, track.FileName, wavPath, skipStart, keepDuration);
          if (!ok) {
            Log (2, this, () => $"wav export failed for \"{track.FileName.SubstitUser ()}\"");
            continue;
          }

          uint number = track.Chapter is null ? (uint)index : _book.ChapterNumber (track.Chapter);
          if (number == 0)
            number = (uint)index;

          manifest.chapters.Add (new ChapterEntry {
            number = number,
            title = track.Chapter?.Name,
            audio = $"{AUDIO_SUBDIR}/{wavName}",
            isFirst = ReferenceEquals (track, firstTrack),
            isLast = ReferenceEquals (track, lastTrack)
          });
        }
      }

      if (_cancel?.Invoke () ?? false)
        return;

      try {
        string json = JsonConvert.SerializeObject (manifest, Formatting.Indented);
        File.WriteAllText (Path.Combine (bookDir, MANIFEST), json, new UTF8Encoding (false));
      } catch (Exception exc) {
        Log (1, this, () => exc.ToShortString ());
      }
    }
  }
}
