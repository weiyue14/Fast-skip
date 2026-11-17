using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Markup;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using I=System.Windows.Input;

namespace FastSkip
{
    internal sealed class MainForm : Form
    {
        readonly string settingsPath;
        Settings settings;
        internal readonly List<WindowEntry> selected=new List<WindowEntry>();
        List<WindowEntry> available=new List<WindowEntry>();
        readonly W.FrameworkElement view;
        readonly C.WrapPanel gallery;
        readonly C.StackPanel queue;
        readonly C.ScrollViewer scroll;
        readonly List<Preview> previews=new List<Preview>();
        internal readonly NotifyIcon tray;
        readonly Timer timer;
        readonly HotkeyService hotkeys;
        readonly Dictionary<IntPtr,WindowEntry> tracked=new Dictionary<IntPtr,WindowEntry>();
        readonly Native.EventProc destroyCallback;
        IntPtr eventHook;
        bool exiting,balloonShown,settingsOpen,loaded;
        internal bool hotkeyActive;

        internal MainForm(string path)
        {
            settingsPath=path; settings=Settings.Load(path);
            Text="Fast Skip · 窗口切换"; AutoScaleMode=AutoScaleMode.None; StartPosition=FormStartPosition.CenterScreen;
            FormBorderStyle=FormBorderStyle.None;
            float scale; using(var g=CreateGraphics()) scale=g.DpiX/96f;
            ClientSize=new Size((int)(1120*scale),(int)(780*scale)); MinimumSize=new Size((int)(1000*scale),(int)(650*scale));
            using(var iconStream=typeof(MainForm).Assembly.GetManifestResourceStream("FastSkip.Icon.ico")) {
                if(iconStream==null) Icon=(Icon)SystemIcons.Application.Clone();
                else using(var icon=new Icon(iconStream)) Icon=(Icon)icon.Clone();
            }
            using(var stream=typeof(MainForm).Assembly.GetManifestResourceStream("FastSkip.UI.xaml")) view=(W.FrameworkElement)XamlReader.Load(stream);
            Controls.Add(new ElementHost { Dock=DockStyle.Fill,Child=view });
            gallery=Find<C.WrapPanel>("Gallery"); queue=Find<C.StackPanel>("Queue"); scroll=Find<C.ScrollViewer>("GalleryScroll");
            Find<C.Button>("RefreshButton").Click+=delegate { RefreshWindows(); };
            Find<C.Button>("ClearButton").Click+=delegate { selected.Clear(); RefreshWindows(); };
            Find<C.Button>("SettingsButton").Click+=delegate { OpenSettings(); };
            Find<C.Button>("MinimizeButton").Click+=delegate { HideToTray(); };
            Find<C.Button>("CloseButton").Click+=delegate { Close(); };
            Find<C.Grid>("WindowHeader").MouseLeftButtonDown+=delegate(object sender,I.MouseButtonEventArgs e) {
                var source=e.OriginalSource as W.DependencyObject;
                while(source!=null) {
                    if(source is C.Button) return;
                    source=source is M.Visual ? M.VisualTreeHelper.GetParent(source) : W.LogicalTreeHelper.GetParent(source);
                }
                e.Handled=true;
                Native.ReleaseCapture(); Native.SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);
            };
            scroll.ScrollChanged+=delegate { UpdatePreviews(); };
            scroll.SizeChanged+=delegate { SizeCards(); UpdatePreviews(); };
            view.LayoutUpdated+=delegate { UpdatePreviews(); };
            var menu=new ContextMenuStrip();
            menu.Items.Add("打开主界面",null,delegate { ShowSettings(); });
            menu.Items.Add("快捷键设置",null,delegate { ShowSettings(); OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出",null,delegate { ExitApp(); });
            tray=new NotifyIcon { Icon=Icon,Text="Fast Skip · 窗口切换",Visible=true,ContextMenuStrip=menu };
            tray.DoubleClick+=delegate { ShowSettings(); };
            destroyCallback=delegate(IntPtr h,uint e,IntPtr hwnd,int obj,int child,uint t,uint time) {
                if(obj!=0 || child!=0) return;
                WindowEntry entry;
                if(tracked.TryGetValue(hwnd,out entry)) entry.Closed=true;
            };
            Shown+=delegate {
                loaded=true;
                int roundedCorners=2;
                Native.DwmSetWindowAttribute(Handle,33,ref roundedCorners,4);
                eventHook=Native.SetWinEventHook(0x8001,0x8001,IntPtr.Zero,destroyCallback,0,0,2);
                RegisterInitial(); RefreshWindows();
            };
            timer=new Timer { Interval=1800 }; timer.Tick+=delegate { UpdateWindows(); }; timer.Start();
            hotkeys=new HotkeyService(SwitchCompleted);
            FormClosing+=delegate(object s,FormClosingEventArgs e) { if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; HideToTray(); } };
            VisibleChanged+=delegate { SyncBackgroundWork(); }; Resize+=delegate { SyncBackgroundWork(); };
            FormClosed+=delegate { Cleanup(); };
        }
        T Find<T>(string name) where T:W.FrameworkElement { return (T)view.FindName(name); }
        static M.SolidColorBrush Brush(string hex) { return (M.SolidColorBrush)new M.BrushConverter().ConvertFromString(hex); }
        C.TextBlock Label(string text,double size,string color)
        { return new C.TextBlock { Text=text,FontSize=size,Foreground=Brush(color),TextTrimming=W.TextTrimming.CharacterEllipsis,VerticalAlignment=W.VerticalAlignment.Center }; }
        C.Button Button(string text,W.RoutedEventHandler action)
        {
            var b=new C.Button { Content=text,Style=(W.Style)view.FindResource(typeof(C.Button)) }; b.Click+=action; return b;
        }
        internal void RefreshWindows()
        {
            ReleasePreviews(); gallery.Children.Clear();
            available=Native.Windows().OrderByDescending(x=>x.Game).ThenBy(x=>x.DisplayApp,StringComparer.CurrentCultureIgnoreCase).Select(x=> {
                var old=selected.FirstOrDefault(y=>y.Handle==x.Handle && y.Alive());
                if(old!=null) { old.Title=x.Title; return old; } return x;
            }).ToList();
            foreach(var entry in available) MakeCard(entry);
            if(available.Count==0) gallery.Children.Add(Label("暂无可选窗口，请打开应用后刷新。",14,"#727985"));
            Find<C.TextBlock>("WindowCount").Text=available.Count+" 个已打开";
            SizeCards(); RenderQueue();
            TrackWindows();
            view.Dispatcher.BeginInvoke(new Action(UpdatePreviews),System.Windows.Threading.DispatcherPriority.Loaded);
        }
        void MakeCard(WindowEntry entry)
        {
            var frame=new C.Border { CornerRadius=new W.CornerRadius(12),Background=Brush("#FFFFFF"),BorderThickness=new W.Thickness(1),Padding=new W.Thickness(5),Margin=new W.Thickness(0,0,12,14) };
            var grid=new C.Grid();
            foreach(int height in new[]{32,145,37}) grid.RowDefinitions.Add(new C.RowDefinition { Height=new W.GridLength(height) });
            var title=new C.Grid { Margin=new W.Thickness(6,0,6,0) };
            title.ColumnDefinitions.Add(new C.ColumnDefinition()); title.ColumnDefinitions.Add(new C.ColumnDefinition { Width=new W.GridLength(32) });
            var name=Label(entry.DisplayApp,12,"#363D48"); name.FontWeight=W.FontWeights.SemiBold; title.Children.Add(name);
            var tick=Label("+",18,"#5268DB"); tick.HorizontalAlignment=W.HorizontalAlignment.Right; C.Grid.SetColumn(tick,1); title.Children.Add(tick); grid.Children.Add(title);
            var area=new C.Border { Background=Brush("#ECEEF3"),CornerRadius=new W.CornerRadius(8),Margin=new W.Thickness(2,2,2,0) };
            var placeholder=Label("窗口预览暂不可用",12,"#626A79"); placeholder.HorizontalAlignment=W.HorizontalAlignment.Center; area.Child=placeholder;
            C.Grid.SetRow(area,1); grid.Children.Add(area);
            var caption=Label(entry.Title,11,"#6F7683"); caption.Margin=new W.Thickness(6,2,6,0); C.Grid.SetRow(caption,2); grid.Children.Add(caption); frame.Child=grid;
            var card=Button("",delegate {
                if(!entry.Alive()) { SetStatus("这个窗口已关闭，请刷新后重新选择。",true); return; }
                if(selected.Contains(entry)) selected.Remove(entry); else selected.Add(entry);
                RenderQueue(); UpdateCardSelection();
            });
            card.Padding=new W.Thickness(0); card.Background=M.Brushes.Transparent; card.BorderThickness=new W.Thickness(0); card.Content=frame; card.HorizontalContentAlignment=W.HorizontalAlignment.Stretch;
            card.ToolTip=entry.DisplayApp+"\n"+entry.Title;
            System.Windows.Automation.AutomationProperties.SetName(card,"选择窗口："+entry.DisplayApp+" · "+entry.Title);
            var preview=new Preview { Entry=entry,Area=area,Frame=frame,Tick=tick,Caption=caption,Placeholder=placeholder,Card=card };
            if(Native.DwmRegisterThumbnail(Handle,entry.Handle,out preview.Thumbnail)!=0) preview.Thumbnail=IntPtr.Zero;
            previews.Add(preview); gallery.Children.Add(card); UpdateCardSelection();
        }
        void SizeCards()
        {
            double width=Math.Max(280,scroll.ActualWidth-22); int cols=Math.Max(1,(int)(width/285));
            foreach(var p in previews) p.Card.Width=Math.Floor(width/cols);
        }
        void UpdateCardSelection()
        {
            foreach(var p in previews) {
                bool chosen=selected.Contains(p.Entry); p.Frame.BorderBrush=Brush(chosen?"#6579E7":"#E1E4EA");
                p.Frame.Background=Brush(chosen?"#F2F4FF":"#FFFFFF"); p.Tick.Text=chosen?"✓":"+"; p.Card.IsEnabled=p.Entry.Alive();
            }
        }
        internal void RenderQueue()
        {
            queue.Children.Clear();
            if(selected.Count==0) {
                var empty=new C.StackPanel { Margin=new W.Thickness(10,65,10,0) };
                foreach(var t in new[]{Label("点击左侧预览卡片添加",12,"#828995")}) {
                    t.HorizontalAlignment=W.HorizontalAlignment.Center; t.Margin=new W.Thickness(0,6,0,6); empty.Children.Add(t);
                } queue.Children.Add(empty);
            }
            for(int i=0;i<selected.Count;i++) {
                WindowEntry entry=selected[i]; int index=i;
                var box=new C.Border { Padding=new W.Thickness(10),Margin=new W.Thickness(0,0,0,10),CornerRadius=new W.CornerRadius(10),Background=Brush("#F7F8FC") };
                var line=new C.Grid(); line.ColumnDefinitions.Add(new C.ColumnDefinition { Width=new W.GridLength(30) }); line.ColumnDefinitions.Add(new C.ColumnDefinition());
                var num=Label((i+1).ToString("00"),14,"#6577D8"); num.VerticalAlignment=W.VerticalAlignment.Top; line.Children.Add(num);
                var content=new C.StackPanel(); C.Grid.SetColumn(content,1); content.Children.Add(Label(entry.DisplayApp,12,"#343B48"));
                content.Children.Add(Label(entry.Alive()?entry.Title:"已关闭 · 自动跳过",11,entry.Alive()?"#757D8B":"#A14D45"));
                var tools=new C.StackPanel { Orientation=C.Orientation.Horizontal,Margin=new W.Thickness(0,9,0,0) };
                var up=Button("↑",delegate { MoveAt(index,-1); }); up.IsEnabled=i>0; up.ToolTip="上移";
                var down=Button("↓",delegate { MoveAt(index,1); }); down.IsEnabled=i<selected.Count-1; down.ToolTip="下移";
                var remove=Button("移除",delegate { selected.Remove(entry); RenderQueue(); UpdateCardSelection(); });
                foreach(var b in new[]{up,down,remove}) { b.Padding=new W.Thickness(9,3,9,3); b.Margin=new W.Thickness(0,0,5,0); tools.Children.Add(b); }
                System.Windows.Automation.AutomationProperties.SetName(up,"上移："+entry.Title);
                System.Windows.Automation.AutomationProperties.SetName(down,"下移："+entry.Title);
                content.Children.Add(tools); line.Children.Add(content); box.Child=line; queue.Children.Add(box);
            }
            if(hotkeys!=null) hotkeys.SetWindows(selected.ToArray());
            TrackWindows();
        }
        void TrackWindows()
        {
            tracked.Clear();
            foreach(var entry in available) tracked[entry.Handle]=entry;
            foreach(var entry in selected) tracked[entry.Handle]=entry;
        }
        void MoveAt(int index,int offset) { int next=index+offset; if(next<0 || next>=selected.Count) return; var entry=selected[index]; selected.RemoveAt(index); selected.Insert(next,entry); RenderQueue(); }
        void UpdateWindows()
        {
            if(!Visible || WindowState==FormWindowState.Minimized || settingsOpen) return;
            bool changed=false;
            foreach(var entry in available.Concat(selected).Distinct()) {
                if(!entry.Alive()) { if(!entry.Closed) changed=true; entry.Closed=true; }
                else { string title=Native.Title(entry.Handle); if(!String.IsNullOrWhiteSpace(title) && title!=entry.Title) { entry.Title=title; changed=true; } }
            }
            if(changed || previews.Any(x=>x.Entry.Closed && x.Card.IsEnabled)) {
                RenderQueue(); UpdateCardSelection(); foreach(var p in previews) p.Caption.Text=p.Entry.Closed?"已关闭 · 刷新更新列表":p.Entry.Title;
            } UpdatePreviews();
        }
        void UpdatePreviews()
        {
            if(view==null || scroll==null || !view.IsLoaded || !IsHandleCreated || !Visible || WindowState==FormWindowState.Minimized || settingsOpen) return;
            foreach(var p in previews) {
                if(p.Thumbnail==IntPtr.Zero && Native.DwmRegisterThumbnail(Handle,p.Entry.Handle,out p.Thumbnail)!=0) { p.Thumbnail=IntPtr.Zero; continue; }
                bool show=Visible && WindowState!=FormWindowState.Minimized && !settingsOpen && !p.Entry.Closed && Native.IsWindow(p.Entry.Handle) && !Native.IsIconic(p.Entry.Handle) && p.Area.ActualWidth>0;
                var prop=new Native.ThumbnailProperties { Flags=31,Opacity=255,ClientOnly=false,Visible=false };
                p.Placeholder.Text=Native.IsIconic(p.Entry.Handle)?"已最小化 · 切换时自动恢复":(p.Entry.Closed?"窗口已关闭":"预览暂不可用");
                if(show) {
                    Native.NativeSize source;
                    if(Native.DwmQueryThumbnailSourceSize(p.Thumbnail,out source)==0 && source.Width>0 && source.Height>0) {
                        var pos=p.Area.PointToScreen(new W.Point(0,0)); var end=p.Area.PointToScreen(new W.Point(p.Area.ActualWidth,p.Area.ActualHeight));
                        double scale=Math.Min((end.X-pos.X)/source.Width,(end.Y-pos.Y)/source.Height);
                        if(scale<=0) continue;
                        var full=new Rectangle((int)(pos.X+((end.X-pos.X)-source.Width*scale)/2),(int)(pos.Y+((end.Y-pos.Y)-source.Height*scale)/2),(int)(source.Width*scale),(int)(source.Height*scale));
                        var top=scroll.PointToScreen(new W.Point(0,0)); var bottom=scroll.PointToScreen(new W.Point(scroll.ActualWidth,scroll.ActualHeight));
                        var clip=Rectangle.Intersect(full,Rectangle.FromLTRB((int)top.X,(int)top.Y,(int)bottom.X,(int)bottom.Y));
                        if(clip.Width>0 && clip.Height>0) {
                            var at=PointToClient(clip.Location);
                            prop.Destination=new Native.Rect { Left=at.X,Top=at.Y,Right=at.X+clip.Width,Bottom=at.Y+clip.Height };
                            prop.Source=new Native.Rect { Left=(int)((clip.Left-full.Left)/scale),Top=(int)((clip.Top-full.Top)/scale),Right=(int)((clip.Right-full.Left)/scale),Bottom=(int)((clip.Bottom-full.Top)/scale) };
                            prop.Visible=true;
                        }
                    }
                }
                // DWM updates the live image itself; submit only geometry changes.
                int result=0;
                if(!p.HasProperties || !SameProperties(p.LastProperties,prop)) {
                    result=Native.DwmUpdateThumbnailProperties(p.Thumbnail,ref prop);
                    if(result==0) { p.LastProperties=prop; p.HasProperties=true; }
                }
                var visibility=result==0 && prop.Visible?W.Visibility.Hidden:W.Visibility.Visible;
                if(p.Placeholder.Visibility!=visibility) p.Placeholder.Visibility=visibility;
            }
        }
        static bool SameProperties(Native.ThumbnailProperties a,Native.ThumbnailProperties b)
        {
            return a.Visible==b.Visible && a.Destination.Left==b.Destination.Left && a.Destination.Top==b.Destination.Top && a.Destination.Right==b.Destination.Right && a.Destination.Bottom==b.Destination.Bottom && a.Source.Left==b.Source.Left && a.Source.Top==b.Source.Top && a.Source.Right==b.Source.Right && a.Source.Bottom==b.Source.Bottom;
        }
        void DetachPreviews()
        {
            foreach(var p in previews) {
                if(p.Thumbnail!=IntPtr.Zero) Native.DwmUnregisterThumbnail(p.Thumbnail);
                p.Thumbnail=IntPtr.Zero; p.HasProperties=false;
            }
        }
        void ReleasePreviews() { DetachPreviews(); previews.Clear(); }
        void SyncBackgroundWork()
        {
            if(timer==null || !loaded) return;
            if(!Visible || WindowState==FormWindowState.Minimized) {
                timer.Stop(); ReleasePreviews(); gallery.Children.Clear(); queue.Children.Clear();
                available.Clear(); TrackWindows();
            }
            else {
                timer.Start();
                if(previews.Count==0) RefreshWindows(); else UpdatePreviews();
            }
        }
        void SwitchCompleted(WindowEntry target,bool success)
        {
            // Successful switches need no UI work or status messages.
            if(success) return;
            if(IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(delegate {
                if(IsDisposed) return;
                string text=target==null?"没有可切换窗口，请选择窗口。":"Windows 未允许激活目标窗口，请检查目标应用权限。";
                SetStatus(text,true);
            })); } catch(InvalidOperationException) { }
        }
        internal static WindowEntry NextWindow(List<WindowEntry> windows,IntPtr current)
        { if(windows.Count==0) return null; int i=windows.FindIndex(x=>x.Handle==current); return windows[(i+1)%windows.Count]; }
        void RegisterInitial()
        {
            hotkeyActive=hotkeys.Register(settings);
            Find<C.Button>("SettingsButton").ToolTip="设置快捷键 · 当前 "+settings.Display();
            SetStatus(hotkeyActive?settings.Display()+" 已启用 · 点击预览卡片选择窗口":settings.Display()+" 被占用或不可用，请从右上角设置更换。",!hotkeyActive);
        }
        void OpenSettings()
        {
            if(settingsOpen) return;
            settingsOpen=true; hotkeys.Suspend(); hotkeyActive=false; DetachPreviews();
            var dialog=new W.Window { Title="设置 · Fast Skip",Width=460,SizeToContent=W.SizeToContent.Height,WindowStyle=W.WindowStyle.None,ResizeMode=W.ResizeMode.NoResize,ShowInTaskbar=false,WindowStartupLocation=W.WindowStartupLocation.CenterOwner,Background=Brush("#F7F8FA"),FontFamily=new M.FontFamily("Microsoft YaHei UI"),FontSize=13 };
            new System.Windows.Interop.WindowInteropHelper(dialog).Owner=Handle;
            dialog.SourceInitialized+=delegate {
                int roundedCorners=2;
                Native.DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(dialog).Handle,33,ref roundedCorners,4);
            };
            var body=new C.StackPanel { Margin=new W.Thickness(28) };
            var heading=new C.Grid { Background=M.Brushes.Transparent };
            heading.ColumnDefinitions.Add(new C.ColumnDefinition()); heading.ColumnDefinitions.Add(new C.ColumnDefinition { Width=W.GridLength.Auto });
            heading.Children.Add(Label("快捷键设置",21,"#252832"));
            var close=Button("×",delegate { dialog.Close(); });
            close.Background=M.Brushes.Transparent; close.BorderThickness=new W.Thickness(0); close.Padding=new W.Thickness(0); close.Width=36; close.Height=36; close.FontSize=23; close.ToolTip="关闭设置";
            System.Windows.Automation.AutomationProperties.SetName(close,"关闭设置");
            C.Grid.SetColumn(close,1); heading.Children.Add(close);
            heading.MouseLeftButtonDown+=delegate { dialog.DragMove(); };
            body.Children.Add(heading);
            var hint=Label("点击下方输入框，然后按下新的组合键。",12,"#707987"); hint.Margin=new W.Thickness(0,12,0,16); body.Children.Add(hint);
            var capture=new C.TextBox { Text=settings.Display(),IsReadOnly=true,Padding=new W.Thickness(12),FontSize=18,BorderBrush=Brush("#D7DDE9"),Background=M.Brushes.White }; body.Children.Add(capture);
            Settings candidate=new Settings { Modifiers=settings.Modifiers,Key=settings.Key };
            var feedback=Label("支持 Alt / Ctrl / Shift + 字母、数字、空格、F1–F11",11,"#77808E"); feedback.Margin=new W.Thickness(0,12,0,16); body.Children.Add(feedback);
            capture.PreviewKeyDown+=delegate(object sender,I.KeyEventArgs e) {
                var key=e.Key==I.Key.System?e.SystemKey:e.Key; if(key==I.Key.Tab) return; e.Handled=true;
                if(key==I.Key.Escape) { candidate=new Settings { Modifiers=settings.Modifiers,Key=settings.Key }; capture.Text=settings.Display(); return; }
                if(key==I.Key.LeftAlt || key==I.Key.RightAlt || key==I.Key.LeftCtrl || key==I.Key.RightCtrl || key==I.Key.LeftShift || key==I.Key.RightShift) return;
                uint mods=(uint)(((I.Keyboard.Modifiers&I.ModifierKeys.Alt)!=0?1:0)|((I.Keyboard.Modifiers&I.ModifierKeys.Control)!=0?2:0)|((I.Keyboard.Modifiers&I.ModifierKeys.Shift)!=0?4:0));
                uint vk=(uint)I.KeyInterop.VirtualKeyFromKey(key);
                if(mods==0 || !Settings.AllowedKey(vk) || (I.Keyboard.Modifiers&I.ModifierKeys.Windows)!=0) { feedback.Text="请使用修饰键 + 字母、数字、空格或 F1–F11。"; return; }
                candidate=new Settings { Modifiers=mods,Key=vk }; capture.Text=candidate.Display(); feedback.Text="点击保存后生效；冲突时会提示。"; feedback.Foreground=Brush("#77808E");
            };
            var actions=new C.StackPanel { Orientation=C.Orientation.Horizontal,HorizontalAlignment=W.HorizontalAlignment.Right };
            var cancel=Button("取消",delegate { dialog.Close(); }); cancel.IsCancel=true; cancel.Margin=new W.Thickness(0,0,10,0); actions.Children.Add(cancel);
            var save=Button("保存快捷键",delegate {
                if(!hotkeys.Register(candidate)) { feedback.Text="快捷键已被占用，请换一个组合。"; feedback.Foreground=Brush("#AF4236"); return; }
                hotkeyActive=true; settings=candidate;
                try { settings.Save(settingsPath); SetStatus(settings.Display()+" 已启用并保存",false); }
                catch(IOException) { SetStatus("快捷键已生效，保存失败。请将应用放在可写文件夹。",true); }
                catch(UnauthorizedAccessException) { SetStatus("快捷键已生效，保存失败。请将应用放在可写文件夹。",true); }
                Find<C.Button>("SettingsButton").ToolTip="设置快捷键 · 当前 "+settings.Display(); dialog.Close();
            });
            save.Background=Brush("#586DE5"); save.Foreground=M.Brushes.White; actions.Children.Add(save); body.Children.Add(actions); dialog.Content=body;
            try { dialog.ShowDialog(); }
            finally {
                settingsOpen=false; if(!hotkeyActive) RegisterInitial(); UpdatePreviews();
                // Modal owner activation restores the launching button's focus. Move
                // it to the non-interactive header after activation has completed.
                view.Dispatcher.BeginInvoke(new Action(delegate {
                    if(IsDisposed || !Visible) return;
                    var header=Find<C.Grid>("WindowHeader");
                    I.FocusManager.SetFocusedElement(I.FocusManager.GetFocusScope(Find<C.Button>("SettingsButton")),header);
                    I.Keyboard.Focus(header);
                }),System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }
        void SetStatus(string text,bool error) { if(error) Notify(text); }
        void Notify(string text) { tray.BalloonTipTitle="Fast Skip"; tray.BalloonTipText=text; tray.ShowBalloonTip(3000); }
        internal void HideToTray() { Hide(); if(!balloonShown) { Notify("正在后台运行。双击托盘图标打开主界面，右键选择“退出”完全关闭。"); balloonShown=true; } }
        internal void ShowSettings() { Show(); WindowState=FormWindowState.Normal; Activate(); }
        internal void ExitApp() { exiting=true; Close(); }
        void Cleanup() {
            timer.Stop(); timer.Dispose(); hotkeys.Dispose();
            if(eventHook!=IntPtr.Zero) Native.UnhookWinEvent(eventHook); ReleasePreviews(); tray.Visible=false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); Icon.Dispose();
        }
        sealed class Preview {
            internal WindowEntry Entry; internal IntPtr Thumbnail; internal C.Border Area,Frame;
            internal C.TextBlock Tick,Caption,Placeholder; internal C.Button Card;
            internal bool HasProperties; internal Native.ThumbnailProperties LastProperties;
        }
    }
}
