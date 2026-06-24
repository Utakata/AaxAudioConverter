using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using audiamus.aaxconv.lib;
using audiamus.aaxconv.lib.ex;
using audiamus.aux;
using audiamus.aux.ex;
using audiamus.aux.win;
using static audiamus.aux.ApplEnv;

namespace audiamus.aaxconv {
  using R = Properties.Resources;

  partial class SettingsForm : Form {
    public const string PART = "Part";

    private readonly IAppSettings _settings = Properties.Settings.Default;
    private readonly AaxAudioConverter _converter;
    private readonly Func<InteractionMessage, bool?> _callback;
    private bool _flag;
    private bool _enabled = true;
    private readonly string _title; 
    private bool _flagResetUpdateOthers;

    private ComboBoxEnumAdapter<EAaxCopyMode> _cbAdapterAaxCopyMode;

    // Transcription tab (built programmatically, see initTranscriptionTab)
    private CheckBox _ckBoxBookFolderForChapterSplit;
    private CheckBox _ckBoxTranscription;
    private ComboBox _comBoxTranscrEngine;
    private Button _btnColabDir;
    private Label _lblColabDir;
    private ComboBox _comBoxTranscrLanguage;
    private ComboBox _comBoxTranscrMarkdown;
    private NumericUpDown _nudTranscrIntro;
    private NumericUpDown _nudTranscrOutro;
    private CheckBox _ckBoxTranscrBoilerplate;
    private Button _btnWhisperLoc;
    private Label _lblWhisperLoc;

    public bool SettingsReset { get; private set; }
    public bool Dirty { get; private set; }
    public bool ListViewDirty { get; private set; }

    public new bool Enabled {
      get => _enabled;
      set
      {
        if (value == Enabled)
          return;
        _enabled = value;
        tabControl1.Enabled = _enabled;
        tabControl1.DrawMode = _enabled ? TabDrawMode.Normal : TabDrawMode.OwnerDrawFixed;
        btnReset.Enabled = _enabled;
        btnOK.Enabled = _enabled;
      }
    }

    private IAppSettings Settings => _settings;

    public SettingsForm (AaxAudioConverter converter, Func<InteractionMessage, bool?> callback) {
      using (new ResourceGuard (x => _flag = x))
        InitializeComponent ();

      _title = this.Text;
      _converter = converter;
      _callback = callback;

      initPartNaming ();
      initFlatFoldersNaming ();
      initReducedBitrate ();
      initTranscriptionTab ();
      initControlsFromSettings ();
    }


    protected override void OnLoad (EventArgs e) {
      base.OnLoad (e);
      this.Text = $"{Owner?.Text}: {_title}";
    }

    protected override void OnKeyDown (KeyEventArgs e) {
      if (e.Modifiers == Keys.Control)
        switch (e.KeyCode) {
          case Keys.A:
            selectAll ();
            break;
          case Keys.C:
            copySelectionToClipboard ();
            break;
          default:
            base.OnKeyDown (e);
            break;
        } else
        base.OnKeyDown (e);
    }

    private void initPartNaming () {
      var rm = R.ResourceManager; // this.GetDefaultResourceManager ();
      var enums = EnumUtil.GetValues<EGeneralNaming> ();
      var data = enums.Select (e => e.ToDisplayString<EGeneralNaming, ChainPunctuationBracket> (rm)).ToArray ();
      using (new ResourceGuard (x => _flag = x))
        comBoxPartName.DataSource = data;
      //txtBoxPartName.DataBindings.Add (nameof (txtBoxPartName.Text), Settings, nameof (Settings.PartName));
      txtBoxPartName.Text = Settings.PartName;
    }

    private void initReducedBitrate () {
      var bitrates = EnumUtil.GetValues<EReducedBitRate> ();
      string defval = comBoxRedBitRate.Items[0] as string;
      comBoxRedBitRate.Items.Clear ();
      foreach (var ebitrate in bitrates) {
        uint bitrate = ebitrate.UInt32 ();
        if (bitrate == 0)
          comBoxRedBitRate.Items.Add (defval);
        else {
          string s = $"{bitrate} kb/s max";
          comBoxRedBitRate.Items.Add (s);
        }
      }
    }

