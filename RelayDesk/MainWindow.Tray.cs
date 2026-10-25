using System.Drawing;
using System.Windows;
using Forms=System.Windows.Forms;

namespace RelayDesk;
public partial class MainWindow
{
    Forms.NotifyIcon? trayIcon;
    Forms.ContextMenuStrip? trayMenu;
    Icon? trayImage;
    string trayText="";
    void InitializeTray()
    {
        TrayChoice.IsChecked=state.TrayEnabled;
        Closed+=(_,_)=>DisposeTray();
        // Automated UI fixtures never create a persistent notification icon.
        if(Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR")!=null)return;
        if(state.TrayEnabled)CreateTray();
    }
    void CreateTray()
    {
        if(trayIcon!=null)return;
        try
        {
            trayImage=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??(System.Drawing.Icon)SystemIcons.Application.Clone();
            trayMenu=new Forms.ContextMenuStrip{BackColor=Color.FromArgb(27,30,37),ForeColor=Color.FromArgb(236,238,242),Font=new Font("Segoe UI",10)};
            trayMenu.Renderer=new TrayRenderer();trayMenu.Opening+=(_,_)=>BuildTrayMenu();
            trayIcon=new Forms.NotifyIcon{Icon=trayImage,ContextMenuStrip=trayMenu,Text="RelayDesk",Visible=true};
            trayIcon.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left)Dispatcher.BeginInvoke(ShowFromTray);};
            trayIcon.DoubleClick+=(_,_)=>Dispatcher.BeginInvoke(ShowFromTray);
            RefreshTrayStatus();
        }
        catch(Exception ex){DisposeTray();Toast("Не удалось создать значок в трее: "+ex.Message);}
    }
    void DisposeTray()
    {
        if(trayIcon!=null){trayIcon.Visible=false;trayIcon.Dispose();trayIcon=null;}
        trayMenu?.Dispose();trayMenu=null;trayImage?.Dispose();trayImage=null;trayText="";
    }
    void RefreshTrayStatus()
    {
        if(trayIcon==null)return;
        string text="RelayDesk\nVPN: "+(vpn?"включён":"выключен")+" | zapret: "+(zapret?"включён":"выключен");
        if(text==trayText)return;trayIcon.Text=text.Length>63?text[..63]:text;trayText=text;
    }
    void BuildTrayMenu()
    {
        if(trayMenu==null)return;trayMenu.Items.Clear();
        Forms.ToolStripMenuItem Item(string text,Action action,bool enabled=true,bool? check=null)
        {
            var item=new Forms.ToolStripMenuItem(text){Enabled=enabled};if(check.HasValue){item.Checked=check.Value;item.CheckOnClick=false;}
            item.Click+=(_,_)=>Dispatcher.BeginInvoke(action);trayMenu.Items.Add(item);return item;
        }
        Item("Открыть RelayDesk",ShowFromTray);
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        Item("VPN · "+(vpn?"выключить":"включить"),()=>ToggleVpn(this,new RoutedEventArgs()),!vpnBusy,vpn);
        Item("zapret · "+(zapret?"выключить":"включить"),()=>ToggleZapret(this,new RoutedEventArgs()),!zapretBusy,zapret);
        var servers=new Forms.ToolStripMenuItem("Выбрать сервер"){Enabled=!vpnBusy};
        servers.DropDownItems.Add(new Forms.ToolStripMenuItem(vpn?"Выбор переподключит VPN":"Для следующего подключения"){Enabled=false});
        foreach(string name in new[]{"Автоматически"}.Concat(state.Servers.Select(s=>s.Name)))
        {
            var item=new Forms.ToolStripMenuItem(name){Checked=state.Server==name};item.Click+=(_,_)=>Dispatcher.BeginInvoke(()=>SelectTrayServer(name));servers.DropDownItems.Add(item);
        }
        trayMenu.Items.Add(servers);
        Item("Правила и приложения",()=>{ShowFromTray();SelectPage(2);});
        Item("Соединения",()=>{ShowFromTray();SelectPage(4);});
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        Item("Скрыть окно",HideToTray);
        Item("Выход",()=>Close());
    }
    void HideToTray()
    {
        if(trayIcon==null){WindowState=WindowState.Minimized;return;}
        Hide();ShowInTaskbar=false;
    }
    void SelectTrayServer(string name)
    {
        if(vpnBusy||!vpn&&state.Server==name)return;
        bool reconnect=vpn;
        ServerChoice.SelectedItem=name;
        if(reconnect&&name!="Автоматически")SwitchServerFromTray();
    }
    async void SwitchServerFromTray()
    {
        try{var profile=backend.ModernProfile??throw new InvalidOperationException("Выбери профиль.");await backend.ModernSession.SwitchProfile(profile);}catch(Exception ex){Toast(ex.Message);}
    }
    void ShowFromTray()
    {
        ShowInTaskbar=true;Show();WindowState=WindowState.Normal;Activate();WakeQuietMode();
    }
    void TrayChoiceChanged(object sender,RoutedEventArgs e)
    {
        if(!initialized)return;state.TrayEnabled=TrayChoice.IsChecked==true;
        if(state.TrayEnabled&&Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR")==null)CreateTray();
        else{ShowFromTray();DisposeTray();}
        Save();
    }
    sealed class TrayRenderer:Forms.ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {using var fill=new SolidBrush(Color.FromArgb(27,30,37));e.Graphics.FillRectangle(fill,e.AffectedBounds);}
        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
        {using var fill=new SolidBrush(Color.FromArgb(27,30,37));e.Graphics.FillRectangle(fill,e.AffectedBounds);}
        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {using var pen=new Pen(Color.FromArgb(64,70,82));e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {e.ArrowColor=Color.FromArgb(220,225,235);base.OnRenderArrow(e);}
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {e.TextColor=e.Item.Enabled?Color.FromArgb(236,238,242):Color.FromArgb(128,138,154);base.OnRenderItemText(e);}
        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            using var fill=new SolidBrush(e.Item.Selected?Color.FromArgb(52,57,68):Color.FromArgb(27,30,37));e.Graphics.FillRectangle(fill,new Rectangle(System.Drawing.Point.Empty,e.Item.Size));
        }
    }
}
