using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MulchPatcher
{
    public class MainForm : Form
    {
        private readonly ComboBox operation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox first = new TextBox();
        private readonly TextBox second = new TextBox();
        private readonly Label firstLabel = new Label { AutoSize = true };
        private readonly Label secondLabel = new Label { AutoSize = true };
        private readonly Button run = new Button();
        private readonly GroupBox parameters = new GroupBox { Text = "パラメーター" };
        private readonly ToolTip paths = new ToolTip();
        private bool wasApplying = true;
        private bool busy;

        private bool Applying { get { return operation.SelectedIndex == 0; } }
        private PatchFormat SelectedFormat
        {
            get { return operation.SelectedIndex == 2 ? PatchFormat.UPS : operation.SelectedIndex == 3 ? PatchFormat.IPS : PatchFormat.BPS; }
        }

        public MainForm(string[] initialPaths)
        {
            Text = "Mulch Patcher";
            Icon = Icon.ExtractAssociatedIcon(typeof(MainForm).Assembly.Location);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(404, 162);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            MenuStrip menu = new MenuStrip { Font = Font };
            ToolStripMenuItem file = new ToolStripMenuItem("ファイル(&F)");
            file.DropDownItems.Add("終了(&X)", null, (s, e) => Close());
            ToolStripMenuItem help = new ToolStripMenuItem("ヘルプ(&H)");
            help.DropDownItems.Add("バージョン情報(&A)", null, (s, e) =>
                ShowMessage("Mulch Patcher " + Application.ProductVersion + "\nIPS / BPS / UPS", MessageBoxIcon.Information));
            menu.Items.Add(file); menu.Items.Add(help);
            MainMenuStrip = menu;
            Controls.Add(menu);
            Label operationLabel = new Label { Text = "操作", AutoSize = true, Location = new Point(12, 31) };
            operation.SetBounds(76, 27, 222, 23);
            operation.Items.AddRange(new object[] { "パッチ適用", "BPSパッチ作成", "UPSパッチ作成", "IPSパッチ作成" });
            operation.SelectedIndex = 0;
            operation.SelectedIndexChanged += (s, e) => ModeChanged();
            run.SetBounds(310, 27, 80, 24);
            run.Click += async (s, e) => await Execute();
            Controls.Add(operationLabel); Controls.Add(operation); Controls.Add(run);
            parameters.SetBounds(8, 54, 388, 100);
            AddPathRow(firstLabel, first, 16, 31);
            AddPathRow(secondLabel, second, 57, 72);
            Controls.Add(parameters);
            DragEnter += DragFiles;
            DragDrop += (s, e) => DropPaths((string[])e.Data.GetData(DataFormats.FileDrop));
            FormClosing += (s, e) => { if (busy) e.Cancel = true; };
            ModeChanged();
            if (initialPaths.Length > 0) DropPaths(initialPaths);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) paths.Dispose();
            base.Dispose(disposing);
        }

        private void AddPathRow(Label label, TextBox box, int labelTop, int boxTop)
        {
            label.Location = new Point(12, labelTop);
            box.SetBounds(12, boxTop, 304, 23);
            Button browse = new Button { Text = "…" };
            browse.SetBounds(332, boxTop - 1, 42, 24);
            browse.Click += (s, e) => BrowseInput(box);
            box.AllowDrop = true;
            box.DragEnter += DragFiles;
            box.DragDrop += (s, e) =>
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (!busy && files.Length > 0) box.Text = files[0];
            };
            box.TextChanged += (s, e) =>
            {
                box.SelectionStart = box.Text.Length;
                box.ScrollToCaret();
                paths.SetToolTip(box, box.Text);
            };
            parameters.Controls.Add(label); parameters.Controls.Add(box); parameters.Controls.Add(browse);
        }

        private void DragFiles(object sender, DragEventArgs e)
        { if (!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }

        private void DropPaths(string[] files)
        {
            if (busy) return;
            foreach (string path in files)
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".ips" || extension == ".bps" || extension == ".ups")
                { operation.SelectedIndex = 0; first.Text = path; }
                else if (Applying) second.Text = path;
                else if (string.IsNullOrWhiteSpace(first.Text)) first.Text = path;
                else second.Text = path;
            }
        }

        private void ModeChanged()
        {
            if (Applying != wasApplying) { first.Clear(); second.Clear(); }
            wasApplying = Applying;
            firstLabel.Text = Applying ? "パッチファイル" : "変更前のファイル";
            secondLabel.Text = Applying ? "パッチするファイル" : "変更後のファイル";
            run.Text = Applying ? "適用" : "作成";
        }

        private string GetOutputPath()
        {
            string a = Path.GetFullPath(first.Text.Trim()), b = Path.GetFullPath(second.Text.Trim());
            string basis = Applying ? a : b;
            string directory = Path.GetDirectoryName(basis);
            string stem = Path.GetFileNameWithoutExtension(basis);
            string extension = Applying ? Path.GetExtension(b) : "." + SelectedFormat.ToString().ToLowerInvariant();
            string output = Path.Combine(directory, stem + extension);
            if (string.Equals(output, a, StringComparison.OrdinalIgnoreCase) || string.Equals(output, b, StringComparison.OrdinalIgnoreCase))
            { stem += Applying ? "_patched" : "_patch"; output = Path.Combine(directory, stem + extension); }
            int suffix = 2;
            while (File.Exists(output) || Directory.Exists(output)
                || string.Equals(output, a, StringComparison.OrdinalIgnoreCase)
                || string.Equals(output, b, StringComparison.OrdinalIgnoreCase))
                output = Path.Combine(directory, stem + "_" + suffix++ + extension);
            return output;
        }

        private void BrowseInput(TextBox box)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Applying && box == first ? "パッチファイルを選択" : "ROMを選択";
                dialog.Filter = Applying && box == first ? "パッチ (*.ips;*.bps;*.ups)|*.ips;*.bps;*.ups|すべてのファイル|*.*"
                    : "ROMファイル|*.gba;*.gb;*.gbc;*.nes;*.sfc;*.smc;*.bin;*.rom|すべてのファイル|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
            }
        }

        protected virtual void ShowMessage(string message, MessageBoxIcon icon)
        { MessageBox.Show(this, message, "Mulch Patcher", MessageBoxButtons.OK, icon); }

        private async Task Execute()
        {
            string a = first.Text.Trim(), b = second.Text.Trim();
            if (a.Length == 0 || b.Length == 0)
            { ShowMessage("2つのファイルを指定してください。", MessageBoxIcon.Exclamation); return; }
            bool applying = Applying;
            PatchFormat selected = SelectedFormat;
            busy = true; parameters.Enabled = false; operation.Enabled = false; run.Enabled = false;
            run.Text = "処理中…"; UseWaitCursor = true;
            try
            {
                string destination = GetOutputPath();
                PatchResult result = await Task.Run(() => applying
                    ? PatchFiles.Apply(b, a, destination) : PatchFiles.Create(a, b, destination, selected));
                ShowMessage((applying ? "適用が完了しました。" : "作成が完了しました。") + "\n\n" + result.OutputPath, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ShowMessage(ex.Message, MessageBoxIcon.Error); }
            finally
            {
                busy = false; parameters.Enabled = true; operation.Enabled = true; run.Enabled = true;
                UseWaitCursor = false; run.Text = applying ? "適用" : "作成";
            }
        }
    }
}