    private void updatePartNaming () {
      var rm = R.ResourceManager; // this.GetDefaultResourceManager ();
      var partNaming = (EGeneralNaming)comBoxPartName.SelectedIndex;
      string standardPrefix = rm.GetStringEx (PART);
      using (new ResourceGuard (x => _flag = x))
        if (partNaming != EGeneralNaming.custom)
          txtBoxPartName.Text = standardPrefix;
        else
          txtBoxPartName.Text = string.IsNullOrWhiteSpace (Settings.PartName) ? standardPrefix : Settings.PartName;
    }

    private void initFlatFoldersNaming () {
      var rm = this.GetDefaultResourceManager ();
      var enums = EnumUtil.GetValues<EFlatFolderNaming> ();
      var data = enums.Select (e => e.ToDisplayString<EFlatFolderNaming, ChainPunctuationDash> (rm)).ToArray ();
      using (new ResourceGuard (x => _flag = x))
        comBoxFlatFolders.DataSource = data;
    }

    private void initControlsFromSettings () {
      tabControl1.SelectedIndex = Settings.SettingsTab;

      initControlsFromSettingsGeneral ();
      initControlsFromSettingsFolder ();
      initControlsFromSettingsConversion ();
      initControlsFromSettingsChapters ();
      intoControlsFromSettingsMetaTags ();
      initControlsFromSettingsTranscription ();
    }

    private void initControlsFromSettingsGeneral () {
      // tab page General

      ckBoxFfmpegVersCheck.Checked = Settings.RelaxedFFmpegVersionCheck;
      
      var codes = _converter.NumericActivationCodes?.Select (c => c.ToHexDashString ()).ToArray ();
      if (!(codes is null))
        listBoxActCode.Items.AddRange (codes);

      ckBoxFileAssoc.Checked = Settings.FileAssoc ?? false;

      ckBoxDateClm.Checked = Settings.FileDateColumn;

      btnAaxCopyDir.Enabled = Settings.AaxCopyMode != default;
      using (new ResourceGuard (x => _flag = x))
        _cbAdapterAaxCopyMode =
          new ComboBoxEnumAdapter<EAaxCopyMode> (comBoxAaxCopy, this.GetDefaultResourceManager (), Settings.AaxCopyMode);

      ckBoxLaunchPlayer.Checked = Settings.AutoLaunchPlayer;

      comBoxUpdate.SelectedIndex = (int)Settings.OnlineUpdate;

      comBoxLang.SetCultures (typeof (MainForm), Settings);
    }

    private void initControlsFromSettingsFolder () {
      // tab page Folder

      ckBoxFlatFolders.Checked = Settings.FlatFolders;      
      enableFlatFoldersDependencies (Settings.FlatFolders);
      using (new ResourceGuard (x => _flag = x))
        comBoxFlatFolders.SelectedIndex = (int)Settings.FlatFolderNaming;

      ckBoxSeries.Checked = Settings.WithSeriesTitle;
      panelSeriesDigits.Enabled = Settings.WithSeriesTitle;
      numUpDnSeriesDigits.Value = Settings.NumDigitsSeriesSeqNo;

      ckBoxFullCaptionBookFolder.Checked = Settings.FullCaptionBookFolder;

      using (new ResourceGuard (x => _flag = x))
        comBoxPartName.SelectedIndex = (int)Settings.PartNaming;
      txtBoxPartName.Enabled = Settings.PartNaming == EGeneralNaming.custom;    
      updatePartNaming ();

      using (new ResourceGuard (x => _flag = x))
        comBoxOutFolderConflict.SelectedIndex = (int)Settings.OutFolderConflict;

    }

    private void initControlsFromSettingsConversion () {

      // tab page Conversion

      txtBoxCustPart.Text = Settings.PartNames;
      
      txtBoxCustTitleChars.Text = Settings.AddnlValTitlePunct;
      
      ckBoxIntermedCopySingle.Checked = Settings.IntermedCopySingle;
      
      using (new ResourceGuard (x => _flag = x))
        comBoxFixAacEncoding.SelectedIndex = (int)Settings.FixAACEncoding;

      ckBoxVarBitRate.Checked = Settings.VariableBitRate;
      
      using (new ResourceGuard (x => _flag = x))
        comBoxRedBitRate.SelectedIndex = (int)Settings.ReducedBitRate;

      comBoxM4B.SelectedIndex = Settings.M4B ? 1 : 0;

      ckBoxLatin1.Checked = Settings.Latin1EncodingForPlaylist;
      
      ckBoxExtraMetaFiles.Checked = Settings.ExtraMetaFiles;
    }

