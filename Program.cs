using System;
using System.Threading;
using System.Windows.Forms;

namespace EchoCore.Bootstrapper
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var mutex = new Mutex(false, "EchoCoreBootstrapper"))
            {
                LaunchRequest request;
                try
                {
                    request = LaunchRequest.Parse(args);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "EchoCore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }

                if (request.Kind == LaunchKind.Uninstall)
                {
                    var answer = MessageBox.Show("Remove EchoCore from this computer?", "EchoCore",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer == DialogResult.Yes)
                        Installer.Uninstall();
                    return 0;
                }

                var window = new ProgressWindow();
                var installer = new Installer(window);
                var failed = false;

                window.Shown += async (sender, e) =>
                {
                    try
                    {
                        mutex.WaitOne();
                        await installer.RunAsync(request);
                    }
                    catch (Exception ex)
                    {
                        failed = true;
                        MessageBox.Show(window, ex.Message, "EchoCore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                        window.Close();
                    }
                };

                Application.Run(window);
                return failed ? 1 : 0;
            }
        }
    }
}
