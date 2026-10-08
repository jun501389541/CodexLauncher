using CodexLauncher.Core;

namespace CodexLauncher.App;

/// <summary>自适应宽度的只读重置卡区域；不提供兑换操作。</summary>
internal sealed class QuotaResetCreditsPanel:TableLayoutPanel
{
    private readonly Font _headingFont=new("Microsoft YaHei UI",10F,FontStyle.Bold);
    private readonly Font _bodyFont=new("Microsoft YaHei UI",9F);
    private readonly List<Control> _labels=[];

    internal QuotaResetCreditsPanel()
    {
        Name="QuotaResetCredits";Dock=DockStyle.Top;AutoSize=true;
        AutoSizeMode=AutoSizeMode.GrowAndShrink;ColumnCount=1;
        ColumnStyles.Add(new(SizeType.Percent,100));
        Margin=new(0,8,0,8);BackColor=UiTheme.Card;
    }

    internal void Render(QuotaResetCredits? summary,DateTimeOffset now)
    {
        var rows=new List<(string Text,bool Heading,string? Expiration)>
        {
            (summary is null?"使用限额重置 · 暂不可用":$"使用限额重置 · 可用 {summary.AvailableCount} 次",true,null)
        };
        if(summary is null)rows.Add(("尚未读取到重置卡信息。",false,null));
        else if(summary.AvailableCount==0)rows.Add(("暂无可用重置卡",false,null));
        else if(summary.Credits is null||summary.Credits.Count==0)
            rows.Add(("卡片详情暂不可用。",false,null));
        else
        {
            var index=0;
            foreach(var card in summary.Credits.OrderBy(c=>c.ExpiresAt??DateTimeOffset.MaxValue))
            {
                var state=card.ExpiresAt<=now?"已到期":card.StatusLabel;
                var prefix=summary.Credits.Count>1?$"{++index}. ":"";
                rows.Add(($"{prefix}{card.TitleLabel} · {state}",false,card.ExpirationLabel(now)));
            }
            if(summary.Credits.Count<summary.AvailableCount)
                rows.Add(("部分卡片详情暂不可用，可用总数以接口返回为准。",false,null));
        }

        SuspendLayout();
        try
        {
            while(_labels.Count>rows.Count)
            {var last=_labels[^1];_labels.RemoveAt(_labels.Count-1);Controls.Remove(last);last.Dispose();}
            while(_labels.Count<rows.Count)
            {
                var label=new Label{AutoSize=true,UseMnemonic=false,Anchor=AnchorStyles.Left|AnchorStyles.Right};
                _labels.Add(label);Controls.Add(label,0,_labels.Count-1);
            }
            RowCount=rows.Count;
            for(var i=0;i<rows.Count;i++)
            {
                var label=_labels[i];
                if((rows[i].Expiration is not null)!=(label is QuotaResetCreditRow))
                {
                    Controls.Remove(label);label.Dispose();
                    label=rows[i].Expiration is not null?new QuotaResetCreditRow():new Label{AutoSize=true,UseMnemonic=false};
                    label.Anchor=AnchorStyles.Left|AnchorStyles.Right;
                    _labels[i]=label;Controls.Add(label,0,i);
                }
                label.Text=rows[i].Text;
                label.Font=rows[i].Heading?_headingFont:_bodyFont;
                label.ForeColor=rows[i].Heading||rows[i].Expiration is not null?UiTheme.Text:UiTheme.Muted;
                label.BackColor=UiTheme.Card;
                label.Margin=new(0,i>1&&rows[i].Expiration is not null?8:0,0,4);
                if(label is QuotaResetCreditRow cardRow)cardRow.SetExpiration(rows[i].Expiration!);
            }
            ApplyWidths();
        }
        finally{ResumeLayout(true);}
    }

    internal void ApplyTheme()
    {
        BackColor=UiTheme.Card;
        foreach(var label in _labels)
        {label.BackColor=UiTheme.Card;label.ForeColor=label.Font.Bold||label is QuotaResetCreditRow?UiTheme.Text:UiTheme.Muted;}
    }

