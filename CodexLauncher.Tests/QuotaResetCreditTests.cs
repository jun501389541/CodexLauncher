using CodexLauncher.App;
using CodexLauncher.Core;
using System.Windows.Forms;

internal static class QuotaResetCreditTests
{
    internal static IEnumerable<(string Name,Action Run)> All =>
    [
        ("reset cards distinguish unavailable, zero and count-only responses", Availability),
        ("reset cards preserve multiple details and authoritative totals", Multiple),
        ("reset cards tolerate malformed optional data without losing quota windows", Malformed),
        ("reset cards use local expiration time and retain unknown fields", Expiration),
        ("reset cards clear old details across refreshed account snapshots", Refresh),
        ("reset cards fit narrow layouts and shrink from multiple cards to empty", Layout)
    ];
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-08T12:00:00Z");
    private const string Cards="""
        {"availableCount":3,"credits":[
          {"id":"a","resetType":"codexRateLimits","status":"available","grantedAt":1790000000,"expiresAt":1794038400,"title":"完全重置（每周 + 5 小时）","description":"恢复符合条件的使用限额。"},
          {"id":"b","resetType":"codexRateLimits","status":"redeeming","grantedAt":1790000000,"expiresAt":null,"title":null,"description":null}
        ]}
        """;
    private static QuotaSnapshot Snapshot(string? cards,bool omit=false) => QuotaResponseParser.Build(
        new(QuotaAccountKind.ChatGpt,"synthetic@example.invalid","plus",true,null),
        "{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"windowDurationMins\":300,\"usedPercent\":20}}"+
        (omit?"":",\"rateLimitResetCredits\":"+(cards??"null"))+"}",Now);
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Availability()
    {
        Check(Snapshot(null,true).ResetCredits is null,"missing cards were interpreted as zero");
        Check(Snapshot(null).ResetCredits is null,"null cards were interpreted as zero");
        var zero=Snapshot("{\"availableCount\":0,\"credits\":[]}").ResetCredits!;
        Check(zero.AvailableCount==0&&zero.Credits!.Count==0,"zero cards were not retained");
        var count=Snapshot("{\"availableCount\":4,\"credits\":null}").ResetCredits!;
        Check(count.AvailableCount==4&&count.Credits is null,"count-only cards invented detail rows");
    }
    private static void Multiple()
    {
        var cards=Snapshot(Cards).ResetCredits!;
        Check(cards.AvailableCount==3&&cards.Credits!.Count==2,"detail length replaced authoritative total");
        Check(cards.Credits![0].Title=="完全重置（每周 + 5 小时）"&&cards.Credits[1].Status=="redeeming","multiple card fields were lost");
    }
    private static void Malformed()
    {
        foreach(var data in new[]{"{}","[]","{\"availableCount\":-1}","{\"availableCount\":\"3\"}"})
        {var snapshot=Snapshot(data);Check(snapshot.ResetCredits is null&&snapshot.HasData,"invalid optional cards broke the quota result");}
        var partial=Snapshot("{\"availableCount\":2,\"credits\":[null,42,{\"resetType\":\"future\",\"status\":\"future\",\"expiresAt\":9999999999999}]}").ResetCredits!;
        Check(partial.AvailableCount==2&&partial.Credits!.Count==1,"malformed card rows were not skipped");
        Check(!partial.Credits![0].ExpiryKnown,"invalid expiration appeared valid");
    }
    private static void Expiration()
    {
        var china=TimeZoneInfo.CreateCustomTimeZone("ResetChina",TimeSpan.FromHours(8),"ResetChina","ResetChina");
        var cards=Snapshot(Cards).ResetCredits!;
        Check(cards.Credits![0].ExpirationLabel(Now,china)=="2026-11-07 16:00 到期","expiration was not converted from Unix seconds into the selected time zone");
        Check(cards.Credits[1].ExpirationLabel(Now,china)=="无到期限制","explicit no-expiration was interpreted as unknown");
        var unknown=Snapshot("{\"availableCount\":1,\"credits\":[{}]}").ResetCredits!.Credits![0];
        Check(unknown.ExpirationLabel(Now,china)=="到期时间未知"&&unknown.StatusLabel=="状态未知","missing card fields were invented");
        Check(cards.Credits[0].ExpirationLabel(Now.AddYears(1),china).Contains("已到期"),"expired cards were labeled available");
    }
    private static void Refresh()
    {
        Check(Snapshot(Cards).ResetCredits!.Credits!.Count==2,"initial multi-card snapshot missing");
        Check(Snapshot("{\"availableCount\":0,\"credits\":[]}").ResetCredits!.Credits!.Count==0,"empty refreshed snapshot retained old cards");
        Check(Snapshot(null).ResetCredits is null,"unknown refreshed account retained old cards");
    }
    private static void Layout()
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try
            {
                using var form=new Form{ClientSize=new System.Drawing.Size(420,800),ShowInTaskbar=false,Opacity=0};
                using var panel=new QuotaResetCreditsPanel{Dock=DockStyle.Top};
                form.Controls.Add(panel);form.Show();Application.DoEvents();
                panel.Render(Snapshot(Cards).ResetCredits,Now);Application.DoEvents();form.PerformLayout();
                Check(panel.Controls.OfType<Label>().First().Text.Contains("可用 3 次"),"multi-card heading omitted the total");
                Check(panel.Controls.OfType<Label>().Any(l=>l.Text.Contains("部分卡片详情暂不可用")),"capped details were reported as complete");
                var expanded=panel.Height;
                foreach(var row in panel.Controls.OfType<QuotaResetCreditRow>())
                {
                    Check(!row.Font.Bold,"card detail rows should use normal weight");
                    var date=row.Controls.OfType<Label>().Single(l=>l.Text.Contains("到期")||l.Text=="无到期限制");
                    Check(date.Right==row.ClientSize.Width,"expiration was not aligned to the right edge");
                    Check(date.Bottom<=row.ClientSize.Height,"wrapped expiration was clipped");
                }
                foreach(var label in panel.Controls.OfType<Label>())
                    Check(label.Right<=panel.ClientSize.Width&&label.Bottom<=panel.ClientSize.Height,"card labels were clipped in a narrow panel");
                ThemeManager.ApplyTo(panel,ThemePalette.Dark,false);
                Check(panel.BackColor==UiTheme.Card,"cards did not follow the dark theme");
                panel.Render(Snapshot("{\"availableCount\":0,\"credits\":[]}").ResetCredits,Now);Application.DoEvents();
                Check(panel.Height<expanded&&panel.Controls.OfType<Label>().Any(l=>l.Text=="暂无可用重置卡"),"empty state retained old card rows or height");
                panel.Render(null,Now);Application.DoEvents();
                Check(panel.Controls.OfType<Label>().Any(l=>l.Text.Contains("暂不可用")),"unknown cards were rendered as none");
                Check(!panel.Controls.OfType<Button>().Any(),"display-only cards exposed a redemption action");
                using var main=new MainForm(null,true);
                main.ClientSize=new System.Drawing.Size(700,800);
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var render=typeof(MainForm).GetMethod("RenderQuota",flags)!;
                render.Invoke(main,new object[]{Snapshot(Cards)});main.PerformLayout();
                var embedded=(QuotaResetCreditsPanel)typeof(MainForm).GetField("_quotaResetCredits",flags)!.GetValue(main)!;
                Check(embedded.Controls.OfType<Label>().Any(l=>l.Text.Contains("可用 3 次")),"main quota section did not render the reset cards");
                main.Scale(new System.Drawing.SizeF(1.5F,1.5F));main.PerformLayout();embedded.PerformLayout();
                foreach(var label in embedded.Controls.OfType<Label>())
                    Check(label.Right<=embedded.ClientSize.Width&&label.Bottom<=embedded.ClientSize.Height,"main-window reset cards were clipped after scaling");
                var quotaLayout=embedded.Parent!;
                var quotaCard=quotaLayout.Parent!;
                var host=quotaCard.Parent!.Parent!;
                var layoutEvents=0;
                quotaLayout.Layout+=(_,_)=>layoutEvents++;
                var resizeWatch=System.Diagnostics.Stopwatch.StartNew();
                for(var iteration=0;iteration<30;iteration++)
                {
                    main.ClientSize=new System.Drawing.Size(iteration%2==0?1320:1040,900);
                    main.PerformLayout();quotaCard.Parent.PerformLayout();quotaLayout.PerformLayout();embedded.PerformLayout();
                    Check(quotaLayout.Right<=quotaCard.ClientSize.Width,
                        $"resize {iteration}: quota layout width {quotaLayout.Width} exceeds card width {quotaCard.ClientSize.Width}; root {quotaCard.Parent.Width}, host {host.Width}");
                    foreach(Control child in quotaLayout.Controls)
                        Check(child.Right<=quotaLayout.ClientSize.Width-quotaLayout.Padding.Right,
                            $"resize {iteration}: {child.GetType().Name} right {child.Right} exceeds layout {quotaLayout.ClientSize.Width-quotaLayout.Padding.Right}; preferred {child.GetPreferredSize(System.Drawing.Size.Empty)}");
                    foreach(var row in embedded.Controls.OfType<QuotaResetCreditRow>())
                        Check(row.Right<=embedded.ClientSize.Width&&row.Controls.OfType<Label>().All(l=>l.Right<=row.ClientSize.Width),"repeated resize moved expiration outside the card");
                }
                // 在有真实句柄的窗口中再验证同一卡片，避免仅检查未显示窗体的布局。
                using var preview=new Form{ClientSize=new System.Drawing.Size(900,620),ShowInTaskbar=false,Opacity=0};
                preview.Controls.Add(quotaCard);preview.Show();Application.DoEvents();
                for(var iteration=0;iteration<30;iteration++)
                {
                    preview.ClientSize=new System.Drawing.Size(iteration%2==0?1000:700,620);
                    Application.DoEvents();
                    foreach(Control child in quotaLayout.Controls)
                        Check(child.Right<=quotaLayout.ClientSize.Width-quotaLayout.Padding.Right,"visible quota card overflowed after repeated resize");
                    foreach(var row in embedded.Controls.OfType<QuotaResetCreditRow>())
                        Check(row.Controls.OfType<Label>().All(l=>l.Right<=row.ClientSize.Width&&l.Bottom<=row.ClientSize.Height),"visible expiration was clipped after repeated resize");
                }
                resizeWatch.Stop();
                Console.WriteLine($"Resize benchmark: 60 transitions, {layoutEvents} quota layouts, {resizeWatch.Elapsed.TotalMilliseconds:F0} ms");
                if(Environment.GetEnvironmentVariable("CODEX_LAYOUT_CAPTURE") is {Length:>0} capture)
                {
                    using var bitmap=new System.Drawing.Bitmap(quotaCard.Width,quotaCard.Height);
                    quotaCard.DrawToBitmap(bitmap,quotaCard.ClientRectangle);
                    bitmap.Save(capture,System.Drawing.Imaging.ImageFormat.Png);
                }
                render.Invoke(main,new object[]{Snapshot("{\"availableCount\":0,\"credits\":[]}")});main.PerformLayout();
                Check(!embedded.Controls.OfType<Label>().Any(l=>l.Text.Contains("完全重置")),"main-window refresh retained old card details");
            }
            catch(Exception e){error=e;}
            finally{UiTheme.Use(ThemePalette.Light,false);}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
}
