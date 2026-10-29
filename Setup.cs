using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Calculator Setup")]
[assembly: AssemblyProduct("Calculator")]
[assembly: AssemblyDescription("Setup for Calculator")]
[assembly: AssemblyCompany("Calculator")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace CalculatorSetup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool uninstall = false;
            bool silent = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "/uninstall" || a == "-uninstall" || a == "--uninstall") uninstall = true;
                else if (a == "/silent" || a == "-silent" || a == "/verysilent" || a == "--silent") silent = true;
            }

            if (uninstall)
            {
                Installer.Uninstall();
                return;
            }

            if (silent)
            {
                Installer.Install();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    internal static class Installer
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Calculator";

        public static string InstallDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Path.Combine("Programs", "Calculator"));
            }
        }

        public static string TargetExe
        {
            get { return Path.Combine(InstallDir, "Calculator.exe"); }
        }

        private static string DesktopLink
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "Calculator.lnk");
            }
        }

        private static string StartLink
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "Calculator.lnk");
            }
        }

        public static void Install()
        {
            byte[] payload = ReadResource("CalculatorPayload.exe");
            Directory.CreateDirectory(InstallDir);
            File.WriteAllBytes(TargetExe, payload);

            try
            {
                string self = Application.ExecutablePath;
                string copy = Path.Combine(InstallDir, "Setup.exe");
                if (!string.Equals(self, copy, StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, copy, true);
            }
            catch (IOException)
            {
            }

            CreateShortcut(DesktopLink, TargetExe);
            CreateShortcut(StartLink, TargetExe);

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "Calculator");
                key.SetValue("DisplayVersion", "1.0");
                key.SetValue("Publisher", "Calculator");
                key.SetValue("DisplayIcon", TargetExe + ",0");
                key.SetValue("InstallLocation", InstallDir);
                key.SetValue("UninstallString", "\"" + Path.Combine(InstallDir, "Setup.exe") + "\" /uninstall");
                key.SetValue("QuietUninstallString", "\"" + Path.Combine(InstallDir, "Setup.exe") + "\" /uninstall /silent");
                key.SetValue("EstimatedSize", Math.Max(1, payload.Length / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void Uninstall()
        {
            TryDelete(DesktopLink);
            TryDelete(StartLink);

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            }
            catch (Exception)
            {
            }

            string selfDir = Path.GetDirectoryName(Application.ExecutablePath);
            bool runningFromInstallDir = selfDir != null &&
                string.Equals(selfDir.TrimEnd('\\'), InstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

            if (runningFromInstallDir)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(
                        "cmd.exe",
                        "/c ping 127.0.0.1 -n 2 > nul & rmdir /s /q \"" + InstallDir + "\"");
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                }
                catch (Exception)
                {
                }
            }
            else
            {
                try
                {
                    if (Directory.Exists(InstallDir))
                        Directory.Delete(InstallDir, true);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void CreateShortcut(string linkPath, string target)
        {
            string folder = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object link = shellType.InvokeMember(
                "CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
            Type linkType = link.GetType();
            linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
            linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
            linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
            linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Calculator" });
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        public static byte[] ReadResource(string name)
        {
            using (Stream stream = typeof(Installer).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("Missing resource: " + name);
                using (MemoryStream memory = new MemoryStream())
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        memory.Write(buffer, 0, read);
                    return memory.ToArray();
                }
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly float dpi;
        private readonly PictureBox iconBox;
        private readonly Label headingLabel;
        private readonly Label versionLabel;
        private readonly Label introLabel;
        private readonly Label locationCaption;
        private readonly TextBox locationBox;
        private readonly ProgressBar progressBar;
        private readonly Label statusLabel;
        private readonly Button primaryButton;
        private readonly Button cancelButton;
        private readonly CheckBox launchCheck;

        private bool installed;

        public SetupForm()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                dpi = g.DpiX / 96f;

            Text = "Calculator Setup";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(S(480), S(330));
            BackColor = SystemColors.Window;

            Icon = LoadIcon();

            int pad = S(20);
            int contentWidth = ClientSize.Width - pad * 2;

            iconBox = new PictureBox();
            iconBox.Location = new Point(pad, pad);
            iconBox.Size = new Size(S(64), S(64));
            iconBox.SizeMode = PictureBoxSizeMode.Zoom;
            iconBox.Image = LoadLogo();
            Controls.Add(iconBox);

            int textX = iconBox.Right + S(16);

            headingLabel = new Label();
            headingLabel.AutoSize = true;
            headingLabel.Font = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Point);
            headingLabel.Text = "Calculator";
            headingLabel.Location = new Point(textX, pad - S(3));
            Controls.Add(headingLabel);

            versionLabel = new Label();
            versionLabel.AutoSize = true;
            versionLabel.ForeColor = SystemColors.GrayText;
            versionLabel.Text = "Version 1.0";
            versionLabel.Location = new Point(textX, pad + S(36));
            Controls.Add(versionLabel);

            Panel divider = new Panel();
            divider.BackColor = SystemColors.ControlDark;
            divider.Location = new Point(pad, pad + S(78));
            divider.Size = new Size(contentWidth, Math.Max(1, S(1)));
            Controls.Add(divider);

            introLabel = new Label();
            introLabel.Location = new Point(pad, divider.Bottom + S(16));
            introLabel.Size = new Size(contentWidth, S(46));
            introLabel.Text = "This wizard will install Calculator on your computer for your Windows account. Shortcuts will be added to your desktop and Start menu. No administrator rights are required.";
            Controls.Add(introLabel);

            locationCaption = new Label();
            locationCaption.AutoSize = true;
            locationCaption.Text = "Install location:";
            locationCaption.Location = new Point(pad, introLabel.Bottom + S(10));
            Controls.Add(locationCaption);

            locationBox = new TextBox();
            locationBox.ReadOnly = true;
            locationBox.TabStop = false;
            locationBox.BackColor = SystemColors.Window;
            locationBox.Text = Installer.InstallDir;
            locationBox.Location = new Point(pad, locationCaption.Bottom + S(4));
            locationBox.Width = contentWidth;
            Controls.Add(locationBox);

            progressBar = new ProgressBar();
            progressBar.Location = new Point(pad, locationBox.Bottom + S(14));
            progressBar.Size = new Size(contentWidth, S(14));
            progressBar.Visible = false;
            Controls.Add(progressBar);

            statusLabel = new Label();
            statusLabel.Location = new Point(pad, progressBar.Bottom + S(8));
            statusLabel.Size = new Size(contentWidth, S(18));
            statusLabel.Text = "";
            Controls.Add(statusLabel);

            launchCheck = new CheckBox();
            launchCheck.AutoSize = true;
            launchCheck.Text = "Launch Calculator";
            launchCheck.Checked = true;
            launchCheck.Visible = false;
            launchCheck.Location = new Point(pad, ClientSize.Height - pad - S(22));
            Controls.Add(launchCheck);

            cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Size = new Size(S(88), S(28));
            cancelButton.Location = new Point(
                ClientSize.Width - pad - cancelButton.Width,
                ClientSize.Height - pad - cancelButton.Height);
            cancelButton.Click += CancelClicked;
            Controls.Add(cancelButton);

            primaryButton = new Button();
            primaryButton.Text = "Install";
            primaryButton.Size = new Size(S(88), S(28));
            primaryButton.Location = new Point(cancelButton.Left - S(8) - primaryButton.Width, cancelButton.Top);
            primaryButton.Click += PrimaryClicked;
            Controls.Add(primaryButton);

            AcceptButton = primaryButton;
            CancelButton = cancelButton;
            ActiveControl = primaryButton;
        }

        private int S(float value)
        {
            return (int)Math.Round(value * dpi);
        }

        private void CancelClicked(object sender, EventArgs e)
        {
            Close();
        }

        private void PrimaryClicked(object sender, EventArgs e)
        {
            if (installed)
            {
                if (launchCheck.Checked)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(Installer.TargetExe) { UseShellExecute = true });
                    }
                    catch (Exception)
                    {
                    }
                }
                Close();
                return;
            }

            primaryButton.Enabled = false;
            cancelButton.Enabled = false;
            progressBar.Visible = true;
            progressBar.Style = ProgressBarStyle.Marquee;
            statusLabel.Text = "Installing Calculator...";
            Application.DoEvents();

            try
            {
                Installer.Install();
                installed = true;
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Maximum = 100;
                progressBar.Value = 100;
                statusLabel.Text = "Calculator has been installed successfully.";
                primaryButton.Text = "Finish";
                primaryButton.Enabled = true;
                cancelButton.Visible = false;
                launchCheck.Visible = true;
                AcceptButton = primaryButton;
            }
            catch (IOException)
            {
                InstallFailed("Please close Calculator, then try again.", MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                InstallFailed(ex.Message, MessageBoxIcon.Error);
            }
        }

        private void InstallFailed(string message, MessageBoxIcon icon)
        {
            progressBar.Visible = false;
            statusLabel.Text = "Installation failed.";
            primaryButton.Enabled = true;
            cancelButton.Enabled = true;
            MessageBox.Show(this, message, "Calculator Setup", MessageBoxButtons.OK, icon);
        }

        private static Icon LoadIcon()
        {
            using (Stream stream = typeof(SetupForm).Assembly.GetManifestResourceStream("SetupIcon.ico"))
            {
                if (stream != null) return new Icon(stream);
            }
            return SystemIcons.Application;
        }

        private static Bitmap LoadLogo()
        {
            try
            {
                byte[] ico = Installer.ReadResource("SetupIcon.ico");
                int count = BitConverter.ToUInt16(ico, 4);
                int bestOffset = -1;
                int bestSize = 0;
                for (int i = 0; i < count; i++)
                {
                    int entry = 6 + i * 16;
                    int width = ico[entry] == 0 ? 256 : ico[entry];
                    int height = ico[entry + 1] == 0 ? 256 : ico[entry + 1];
                    if (width == height && width > bestSize)
                    {
                        bestSize = width;
                        bestOffset = entry;
                    }
                }
                if (bestOffset >= 0)
                {
                    int bytes = BitConverter.ToInt32(ico, bestOffset + 8);
                    int dataOffset = BitConverter.ToInt32(ico, bestOffset + 12);
                    using (MemoryStream stream = new MemoryStream(ico, dataOffset, bytes))
                    using (Image image = Image.FromStream(stream))
                        return new Bitmap(image);
                }
            }
            catch (Exception)
            {
            }
            return null;
        }
    }
}
