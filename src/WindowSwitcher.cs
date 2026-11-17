using System;
using System.Collections.Generic;

namespace FastSkip
{
    internal interface IWindowSwitchApi
    {
        IntPtr Foreground();
        IntPtr Ancestor(IntPtr window, uint kind);
        bool Available(WindowEntry entry);
        bool Minimized(IntPtr window);
        void Restore(IntPtr window);
        bool Activate(IntPtr window);
    }

    internal sealed class WindowSwitchApi : IWindowSwitchApi
    {
        public IntPtr Foreground() { return Native.GetForegroundWindow(); }
        public IntPtr Ancestor(IntPtr window,uint kind) { return Native.GetAncestor(window,kind); }
        public bool Available(WindowEntry entry) { return entry.AliveFast() && Native.IsWindowVisible(entry.Handle); }
        public bool Minimized(IntPtr window) { return Native.IsIconic(window); }
        public void Restore(IntPtr window) { Native.ShowWindowAsync(window,9); }
        public bool Activate(IntPtr window) { return Native.SetForegroundWindow(window); }
    }

    internal sealed class WindowSwitcher
    {
        readonly IWindowSwitchApi api;
        internal WindowSwitcher(IWindowSwitchApi api) { this.api=api; }
        internal bool Cycle(IList<WindowEntry> windows,out WindowEntry target)
        {
            target=null;
            if(windows.Count==0) return false;
            IntPtr current=api.Foreground();
            int index=Index(windows,current);
            if(index<0) index=Index(windows,api.Ancestor(current,2));
            if(index<0) index=Index(windows,api.Ancestor(current,3));
            // Validate candidates in order; no process queries, list copies or timers.
            for(int step=1;step<=windows.Count;step++) {
                WindowEntry next=windows[(index+step)%windows.Count];
                if(!api.Available(next)) continue;
                target=next;
                if(api.Minimized(next.Handle)) api.Restore(next.Handle);
                return api.Activate(next.Handle);
            }
            return false;
        }
        static int Index(IList<WindowEntry> windows,IntPtr handle)
        { for(int i=0;i<windows.Count;i++) if(windows[i].Handle==handle) return i; return -1; }
    }
}