    private void initControlsFromSettingsChapters () {
      // tab page Chapters

      comBoxNamedChapters.SelectedIndex = (int)Settings.NamedChapters;
      enablePreferEmbeddedChapterTimes ();
      
      nudShortChapter.Value = Settings.ShortChapterSec;
      nudVeryShortChapter.Value = Settings.VeryShortChapterSec;

      comBoxVerAdjChapters.SelectedIndex = (int)Settings.VerifyAdjustChapterMarks;
      comBoxPrefEmbChapTimes.SelectedIndex = (int)Settings.PreferEmbeddedChapterTimes;
    }

    private void intoControlsFromSettingsMetaTags () {
      // tab page Tags

      comBoxArtist.SelectedIndex = indexOfRole (Settings.TagArtist);
      comBoxAlbumArtist.SelectedIndex = indexOfRole (Settings.TagAlbumArtist);
      comBoxComposer.SelectedIndex = indexOfRole (Settings.TagComposer);
      comBoxConductor.SelectedIndex = indexOfRole (Settings.TagConductor);

      Settings.Narrator = null;
    }

    private int indexOfRole (ERoleTagAssignment role) {
      bool narrator = Settings.Narrator ?? false;
      switch (role) {
        default:
        case ERoleTagAssignment.none:
          return 0;
        case ERoleTagAssignment.author:
          return 1;
        case ERoleTagAssignment.author__narrator__:
          if (narrator)
            return 2;
          else
            return 1;
        case ERoleTagAssignment.author_narrator:
          return 2;
        case ERoleTagAssignment.__narrator__:
          if (narrator)
            return 3;
          else
            return 0;
        case ERoleTagAssignment.narrator:
          return 3;
      }
    }

    private ERoleTagAssignment roleOfIndex (int index) {
      switch (index) {
        default:
        case 0: 
          return ERoleTagAssignment.none;
        case 1:
          return ERoleTagAssignment.author;
        case 2:
          return ERoleTagAssignment.author_narrator;
        case 3:
          return ERoleTagAssignment.narrator;
      }
    }

    private void selectAll () {
      for (int i = 0; i < listBoxActCode.Items.Count; i++)
        listBoxActCode.SetSelected (i, true);
    }

    private void copySelectionToClipboard () {
      try {
        var sb = new StringBuilder ();
        foreach (object row in listBoxActCode.SelectedItems) {
          if (row is string s) {
            if (string.IsNullOrWhiteSpace (s))
              continue;
            if (sb.Length > 0)
              sb.AppendLine ();
            sb.Append (s);
          }
        }
        Clipboard.SetData (DataFormats.Text, sb.ToString ());
      } catch (Exception) { }
    }
       
    private void txtBoxCustPart_Leave (object sender, EventArgs e) {
      string partNames = txtBoxCustPart.Text.SplitTrim (new char[] {' ', ';', ','}).Combine();
      txtBoxCustPart.Text = partNames;
    }

    private static readonly Regex _rgxWord = new Regex (@"[\w\s]", RegexOptions.Compiled);

    private void txtBoxCustTitleChars_TextChanged (object sender, EventArgs e) {
      if (_flag)
        return;

      string s = txtBoxCustTitleChars.Text;
      var match = _rgxWord.Match (s);
      if (match.Success)
        s = s.Remove (match.Index, 1);

      char[] chars = s.ToCharArray ();
      chars = chars.Distinct ().ToArray();
      using (new ResourceGuard (x => _flag = x)) 
        txtBoxCustTitleChars.Text = new string (chars);
      txtBoxCustTitleChars.SelectionStart = txtBoxCustTitleChars.Text.Length;
      txtBoxCustTitleChars.SelectionLength = 0;
    }


