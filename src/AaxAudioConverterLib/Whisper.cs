using System;
using System.IO;
using System.Text;
using audiamus.aux;
using audiamus.aux.ex;
using static audiamus.aux.Logging;

namespace audiamus.aaxconv.lib {

  // Wrapper around the local whisper.cpp command line tool ("whisper-cli.exe",
  // formerly "main.exe"). The audio is first down-mixed to the 16 kHz mono PCM wav
  // format required by whisper.cpp using the bundled FFmpeg, then transcribed to
  // plain text.
  public class Whisper : ProcessHost {

    #region const
    public const string WHISPER_EXE = "whisper-cli.exe";
    public const string WHISPER_EXE_LEGACY = "main.exe";
    public const string WHISPER_MODEL = "ggml-base.bin";
    public const string WHISPER_SUBDIR = "whisper";

    const string INPUT = "<INPUT>";
    const string OUTPUT = "<OUTPUT>";
    const string MODEL = "<MODEL>";
    const string LANG = "<LANG>";
    const string OUTPREFIX = "<OUTPREFIX>";
    const string SS = "<SS>";
    const string TT = "<TT>";

    // FFmpeg: down-mix to 16 kHz mono PCM wav as required by whisper.cpp
    const string FFMPEG_TO_WAV = @"-hide_banner -y <SS> -i ""<INPUT>"" <TT> -vn -ar 16000 -ac 1 -c:a pcm_s16le ""<OUTPUT>""";
    const string SS_PARAM = @"-ss <SSV>";
    const string TT_PARAM = @"-t <TTV>";

    // whisper.cpp cli: transcribe wav to plain text without timestamps
    const string WHISPER_TRANSCRIBE = @"-m ""<MODEL>"" -l <LANG> -nt -np -otxt -of ""<OUTPREFIX>"" -f ""<INPUT>""";
    #endregion

    #region props
    public static Func<string> GetWhisperDir { get; set; }

    internal Func<bool> Cancel { private get; set; }

    private bool _error;
    public bool TranscriptionError => _error;

    private string WhisperDir {
      get {
        string dir = GetWhisperDir?.Invoke ();
        if (dir.IsNullOrWhiteSpace () || !Directory.Exists (dir))
          dir = Path.Combine (ApplEnv.ApplDirectory, WHISPER_SUBDIR);
        return dir;
      }
    }

    public string WhisperExePath {
      get {
        string dir = WhisperDir;
        string path = Path.Combine (dir, WHISPER_EXE);
        if (!File.Exists (path)) {
          string legacy = Path.Combine (dir, WHISPER_EXE_LEGACY);
          if (File.Exists (legacy))
            path = legacy;
        }
        return path;
      }
    }

    public string ModelPath {
      get {
        string dir = WhisperDir;
        string path = Path.Combine (dir, WHISPER_MODEL);
        if (File.Exists (path))
          return path;
        // fall back to the first ggml model present in the folder
        if (Directory.Exists (dir)) {
          var models = Directory.GetFiles (dir, "ggml-*.bin");
          if (models.Length > 0)
            return models[0];
        }
        return path;
      }
    }

    public bool IsAvailable => File.Exists (WhisperExePath) && File.Exists (ModelPath);
    #endregion

    #region public methods

    // Transcribe a single audio file to plain text.
    // skipStart    optionally skips the leading seconds (e.g. brand intro of the first track).
    // keepDuration optionally limits the duration (e.g. to drop the brand outro of the last track).
    public string Transcribe (
      string ffmpegExePath, string audioFile, string languageCode,
      TimeSpan? skipStart = null, TimeSpan? keepDuration = null) {

      _error = false;

      if (audioFile.IsNullOrWhiteSpace () || !File.Exists (audioFile)) {
        Log (1, this, () => $"audio file not found: \"{audioFile.SubstitUser ()}\"");
        _error = true;
        return null;
      }

      if (Cancel?.Invoke () ?? false)
        return null;

      string baseName = "whisper_" + Guid.NewGuid ().ToString ("N");
      string wavFile = Path.Combine (ApplEnv.TempDirectory, baseName + ".wav");
      string outPrefix = Path.Combine (ApplEnv.TempDirectory, baseName);
      string txtFile = outPrefix + ".txt";

      try {
        if (!MakeWav (ffmpegExePath, audioFile, wavFile, skipStart, keepDuration)) {
          _error = true;
          return null;
        }

        if (Cancel?.Invoke () ?? false)
          return null;

        string param = WHISPER_TRANSCRIBE
          .Replace (MODEL, ModelPath)
          .Replace (LANG, languageCode)
          .Replace (OUTPREFIX, outPrefix)
          .Replace (INPUT, wavFile);

        Log (4, this, () => param.SubstitUser ());
        runProcess (WhisperExePath, param, false, null);

        if (!File.Exists (txtFile)) {
          Log (2, this, () => $"no transcript produced for \"{audioFile.SubstitUser ()}\"");
          _error = true;
          return null;
        }

        string text = File.ReadAllText (txtFile, Encoding.UTF8);
        return text;

      } catch (Exception exc) {
        Log (1, this, () => exc.ToShortString ());
        _error = true;
        return null;
      } finally {
        tryDelete (wavFile);
        tryDelete (txtFile);
      }
    }
    #endregion

    // Down-mix any audio file to the 16 kHz mono PCM wav whisper.cpp/faster-whisper expect.
    // Also reused by ColabExporter to package audio for remote transcription.
    #region helpers
    public bool MakeWav (string ffmpegExePath, string audioFile, string wavFile, TimeSpan? skipStart = null, TimeSpan? keepDuration = null) {
      string param = FFMPEG_TO_WAV;

      if (skipStart.HasValue && skipStart.Value > TimeSpan.Zero) {
        param = param.Replace (SS, SS_PARAM);
        param = param.Replace ("<SSV>", skipStart.Value.TotalSeconds.ToString ("f3"));
      } else
        param = param.Replace (SS, string.Empty);

      if (keepDuration.HasValue && keepDuration.Value > TimeSpan.Zero) {
        param = param.Replace (TT, TT_PARAM);
        param = param.Replace ("<TTV>", keepDuration.Value.TotalSeconds.ToString ("f3"));
      } else
        param = param.Replace (TT, string.Empty);

      param = param.Replace (INPUT, audioFile);
      param = param.Replace (OUTPUT, wavFile);

      Log (4, this, () => param.SubstitUser ());
      runProcess (ffmpegExePath, param, true, null);

      return File.Exists (wavFile);
    }

    private void tryDelete (string path) {
      try {
        if (File.Exists (path))
          File.Delete (path);
      } catch (Exception exc) {
        Log (3, this, () => exc.ToShortString ());
      }
    }
    #endregion
  }
}
