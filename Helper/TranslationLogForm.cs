using System;
using System.Drawing;
using System.Windows.Forms;

namespace KenshiTranslator.Helper
{
    public partial class TranslationLogForm : Form
    {
        private RichTextBox? logTextBox;
        private Label? statusLabel;
        private Button? clearButton;
        private Button? saveButton;
        private int translationCount = 0;
        private DateTime startTime;

        public TranslationLogForm()
        {
            InitializeComponent();
            startTime = DateTime.Now;
        }

        private void InitializeComponent()
        {
            this.Size = new Size(800, 600);
            this.Text = "Translation Log";
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;

            // Create main layout
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 2,
                Padding = new Padding(5)
            };

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // Status label
            statusLabel = new Label
            {
                Text = "Translation Log - Ready",
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(statusLabel, 0, 0);
            layout.SetColumnSpan(statusLabel, 2);

            // Log text box
            logTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both
            };
            layout.Controls.Add(logTextBox, 0, 1);
            layout.SetColumnSpan(logTextBox, 2);

            // Buttons
            clearButton = new Button
            {
                Text = "Clear Log",
                Dock = DockStyle.Fill,
                Height = 30
            };
            clearButton.Click += (s, e) =>
            {
                logTextBox.Clear();
                translationCount = 0;
                UpdateStatus();
            };

            saveButton = new Button
            {
                Text = "Save Log",
                Dock = DockStyle.Fill,
                Height = 30
            };
            saveButton.Click += SaveLog_Click;

            layout.Controls.Add(clearButton, 0, 2);
            layout.Controls.Add(saveButton, 1, 2);

            this.Controls.Add(layout);
        }

        public void LogTranslation(string original, string translated, bool success)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, string, bool>(LogTranslation), original, translated, success);
                return;
            }

            translationCount++;
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var status = success ? "✓" : "✗";
            var color = success ? Color.LightGreen : Color.LightCoral;

            // Add timestamp and status
            logTextBox!.SelectionStart = logTextBox.TextLength;
            logTextBox.SelectionColor = Color.Gray;
            logTextBox.AppendText($"[{timestamp}] ");

            logTextBox.SelectionColor = color;
            logTextBox.AppendText($"{status} ");

            // Add original text
            logTextBox.SelectionColor = Color.LightBlue;
            logTextBox.AppendText($"EN: {original}");
            logTextBox.AppendText(Environment.NewLine);

            // Add translated text
            logTextBox.SelectionColor = success ? Color.LightGreen : Color.Yellow;
            logTextBox.AppendText($"    RU: {translated}");
            logTextBox.AppendText(Environment.NewLine);
            logTextBox.AppendText(Environment.NewLine);

            // Auto-scroll to bottom
            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.ScrollToCaret();

            UpdateStatus();
        }

        public void LogError(string original, string error)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, string>(LogError), original, error);
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");

            logTextBox!.SelectionStart = logTextBox.TextLength;
            logTextBox.SelectionColor = Color.Gray;
            logTextBox.AppendText($"[{timestamp}] ");

            logTextBox.SelectionColor = Color.Red;
            logTextBox.AppendText($"❌ ERROR: {original}");
            logTextBox.AppendText(Environment.NewLine);

            logTextBox.SelectionColor = Color.Pink;
            logTextBox.AppendText($"    {error}");
            logTextBox.AppendText(Environment.NewLine);
            logTextBox.AppendText(Environment.NewLine);

            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.ScrollToCaret();

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            var elapsed = DateTime.Now - startTime;
            statusLabel!.Text = $"Translation Log - {translationCount} translations | Elapsed: {elapsed:hh\\:mm\\:ss}";
        }

        private void SaveLog_Click(object? sender, EventArgs e)
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = $"translation_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, logTextBox!.Text);
                    MessageBox.Show($"Log saved to: {dialog.FileName}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save log: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public void Reset()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(Reset));
                return;
            }

            logTextBox!.Clear();
            translationCount = 0;
            startTime = DateTime.Now;
            UpdateStatus();
        }
    }
}