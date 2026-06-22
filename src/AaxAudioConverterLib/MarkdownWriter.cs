using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using audiamus.aux.ex;
using static audiamus.aux.Logging;

namespace audiamus.aaxconv.lib {

  // One transcribed chapter (or, in time-split mode, one track) with its paragraphs.
  class TranscriptChapter {
    public uint Number { get; set; }
    public string Title { get; set; }
    // Directory the chapter audio lives in; used for per-chapter Markdown placement.
    public string Directory { get; set; }
    // Base file name (without extension) for per-chapter Markdown.
    public string FileStub { get; set; }
    public List<string> Paragraphs { get; } = new List<string> ();

    public bool HasText => Paragraphs.Any (p => !p.IsNullOrWhiteSpace ());
  }

  // Writes the collected transcripts as Markdown, either as one self-contained file
  // per book (ideal for upload to tools like NotebookLM) or as one file per chapter.
  class MarkdownWriter {
    const string EXT_MD = ".md";

    private readonly Book _book;
    private readonly IConvSettings _settings;
    private readonly IResources _resources;
    private IResources R => _resources;

    public MarkdownWriter (Book book, IConvSettings settings, IResources resources) {
      _book = book;
      _settings = settings;
      _resources = resources;
    }

    public void Write (IReadOnlyList<TranscriptChapter> chapters, Func<Book, string> nameFunc) {
      if (chapters is null || chapters.Count == 0)
        return;

      if (_settings.TranscriptionMarkdown == ETranscriptionMarkdown.perChapter)
        writePerChapter (chapters);
      else
        writePerBook (chapters, nameFunc);
    }

    private void writePerBook (IReadOnlyList<TranscriptChapter> chapters, Func<Book, string> nameFunc) {
      if (nameFunc is null || _book.OutDirectoryLong is null)
        return;

      string filename = $"{nameFunc (_book)}{EXT_MD}";
      string path = Path.Combine (_book.OutDirectoryLong, filename);
      Log (3, this, () => $"\"{path.SubstitUser ()}\"");

      try {
        using (var osm = new StreamWriter (path, false, new UTF8Encoding (false))) {
          writeBookHeader (osm);
          foreach (var ch in chapters) {
            if (!ch.HasText)
              continue;
            osm.WriteLine ($"## {chapterHeading (ch)}");
            osm.WriteLine ();
            writeParagraphs (osm, ch);
          }
        }
      } catch (Exception exc) {
        Log (1, this, () => exc.ToShortString ());
      }
    }

    private void writePerChapter (IReadOnlyList<TranscriptChapter> chapters) {
      foreach (var ch in chapters) {
        if (!ch.HasText || ch.Directory is null)
          continue;

        string stub = ch.FileStub.IsNullOrWhiteSpace () ? ch.Number.ToString ("D3") : ch.FileStub;
        string path = Path.Combine (ch.Directory, $"{stub}{EXT_MD}");
        Log (3, this, () => $"\"{path.SubstitUser ()}\"");

        try {
          using (var osm = new StreamWriter (path, false, new UTF8Encoding (false))) {
            osm.WriteLine ($"# {chapterHeading (ch)}");
            osm.WriteLine ();
            osm.WriteLine ($"*{_book.TitleTag} — {_book.AuthorTag}*");
            osm.WriteLine ();
            writeParagraphs (osm, ch);
          }
        } catch (Exception exc) {
          Log (1, this, () => exc.ToShortString ());
        }
      }
    }

    private void writeBookHeader (StreamWriter osm) {
      var afi = _book.Parts.FirstOrDefault ()?.AaxFileItem;
      double durationSec = _book.Parts.SelectMany (p => p.Tracks).Select (t => t.Time.Duration.TotalSeconds).Sum ();
      var duration = TimeSpan.FromSeconds (durationSec);

      osm.WriteLine ($"# {_book.TitleTag}");
      osm.WriteLine ();
      osm.WriteLine ($"- **{R.HdrAuthor}**: {_book.AuthorTag}");
      if (!(afi?.Narrator).IsNullOrWhiteSpace ())
        osm.WriteLine ($"- **{R.HdrNarrator}**: {afi.Narrator}");
      osm.WriteLine ($"- **{R.HdrDuration}**: {duration.ToStringHMS ()}");
      osm.WriteLine ();
    }

    private static void writeParagraphs (StreamWriter osm, TranscriptChapter ch) {
      foreach (var para in ch.Paragraphs) {
        if (para.IsNullOrWhiteSpace ())
          continue;
        osm.WriteLine (para.Trim ());
        osm.WriteLine ();
      }
    }

    private static string chapterHeading (TranscriptChapter ch) {
      if (!ch.Title.IsNullOrWhiteSpace ())
        return $"{ch.Number}. {ch.Title}";
      return ch.Number.ToString ();
    }
  }
}
