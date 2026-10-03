using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MulchPatcher
{
    public sealed class MainForm : Form
    {
        private readonly RadioButton apply = new RadioButton { Text = "パッチ適用", Checked = true, AutoSize = true };
        private readonly RadioButton create = new RadioButton { Text = "パッチ作成", AutoSize = true };
        private readonly ComboBox format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
        private readonly TextBox first = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox second = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox output = new TextBox { Dock = DockStyle.Fill };
        private readonly Label firstLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly Label secondLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly Label outputLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly Label hint = new Label { AutoSize = true, Dock = DockStyle.Fill };
        private readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle };
        private readonly Button run = new Button { Text = "適用", AutoSize = true, MinimumSize = new Size(100, 32) };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Visible = false, Style = ProgressBarStyle.Marquee };
        private readonly TableLayoutPanel layout;
        private string lastSuggestion = "";
        private bool updating;
        private bool busy;

        public MainForm(string[] initialPaths)
        {
            Text = "Mulch Patcher";
            Icon = Icon.ExtractAssociatedIcon(typeof(MainForm).Assembly.Location);
            Font = new Font("Yu Gothic UI", 9F);
            ClientSize = new Size(700, 420);
            MinimumSize = new Size(620, 390);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AllowDrop = true;
            format.Items.AddRange(new object[] { "BPS", "UPS", "IPS" });
            format.SelectedIndex = 0;
            layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 3, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            FlowLayoutPanel modes = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            apply.Margin = new Padding(0, 6, 20, 0);
            create.Margin = new Padding(0, 6, 24, 0);
            modes.Controls.Add(apply);
            modes.Controls.Add(create);
            modes.Controls.Add(new Label { Text = "作成形式", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            modes.Controls.Add(format);
            layout.Controls.Add(modes, 0, 0); layout.SetColumnSpan(modes, 3);
            AddPathRow(firstLabel, first, 1, () => BrowseInput(first));
            AddPathRow(secondLabel, second, 2, () => BrowseInput(second));
            AddPathRow(outputLabel, output, 3, BrowseOutput);
            layout.Controls.Add(hint, 0, 4); layout.SetColumnSpan(hint, 3);
            layout.Controls.Add(log, 0, 5); layout.SetColumnSpan(log, 3);
            layout.Controls.Add(progress, 0, 6); layout.SetColumnSpan(progress, 3);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            run.Click += async (s, e) => await Execute();
            actions.Controls.Add(run);
            Button openFolder = new Button { Text = "保存先フォルダー", AutoSize = true, MinimumSize = new Size(125, 32) };
            openFolder.Click += (s, e) =>
            {
                try
                {
                    string directory = Path.GetDirectoryName(Path.GetFullPath(output.Text));
                    if (Directory.Exists(directory)) System.Diagnostics.Process.Start("explorer.exe", "\"" + directory + "\"");
                }
                catch (Exception ex) { log.Text = ex.Message; }
            };
            actions.Controls.Add(openFolder);
            layout.Controls.Add(actions, 0, 7); layout.SetColumnSpan(actions, 3);
            Controls.Add(layout);
            apply.CheckedChanged += (s, e) => { if (apply.Checked) ModeChanged(); };
            create.CheckedChanged += (s, e) => { if (create.Checked) ModeChanged(); };
            format.SelectedIndexChanged += (s, e) => { UpdateHint(); SuggestOutput(); };
            first.TextChanged += (s, e) => SuggestOutput();
            second.TextChanged += (s, e) => SuggestOutput();
            output.TextChanged += (s, e) => { if (!updating && output.Text != lastSuggestion) lastSuggestion = ""; };
            DragEnter += DragFiles;
            DragDrop += (s, e) => DropPaths((string[])e.Data.GetData(DataFormats.FileDrop));
            FormClosing += (s, e) => { if (busy) e.Cancel = true; };
            ModeChanged();
            if (initialPaths.Length > 0) DropPaths(initialPaths);
            log.Text = "ファイルを選択するか、この画面へドラッグ＆ドロップしてください。\r\n元ROMと既存ファイルは上書きしません。";
        }

        private void AddPathRow(Label label, TextBox box, int row, Action browse)
        {
            Button button = new Button { Text = "参照…", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 8) };
            button.Click += (s, e) => browse();
            box.Margin = new Padding(0, 3, 0, 8);
            box.AllowDrop = true;
            box.DragEnter += DragFiles;
            box.DragDrop += (s, e) =>
            {
                string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (paths.Length > 0 && !busy) box.Text = paths[0];
            };
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(box, 1, row);
            layout.Controls.Add(button, 2, row);
        }

        private void DragFiles(object sender, DragEventArgs e)
        { if (!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }

        private void DropPaths(string[] paths)
        {
            if (busy) return;
            foreach (string path in paths)
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".ips" || extension == ".bps" || extension == ".ups")
                { apply.Checked = true; first.Text = path; }
                else if (apply.Checked) second.Text = path;
                else if (string.IsNullOrWhiteSpace(first.Text)) first.Text = path;
                else second.Text = path;
            }
        }

        private void ModeChanged()
        {
            firstLabel.Text = apply.Checked ? "パッチファイル" : "変更前のROM";
            secondLabel.Text = apply.Checked ? "元ROM" : "変更後のROM";
            outputLabel.Text = apply.Checked ? "出力ROM" : "出力パッチ";
            run.Text = apply.Checked ? "適用" : "作成";
            format.Enabled = create.Checked;
            // Different operations have different input roles; avoid reusing misleading selections.
            updating = true;
            first.Clear(); second.Clear(); output.Clear(); lastSuggestion = "";
            updating = false;
            UpdateHint();
        }

        private void UpdateHint()
        {
            hint.Text = apply.Checked
                ? "形式は自動判定。BPS / UPSはサイズ・CRC32を検証します。\nIPSには元ROMの検証情報がありません。正しいROMを選んでください。"
                : format.Text == "IPS"
                    ? "IPSは互換用です。16 MiB→32 MiBの拡張にはBPS / UPSを選択してください。\n作成したパッチは、適用結果が変更後ROMと一致することを確認して保存します。"
                    : "BPS / UPSは16 MiB→32 MiBの拡張に対応。元ROMのサイズとCRC32も記録します。\n作成したパッチは、適用結果が変更後ROMと一致することを確認して保存します。";
        }

        private void SuggestOutput()
        {
            if (updating || (!string.IsNullOrWhiteSpace(output.Text) && output.Text != lastSuggestion)) return;
            try
            {
                if (string.IsNullOrWhiteSpace(first.Text) || string.IsNullOrWhiteSpace(second.Text)) return;
                string basis = apply.Checked ? Path.GetFullPath(first.Text) : Path.GetFullPath(second.Text);
                string extension = apply.Checked ? Path.GetExtension(second.Text) : "." + format.Text.ToLowerInvariant();
                string stem = Path.GetFileNameWithoutExtension(basis);
                string directory = Path.GetDirectoryName(basis);
                string suggested = Path.Combine(directory, stem + extension);
                if (apply.Checked && string.Equals(Path.GetFullPath(second.Text), suggested, StringComparison.OrdinalIgnoreCase))
                { stem += "_patched"; suggested = Path.Combine(directory, stem + extension); }
                int suffix = 2;
                while (File.Exists(suggested) || Directory.Exists(suggested))
                    suggested = Path.Combine(directory, stem + "_" + suffix++ + extension);
                updating = true;
                lastSuggestion = suggested;
                output.Text = suggested;
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (PathTooLongException) { }
            finally { updating = false; }
        }

        private void BrowseInput(TextBox box)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = apply.Checked && box == first ? "パッチファイルを選択" : "ROMを選択";
                dialog.Filter = apply.Checked && box == first ? "パッチ (*.ips;*.bps;*.ups)|*.ips;*.bps;*.ups|すべてのファイル|*.*"
                    : "ROMファイル|*.gba;*.gb;*.gbc;*.nes;*.sfc;*.smc;*.bin;*.rom|すべてのファイル|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
            }
        }

        private void BrowseOutput()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "新しい保存先を選択";
                dialog.Filter = "すべてのファイル|*.*";
                dialog.FileName = output.Text;
                dialog.OverwritePrompt = false;
                if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.FileName;
            }
        }

        private async Task Execute()
        {
            bool applying = apply.Checked;
            string a = first.Text.Trim(), b = second.Text.Trim(), destination = output.Text.Trim();
            if (a.Length == 0 || b.Length == 0 || destination.Length == 0)
            { log.Text = "入力ファイルと保存先を指定してください。"; return; }
            PatchFormat selected = (PatchFormat)Enum.Parse(typeof(PatchFormat), format.Text);
            busy = true; layout.Enabled = false; progress.Visible = true;
            log.Text = applying ? "パッチを適用しています…" : "パッチを作成・検証しています…";
            try
            {
                PatchResult result = await Task.Run(() => applying
                    ? PatchFiles.Apply(b, a, destination) : PatchFiles.Create(a, b, destination, selected));
                log.Text = (applying ? "適用完了" : "作成・検証完了") + " / " + result.Format + "\r\n" +
                    (result.SourceSize / 1048576.0).ToString("0.00") + " MiB → " + (result.TargetSize / 1048576.0).ToString("0.00") +
                    " MiB\r\n出力ROM CRC32: " + result.TargetCrc.ToString("X8") + "\r\n保存先: " + result.OutputPath + "\r\n元ROMは保持されています。";
            }
            catch (Exception ex) { log.Text = "処理できませんでした。\r\n" + ex.Message; }
            finally { busy = false; layout.Enabled = true; format.Enabled = create.Checked; progress.Visible = false; }
        }
    }
}
