using System.Threading;
using System.Windows;

namespace KT_Explorer;

public partial class App : Application
{
    // 単一起動: トレイに常駐する以上、2つ目を起動すると取り込みフックが二重になるため防ぐ
    private const string MutexName = @"Local\KT_Explorer_SingleInstance";
    private const string ShowEventName = @"Local\KT_Explorer_Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            // 既に起動中: そちらのウィンドウを表示させて、自分は終了する
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* 起動直後などで未作成 */ }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var waiter = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray());
        })
        { IsBackground = true, Name = "KT_Explorer show-listener" };
        waiter.Start();

        base.OnStartup(e);

        // メインウィンドウを自分で作る(自動起動のときは表示しない)
        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Contains(Services.StartupService.StartupArg))
        {
            // 表示しないままウィンドウのハンドルだけ作る(トレイ・Explorer 取り込み・仮想デスクトップ追従を始めるため)。
            // トレイのアイコンをクリックすると表示される。
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        }
        else
        {
            window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { /* 所有していなければ無視 */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
