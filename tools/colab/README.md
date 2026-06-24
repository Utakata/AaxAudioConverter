# Google Colab transcription

Offload the speech-to-text to **Google Colab's free GPU** instead of the local machine,
and get a Markdown transcript per book that you can drop straight into **NotebookLM**.

This is the *Google Colab export* engine on the Transcription tab in AAX Audio Converter.
The app only **exports** audio + a manifest; the GPU work happens in
[`aax_transcribe.ipynb`](aax_transcribe.ipynb).

## One-time setup

1. Install **Google Drive for Desktop** and sign in.
2. Create a folder inside your Google Drive, e.g. `MyDrive/AaxColab`.

## Each time

1. In AAX Audio Converter → *Settings* → **Transcription** tab:
   - Check **Transcribe audio to text**.
   - **Engine** → *Google Colab export*.
   - **Colab export folder** → the Drive-synced folder (e.g. the local path of `MyDrive/AaxColab`).
   - Pick **Language**, **Markdown** granularity (per book / per chapter), and the intro/outro
     skip + boilerplate options (these are written into the manifest).
2. Convert your book(s). For each book the app writes:

   ```
   <export>/<Author - Title>/
     audio/001.wav, 002.wav, ...   (16 kHz mono, intro/outro already trimmed)
     manifest.json
   ```

3. Wait for Google Drive for Desktop to finish uploading.
4. Open [`aax_transcribe.ipynb`](aax_transcribe.ipynb) in Colab
   (*Runtime → Change runtime type → GPU*), set `EXPORT_ROOT` to your Drive folder
   (e.g. `/content/drive/MyDrive/AaxColab`), then **Runtime → Run all**.
5. The notebook writes `<Author - Title>.md` (or one `NNN.md` per chapter) into each book
   folder and a `.done` marker. These sync back to your PC via Drive.
6. Import the `.md` into NotebookLM.

## Notes

- The exported wav is already trimmed for the configured intro/outro seconds; the notebook
  applies only the **text** boilerplate filter (`filterBoilerplate` in the manifest).
- The boilerplate phrase list and Markdown layout are mirrored from the local engine
  (`AaxAudioConverterLib/Transcriber.cs` and `MarkdownWriter.cs`). Keep them in sync if changed.
- Re-running is cheap: books with a `.done` marker are skipped unless `FORCE_REPROCESS = True`.
- Colab free tier disconnects after ~90 min idle / 12 h; run interactively (this design is
  manual/semi-automatic by intent).

## manifest.json

```json
{
  "schema": "aaxconv-colab/1",
  "title": "…", "author": "…", "narrator": "…",
  "language": "en",            // or "ja"
  "markdown": "perBook",       // or "perChapter"
  "filterBoilerplate": true,
  "audioPreTrimmed": true,
  "skipIntroSec": 10, "skipOutroSec": 8,
  "chapters": [
    { "number": 1, "title": "…", "audio": "audio/001.wav", "isFirst": true, "isLast": false }
  ]
}
```
