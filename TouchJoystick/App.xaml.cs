using System;
using System.IO;
using System.Windows;

namespace TouchJoystick
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                string msg = ev.ExceptionObject.ToString();
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), msg); } catch {}
                MessageBox.Show(msg, "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, ev) =>
            {
                string msg = ev.Exception.ToString();
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), msg); } catch {}
                MessageBox.Show(msg, "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ev.Handled = true;
            };

            base.OnStartup(e);
        }
    }
}
