using CodexLauncher.Core;
using QRCoder;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CodexLauncher.App;

internal sealed record BridgeBatchOutcome(string Id,string Name,bool Succeeded,string? Error);

/// <summary>Main-window bridge section. All actions run on the UI thread; no secrets leave memory.</summary>
internal sealed class BridgePanel:UserControl
{
    private const int HorizontalScrollBar=0;
    private const int VerticalScrollBar=1;
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeScrollInfo{public uint Size,Mask;public int Min,Max;public uint Page;public int Position,TrackPosition;}
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollInfo(IntPtr handle,int bar,ref NativeScrollInfo info);
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr handle,int bar,[MarshalAs(UnmanagedType.Bool)]bool show);
    private readonly Func<IReadOnlyList<BridgeAdapterChoice>> _adapterCatalog;
    private readonly Button _toggle=new(){Text="AI 额度桥　›",Name="BridgeToggle",FlatStyle=FlatStyle.Flat,ForeColor=UiTheme.Text,BackColor=UiTheme.Card,Cursor=Cursors.Hand,UseVisualStyleBackColor=false};
    private readonly TableLayoutPanel _content=new(){Name="BridgeContent",Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Visible=false,BackColor=UiTheme.Card,ForeColor=UiTheme.Text};
    private bool _expanded;
    private readonly CheckBox _enabled=new(){Text="启用手机额度共享",AutoSize=true};
    private readonly ComboBox _adapters=new(){Name="BridgeAdapterSelector",DropDownStyle=ComboBoxStyle.DropDownList,Width=360,DropDownWidth=360,MaxDropDownItems=8,BackColor=UiTheme.Card,ForeColor=UiTheme.Text};
    private readonly ToolTip _adapterToolTip=new(){AutoPopDelay=15000,InitialDelay=400,ReshowDelay=100,ShowAlways=true};
    private readonly Label _adapterInfo=new(){AutoSize=true,MaximumSize=new(900,0),Text="点击“刷新网卡”检测可用于手机配对的 Wi-Fi / 以太网。"};
    private readonly NumericUpDown _port=new(){Minimum=1,Maximum=65535,Value=43189,Width=110,BackColor=UiTheme.Card,ForeColor=UiTheme.Text};
    private readonly Label _status=new(){AutoSize=true,Text="共享已关闭",MaximumSize=new(650,0)};
    private readonly Label _identity=new(){AutoSize=true,MaximumSize=new(650,0)};
    private readonly Label _qrInfo=new(){AutoSize=true,MaximumSize=new(650,0)};
    private readonly Label _account=new(){AutoSize=true,Text="共享范围：当前已登录账号的只读额度"};
    private readonly TextBox _nickname=new(){Name="BridgeNickname",MaxLength=80,Width=240,Text="Codex账号",BackColor=UiTheme.Card,ForeColor=UiTheme.Text};
    private readonly PictureBox _qr=new(){Size=new(240,240),SizeMode=PictureBoxSizeMode.Zoom,Visible=false};
    private readonly ListView _pending=new(){Name="BridgePending",View=View.Details,FullRowSelect=true,HideSelection=false,CheckBoxes=true,MultiSelect=true,Height=100,Dock=DockStyle.Top,OwnerDraw=true,BorderStyle=BorderStyle.None,BackColor=UiTheme.Card,ForeColor=UiTheme.Text,ShowItemToolTips=true};
    private readonly ListView _devices=new(){Name="BridgeDevices",View=View.Details,FullRowSelect=true,HideSelection=false,CheckBoxes=true,MultiSelect=true,Height=120,Dock=DockStyle.Top,OwnerDraw=true,BorderStyle=BorderStyle.None,BackColor=UiTheme.Card,ForeColor=UiTheme.Text,ShowItemToolTips=true};
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=1000};
    private Button? _approveButton;
    private Button? _rejectButton;
    private Button? _replaceButton;
    private Button? _renameButton;
    private Button? _authorizeButton;
    private Button? _revokeButton;
    private bool _updatingList;
    private Form? _hostForm;
    private BridgeRuntime? _runtime;
    private IReadOnlyList<BridgeAdapterChoice> _checkedAdapters=Array.Empty<BridgeAdapterChoice>();
    private BridgeInvitation? _invitation;
    private BridgePairingService? _invitationOwner;
    private bool _busy;
    internal bool HasInvitation=>_invitation is not null;
    internal event Func<bool,string?,int,Task>? ConfigureRequested;
    internal BridgePanel(Func<IReadOnlyList<BridgeAdapterChoice>>? adapterCatalog=null)
    {
        _adapterCatalog=adapterCatalog??BridgeRuntime.AvailableAdapters;
        AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;BackColor=UiTheme.Window;ForeColor=UiTheme.Text;Margin=new Padding(0,0,0,14);
        var card=new RoundedPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=Padding.Empty};
        var layout=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new(22,10,22,16),BackColor=UiTheme.Card,ForeColor=UiTheme.Text};
        layout.ColumnStyles.Add(new(SizeType.Percent,100));card.Controls.Add(layout);Controls.Add(card);
        _toggle.Dock=DockStyle.Top;_toggle.AutoSize=true;_toggle.MinimumSize=new Size(0,UiTheme.RowHeight(46));_toggle.TextAlign=ContentAlignment.MiddleLeft;_toggle.Margin=Padding.Empty;
        _toggle.FlatAppearance.BorderSize=0;
        _toggle.FlatAppearance.MouseOverBackColor=UiTheme.Card;
        _toggle.FlatAppearance.MouseDownBackColor=UiTheme.Card;
        _toggle.Font=new Font(Font.FontFamily,13,FontStyle.Bold);
        layout.Controls.Add(_toggle);layout.Controls.Add(_content);
        _content.Margin=new Padding(0,8,0,0);_content.ColumnStyles.Add(new(SizeType.Percent,100));
        _adapters.SelectedIndexChanged+=(_,_)=>UpdateAdapterInfo();
        _adapters.DropDown+=(_,_)=>UpdateAdapterDropDownWidth();
        _enabled.Click+=(_,_)=>{if(_enabled.Checked)RefreshAdapters(preferRecommended:true);};
        Disposed+=(_,_)=>_adapterToolTip.Dispose();
        _toggle.Click+=(_,_)=>
        {
            var layoutChain=new List<Control>();
            for(Control? current=_content;current is not null;current=current.Parent)
            {
                current.SuspendLayout();
                layoutChain.Add(current);
            }
            try
            {
                _expanded=!_expanded;_content.Visible=_expanded;
                _toggle.Text=_expanded?"AI 额度桥　⌄":"AI 额度桥　›";
                if(!_expanded)ClearInvitation();
                UpdateRefreshTimer();
            }
            finally
            {
                for(var index=layoutChain.Count-1;index>=0;index--)layoutChain[index].ResumeLayout(false);
                _content.Parent?.PerformLayout();
            }
        };
        void Add(Control c){c.Margin=new(0,4,0,4);_content.Controls.Add(c);}
        var config=WrapRow("BridgeConfigurationRow",
            _enabled,
            _adapters,
            _port,
            Button("应用",async()=>
            {
                var choice=_adapters.SelectedItem as BridgeAdapterChoice;
                if(_enabled.Checked&&choice?.CanListen!=true)
                    throw new InvalidOperationException(choice?.UnavailableReason??"请先刷新并选择可用的 Wi-Fi / 以太网网卡。");
                if(ConfigureRequested is {} handler)await handler(_enabled.Checked,choice?.Id,(int)_port.Value);
            },"BridgeApply"),
            Button("刷新网卡",()=>{RefreshAdapters();return Task.CompletedTask;},"BridgeRefreshAdapters"));
        Add(config);Add(_adapterInfo);
        Add(_status);Add(_identity);Add(_account);
        Add(AlignedRow(_nickname,Button("保存账号昵称",()=>{Required().Grants.RenameCurrentAccount(_nickname.Text);return Task.CompletedTask;})));
        var pairing=Flow();pairing.Controls.Add(Button("显示添加设备二维码",ShowInvitation));pairing.Controls.Add(Button("隐藏二维码",()=>{ClearInvitation();return Task.CompletedTask;}));Add(pairing);Add(_qrInfo);Add(_qr);
        Add(new Label{Text="待确认申请（勾选复选框或点击行以选择；可批量批准或拒绝）",AutoSize=true});
        _pending.Columns.Add("手机名称");_pending.Columns.Add("来源");_pending.Columns.Add("到期时间");Add(_pending);
        StyleTable(_pending,firstColumnShare:0.52,dateMinimum:160);
        var approveButton=Button("批准（0）",()=>Decide(true),"BridgeApproveSelected");_approveButton=approveButton;
        var rejectButton=Button("拒绝（0）",()=>Decide(false),"BridgeRejectSelected");_rejectButton=rejectButton;
        var replaceButton=Button("替换并批准",ReplaceAndApprove,"BridgeReplaceSelected");_replaceButton=replaceButton;
        var approve=Flow();approve.Controls.Add(approveButton);approve.Controls.Add(rejectButton);approve.Controls.Add(replaceButton);Add(approve);
        Add(new Label{Text="已配对设备（最多 20 台；勾选复选框或点击行以选择，配对时间可区分同名设备）",AutoSize=true});
        _devices.Columns.Add("设备");_devices.Columns.Add("当前账号");_devices.Columns.Add("配对时间");Add(_devices);StyleTable(_devices,firstColumnShare:0.62,dateMinimum:160);
        var manage=Flow();
        var renameButton=Button("重命名",Rename,"BridgeRenameSelected");_renameButton=renameButton;manage.Controls.Add(renameButton);
        var authorizeButton=Button("授权当前账号（0）",AuthorizeSelected,"BridgeAuthorizeSelected");_authorizeButton=authorizeButton;manage.Controls.Add(authorizeButton);
        var revokeButton=Button("撤销设备（0）",RevokeSelected,"BridgeRevokeSelected");_revokeButton=revokeButton;manage.Controls.Add(revokeButton);
        manage.Controls.Add(Button("全部撤销",()=>{if(Confirm("撤销所有设备？所有手机都必须重新扫码。"))Required().RevokeAll();return Task.CompletedTask;}));Add(manage);
        var help=Flow();help.Controls.Add(Button("连接排障",()=>{MessageBox.Show(this,BridgeConnectionGuidance.ManualConnection(_runtime?.Status??new("STOPPED"))+"\n自动发现隔离尚未验证，当前使用二维码直连。\n开机启动可在高级设置或托盘开启；后台启动不会打开 Codex。","连接说明");return Task.CompletedTask;}));
        help.Controls.Add(Button("复制防火墙命令",()=>
        {
            var status=Required().Status;
            if(status.State!="RUNNING"||!Uri.TryCreate(status.Endpoint,UriKind.Absolute,out var endpoint)||
                !System.Net.IPAddress.TryParse(endpoint.Host,out var address))
                throw new InvalidOperationException("请先选择可用网卡并点击“应用”启动共享，再复制当前地址的防火墙命令。");
            Clipboard.SetText(BridgeConnectionGuidance.FirewallCommands(Application.ExecutablePath,endpoint.Port,address));
            MessageBox.Show(this,"已复制公用 / 专用网络规则，仅允许当前程序、监听地址和端口的本地子网连接。请检查后在管理员终端执行；启动器不会提权或修改规则。监听地址变化后需重新生成规则。","防火墙指引");
            return Task.CompletedTask;
        }));Add(help);
        _timer.Tick+=(_,_)=>RefreshState();
        _pending.ItemChecked+=(_,_)=>UpdateBatchControls();
        _devices.ItemChecked+=(_,_)=>UpdateBatchControls();
        _pending.ItemSelectionChanged+=(_,e)=>OnRowSelected(e);
        _devices.ItemSelectionChanged+=(_,e)=>OnRowSelected(e);
        HandleCreated+=(_,_)=>UpdateRefreshTimer();
        ParentChanged+=(_,_)=>AttachHostForm();
        VisibleChanged+=(_,_)=>{if(!Visible)ClearInvitation();UpdateRefreshTimer();};
        UpdateBatchControls();
        UiTheme.Style(this);
    }
    private void AttachHostForm()
    {
        if(_hostForm is not null)
        {
            _hostForm.VisibleChanged-=OnHostVisibilityChanged;
            _hostForm.Resize-=OnHostVisibilityChanged;
        }
        _hostForm=FindForm();
        if(_hostForm is not null)
        {
            _hostForm.VisibleChanged+=OnHostVisibilityChanged;
            _hostForm.Resize+=OnHostVisibilityChanged;
        }
        UpdateRefreshTimer();
    }
    private void OnHostVisibilityChanged(object? sender,EventArgs e)=>UpdateRefreshTimer();
    private void UpdateRefreshTimer()
    {
        var hostVisible=_hostForm is null||(_hostForm.Visible&&_hostForm.WindowState!=FormWindowState.Minimized);
        if(!_expanded||!Visible||!hostVisible||!IsHandleCreated||IsDisposed)
        {
            _timer.Stop();
            return;
        }
        if(!_timer.Enabled)
        {
            RefreshState();
            _timer.Start();
        }
    }
    private static UiTheme.WrapPanel Flow()=>new(){Dock=DockStyle.Top,AutoSize=true,BackColor=UiTheme.Card,ForeColor=UiTheme.Text};
    private static UiTheme.WrapPanel WrapRow(string name,params Control[] controls)
    {
        var row=Flow();row.Name=name;row.FlowDirection=FlowDirection.LeftToRight;row.WrapContents=true;row.Margin=Padding.Empty;row.Padding=Padding.Empty;
        for(var index=0;index<controls.Length;index++)
        {
            controls[index].Anchor=AnchorStyles.Left;
            controls[index].Margin=new Padding(index==0?0:8,0,0,0);
            row.Controls.Add(controls[index]);
        }
        return row;
    }
    private static TableLayoutPanel AlignedRow(params Control[] controls)
    {
        var row=new TableLayoutPanel
        {
            Dock=DockStyle.Top,
            AutoSize=true,
            AutoSizeMode=AutoSizeMode.GrowAndShrink,
            ColumnCount=controls.Length,
            RowCount=1,
            Margin=Padding.Empty,
            Padding=Padding.Empty,
            BackColor=UiTheme.Card,
            ForeColor=UiTheme.Text
        };
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for(var index=0;index<controls.Length;index++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            controls[index].Anchor=AnchorStyles.Left;
            controls[index].Margin=new Padding(index==0?0:8,0,0,0);
            row.Controls.Add(controls[index],index,0);
        }
        return row;
    }
    private static void StyleTable(ListView list,double firstColumnShare,int dateMinimum)
    {
        list.DrawColumnHeader+=(_,e)=>DiagnosticsHeaderPainter.PaintColumn(e.Graphics,e.Bounds,e.Header?.Text,UiTheme.Muted,UiTheme.Card,UiTheme.Border,list.Font);
        list.DrawSubItem+=(_,e)=>
        {
            if(e.Item is null||e.SubItem is null)return;
            var bounds=e.Bounds;
            if(e.ColumnIndex==list.Columns.Count-1)bounds.Width=Math.Max(bounds.Right,list.ClientRectangle.Right)-bounds.Left;
            using(var brush=new SolidBrush(e.Item.Selected?UiTheme.PrimarySurface:UiTheme.Card))e.Graphics.FillRectangle(brush,bounds);
            var inset=Math.Max(6,(int)Math.Ceiling(8*list.DeviceDpi/96d));
            var textLeft=e.Bounds.Left+inset;
            if(e.ColumnIndex==0&&list.CheckBoxes)
            {
                var state=e.Item.Checked?System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal:System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
                var glyph=CheckBoxRenderer.GetGlyphSize(e.Graphics,state);
                CheckBoxRenderer.DrawCheckBox(e.Graphics,new Point(textLeft,e.Bounds.Top+(e.Bounds.Height-glyph.Height)/2),state);
                textLeft+=glyph.Width+inset;
            }
            var textBounds=Rectangle.FromLTRB(textLeft,e.Bounds.Top,Math.Max(textLeft,e.Bounds.Right-inset),e.Bounds.Bottom);
            TextRenderer.DrawText(e.Graphics,e.SubItem.Text,list.Font,textBounds,e.Item.Selected?UiTheme.AccentText:UiTheme.Text,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        };
        void ResizeColumns()
        {
            ResizeTableColumns(list,firstColumnShare,dateMinimum);
        }
        list.Resize+=(_,_)=>ResizeColumns();
        list.HandleCreated+=(_,_)=>ResizeColumns();
        list.DpiChangedAfterParent+=(_,_)=>ResizeColumns();
        ResizeColumns();
    }
    private static void ResizeTableColumns(ListView list,double firstColumnShare,int dateMinimum)
    {
        if(list.Columns.Count!=3||list.ClientSize.Width<1)return;
        var total=list.ClientSize.Width;
        if(list.IsHandleCreated)
        {
            var vertical=new NativeScrollInfo{Size=(uint)Marshal.SizeOf<NativeScrollInfo>(),Mask=0x17};
            if(GetScrollInfo(list.Handle,VerticalScrollBar,ref vertical)&&
                (long)vertical.Max-vertical.Min+1>vertical.Page)
                total-=SystemInformation.VerticalScrollBarWidth;
        }
        total=Math.Max(1,total-Math.Max(2,(int)Math.Ceiling(2*list.DeviceDpi/96d)));
        var preferredDate=(int)Math.Ceiling(dateMinimum*list.DeviceDpi/96d);
        var dateWidth=Math.Min(total,preferredDate);
        var flexibleWidth=total-dateWidth;
        var firstWidth=(int)Math.Round(flexibleWidth*firstColumnShare);
        var widths=new[]{firstWidth,flexibleWidth-firstWidth,dateWidth};
        if(Enumerable.Range(0,3).Any(index=>list.Columns[index].Width!=widths[index]))
        {
            list.BeginUpdate();
            try
            {
                for(var index=0;index<3;index++)
                    if(list.Columns[index].Width!=widths[index])list.Columns[index].Width=widths[index];
            }
            finally{list.EndUpdate();}
        }
        HideHorizontalScrollbar(list);
    }
    private static void HideHorizontalScrollbar(ListView list)
    {
        if(list.IsHandleCreated)ShowScrollBar(list.Handle,HorizontalScrollBar,false);
    }
    private Button Button(string text,Func<Task> action,string? name=null)
    {
        var button=UiTheme.SecondaryButton(text);button.Name=name??text;
        button.Click+=async(_,_)=>
        {
            // Disabling the focused button moves focus to the next action (for
            // approval that is Reject). The busy guard already prevents repeats.
            if(_busy)return;_busy=true;
            try{await action();RefreshState();}
            catch(Exception e){MessageBox.Show(this,e is InvalidOperationException or ArgumentException?e.Message:"操作失败，请检查权限或连接状态。","AI 额度桥",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{_busy=false;}
        };return button;
    }
    internal void Bind(BridgeRuntime runtime){_runtime=runtime;RefreshAdapters();RefreshState();}
    internal void SetConfiguration(bool enabled,int port,string? adapter)
    {
        _enabled.Checked=enabled;_port.Value=Math.Clamp(port,1,65535);
        if(adapter is not null)
        {
            var choice=_adapters.Items.Cast<BridgeAdapterChoice>().FirstOrDefault(a=>a.Id==adapter);
            if(choice is null){choice=BridgeAdapterChoice.FromError(adapter,"原选定网卡","NETWORK_ADAPTER_NOT_FOUND");_adapters.Items.Add(choice);}_adapters.SelectedItem=choice;
        }
        UpdateAdapterInfo();
    }
    internal void RefreshAdapters(bool preferRecommended=false)
    {
        var selected=(_adapters.SelectedItem as BridgeAdapterChoice)?.Id??_runtime?.SelectedAdapter;
        _adapters.Items.Clear();
        try
        {
            var choices=_adapterCatalog();
            _checkedAdapters=choices.ToArray();
            foreach(var item in _checkedAdapters)_adapters.Items.Add(item);
            var previous=choices.FirstOrDefault(a=>a.Id==selected);
            var recommended=choices.FirstOrDefault(a=>a.Recommended&&a.CanListen);
            var next=preferRecommended
                ?recommended??(previous?.CanListen==true?previous:null)??choices.FirstOrDefault(a=>a.CanListen)
                :(previous?.CanListen==true?previous:null)??recommended??choices.FirstOrDefault(a=>a.CanListen);
            if(next is not null)_adapters.SelectedItem=next;
            else if(selected is not null)SetConfiguration(_enabled.Checked,(int)_port.Value,selected);
            else if(_adapters.Items.Count>0)_adapters.SelectedItem=_adapters.Items[0];
            UpdateAdapterInfo();
        }
        catch(Exception e)
        {
            _checkedAdapters=Array.Empty<BridgeAdapterChoice>();
            _adapterInfo.Text=$"读取网卡列表失败：{e.Message}";
        }
    }
    private void UpdateAdapterInfo()
    {
        var choice=_adapters.SelectedItem as BridgeAdapterChoice;
        _adapterInfo.Text=BridgeAdapterDisplay.QualificationSummary(_checkedAdapters,choice);
        _adapterToolTip.SetToolTip(_adapters,choice is not null
            ?BridgeAdapterDisplay.ToolTipText(choice)
            :_checkedAdapters.Count==0?"没有检测到可推荐的 Wi-Fi 或以太网接口。":null);
    }
    private void UpdateAdapterDropDownWidth()
    {
        if(_adapters.IsDisposed||_adapters.Items.Count==0)return;
        var workingArea=Screen.FromControl(_adapters).WorkingArea;
        var selectorBounds=_adapters.RectangleToScreen(_adapters.ClientRectangle);
        var spaceAbove=selectorBounds.Top-workingArea.Top;
        var spaceBelow=workingArea.Bottom-selectorBounds.Bottom;
        var availableHeight=Math.Max(0,Math.Max(spaceAbove,spaceBelow)-2*SystemInformation.BorderSize.Height);
        var itemHeight=Math.Max(1,_adapters.ItemHeight);
        var visibleItems=Math.Clamp(availableHeight/itemHeight,1,Math.Min(8,_adapters.Items.Count));
        _adapters.MaxDropDownItems=visibleItems;
        var availableWidth=BridgeAdapterDisplay.AvailableDropDownWidth(workingArea,selectorBounds);
        var extraWidth=SystemInformation.VerticalScrollBarWidth+(int)Math.Ceiling(16*_adapters.DeviceDpi/96d);
        _adapters.DropDownWidth=BridgeAdapterDisplay.CalculateDropDownWidth(
            _adapters.Items.Cast<BridgeAdapterChoice>().Select(choice=>choice.ToString()),
            _adapters.Width,
            availableWidth,
            name=>TextRenderer.MeasureText(name,_adapters.Font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Width,
            extraWidth);
    }
    private BridgeRuntime Required()=>_runtime??throw new InvalidOperationException("请先应用桥设置，再管理设备。");
    private static ListViewItem[] CheckedRows(ListView list)=>list.CheckedItems.Cast<ListViewItem>().ToArray();
    private void UpdateBatchControls()
    {
        var pendingCount=_pending.CheckedItems.Count;
        var deviceCount=_devices.CheckedItems.Count;
        if(_approveButton is not null){_approveButton.Text=$"批准（{pendingCount}）";_approveButton.Enabled=pendingCount>0;}
        if(_rejectButton is not null){_rejectButton.Text=$"拒绝（{pendingCount}）";_rejectButton.Enabled=pendingCount>0;}
        if(_replaceButton is not null)_replaceButton.Enabled=pendingCount==1&&deviceCount==1;
        if(_renameButton is not null)_renameButton.Enabled=deviceCount==1;
        if(_authorizeButton is not null){_authorizeButton.Text=$"授权当前账号（{deviceCount}）";_authorizeButton.Enabled=deviceCount>0;}
        if(_revokeButton is not null){_revokeButton.Text=$"撤销设备（{deviceCount}）";_revokeButton.Enabled=deviceCount>0;}
    }
    private void OnRowSelected(ListViewItemSelectionChangedEventArgs e)
    {
        if(!_updatingList&&e.IsSelected&&e.Item is{Checked:false} item)item.Checked=true;
    }
    internal static IReadOnlyList<BridgeBatchOutcome> ExecuteBatch(IEnumerable<(string Id,string Name)> entries,Func<string,bool> action)
    {
        var outcomes=new List<BridgeBatchOutcome>();
        foreach(var (id,name) in entries)
        {
            try
            {
                var succeeded=action(id);
                outcomes.Add(new(id,name,succeeded,succeeded?null:"操作未完成，记录可能已失效或需要重新确认。"));
            }
            catch(Exception exception){outcomes.Add(new(id,name,false,exception.Message));}
        }
        return outcomes;
    }
    private void ShowBatchResults(string operation,IReadOnlyList<BridgeBatchOutcome> outcomes)
    {
        var succeeded=outcomes.Count(outcome=>outcome.Succeeded);
        var lines=outcomes.Select(outcome=>$"{(outcome.Succeeded?"成功":"失败")} · {outcome.Name}"+
            (outcome.Succeeded?"":$"：{outcome.Error}"));
        MessageBox.Show(this,$"{operation}完成：成功 {succeeded} 项，失败 {outcomes.Count-succeeded} 项。\n"+string.Join("\n",lines),
            "AI 额度桥批量结果",MessageBoxButtons.OK,succeeded==outcomes.Count?MessageBoxIcon.Information:MessageBoxIcon.Warning);
    }
    private bool Confirm(string message)=>MessageBox.Show(this,message,"确认共享操作",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes;
    private Task Decide(bool approve)
    {
        var selected=CheckedRows(_pending);
        if(selected.Length==0)throw new InvalidOperationException("请先勾选“待确认申请”中的手机，再执行批量操作。");
        var pairing=Required().Pairing??throw new InvalidOperationException("共享已停止，请重新应用共享设置，让手机重新扫码申请。");
        var verb=approve?"批准":"拒绝";
        if(!Confirm($"{verb}已勾选的 {selected.Length} 个手机申请？"+(approve?"批准后将授权读取当前账号额度。":"")))return Task.CompletedTask;
        var outcomes=ExecuteBatch(selected.Select(item=>((string)item.Tag!,item.Text)),id=>approve?pairing.Approve(id):pairing.Reject(id));
        ShowBatchResults(verb,outcomes);
        return Task.CompletedTask;
    }
    private Task ReplaceAndApprove()
    {
        var pending=CheckedRows(_pending);var devices=CheckedRows(_devices);
        if(pending.Length!=1||devices.Length!=1)
            throw new InvalidOperationException("“替换并批准”需要恰好勾选一条待确认申请和一台已配对设备。");
        var newName=pending[0].Text;var oldName=devices[0].Text;
        var newExpiry=pending[0].SubItems.Count>2?pending[0].SubItems[2].Text:"未知";
        var oldPairingTime=devices[0].SubItems.Count>2?devices[0].SubItems[2].Text:"未知";
        var message=$"确认用新设备“{newName}”替换旧设备“{oldName}”？\n新申请到期时间：{newExpiry}\n旧设备配对时间：{oldPairingTime}\n替换成功后旧设备令牌将立即失效。";
        if(!Confirm(message))return Task.CompletedTask;
        var pairing=Required().Pairing??throw new InvalidOperationException("共享已停止，请重新应用共享设置，让手机重新扫码申请。");
        if(!pairing.ReplaceApproved((string)pending[0].Tag!,(string)devices[0].Tag!))
            throw new InvalidOperationException("替换未完成：申请可能已过期、旧设备已移除，或当前账号尚不可授权。旧设备保持不变，请刷新列表后重试。");
        return Task.CompletedTask;
    }
    private Task AuthorizeSelected()
    {
        var selected=CheckedRows(_devices);
        if(selected.Length==0)throw new InvalidOperationException("请先勾选要授权的设备。");
        if(!Confirm($"允许已勾选的 {selected.Length} 台设备读取当前账号额度？换号后需重新确认。"))return Task.CompletedTask;
        var runtime=Required();
        var outcomes=ExecuteBatch(selected.Select(item=>((string)item.Tag!,item.Text)),runtime.ConfirmAccount);
        ShowBatchResults("授权当前账号",outcomes);return Task.CompletedTask;
    }
    private Task RevokeSelected()
    {
        var selected=CheckedRows(_devices);
        if(selected.Length==0)throw new InvalidOperationException("请先勾选要撤销的设备。");
        if(!Confirm($"撤销已勾选的 {selected.Length} 台设备？这些手机之后必须重新扫码配对。"))return Task.CompletedTask;
        var runtime=Required();
        var outcomes=ExecuteBatch(selected.Select(item=>((string)item.Tag!,item.Text)),runtime.RevokeDevice);
        ShowBatchResults("撤销设备",outcomes);return Task.CompletedTask;
    }
    private Task Rename()
    {
        var selected=CheckedRows(_devices);
        if(selected.Length!=1)throw new InvalidOperationException("重命名需要恰好勾选一台设备。");
        var id=(string)selected[0].Tag!;
        using var dialog=new Form{Text="设备重命名",Size=new(360,160),StartPosition=FormStartPosition.CenterParent,AutoScaleMode=AutoScaleMode.Dpi};
        var input=new TextBox{Text=selected[0].Text,MaxLength=80,Dock=DockStyle.Top,BackColor=UiTheme.Card,ForeColor=UiTheme.Text};
        var save=UiTheme.SecondaryButton("保存");save.Dock=DockStyle.Bottom;save.DialogResult=DialogResult.OK;dialog.Controls.Add(input);dialog.Controls.Add(save);dialog.AcceptButton=save;
        ThemeManager.ApplyTo(dialog,UiTheme.Palette,UiTheme.UseSystemColors);
        if(dialog.ShowDialog(this)==DialogResult.OK)Required().RenameDevice(id,input.Text);return Task.CompletedTask;
    }
    internal Task ShowInvitation()
    {
        ClearInvitation();var pairing=Required().Pairing??throw new InvalidOperationException("共享未监听，请检查选定 Wi-Fi / 以太网网卡并点击“应用”。");
        var result=pairing.CreateInvitation();if(result.Value is null)throw new InvalidOperationException(result.ErrorCode);
        _invitation=result.Value;_invitationOwner=pairing;
        using var data=QRCodeGenerator.GenerateQrCode(pairing.DevicePageUrl(_invitation),QRCodeGenerator.ECCLevel.Q);using var code=new PngByteQRCode(data);
        using var stream=new MemoryStream(code.GetGraphic(6));using var loaded=Image.FromStream(stream);_qr.Image=new Bitmap(loaded);_qr.Visible=true;
        _qrInfo.Text="手机扫码后，先通过浏览器证书查看器核对指纹，再提交配对申请。";return Task.CompletedTask;
    }
    internal void ClearInvitation()
    {var image=_qr.Image;_qr.Image=null;image?.Dispose();_qr.Visible=false;_invitation=null;_invitationOwner=null;_qrInfo.Text="";}
    internal void RefreshState()
    {
        if(_runtime is null)return;
        try
        {
            var s=_runtime.Status;
            _status.Text=$"状态：{s.State}  {s.Endpoint}\n{s.ErrorCode ?? s.DiscoveryErrorCode}";
            if(_runtime.SelectedAdapter is null)_status.Text="请选择连接手机同一局域网的实体 Wi-Fi / 以太网（公用、专用网络均支持），再点“应用”。";
            _identity.Text=(_runtime.IdentityWasReset?"身份已重置：旧设备需重新扫码。\n":"")+"证书 SHA-256："+_runtime.Fingerprint;
            var account=_runtime.Grants.CurrentAccount;
            _account.Text=account is null?"当前账号不可识别或监测关闭；配对后暂不共享额度。":"共享范围："+account.DisplayName+" 的只读额度；换号后需逐设备重新授权。";
            if(!_nickname.Focused&&account is not null)_nickname.Text=account.DisplayName;
            var pairing=_runtime.Pairing;
            if(_invitation is {} invite)
            {
                if(pairing is null||pairing!=_invitationOwner||!pairing.HasActiveInvitation||invite.ExpiresAt<=DateTimeOffset.UtcNow)ClearInvitation();
                else _qrInfo.Text=$"邀请码剩余 {Math.Max(0,(int)(invite.ExpiresAt-DateTimeOffset.UtcNow).TotalSeconds)} 秒；请核对浏览器实际证书。";
            }
            UpdateList(_pending,(pairing?.Pending()??[]).Select(p=>(p.PairId,new[]{p.DeviceName,p.RemoteAddress,
                p.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture)})));
            UpdateList(_devices,_runtime.Devices.List().Select(d=>(d.Id,new[]{d.Name,_runtime.Grants.IsGranted(d.Id)?"已授权":"需确认当前账号",
                d.AddedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture)})));
            UpdateBatchControls();
        }
        catch{_status.Text="状态读取失败；共享按网络验证失败关闭。";ClearInvitation();}
    }
    private void UpdateList(ListView list,IEnumerable<(string Id,string[] Text)> entries)
    {
        var rows=entries.ToArray();
        if(list.Items.Count==rows.Length&&rows.Select((r,i)=>Equals(list.Items[i].Tag,r.Id)&&list.Items[i].SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s=>s.Text).SequenceEqual(r.Text)).All(v=>v))return;
        var selected=list.SelectedItems.Cast<ListViewItem>().Select(item=>item.Tag).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var checkedIds=list.CheckedItems.Cast<ListViewItem>().Select(item=>item.Tag).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var focusedId=list.FocusedItem?.Tag as string;
        var topId=list.IsHandleCreated?list.TopItem?.Tag as string:null;
        _updatingList=true;list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach(var row in rows)
            {
                var item=new ListViewItem(row.Text){Tag=row.Id};
                item.ToolTipText=list.Name=="BridgePending"
                    ?$"手机名称：{row.Text.ElementAtOrDefault(0)}\n来源：{row.Text.ElementAtOrDefault(1)}\n到期时间：{row.Text.ElementAtOrDefault(2)}"
                    :$"设备：{row.Text.ElementAtOrDefault(0)}\n设备 ID：{row.Id}\n当前账号：{row.Text.ElementAtOrDefault(1)}\n配对时间：{row.Text.ElementAtOrDefault(2)}";
                list.Items.Add(item);
                item.Checked=checkedIds.Contains(row.Id);
                item.Selected=selected.Contains(row.Id);
                item.Focused=row.Id==focusedId;
            }
        }
        finally{list.EndUpdate();_updatingList=false;}
        if(topId is not null&&list.Items.Cast<ListViewItem>().FirstOrDefault(item=>Equals(item.Tag,topId)) is {} top)
            list.TopItem=top;
        ResizeTableColumns(list,list==_pending?0.52:0.62,160);
        HideHorizontalScrollbar(list);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            _timer.Stop();_timer.Dispose();ClearInvitation();
            if(_hostForm is not null)
            {
                _hostForm.VisibleChanged-=OnHostVisibilityChanged;
                _hostForm.Resize-=OnHostVisibilityChanged;
                _hostForm=null;
            }
        }
        base.Dispose(disposing);
    }
}