    private void ApplyWidths(int? availableWidth=null)
    {
        foreach(var label in _labels)
        {
            var maximum=new Size(Math.Max(1,(availableWidth??ClientSize.Width)-Padding.Horizontal-label.Margin.Horizontal),0);
            if(label.MaximumSize!=maximum)label.MaximumSize=maximum;
        }
    }

    protected override void SetBoundsCore(int x,int y,int width,int height,BoundsSpecified specified)
    {
        if(_labels is null||width==Width)
        {base.SetBoundsCore(x,y,width,height,specified);return;}
        // Constrain text before layout measures the new width, rather than
        // invalidating its just-completed layout from OnSizeChanged.
        SuspendLayout();
        try
        {
            ApplyWidths(width);
            base.SetBoundsCore(x,y,width,height,specified);
        }
        finally{ResumeLayout(false);}
        PerformLayout();
    }

    protected override void Dispose(bool disposing)
    {base.Dispose(disposing);if(disposing){_headingFont.Dispose();_bodyFont.Dispose();}}
}

/// <summary>卡片说明靠左，到期时间靠右；空间不足时日期独占下一行。</summary>
internal sealed class QuotaResetCreditRow : Panel
{
    private readonly Label _details=new(){UseMnemonic=false};
    private readonly Label _expiration=new(){UseMnemonic=false,TextAlign=ContentAlignment.TopRight};
    private string? _measuredDetails;
    private string? _measuredExpiration;
    private Font? _measuredFont;
    private Size _detailsSize;
    private Size _expirationSize;
    private int _wrappedWidth=-1;
    private Size _wrappedDetails;
    private Size _wrappedExpiration;
    internal QuotaResetCreditRow()
    {
        AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        Controls.Add(_details);Controls.Add(_expiration);
    }
    internal void SetExpiration(string expiration)
    {if(_expiration.Text==expiration)return;_expiration.Text=expiration;PerformLayout();}
    protected override void OnTextChanged(EventArgs e)
    {base.OnTextChanged(e);if(_details is not null)_details.Text=Text;}
    public override Size GetPreferredSize(Size proposedSize)
    {
        var width=MaximumSize.Width>0?MaximumSize.Width:Math.Max(1,proposedSize.Width);
        MeasureText();
        var left=_detailsSize;
        var right=_expirationSize;
        var stacked=left.Width+right.Width+12>width;
        if(stacked)MeasureWrappedText(width);
        var height=stacked?_wrappedDetails.Height+_wrappedExpiration.Height:Math.Max(left.Height,right.Height);
        return new Size(width,height);
    }
    private void MeasureText()
    {
        if(_measuredDetails==Text&&_measuredExpiration==_expiration.Text&&ReferenceEquals(_measuredFont,Font))return;
        _detailsSize=TextRenderer.MeasureText(Text,Font);
        _expirationSize=TextRenderer.MeasureText(_expiration.Text,Font);
        _measuredDetails=Text;_measuredExpiration=_expiration.Text;_measuredFont=Font;
        _wrappedWidth=-1;
    }
    private void MeasureWrappedText(int width)
    {
        if(_wrappedWidth==width)return;
        var flags=TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl;
        _wrappedDetails=TextRenderer.MeasureText(Text,Font,new Size(width,int.MaxValue),flags);
        _wrappedExpiration=TextRenderer.MeasureText(_expiration.Text,Font,new Size(width,int.MaxValue),flags);
        _wrappedWidth=width;
    }
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {_measuredFont=null;base.OnDpiChangedAfterParent(e);PerformLayout();}
    protected override void OnLayout(LayoutEventArgs e)
    {
        if(_details is not null&&_expiration is not null)
        {
            var width=Math.Max(1,ClientSize.Width);
            MeasureText();
            var left=_detailsSize;
            var right=_expirationSize;
            var stacked=left.Width+right.Width+12>width;
            if(stacked)MeasureWrappedText(width);
            var top=stacked?_wrappedDetails.Height:0;
            _details.Bounds=new Rectangle(0,0,stacked?width:Math.Max(1,width-right.Width-12),stacked?top:ClientSize.Height);
            _expiration.Bounds=new Rectangle(stacked?0:Math.Max(0,width-right.Width),top,stacked?width:Math.Min(width,right.Width),Math.Max(0,ClientSize.Height-top));
        }
        base.OnLayout(e);
    }
}
