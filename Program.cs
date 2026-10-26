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
            try
            {
                var updater=new SelfUpdater(SelfUpdater.DefaultRoot);
                if(args.Length==2 && args[0]=="--apply-update"){updater.Apply(args[1]);return 0;}
                if(args.Length==2 && args[0]=="--resume-update")args=updater.Resume(args[1]);
            }
            catch(Exception error){MessageBox.Show(error.Message,"EchoCore",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
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
                        await installer.RunAsync(request,args);
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
