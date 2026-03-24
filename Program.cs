using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace leeyez_kai;

static class Program
{
    [STAThread]
    static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 全未処理例外をキャッチしてログに残す（クラッシュ防止）
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) =>
        {
            Logger.Log($"ThreadException: {e.Exception}");
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Logger.Log($"UnhandledException: {e.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Logger.Log($"UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
