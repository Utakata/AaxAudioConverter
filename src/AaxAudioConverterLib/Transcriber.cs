using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using audiamus.aux.ex;
using static audiamus.aux.Logging;

namespace audiamus.aaxconv.lib {

  // Drives speech-to-text over the converted (possibly chapter-split) audio of a book
  // and hands the result to the MarkdownWriter. Audible brand boilerplate at the very
  // start/end of a title is skipped and known promo phrases are filtered out.
  class Transcriber {

    // Known Audible boilerplate spoken at the very beginning/end of a title.
    private static readonly Regex[] __boilerplate = {
      new Regex (@"this is audible", RegexOptions.IgnoreCase | RegexOptions.Compiled),
      new Regex (@"audible\.com", RegexOptions.IgnoreCase | RegexOptions.Compiled),
      new Regex (@"audible hopes", RegexOptions.IgnoreCase | RegexOptions.Compiled),
      new Regex (@"audible original", RegexOptions.IgnoreCase | RegexOptions.Compiled),
      new Regex (@"audible studios", RegexOptions.IgnoreCase | RegexOptions.Compiled),
      new Regex (@"brought to you by audible", RegexOptions.IgnoreCase | RegexOptions.Compiled),
      new Regex (@"オーディブル", RegexOptions.Compiled),
    };

    private readonly Book _book;
    private readonly IConvSettings _settings;
    private readonly IResources _resources;
    private readonly Func<bool> _cancel;

    public Transcriber (Book book, IConvSettings settings, IResources resources, Func<bool> cancel) {
      _book = book;
      _settings = settings;
      _resources = resources;
      _cancel = cancel;
    }

    public void Run (string ffmpegExePath, Func<Book, string> nameFunc) {
      var whisper = new Whisper { Cancel = _cancel };
      if (!whisper.IsAvailable) {
        Log (1, this, () => $"Whisper not available: exe=\"{whisper.WhisperExePath.SubstitUser ()}\", " +
          $"model=\"{whisper.ModelPath.SubstitUser ()}\". Transcription skipped.");
        return;
      }

      string lang = _settings.TranscriptionLanguage == ETranscriptionLanguage.japanese ? "ja" : "en";
      bool filter = _settings.TranscriptionFilterBoilerplate;
      var introSkip = TimeSpan.FromSeconds (_settings.TranscriptionSkipIntroSec);
      var outroSkip = TimeSpan.FromSeconds (_settings.TranscriptionSkipOutroSec);

      var allTracks = _book.Parts.SelectMany (p => p.Tracks).ToList ();
      if (allTracks.Count == 0)
        return;

      Log (3, this, () => $"\"{_book.SortingTitle.Shorten ()}\", #tracks={allTracks.Count}, lang={lang}");

      Track firstTrack = allTracks.First ();
      Track lastTrack = allTracks.Last ();

      var entriesByChapter = new Dictionary<Chapter, TranscriptChapter> ();
      var entries = new List<TranscriptChapter> ();
      uint runningNumber = 0;

      foreach (var part in _book.Parts) {
        foreach (var track in part.Tracks) {
          if (_cancel?.Invoke () ?? false)
            return;

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

          string text = whisper.Transcribe (ffmpegExePath, track.FileName, lang, skipStart, keepDuration);
          if (filter)
            text = filterBoilerplate (text);

          var entry = getOrCreateEntry (entriesByChapter, entries, track, ref runningNumber);
          if (!text.IsNullOrWhiteSpace ())
            entry.Paragraphs.Add (text);
        }
      }

      if (_cancel?.Invoke () ?? false)
        return;

      var writer = new MarkdownWriter (_book, _settings, _resources);
      writer.Write (entries, nameFunc);
    }

    private TranscriptChapter getOrCreateEntry (
      Dictionary<Chapter, TranscriptChapter> map, List<TranscriptChapter> entries,
      Track track, ref uint runningNumber) {

      Chapter chapter = track.Chapter;
      if (!(chapter is null) && map.TryGetValue (chapter, out var existing))
        return existing;

      runningNumber++;

      string dir = null;
      string stub = null;
      try {
        dir = Path.GetDirectoryName (track.FileName);
        stub = Path.GetFileNameWithoutExtension (track.FileName);
      } catch (Exception exc) {
        Log (3, this, () => exc.ToShortString ());
      }

      uint number = chapter is null ? runningNumber : _book.ChapterNumber (chapter);
      if (number == 0)
        number = runningNumber;

      var entry = new TranscriptChapter {
        Number = number,
        Title = chapter?.Name,
        Directory = dir,
        FileStub = stub
      };

      if (!(chapter is null))
        map[chapter] = entry;
      entries.Add (entry);
      return entry;
    }

    private static string filterBoilerplate (string text) {
      if (text.IsNullOrWhiteSpace ())
        return text;

      var lines = text.Replace ("\r", string.Empty).Split ('\n');
      var kept = lines
        .Select (l => l.Trim ())
        .Where (l => !l.IsNullOrWhiteSpace ())
        .Where (l => !__boilerplate.Any (rx => rx.IsMatch (l)));

      return string.Join (" ", kept);
    }
  }
}