    private void btnUsrActCode_Click (object sender, EventArgs e) {
      var result = new ActivationCodeForm () { Owner = this }.ShowDialog ();
      if (result == DialogResult.OK)
        _converter.ReinitActivationCode ();
    }

    private void btnRegActCode_Click (object sender, EventArgs e) {
      listBoxActCode.Visible = !listBoxActCode.Visible;
      btnRegActCode.Text = listBoxActCode.Visible ? R.CptHide : R.CptShow;
    }

    private void btnFfmpegLoc_Click (object sender, EventArgs e) {
      string oldSetting = _settings.FFMpegDirectory;
      var dlg = new FFmpegLocationForm (_converter, _callback) { Owner = this };
      dlg.ShowDialog ();
      string newSetting = _settings.FFMpegDirectory;
      Dirty |= string.Equals (newSetting, newSetting);
    }

    private void btnResetOnlineUpdOthers_Click (object sender, EventArgs e) {
      _flagResetUpdateOthers = true;
    }

    private void btnReset_Click (object sender, EventArgs e) {
      if (MsgBox.Show (this, R.MsgResetAllSettings, 
        this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        return;
      if (MsgBox.Show (this, R.MsgAllModifLost, 
        this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        return;

      DefaultSettings.ResetToDefault (Properties.Settings.Default);
      SettingsReset = true;
      initControlsFromSettings ();

    }

    private void btnOK_Click (object sender, EventArgs e) {

      Settings.SettingsTab = tabControl1.SelectedIndex;

      updateSettingsFromControlsGeneral ();
      updateSettingsFromControlsFolder ();
      updateSettingsFromControlsConversion ();
      updateSettingsFromControlsChapters ();
      updateSettingsFromControlsMetaTags ();
      updateSettingsFromControlsTranscription ();
    }

    private void updateSettingsFromControlsGeneral () {
      // tab page General

      Settings.RelaxedFFmpegVersionCheck = updateSettings (Settings.RelaxedFFmpegVersionCheck, ckBoxFfmpegVersCheck.Checked);
      
      bool ck = ckBoxFileAssoc.Checked;
      if ((Settings.FileAssoc ?? false) != ck) {
        Settings.FileAssoc = ck;
        new FileAssoc (Settings, this).Update ();
      }

      Settings.FileDateColumn = updateSettings (Settings.FileDateColumn, ckBoxDateClm.Checked, true);

      Settings.AaxCopyMode = updateSettings (Settings.AaxCopyMode, _cbAdapterAaxCopyMode.Value);

      Settings.AutoLaunchPlayer = ckBoxLaunchPlayer.Checked;
      
      Settings.OnlineUpdate = (EOnlineUpdate)comBoxUpdate.SelectedIndex;

      if (_flagResetUpdateOthers)
        Settings.OnlineUpdateOthersDeclined = string.Empty;

      if (Culture.ChangeLanguage (comBoxLang, Settings)) {
        Settings.Save ();

        if (MsgBox.Show (this, $"{ApplName} {R.MsgLangRestart}", Owner.Text, MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
          return;

        try {
          Application.Restart ();
        } catch (Exception) { }

        Environment.Exit (0);
      }
    }

    private void updateSettingsFromControlsFolder () {
      // tab page Folder
      Settings.FlatFolders = updateSettings (Settings.FlatFolders, ckBoxFlatFolders.Checked);
      Settings.FlatFolderNaming = updateSettings (Settings.FlatFolderNaming, (EFlatFolderNaming)comBoxFlatFolders.SelectedIndex);

      Settings.WithSeriesTitle = updateSettings (Settings.WithSeriesTitle, ckBoxSeries.Checked);
      Settings.NumDigitsSeriesSeqNo = updateSettings (Settings.NumDigitsSeriesSeqNo, (byte)numUpDnSeriesDigits.Value);
      
      Settings.FullCaptionBookFolder = updateSettings (Settings.FullCaptionBookFolder, ckBoxFullCaptionBookFolder.Checked);

      Settings.PartNaming = updateSettings (Settings.PartNaming, (EGeneralNaming)comBoxPartName.SelectedIndex);
      Settings.PartName = updateSettings (Settings.PartName, txtBoxPartName.Text);

      Settings.OutFolderConflict =
        updateSettings (Settings.OutFolderConflict, (EOutFolderConflict)comBoxOutFolderConflict.SelectedIndex);
    }

    private void updateSettingsFromControlsConversion () {
      // tab page Conversion
      
      Settings.PartNames = updateSettings (Settings.PartNames, txtBoxCustPart.Text);
      
      Settings.AddnlValTitlePunct = updateSettings (Settings.AddnlValTitlePunct, txtBoxCustTitleChars.Text);
      
      Settings.IntermedCopySingle = updateSettings (Settings.IntermedCopySingle, ckBoxIntermedCopySingle.Checked);
      
      Settings.FixAACEncoding = updateSettings (Settings.FixAACEncoding, (EFixAACEncoding)comBoxFixAacEncoding.SelectedIndex);
      
      Settings.VariableBitRate = updateSettings (Settings.VariableBitRate, ckBoxVarBitRate.Checked);
      
      Settings.ReducedBitRate = updateSettings (Settings.ReducedBitRate, (EReducedBitRate)comBoxRedBitRate.SelectedIndex);
      
      Settings.M4B = updateSettings (Settings.M4B, comBoxM4B.SelectedIndex == 1);
      
      Settings.Latin1EncodingForPlaylist = updateSettings (Settings.Latin1EncodingForPlaylist, ckBoxLatin1.Checked);
      
      Settings.ExtraMetaFiles = updateSettings (Settings.ExtraMetaFiles, ckBoxExtraMetaFiles.Checked);
    }

    private void updateSettingsFromControlsChapters () {
      // tab page Chapters
      Settings.NamedChapters = updateSettings (Settings.NamedChapters, (ENamedChapters)comBoxNamedChapters.SelectedIndex);

      Settings.ShortChapterSec = updateSettings (Settings.ShortChapterSec, (uint)nudShortChapter.Value);
      
      Settings.VeryShortChapterSec = updateSettings (Settings.VeryShortChapterSec, (uint)nudVeryShortChapter.Value);
      
      Settings.VerifyAdjustChapterMarks = 
        updateSettings (Settings.VerifyAdjustChapterMarks, (EVerifyAdjustChapterMarks)comBoxVerAdjChapters.SelectedIndex);
      
      Settings.PreferEmbeddedChapterTimes = 
        updateSettings (Settings.PreferEmbeddedChapterTimes, (EPreferEmbeddedChapterTimes)comBoxPrefEmbChapTimes.SelectedIndex);
    }

    private void updateSettingsFromControlsMetaTags () {
      // tab page Meta Tags

      Settings.TagArtist = updateSettings (Settings.TagArtist, roleOfIndex (comBoxArtist.SelectedIndex));
      Settings.TagAlbumArtist = updateSettings (Settings.TagAlbumArtist, roleOfIndex (comBoxAlbumArtist.SelectedIndex));
      Settings.TagComposer = updateSettings (Settings.TagComposer, roleOfIndex (comBoxComposer.SelectedIndex));
      Settings.TagConductor = updateSettings (Settings.TagConductor, roleOfIndex(comBoxConductor.SelectedIndex));
    }

    private T updateSettings<T> (T oldValue, T newValue, bool affectsListView = false) {
      if (!object.Equals (oldValue, newValue)) {
        Dirty = true;
        if (affectsListView)
          ListViewDirty = true;
      }
      return newValue;
    }

    private void comBoxPartName_SelectedIndexChanged (object sender, EventArgs e) {
      if (_flag)
        return;
      var partNaming = (EGeneralNaming)comBoxPartName.SelectedIndex;
      txtBoxPartName.Enabled = partNaming == EGeneralNaming.custom;
      updatePartNaming ();
    }

    private void ckBoxFlatFolders_CheckedChanged (object sender, EventArgs e) {
      bool flatFolders = ckBoxFlatFolders.Checked;
      enableFlatFoldersDependencies (flatFolders);
    }

    private void enableFlatFoldersDependencies (bool flatFolders) {
      comBoxFlatFolders.Enabled = flatFolders;
      lblFullCaptionBookFolder.Enabled = !flatFolders;
      ckBoxFullCaptionBookFolder.Enabled = !flatFolders;
      lblSeries.Enabled = !flatFolders;
      panelSeries.Enabled = !flatFolders;
    }

    private void ckBoxSeries_CheckedChanged (object sender, EventArgs e) {
      bool withSeries = ckBoxSeries.Checked;
      panelSeriesDigits.Enabled = withSeries;
    }


    private void comBoxAaxCopy_SelectedIndexChanged (object sender, EventArgs e) {
      if (_flag || _cbAdapterAaxCopyMode is null)
        return;
      btnAaxCopyDir.Enabled = _cbAdapterAaxCopyMode.Value != default;
      if (btnAaxCopyDir.Enabled) {
        if (string.IsNullOrWhiteSpace (Settings.AaxCopyDirectory) || !Directory.Exists(Settings.AaxCopyDirectory))
          MsgBox.Show (this, R.MsgAaxCopyNoFolderYet, R.MsgAaxCopyFolder, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }
    }

    private void btnAaxCopyDir_Click (object sender, EventArgs e) {
      string dir = MainForm.SetDestinationDirectory (this, Settings.AaxCopyDirectory, R.Audible, this.Text, R.MsgAaxCopyFolder, R.MsgAaxCopyNoFolder);
      if (dir is null)
        return;

      Settings.AaxCopyDirectory = dir;
    }

    private void comBoxNamedChapters_SelectedIndexChanged (object sender, EventArgs e) => 
      enablePreferEmbeddedChapterTimes ();

    private void enablePreferEmbeddedChapterTimes () {
      bool useNamedChapters = comBoxNamedChapters.SelectedIndex > 0;
      lblPrefEmbChapTimes.Enabled = useNamedChapters;
      comBoxPrefEmbChapTimes.Enabled = useNamedChapters;
    }

    private void ckBoxFfmpegVersCheck_CheckedChanged (object sender, EventArgs e) {
      bool succ = _converter.VerifyFFmpegPathVersion (_callback, ckBoxFfmpegVersCheck.Checked);
      if (!succ)
        btnFfmpegLoc_Click (sender, e);
    }

    private void tabControl1_DrawItem (object sender, DrawItemEventArgs e) {
      TabPage tp = tabControl1.TabPages[e.Index];
      using (SolidBrush brush =
             new SolidBrush (Enabled ? tp.BackColor : SystemColors.ControlLight))
      using (SolidBrush textBrush =
             new SolidBrush (Enabled ? tp.ForeColor : SystemColors.ControlDark)) {
        e.Graphics.FillRectangle (brush, e.Bounds);
        e.Graphics.DrawString (tp.Text, e.Font, textBrush, e.Bounds.X + 1, e.Bounds.Y + 3);
      }
    }

    #region Transcription tab

    // The transcription tab is created in code rather than in the designer to keep the
    // resource-heavy designer/resx files untouched.
    private void initTranscriptionTab () {
      var tabPage = new TabPage {
        Name = "tabPageTranscription",
        Text = "Transcription",
        UseVisualStyleBackColor = true,
        Padding = new Padding (3)
      };

      int x = 12;
      int xIndent = 28;
      int y = 12;

      _ckBoxBookFolderForChapterSplit = new CheckBox {
        AutoSize = true,
        Location = new Point (x, y),
        Text = "Keep chapter-split tracks in one book folder (no chapter sub-folders)"
      };
      y += 32;

      _ckBoxTranscription = new CheckBox {
        AutoSize = true,
        Location = new Point (x, y),
        Text = "Transcribe audio to text"
      };
      _ckBoxTranscription.CheckedChanged += ckBoxTranscription_CheckedChanged;
      y += 30;

      var lblEngine = new Label {
        AutoSize = true,
        Location = new Point (xIndent, y + 3),
        Text = "Engine"
      };
      _comBoxTranscrEngine = new ComboBox {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Location = new Point (xIndent + 110, y),
        Width = 220
      };
      // order must match ETranscriptionEngine: localWhisper (0), colabExport (1)
      _comBoxTranscrEngine.Items.AddRange (new object[] {
        "Local (whisper.cpp)",
        "Google Colab export"
      });
      _comBoxTranscrEngine.SelectedIndexChanged += comBoxTranscrEngine_SelectedIndexChanged;
      y += 30;

      var lblLanguage = new Label {
        AutoSize = true,
        Location = new Point (xIndent, y + 3),
        Text = "Language"
      };
      _comBoxTranscrLanguage = new ComboBox {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Location = new Point (xIndent + 110, y),
        Width = 160
      };
      _comBoxTranscrLanguage.Items.AddRange (new object[] { "English", "日本語 (Japanese)" });
      y += 30;

      var lblMarkdown = new Label {
        AutoSize = true,
        Location = new Point (xIndent, y + 3),
        Text = "Markdown"
      };
      _comBoxTranscrMarkdown = new ComboBox {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Location = new Point (xIndent + 110, y),
        Width = 220
      };
      // order must match ETranscriptionMarkdown: perChapter (0), perBook (1)
      _comBoxTranscrMarkdown.Items.AddRange (new object[] {
        "One file per chapter",
        "One file per book"
      });
      y += 30;

      var lblIntro = new Label {
        AutoSize = true,
        Location = new Point (xIndent, y + 3),
        Text = "Skip intro (sec)"
      };
      _nudTranscrIntro = new NumericUpDown {
        Location = new Point (xIndent + 110, y),
        Width = 60,
        Minimum = 0,
        Maximum = 600
      };
      y += 30;

      var lblOutro = new Label {
        AutoSize = true,
        Location = new Point (xIndent, y + 3),
        Text = "Skip outro (sec)"
      };
      _nudTranscrOutro = new NumericUpDown {
        Location = new Point (xIndent + 110, y),
        Width = 60,
        Minimum = 0,
        Maximum = 600
      };
      y += 30;

      _ckBoxTranscrBoilerplate = new CheckBox {
        AutoSize = true,
        Location = new Point (xIndent, y),
        Text = "Skip Audible boilerplate (\"This is Audible …\" etc.)"
      };
      y += 34;

      _btnWhisperLoc = new Button {
        AutoSize = true,
        Location = new Point (xIndent, y),
        Text = "Whisper folder …"
      };
      _btnWhisperLoc.Click += btnWhisperLoc_Click;
      _lblWhisperLoc = new Label {
        AutoSize = true,
        Location = new Point (xIndent + 140, y + 5),
        MaximumSize = new Size (300, 0)
      };
      y += 34;

      _btnColabDir = new Button {
        AutoSize = true,
        Location = new Point (xIndent, y),
        Text = "Colab export folder …"
      };
      _btnColabDir.Click += btnColabDir_Click;
      _lblColabDir = new Label {
        AutoSize = true,
        Location = new Point (xIndent + 140, y + 5),
        MaximumSize = new Size (300, 0)
      };

      tabPage.Controls.AddRange (new Control[] {
        _ckBoxBookFolderForChapterSplit,
        _ckBoxTranscription,
        lblEngine, _comBoxTranscrEngine,
        lblLanguage, _comBoxTranscrLanguage,
        lblMarkdown, _comBoxTranscrMarkdown,
        lblIntro, _nudTranscrIntro,
        lblOutro, _nudTranscrOutro,
        _ckBoxTranscrBoilerplate,
        _btnWhisperLoc, _lblWhisperLoc,
        _btnColabDir, _lblColabDir
      });

      tabControl1.TabPages.Add (tabPage);
    }

    private void initControlsFromSettingsTranscription () {
      _ckBoxBookFolderForChapterSplit.Checked = Settings.BookFolderForChapterSplit;
      _ckBoxTranscription.Checked = Settings.Transcription == ETranscription.enabled;
      _comBoxTranscrEngine.SelectedIndex = (int)Settings.TranscriptionEngine;
      _comBoxTranscrLanguage.SelectedIndex = (int)Settings.TranscriptionLanguage;
      _comBoxTranscrMarkdown.SelectedIndex = (int)Settings.TranscriptionMarkdown;
      _nudTranscrIntro.Value = Math.Min (Settings.TranscriptionSkipIntroSec, (uint)_nudTranscrIntro.Maximum);
      _nudTranscrOutro.Value = Math.Min (Settings.TranscriptionSkipOutroSec, (uint)_nudTranscrOutro.Maximum);
      _ckBoxTranscrBoilerplate.Checked = Settings.TranscriptionFilterBoilerplate;
      _lblWhisperLoc.Text = Settings.WhisperDirectory;
      _lblColabDir.Text = Settings.ColabExportDirectory;
      enableTranscriptionDependencies (_ckBoxTranscription.Checked);
    }

    private void updateSettingsFromControlsTranscription () {
      Settings.BookFolderForChapterSplit =
        updateSettings (Settings.BookFolderForChapterSplit, _ckBoxBookFolderForChapterSplit.Checked);
      Settings.Transcription =
        updateSettings (Settings.Transcription, _ckBoxTranscription.Checked ? ETranscription.enabled : ETranscription.no);
      Settings.TranscriptionEngine =
        updateSettings (Settings.TranscriptionEngine, (ETranscriptionEngine)Math.Max (0, _comBoxTranscrEngine.SelectedIndex));
      Settings.TranscriptionLanguage =
        updateSettings (Settings.TranscriptionLanguage, (ETranscriptionLanguage)Math.Max (0, _comBoxTranscrLanguage.SelectedIndex));
      Settings.TranscriptionMarkdown =
        updateSettings (Settings.TranscriptionMarkdown, (ETranscriptionMarkdown)Math.Max (0, _comBoxTranscrMarkdown.SelectedIndex));
      Settings.TranscriptionSkipIntroSec =
        updateSettings (Settings.TranscriptionSkipIntroSec, (uint)_nudTranscrIntro.Value);
      Settings.TranscriptionSkipOutroSec =
        updateSettings (Settings.TranscriptionSkipOutroSec, (uint)_nudTranscrOutro.Value);
      Settings.TranscriptionFilterBoilerplate =
        updateSettings (Settings.TranscriptionFilterBoilerplate, _ckBoxTranscrBoilerplate.Checked);
    }

    private void ckBoxTranscription_CheckedChanged (object sender, EventArgs e) {
      enableTranscriptionDependencies (_ckBoxTranscription.Checked);
    }

    private void comBoxTranscrEngine_SelectedIndexChanged (object sender, EventArgs e) {
      enableTranscriptionDependencies (_ckBoxTranscription.Checked);
    }

    private void enableTranscriptionDependencies (bool enabled) {
      bool colab = _comBoxTranscrEngine.SelectedIndex == (int)ETranscriptionEngine.colabExport;
      _comBoxTranscrEngine.Enabled = enabled;
      _comBoxTranscrLanguage.Enabled = enabled;
      _comBoxTranscrMarkdown.Enabled = enabled;
      _nudTranscrIntro.Enabled = enabled;
      _nudTranscrOutro.Enabled = enabled;
      _ckBoxTranscrBoilerplate.Enabled = enabled;
      // Whisper folder only matters for the local engine, Colab folder only for the export engine
      _btnWhisperLoc.Enabled = enabled && !colab;
      _btnColabDir.Enabled = enabled && colab;
    }

    private void btnColabDir_Click (object sender, EventArgs e) {
      using (var fbd = new FolderBrowserDialog {
        Description = "Select the folder synced to Google Drive for Colab transcription",
        SelectedPath = Settings.ColabExportDirectory ?? string.Empty
      }) {
        if (fbd.ShowDialog (this) != DialogResult.OK)
          return;
        Settings.ColabExportDirectory = fbd.SelectedPath;
        _lblColabDir.Text = fbd.SelectedPath;
        Dirty = true;
      }
    }

    private void btnWhisperLoc_Click (object sender, EventArgs e) {
      using (var fbd = new FolderBrowserDialog {
        Description = "Select the folder containing whisper-cli.exe and the ggml model",
        SelectedPath = Settings.WhisperDirectory ?? string.Empty
      }) {
        if (fbd.ShowDialog (this) != DialogResult.OK)
          return;
        Settings.WhisperDirectory = fbd.SelectedPath;
        _lblWhisperLoc.Text = fbd.SelectedPath;
        Dirty = true;
      }
    }

    #endregion

  }
}
