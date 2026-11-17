using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using FastSkip;

internal static class TestRunner
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            Application.EnableVisualStyles();
            Application.Run(new Form { Text = "FastSkip Test " + args[0], Width = 480, Height = 260, Controls = { new Label { Text = "Temporary test window " + args[0], Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleCenter } } });
            return 0;
        }
        string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FastSkip-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        try
        {
            Check(Settings.Load(path).Display() == "Alt + X", "missing settings defaults to Alt+X");
            new Settings { Key = (uint)Keys.K, Modifiers = 3 }.Save(path);
            Check(Settings.Load(path).Display() == "Ctrl + Alt + K", "custom shortcut round trip");
            new Settings().Save(path);
            Check(Settings.Load(path).Key == (uint)Keys.X, "replacement of existing settings");
            File.WriteAllText(path, "broken XML");
            Check(Settings.Load(path).Key == (uint)Keys.X, "corrupt settings recover to defaults");
            new Settings { Modifiers = 0 }.Save(path);
            Check(Settings.Load(path).Modifiers == 1, "invalid modifier settings recover");
            Check(!Settings.AllowedKey((uint)Keys.F12), "reserved F12 excluded");
            var a = new WindowEntry { Handle = new IntPtr(1) };
            var b = new WindowEntry { Handle = new IntPtr(2) };
            var c = new WindowEntry { Handle = new IntPtr(3) };
            var list = new List<WindowEntry> { a, b, c };
            Check(MainForm.NextWindow(list, a.Handle) == b, "advance from current window");
            Check(MainForm.NextWindow(list, c.Handle) == a, "wrap to first window");
            Check(MainForm.NextWindow(list, new IntPtr(99)) == a, "unselected foreground starts at first");
            list.Remove(b);
            Check(MainForm.NextWindow(list, a.Handle) == c, "skip unavailable window");
            list.Reverse();
            Check(MainForm.NextWindow(list, IntPtr.Zero) == c, "use configured order");
            Check(MainForm.NextWindow(new List<WindowEntry>(), IntPtr.Zero) == null, "empty selection handled");
            Check(MainForm.NextWindow(new List<WindowEntry> { a }, a.Handle) == a, "single window handled");
            Check(!new WindowEntry { Handle = IntPtr.Zero }.Alive(), "invalid window rejected");
            Check(Native.IsSpire("Modded Slay the Spire", "LWJGL"), "recognize modded game");
            Check(!Native.IsSpire("ModTheSpire 3.30.3", "SunAwtFrame"), "launcher is not game");
            Check(!Native.IsSpire("Slay the Spire - Google Chrome", "Chrome_WidgetWin_1"), "browser title is not game");
            Check(!Native.IsShellSurface("LWJGL"), "game is selectable");
            Check(!Native.IsShellSurface("SunAwtFrame"), "owned Java windows are selectable");
            Check(Native.IsShellSurface("Shell_TrayWnd"), "taskbar is not an app window");
            TestSwitching();
            using(var service=new HotkeyService(delegate(WindowEntry entry,bool success) { })) {
                service.SetWindows(new WindowEntry[0]);
                service.Suspend();
            }
            Check(true,"background message thread starts and shuts down without registering a hotkey");
            Console.WriteLine("Passed " + count + " checks."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(folder)) Directory.Delete(folder); }
    }
    static void TestSwitching()
    {
        var a=new WindowEntry { Handle=new IntPtr(11) };
        var b=new WindowEntry { Handle=new IntPtr(12) };
        var c=new WindowEntry { Handle=new IntPtr(13) };
        var windows=new[] { a,b,c };
        var api=new FakeSwitchApi { Current=a.Handle };
        var switcher=new WindowSwitcher(api);
        WindowEntry target;
        Check(switcher.Cycle(windows,out target) && target==b && api.Activated==b.Handle,"hot path directly activates next window");
        Check(api.Checks==1,"validate only next candidate instead of all windows");
        api.Current=c.Handle;
        Check(switcher.Cycle(windows,out target) && target==a,"hot path wraps");
        api.Current=a.Handle; b.Closed=true;
        Check(switcher.Cycle(windows,out target) && target==c,"hot path skips destroyed windows");
        b.Closed=false; api.Current=new IntPtr(99); api.Owner=a.Handle;
        Check(switcher.Cycle(windows,out target) && target==b,"resolve selected window from foreground popup owner");
        api.Current=b.Handle; api.Owner=a.Handle;
        Check(switcher.Cycle(windows,out target) && target==c,"exact selected popup takes priority over owner");
        api.Current=a.Handle; api.MinimizedHandle=b.Handle;
        Check(switcher.Cycle(windows,out target) && api.Restored==b.Handle,"restore minimized target before activating");
        api.FailActivation=true;
        Check(!switcher.Cycle(windows,out target) && target==b,"report actual activation failure");
        api.FailActivation=false; a.Closed=b.Closed=c.Closed=true;
        Check(!switcher.Cycle(windows,out target) && target==null,"all closed windows produce no target");
        Check(!switcher.Cycle(new WindowEntry[0],out target),"empty hot path handled");
        a.Closed=b.Closed=c.Closed=false;
        api.Owner=IntPtr.Zero; api.MinimizedHandle=IntPtr.Zero; api.Current=a.Handle;
        for(int i=0;i<1000;i++) switcher.Cycle(windows,out target);
        var clock=Stopwatch.StartNew(); int collections=GC.CollectionCount(0);
        for(int i=0;i<200000;i++) switcher.Cycle(windows,out target);
        clock.Stop();
        Console.WriteLine("Routing benchmark (fake native API, excludes OS/display): {0:F3} us/call; Gen0 collections: {1}",clock.Elapsed.TotalMilliseconds*1000/200000,GC.CollectionCount(0)-collections);
    }
    sealed class FakeSwitchApi : IWindowSwitchApi
    {
        internal IntPtr Current,Owner,Activated,Restored,MinimizedHandle;
        internal bool FailActivation;
        internal int Checks;
        public IntPtr Foreground() { return Current; }
        public IntPtr Ancestor(IntPtr window,uint kind) { return Owner; }
        public bool Available(WindowEntry entry) { Checks++; return !entry.Closed; }
        public bool Minimized(IntPtr window) { return window==MinimizedHandle; }
        public void Restore(IntPtr window) { Restored=window; }
        public bool Activate(IntPtr window) { Activated=window; return !FailActivation; }
    }
}
