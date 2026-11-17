using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FastSkip;
internal static class WindowProbe
{
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int size);
    static int Main()
    {
        var listed = Native.Windows();
        bool found = false;
        Native.EnumWindows(delegate(IntPtr hwnd, IntPtr p) {
            string title = Native.Title(hwnd);
            if (title.IndexOf("spire", StringComparison.OrdinalIgnoreCase) < 0 && title.IndexOf("杀戮", StringComparison.OrdinalIgnoreCase) < 0) return true;
            var cls = new StringBuilder(256); GetClassName(hwnd, cls, cls.Capacity);
            int cloak; Native.DwmGetWindowAttribute(hwnd,14,out cloak,4);
            bool included = listed.Any(x => x.Handle == hwnd);
            Console.WriteLine("{0} | class={1} visible={2} owner={3} exstyle=0x{4:X} cloak={5} listed={6}", title, cls, Native.IsWindowVisible(hwnd), Native.GetWindow(hwnd,4), Native.GetWindowLong(hwnd,-20), cloak, included);
            if (title.IndexOf("Slay the Spire", StringComparison.OrdinalIgnoreCase)>=0) found = included;
            return true;
        }, IntPtr.Zero);
        Console.WriteLine(found ? "PASS game window is selectable" : "FAIL game window missing");
        return found ? 0 : 1;
    }
}
