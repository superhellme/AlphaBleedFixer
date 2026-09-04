using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaBleedFixer
{
    internal sealed class MainForm : Form
    {
        private readonly string[] initialPaths;
        private readonly NumericUpDown paddingBox = new NumericUpDown();
        private readonly CheckBox recursiveBox = new CheckBox();
        private readonly CheckBox backupBox = new CheckBox();
        private readonly Button processFolderButton = new Button();
        private readonly Button checkUpdateButton = new Button();
        private readonly TextBox logBox = new TextBox();
        private readonly Label dropLabel = new Label();
        private bool isBusy;
        private bool initialPathsHandled;

        public MainForm(IEnumerable<string> initialPaths)
        {
            this.initialPaths = (initialPaths ?? Enumerable.Empty<string>()).ToArray();

            Text = "Alpha Bleed Fixer for RimWorld " + UpdateService.CurrentVersionLabel;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(720, 520);
            Size = new Size(860, 620);
            Font = new Font("Yu Gothic UI", 9F);
            AllowDrop = true;

            BuildLayout(SettingsStore.Load());
            WireEvents();
        }

        private void BuildLayout(AppSettings settings)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            Controls.Add(root);

            dropLabel.Text = "PNGまたはフォルダをここへドロップすると、すぐに処理を開始します\r\n可視画素とアルファは変更せず、完全透明画素のRGBだけを拡張します";
            dropLabel.Dock = DockStyle.Fill;
            dropLabel.TextAlign = ContentAlignment.MiddleCenter;
            dropLabel.BorderStyle = BorderStyle.FixedSingle;
            dropLabel.BackColor = Color.FromArgb(242, 246, 250);
            dropLabel.AllowDrop = true;
            root.Controls.Add(dropLabel, 0, 0);

            var folderButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            processFolderButton.Text = "フォルダ内の画像を処理...";
            processFolderButton.AutoSize = true;
            folderButtons.Controls.Add(processFolderButton);
            checkUpdateButton.Text = "アップデートを確認...";
            checkUpdateButton.AutoSize = true;
            folderButtons.Controls.Add(checkUpdateButton);
            root.Controls.Add(folderButtons, 0, 1);

            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0) };
            options.Controls.Add(new Label { Text = "拡張幅:", AutoSize = true, Margin = new Padding(0, 5, 4, 0) });
            paddingBox.Minimum = 1;
            paddingBox.Maximum = 128;
            paddingBox.Value = settings.Padding;
            paddingBox.Width = 64;
            options.Controls.Add(paddingBox);
            options.Controls.Add(new Label { Text = "px", AutoSize = true, Margin = new Padding(2, 5, 16, 0) });
            recursiveBox.Text = "サブフォルダも処理";
            recursiveBox.Checked = settings.Recursive;
            recursiveBox.AutoSize = true;
            recursiveBox.Margin = new Padding(0, 4, 16, 0);
            options.Controls.Add(recursiveBox);
            backupBox.Text = "元画像を.bakで保存";
            backupBox.Checked = settings.CreateBackup;
            backupBox.AutoSize = true;
            backupBox.Margin = new Padding(0, 4, 16, 0);
            options.Controls.Add(backupBox);
            root.Controls.Add(options, 0, 2);

            root.Controls.Add(new Label { Text = "処理ログ", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, 3);
            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Both;
            logBox.WordWrap = false;
            logBox.Font = new Font("Consolas", 9F);
            root.Controls.Add(logBox, 0, 4);
        }

        private void WireEvents()
        {
            DragEnter += HandleDragEnter;
            DragDrop += HandleDragDrop;
            dropLabel.DragEnter += HandleDragEnter;
            dropLabel.DragDrop += HandleDragDrop;
            processFolderButton.Click += ProcessFolderButtonClick;
            checkUpdateButton.Click += CheckUpdateButtonClick;
            paddingBox.ValueChanged += OptionsChanged;
            recursiveBox.CheckedChanged += OptionsChanged;
            backupBox.CheckedChanged += OptionsChanged;
            Shown += MainFormShown;
        }

        private void HandleDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !isBusy && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private async void HandleDragDrop(object sender, DragEventArgs e)
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            await ProcessPathsAsync(paths ?? new string[0]);
        }

        private async void ProcessFolderButtonClick(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "PNGを処理するフォルダを選択してください";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    await ProcessPathsAsync(new[] { dialog.SelectedPath });
                }
            }
        }

        private async void MainFormShown(object sender, EventArgs e)
        {
            if (initialPathsHandled)
            {
                return;
            }

            initialPathsHandled = true;
            if (initialPaths.Length > 0)
            {
                await ProcessPathsAsync(initialPaths);
            }

            await CheckForUpdatesAsync(false);
        }

        private async void CheckUpdateButtonClick(object sender, EventArgs e)
        {
            await CheckForUpdatesAsync(true);
        }

        private async Task CheckForUpdatesAsync(bool manualCheck)
        {
            if (isBusy)
            {
                return;
            }

            SetBusy(true);
            AppendLog(manualCheck ? "\r\nアップデートを確認しています...\r\n" : "起動時のアップデートを確認しています...\r\n");
            var updateWillRestart = false;
            try
            {
                var release = await UpdateService.FindUpdateAsync();
                if (release == null)
                {
                    AppendLog("最新版を使用しています (" + UpdateService.CurrentVersionLabel + ")。\r\n");
                    return;
                }

                AppendLog("新しいバージョン " + release.VersionLabel + " が利用できます。\r\n");
                var answer = MessageBox.Show(this,
                    string.Format("新しいバージョン {0} が利用できます。\r\n\r\nダウンロードして更新しますか？\r\n更新後、アプリは自動的に再起動します。", release.VersionLabel),
                    Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (answer != DialogResult.Yes)
                {
                    AppendLog("アップデートをキャンセルしました。\r\n");
                    return;
                }

                AppendLog("アップデートをダウンロードしています...\r\n");
                await UpdateService.PrepareAndLaunchUpdaterAsync(release);
                AppendLog("ダウンロード完了。アプリを再起動して更新します。\r\n");
                updateWillRestart = true;
                Application.Exit();
            }
            catch (Exception ex)
            {
                AppendLog("アップデートの確認に失敗しました: " + ex.Message + "\r\n");
            }
            finally
            {
                if (!updateWillRestart)
                {
                    SetBusy(false);
                }
            }
        }

        private async Task ProcessPathsAsync(IEnumerable<string> paths)
        {
            if (isBusy)
            {
                return;
            }

            var inputs = NormalizePaths(paths);
            if (inputs.Length == 0)
            {
                MessageBox.Show(this, "PNGまたはフォルダを指定してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var options = ReadOptions();
            SetBusy(true);
            logBox.Clear();
            try
            {
                var files = await Task.Run(() => AlphaBleedProcessor.CollectPngFiles(inputs, options.Recursive));
                if (files.Count == 0)
                {
                    AppendLog("対象のPNGがありません。\r\n");
                    return;
                }

                AppendLog(string.Format("対象: {0} ファイル / 拡張幅: {1}px / バックアップ: {2}\r\n\r\n",
                    files.Count,
                    options.Padding,
                    options.CreateBackup ? "有効" : "無効"));

                var results = await Task.Run(() => files.Select(file => AlphaBleedProcessor.ProcessFile(file, options)).ToList());

                foreach (var result in results)
                {
                    AppendLog(result.ToLogLine() + "\r\n");
                    if (!string.IsNullOrEmpty(result.BackupPath))
                    {
                        AppendLog("       backup: " + result.BackupPath + "\r\n");
                    }
                }

                var changed = results.Count(r => r.Status == ProcessStatus.Changed);
                var skipped = results.Count(r => r.Status == ProcessStatus.Skipped);
                var failed = results.Count(r => r.Status == ProcessStatus.Failed);
                AppendLog(string.Format("\r\n完了: 変更 {0} / スキップ {1} / 失敗 {2}\r\n", changed, skipped, failed));
            }
            catch (Exception ex)
            {
                AppendLog("致命的エラー: " + ex + "\r\n");
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private static string[] NormalizePaths(IEnumerable<string> paths)
        {
            if (paths == null)
            {
                return new string[0];
            }

            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                try
                {
                    var path = Path.GetFullPath(rawPath.Trim('"'));
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        normalized.Add(path);
                    }
                }
                catch
                {
                }
            }
            return normalized.ToArray();
        }

        private ProcessOptions ReadOptions()
        {
            return new ProcessOptions
            {
                Padding = (int)paddingBox.Value,
                Recursive = recursiveBox.Checked,
                CreateBackup = backupBox.Checked
            };
        }

        private void OptionsChanged(object sender, EventArgs e)
        {
            var options = ReadOptions();
            try
            {
                SettingsStore.Save(new AppSettings
                {
                    Padding = options.Padding,
                    Recursive = options.Recursive,
                    CreateBackup = options.CreateBackup
                });
            }
            catch (Exception ex)
            {
                AppendLog("設定を保存できませんでした: " + ex.Message + "\r\n");
            }
        }

        private void AppendLog(string text)
        {
            logBox.AppendText(text);
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            processFolderButton.Enabled = !busy;
            checkUpdateButton.Enabled = !busy;
            paddingBox.Enabled = !busy;
            recursiveBox.Enabled = !busy;
            backupBox.Enabled = !busy;
            AllowDrop = !busy;
            dropLabel.AllowDrop = !busy;
            UseWaitCursor = busy;
        }
    }
}
