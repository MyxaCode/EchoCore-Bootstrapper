using System;
using System.Drawing;
using System.Windows.Forms;

namespace EchoCore.Bootstrapper
{
    internal sealed class ProgressWindow : Form
    {
        private readonly Label _status;
        private readonly ProgressBar _progress;

        public ProgressWindow()
        {
            Text = "EchoCore";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(500, 280);
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            var logo = new PictureBox
            {
                Image = LoadLogo(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(90, 40),
                Size = new Size(320, 80),
            };

            _status = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(40, 140),
                Size = new Size(420, 24),
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(50, 50, 50),
                Text = "Starting EchoCore...",
            };

            _progress = new ProgressBar
            {
                Location = new Point(75, 175),
                Size = new Size(350, 20),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
            };

            var cancel = new Button
            {
                Text = "Cancel",
                Location = new Point(400, 238),
                Size = new Size(85, 28),
                FlatStyle = FlatStyle.System,
            };
            cancel.Click += (sender, e) => Environment.Exit(0);

            Controls.Add(logo);
            Controls.Add(_status);
            Controls.Add(_progress);
            Controls.Add(cancel);
        }

        private static Image LoadLogo()
        {
            var stream = typeof(ProgressWindow).Assembly.GetManifestResourceStream("EchoCore.Bootstrapper.logo.png");
            return stream == null ? null : Image.FromStream(stream);
        }

        public void SetStatus(string text)
        {
            _status.Text = text;
        }

        public void SetProgress(int percent)
        {
            if (percent < 0)
            {
                _progress.Style = ProgressBarStyle.Marquee;
                return;
            }

            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = percent > 100 ? 100 : percent;
        }

        public void ShowMessage(string text)
        {
            MessageBox.Show(this, text, "EchoCore", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
