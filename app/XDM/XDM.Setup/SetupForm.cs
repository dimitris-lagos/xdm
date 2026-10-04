using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XDM.Setup
{
    internal sealed class SetupForm : Form
    {
        static readonly Color Ink = Color.FromArgb(27, 38, 58);
        static readonly Color Muted = Color.FromArgb(100, 114, 134);
        static readonly Color Blue = Color.FromArgb(35, 103, 218);
        static readonly Color Line = Color.FromArgb(222, 229, 238);
        public int ExitCode { get; private set; } = 1;
        readonly Panel options = new Panel();
        readonly Panel progressPage = new Panel();
        readonly Panel warning = new Panel();
        readonly Label warningText = new Label();
        readonly Label pageTitle = new Label();
        readonly Label pageSubtitle = new Label();
        readonly Label step = new Label();
        readonly Label footerText = new Label();
        readonly CheckBox desktopCheck = new CheckBox { Checked = true };
        readonly CheckBox ytCheck = new CheckBox { Checked = true };
        readonly CheckBox ffCheck = new CheckBox { Checked = true };
        readonly Button primary = new Button();
        readonly Button cancel = new Button();
        readonly ProgressRow ytRow = new ProgressRow("yt-dlp");
        readonly ProgressRow ffRow = new ProgressRow("FFmpeg");
        readonly ProgressRow installRow = new ProgressRow("Install XDM");
        readonly string log;
        CancellationTokenSource cancellation;
        bool busy, installing, complete;
        public SetupForm(string log)
        {
            this.log = log;
            Text = "XDM Setup";
            Font = new Font("Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(780, 584);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(246, 248, 251);
            Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);

            var header = new Panel { Bounds = new Rectangle(0, 0, 780, 88), BackColor = Color.White };
            header.Paint += (s, e) => { using (var pen = new Pen(Line)) e.Graphics.DrawLine(pen, 0, 87, header.Width, 87); };
            var logo = new Label { Text = "↓", Bounds = new Rectangle(28, 20, 48, 48), BackColor = Blue, ForeColor = Color.White,
                Font = new Font("Segoe UI", 25F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
            header.Controls.Add(logo);
            header.Controls.Add(Label("Xtreme Download Manager", 90, 20, 450, 28, 16F, true, Ink));
            header.Controls.Add(Label("SETUP  •  " + Program.Config.version + "  •  WINDOWS " + Program.Arch.ToUpperInvariant(), 92, 52, 430, 18, 8F, false, Muted));
            step.Bounds = new Rectangle(538, 30, 208, 28); step.TextAlign = ContentAlignment.MiddleRight; step.ForeColor = Muted;
            step.Text = "01  Options     /     02  Install"; header.Controls.Add(step);
            Controls.Add(header);

            options.Bounds = progressPage.Bounds = new Rectangle(0, 88, 780, 424);
            options.Controls.Add(Label("Choose your downloads", 32, 22, 710, 32, 19F, true, Ink));
            options.Controls.Add(Label("Recommended tools for video downloads, merging and conversion.", 34, 62, 710, 22, 9F, false, Muted));
            options.Controls.Add(DownloadCard(ytCheck, "yt-dlp", "Downloads videos from YouTube and other supported sites.",
                Program.Config.media.ytdlp.version, 98));
            options.Controls.Add(DownloadCard(ffCheck, "FFmpeg", "Merges video and audio, and converts media files.",
                Program.Config.media.ffmpeg.version, 190));
            desktopCheck.Text = "Create a desktop shortcut"; desktopCheck.Bounds = new Rectangle(32, 284, 716, 28); desktopCheck.ForeColor = Ink;
            options.Controls.Add(desktopCheck);
            warning.Bounds = new Rectangle(32, 320, 716, 88);
            warning.BackColor = Color.FromArgb(255, 245, 221);
            warning.Controls.Add(Label("Manual installation required", 16, 10, 682, 22, 10F, true, Color.FromArgb(133, 86, 14)));
            warningText.Bounds = new Rectangle(16, 34, 682, 48);
            warningText.ForeColor = Color.FromArgb(133, 86, 14);
            warning.Controls.Add(warningText);
            options.Controls.Add(warning);
            Controls.Add(options);

            pageTitle.Bounds = new Rectangle(32, 22, 714, 34); pageTitle.Font = new Font(Font.FontFamily, 19F, FontStyle.Bold); pageTitle.ForeColor = Ink;
            pageSubtitle.Bounds = new Rectangle(34, 62, 712, 24); pageSubtitle.ForeColor = Muted;
            progressPage.Controls.Add(pageTitle); progressPage.Controls.Add(pageSubtitle);
            ytRow.Location = new Point(32, 100); ffRow.Location = new Point(32, 192); installRow.Location = new Point(32, 284);
            progressPage.Controls.Add(ytRow); progressPage.Controls.Add(ffRow); progressPage.Controls.Add(installRow);
            progressPage.Visible = false; Controls.Add(progressPage);

            var footer = new Panel { Bounds = new Rectangle(0, 512, 780, 72), BackColor = Color.White };
            footer.Paint += (s, e) => { using (var pen = new Pen(Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            footerText.Bounds = new Rectangle(32, 25, 430, 24); footerText.ForeColor = Muted;
            footerText.Text = "Install location: " + Program.InstallDirectory; footer.Controls.Add(footerText);
            ConfigureButton(cancel, "Cancel", new Rectangle(504, 17, 108, 38), false);
            ConfigureButton(primary, "Install", new Rectangle(624, 17, 124, 38), true);
            footer.Controls.Add(cancel); footer.Controls.Add(primary); Controls.Add(footer);
            AcceptButton = primary; CancelButton = cancel;
            ytCheck.CheckedChanged += (s, e) => UpdateWarning();
            ffCheck.CheckedChanged += (s, e) => UpdateWarning();
            primary.Click += async (s, e) => {
                if (complete) {
                    try { Program.LaunchInstalledApp(); Close(); }
                    catch (Exception ex) { MessageBox.Show(this, "XDM was installed, but could not start.\n" + ex.Message, "XDM Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                } else if (!busy) await Run();
            };
            cancel.Click += (s, e) => { if (busy && !installing) cancellation?.Cancel(); else Close(); };
            FormClosing += (s, e) =>
            {
                if (busy) { e.Cancel = true; if (!installing) cancellation?.Cancel(); }
            };
            UpdateWarning();
        }

        static Label Label(string text, int x, int y, int width, int height, float size, bool bold, Color color)
        {
            return new Label { Text = text, Bounds = new Rectangle(x, y, width, height), ForeColor = color,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
        }
        static void ConfigureButton(Button button, string text, Rectangle bounds, bool accent)
        {
            button.Text = text; button.Bounds = bounds; button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = accent ? 0 : 1; button.FlatAppearance.BorderColor = Line;
            button.BackColor = accent ? Blue : Color.White; button.ForeColor = accent ? Color.White : Ink;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold); button.Cursor = Cursors.Hand;
        }
        Panel DownloadCard(CheckBox box, string name, string description, string version, int y)
        {
            var card = new Panel { Bounds = new Rectangle(32, y, 716, 78), BackColor = Color.White };
            card.Paint += (s, e) => { using (var pen = new Pen(Line)) e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1); };
            box.Bounds = new Rectangle(18, 17, 140, 26); box.Text = name; box.Font = new Font("Segoe UI", 11F, FontStyle.Bold); box.ForeColor = Ink;
            card.Controls.Add(box);
            var versionLabel = Label(version + "  /  " + Program.Arch, 190, 20, 506, 20, 8.5F, false, Muted);
            versionLabel.TextAlign = ContentAlignment.MiddleRight; card.Controls.Add(versionLabel);
            card.Controls.Add(Label(description, 38, 48, 650, 20, 9F, false, Muted));
            return card;
        }
        void UpdateWarning()
        {
            warning.Visible = !ytCheck.Checked || !ffCheck.Checked;
            var names = !ytCheck.Checked && !ffCheck.Checked ? "yt-dlp and FFmpeg" : !ytCheck.Checked ? "yt-dlp" : "FFmpeg";
            warningText.Text = "Download " + names + " manually using the tested versions shown above and the matching " + Program.Arch +
                " architecture. Newer versions may work, but compatibility is not guaranteed. Place the files in the XDM install folder.";
        }
        void ShowProgress(DownloadSelection selection)
        {
            options.Visible = false; progressPage.Visible = true;
            pageTitle.Text = "Installing XDM";
            pageSubtitle.Text = "Downloading your selected tools before installing the application.";
            step.Text = "01  Options     /     02  Install";
            ytRow.Set(0, selection.Ytdlp ? "Waiting to download" : "Skipped — manual installation", !selection.Ytdlp);
            ffRow.Set(0, selection.Ffmpeg ? "Waiting to download" : "Skipped — manual installation", !selection.Ffmpeg);
            installRow.Set(0, "Waiting for downloads");
            primary.Visible = false; cancel.Visible = true; cancel.Location = new Point(624, 17); cancel.Text = "Cancel"; footerText.Text = "You can cancel while downloads are in progress.";
        }
        void ShowFinished(DownloadSelection selection)
        {
            complete = true; busy = installing = false;
            pageTitle.Text = "Installation complete";
            pageSubtitle.Text = ExitCode == 3010 ? "XDM is installed. Restart Windows to finish setup." : "XDM is ready. Click Finish to launch it and close setup.";
            installRow.Set(100, "Installed");
            primary.Text = "Finish"; primary.Enabled = primary.Visible = true;
            cancel.Visible = false; footerText.Text = selection.All ? "Setup completed successfully." : "Install the skipped media tools manually before using them.";
        }
        async Task Run()
        {
            var selection = new DownloadSelection(ytCheck.Checked, ffCheck.Checked);
            var createDesktopShortcut = desktopCheck.Checked;
            busy = true; cancellation = new CancellationTokenSource();
            ShowProgress(selection);
            try
            {
                using (var work = new Workspace())
                {
                    await Program.DownloadSelected(work.Path, selection,
                        new Progress<int>(v => ytRow.Set(v, v == 100 ? "Downloaded and verified" : "Downloading…")),
                        new Progress<int>(v => ffRow.Set(v, v == 100 ? "Downloaded and verified" : "Downloading…")), cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    installing = true; cancel.Enabled = false; cancel.Visible = false;
                    pageSubtitle.Text = "Installing application files and the tools you selected.";
                    footerText.Text = "Please wait while setup completes.";
                    installRow.Set(0, "Installing…");
                    var progress = new Progress<int>(v => installRow.Set(v, v >= 95 ? "Finishing setup…" : "Installing…"));
                    ExitCode = await Task.Run(() => Program.Install(work.Path, log, selection, createDesktopShortcut, progress));
                    ShowFinished(selection);
                }
            }
            catch (OperationCanceledException)
            {
                busy = installing = false;
                options.Visible = true; progressPage.Visible = false;
                primary.Enabled = primary.Visible = cancel.Enabled = cancel.Visible = true; cancel.Location = new Point(504, 17); cancel.Text = "Cancel"; footerText.Text = "Downloads cancelled. You can change the options and retry.";
            }
            catch (Exception ex)
            {
                busy = installing = false;
                pageTitle.Text = "Setup could not finish";
                pageSubtitle.Text = "Review the error below, then retry.";
                footerText.Text = "Log: " + log;
                installRow.Set(installRow.Value, ex.Message);
                primary.Text = "Retry"; primary.Enabled = primary.Visible = cancel.Enabled = cancel.Visible = true; cancel.Location = new Point(504, 17);
            }
            finally { cancellation.Dispose(); cancellation = null; }
        }

        // These previews change display state only; they never download or install.
        internal void Preview(string state)
        {
            if (state == "warning") { ytCheck.Checked = ffCheck.Checked = false; }
            if (state == "download" || state == "install" || state == "finish")
            {
                var selection = new DownloadSelection(true, true); ShowProgress(selection);
                ytRow.Set(state == "download" ? 58 : 100, state == "download" ? "Downloading…" : "Downloaded and verified");
                ffRow.Set(state == "download" ? 34 : 100, state == "download" ? "Downloading…" : "Downloaded and verified");
                if (state == "install") { installRow.Set(63, "Installing…"); cancel.Visible = false; pageSubtitle.Text = "Installing application files and the tools you selected."; footerText.Text = "Please wait while setup completes."; }
                if (state == "finish") { ExitCode = 0; ShowFinished(selection); }
            }
        }
    }

    internal sealed class ProgressRow : Panel
    {
        readonly Label nameLabel, statusLabel, percentLabel;
        readonly SmoothProgress bar;
        public int Value => bar.Value;
        public ProgressRow(string name)
        {
            Size = new Size(716, 78); BackColor = Color.White;
            nameLabel = new Label { Text = name, Bounds = new Rectangle(18, 13, 150, 23), Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), ForeColor = Color.FromArgb(27, 38, 58) };
            statusLabel = new Label { Bounds = new Rectangle(180, 15, 440, 21), TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(100, 114, 134), AutoEllipsis = true };
            percentLabel = new Label { Bounds = new Rectangle(624, 13, 74, 23), TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            bar = new SmoothProgress { Bounds = new Rectangle(18, 49, 680, 8) };
            Controls.Add(nameLabel); Controls.Add(statusLabel); Controls.Add(percentLabel); Controls.Add(bar);
            Paint += (s, e) => { using (var pen = new Pen(Color.FromArgb(222, 229, 238))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); };
        }
        public void Set(int value, string status, bool skipped = false)
        {
            bar.Value = Math.Max(0, Math.Min(100, value));
            statusLabel.Text = status;
            percentLabel.Text = skipped ? "—" : bar.Value + "%";
            percentLabel.ForeColor = skipped ? Color.FromArgb(100, 114, 134) : Color.FromArgb(35, 103, 218);
            bar.Invalidate();
        }
    }
    internal sealed class SmoothProgress : Control
    {
        public int Value { get; set; }
        public SmoothProgress() { DoubleBuffered = true; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var background = new SolidBrush(Color.FromArgb(231, 237, 245))) e.Graphics.FillRectangle(background, ClientRectangle);
            if (Value > 0) using (var fill = new SolidBrush(Color.FromArgb(35, 103, 218))) e.Graphics.FillRectangle(fill, 0, 0, Width * Value / 100, Height);
        }
    }
}
