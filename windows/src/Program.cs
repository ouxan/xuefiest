using System;
using System.Net;
using System.Windows;
using System.Windows.Threading;

[assembly: System.Reflection.AssemblyTitle("Xuefiest")]
[assembly: System.Reflection.AssemblyDescription("Keeps Steam games on their installed version by writing up-to-date app manifests.")]
[assembly: System.Reflection.AssemblyProduct("Xuefiest")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace Xuefiest
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Resources = (ResourceDictionary)Ui.Load("Theme.xaml");
            L.Apply(Settings.Load().Get("language"));
            app.DispatcherUnhandledException += OnUnhandled;
            app.Run(new MainWindow());
        }

        private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            try
            {
                Dialog.Info(Application.Current.MainWindow, DialogKind.Error, L.T("Err.Unexpected"), e.Exception.Message);
            }
            catch (Exception)
            {
                MessageBox.Show(e.Exception.ToString(), "Xuefiest");
            }
        }
    }
}
