using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FastSkip
{
    internal static class Native
    {
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr param);
        internal delegate void EventProc(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr dll, EventProc callback, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("dwmapi.dll")] internal static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
        [DllImport("dwmapi.dll")] internal static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
        [DllImport("dwmapi.dll")] internal static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out NativeSize size);
        [DllImport("dwmapi.dll")] internal static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref ThumbnailProperties properties);
        [StructLayout(LayoutKind.Sequential)] internal struct NativeSize { internal int Width, Height; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct ThumbnailProperties
        {
            internal uint Flags;
            internal Rect Destination, Source;
            internal byte Opacity;
            [MarshalAs(UnmanagedType.Bool)] internal bool Visible;
            [MarshalAs(UnmanagedType.Bool)] internal bool ClientOnly;
        }
        internal static bool IsSpire(string title, string windowClass)
        {
            return (windowClass == "LWJGL" || windowClass == "GLFW30" || windowClass == "SDL_app") &&
                (title.IndexOf("Slay the Spire", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("杀戮尖塔", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static string Title(IntPtr hwnd)
        {
            var text = new StringBuilder(1024);
            GetWindowText(hwnd, text, text.Capacity);
            return text.ToString();
        }

        internal static List<WindowEntry> Windows()
        {
            var result = new List<WindowEntry>();
            uint own = (uint)Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr hwnd, IntPtr ignored) {
                uint pid;
                uint thread = GetWindowThreadProcessId(hwnd, out pid);
                if (pid == own || !IsWindowVisible(hwnd)) return true;
                int cloaked;
                if (DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                string title = Title(hwnd);
                var cls = new StringBuilder(256); GetClassName(hwnd, cls, cls.Capacity);
                string className = cls.ToString();
                if (IsShellSurface(className)) return true;
                Rect bounds;
                if (!GetWindowRect(hwnd, out bounds) || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return true;
                string app = "应用";
                long started = 0;
                try { using (var p = Process.GetProcessById((int)pid)) { app = p.ProcessName; started = p.StartTime.ToFileTimeUtc(); } }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { return true; }
                catch (ArgumentException) { return true; }
                if (String.IsNullOrWhiteSpace(title)) title = app + " · 无标题窗口";
                result.Add(new WindowEntry { Handle = hwnd, ProcessId = pid, ThreadId = thread, Started = started, App = app, Title = title, Game = IsSpire(title, className) });
                return true;
            }, IntPtr.Zero);
            return result;
        }
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        internal static bool IsShellSurface(string cls)
        {
            // Exclude desktop chrome only, never applications based on owner/tool style.
            return cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                cls == "Shell_SecondaryTrayWnd" || cls == "tooltips_class32" || cls == "#32768" ||
                cls == "SysShadow";
        }
    }

    internal sealed class WindowEntry
    {
        internal IntPtr Handle;
        internal uint ProcessId, ThreadId;
        internal long Started;
        internal string App, Title;
        internal volatile bool Closed;
        internal bool Game;
        internal string DisplayApp { get { return Game ? "杀戮尖塔" : (Title.StartsWith("ModTheSpire", StringComparison.OrdinalIgnoreCase) ? "ModTheSpire · 日志 / 启动器" : App); } }
        internal bool Alive()
        {
            if (!AliveFast()) return false;
            if (Started != 0)
            {
                try { using (var p = Process.GetProcessById((int)ProcessId)) if (p.StartTime.ToFileTimeUtc() != Started) return false; }
                catch { return false; }
            }
            return true;
        }
        internal bool AliveFast()
        {
            uint pid;
            return !Closed && Native.IsWindow(Handle) && Native.GetWindowThreadProcessId(Handle,out pid)==ThreadId && pid==ProcessId;
        }
    }
}
