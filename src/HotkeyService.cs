using System;
using System.Threading;
using System.Windows.Forms;

namespace FastSkip
{
    // Own message loop: rendering the gallery cannot delay a hotkey message.
    internal sealed class HotkeyService : IDisposable
    {
        const uint Configure=0x8001, Stop=0x8002;
        readonly Thread thread;
        readonly ManualResetEvent ready=new ManualResetEvent(false);
        readonly WindowSwitcher switcher=new WindowSwitcher(new WindowSwitchApi());
        volatile WindowEntry[] windows=new WindowEntry[0];
        readonly Action<WindowEntry,bool> completed;
        MessageWindow receiver;
        Exception startupError;
        internal HotkeyService(Action<WindowEntry,bool> completed)
        {
            this.completed=completed;
            thread=new Thread(Run) { IsBackground=true,Name="FastSkip hotkey" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.WaitOne(); ready.Dispose();
            if(startupError!=null) throw new InvalidOperationException("快捷键线程启动失败。",startupError);
        }
        void Run()
        {
            try { receiver=new MessageWindow(this); }
            catch(Exception ex) { startupError=ex; ready.Set(); return; }
            ready.Set();
            try { Application.Run(); }
            finally { receiver.Release(); }
        }
        internal void SetWindows(WindowEntry[] selection) { windows=selection; }
        internal bool Register(Settings settings)
        { return Native.SendMessage(receiver.Handle,Configure,new IntPtr(settings.Modifiers),new IntPtr(settings.Key))!=IntPtr.Zero; }
        internal void Suspend() { Native.SendMessage(receiver.Handle,Configure,IntPtr.Zero,IntPtr.Zero); }
        public void Dispose()
        { Native.SendMessage(receiver.Handle,Stop,IntPtr.Zero,IntPtr.Zero); thread.Join(); }
        sealed class MessageWindow : NativeWindow
        {
            readonly HotkeyService owner;
            int activeId=100;
            bool registered;
            internal MessageWindow(HotkeyService owner)
            {
                this.owner=owner;
                CreateHandle(new CreateParams { Caption="FastSkip.Hotkey",Parent=new IntPtr(-3) });
            }
            protected override void WndProc(ref Message m)
            {
                if(m.Msg==Configure) {
                    if(m.LParam==IntPtr.Zero) { if(registered) Native.UnregisterHotKey(Handle,activeId); registered=false; m.Result=new IntPtr(1); return; }
                    int next=activeId==100?101:100;
                    bool success=Native.RegisterHotKey(Handle,next,(uint)m.WParam.ToInt64()|0x4000,(uint)m.LParam.ToInt64());
                    if(success) { if(registered) Native.UnregisterHotKey(Handle,activeId); activeId=next; registered=true; }
                    m.Result=success?new IntPtr(1):IntPtr.Zero; return;
                }
                if(m.Msg==0x312 && registered && m.WParam.ToInt32()==activeId) {
                    WindowEntry target; bool success=owner.switcher.Cycle(owner.windows,out target);
                    owner.completed(target,success); return;
                }
                if(m.Msg==Stop) { Application.ExitThread(); return; }
                base.WndProc(ref m);
            }
            internal void Release() { if(registered) Native.UnregisterHotKey(Handle,activeId); DestroyHandle(); }
        }
    }
}
