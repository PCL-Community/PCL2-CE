using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI.Controls;
using PCL.Core.Utils;
using PCL.Core.IO.Net.Http;
using PCL.Core.Logging;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.IdentityModel.OAuth;

namespace PCL;

public partial class MyMsgLogin
{
    private readonly JsonObject data;
    private string userCode; // 需要用户在网页上输入的设备代码
    private string website; // 验证网页的网址
    private readonly CancellationTokenSource _cancellation = new();

    public MyMsgLogin()
    {
        InitializeComponent();
        // Handles
        Loaded += Load;
        Btn1.Click += Btn1_Click;
        Btn3.Click += Btn3_Click;
        PanBorder.MouseLeftButtonDown += Drag;
        LabTitle.MouseLeftButtonDown += Drag;
    }

    private void Finished(object result)
    {
        if (myConverter.IsExited)
            return;
        myConverter.IsExited = true;
        _cancellation.Cancel();
        myConverter.Result = result;
        myConverter.CompletionHandler?.Invoke(result);
        ModBase.RunInUi(Close);
        Thread.Sleep(200);
        ModMain.frmMain.ShowWindowToTop();
    }

    private void Init()
    {
        userCode = (string)data["user_code"];
        if (data["verification_uri_complete"] is not null)
        {
            website = (string)data["verification_uri_complete"];
            LabCaption.Text = Lang.Text("Launch.Account.LoginDialog.MicrosoftInstructions.WithAutoFill", userCode, website);
        }
        else
        {
            website = (string)data["verification_uri"];
            LabCaption.Text = Lang.Text("Launch.Account.LoginDialog.MicrosoftInstructions", userCode, website);
        }

        // 设置 UI
        LabTitle.Text = Lang.Text("Launch.Account.LoginDialog.MinecraftLogin");
        CustomEventService.SetEventData(Btn1, website);
        CustomEventService.SetEventData(Btn2, userCode);
        // 启动工作线程
        _ = WorkThreadAsync();
    }

    private async Task WorkThreadAsync()
    {
        var token = _cancellation.Token;
        try
        {
            await Task.Delay(2000, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (myConverter.IsExited)
        {
            return;
        }
        if (myConverter.IsExited)
            return;
        ModBase.OpenWebsite(website);
        ModBase.ClipboardSet(userCode);
        var device = data.ToObject<DeviceCodeData>() ?? throw new InvalidDataException("设备授权数据无效");
        var delayTime = TimeSpan.FromSeconds(device.Interval ?? 5);
        // 轮询
        var unknownFailureCount = 0;
        while (!myConverter.IsExited && !token.IsCancellationRequested)
        {
            try
            {
                if (myConverter.DeviceCodePoll is null)
                    throw new InvalidOperationException("设备授权没有配置轮询处理程序。");
                var resultJson = await myConverter.DeviceCodePoll(data, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (resultJson is { IsError: true })
                {
                    switch(resultJson.Error)
                    {
                        case "authorization_pending":
                            {
                                await Task.Delay(delayTime, token)
                                    .ConfigureAwait(false);
                                continue;
                            }
                        case "slow_down":
                            delayTime += TimeSpan.FromSeconds(5);
                            await Task.Delay(delayTime, token).ConfigureAwait(false);
                            continue;
                        case "expired_token":
                            Finished(new IdentityModelAuthenticationException(
                                resultJson.Error, resultJson.ErrorDescription ?? "设备授权已过期。"));
                            return;
                        case "access_denied":
                        case "authorization_declined":
                            Finished(new IdentityModelAuthenticationException(
                                resultJson.Error, resultJson.ErrorDescription ?? "用户拒绝了设备授权。"));
                            return;
                        default:
                            {
                            throw new IdentityModelAuthenticationException(
                                resultJson.Error ?? "invalid_response",
                                resultJson.ErrorDescription ?? "Unable to get body");
                            }
                    }
                }
                // 获取结果
                if (resultJson is null) throw new InvalidDataException("微软令牌返回为空");
                if (myConverter.LoginResultHandler is not null)
                {
                    try
                    {
                        await myConverter.LoginResultHandler(resultJson, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                    }
                    catch (Exception handlerException)
                    {
                        Finished(handlerException);
                        return;
                    }
                }
                LogWrapper.Info("Profile",$"令牌过期时间：{resultJson.ExpiresIn} 秒");
                HintService.Hint(Lang.Text("Launch.Account.LoginDialog.Success"), HintType.Success);
                Finished(new[] { resultJson.AccessToken ?? string.Empty, resultJson.RefreshToken ?? string.Empty });
                return;
            }
            catch (OperationCanceledException) when (myConverter.IsExited)
            {
                return;
            }
            catch (Exception ex)
            {
                if (unknownFailureCount <= 2)
                {
                    unknownFailureCount += 1;
                    ModBase.Log(ex, $"正版验证轮询第 {unknownFailureCount} 次失败");
                    ModBase.Log(ex.Message);
                    try
                    {
                        await Task.Delay(2000, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (myConverter.IsExited)
                    {
                        return;
                    }
                }
                else
                {
                    Finished(new InvalidOperationException(Lang.Text("Launch.Account.LoginDialog.PollingFailed"), ex));
                    return;
                }
            }
        }
    }


    #region 弹窗

    private readonly ModMain.MyMsgBoxConverter myConverter;
    private readonly int uuid = ModBase.GetUuid();

    public MyMsgLogin(ModMain.MyMsgBoxConverter converter)
    {
        try
        {
            InitializeComponent();
            Btn1.Name += ModBase.GetUuid();
            Btn2.Name += ModBase.GetUuid();
            Btn3.Name += ModBase.GetUuid();
            myConverter = converter;
            ShapeLine.StrokeThickness = ModBase.GetWPFSize(1d);
            data = (JsonObject)converter.Content;
            Init();
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Launch.Account.LoginDialog.Error.Init"),
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Launch.Account.LoginDialog.Error.Init"));
        }

        Loaded += Load;
    }

    private void Load(object sender, EventArgs e)
    {
        try
        {
            // 动画
            Opacity = 0d;
            ModAnimation.AniStart(
                ModAnimation.AaColor(ModMain.frmMain.PanMsgBackground, BlurBorder.BackgroundProperty,
                    (myConverter.IsWarn
                        ? new ModBase.MyColor(140d, 80d, 0d, 0d)
                        : new ModBase.MyColor(90d, 0d, 0d, 0d)) - ModMain.frmMain.PanMsgBackground.Background, 200),
                "PanMsgBackground Background");
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaOpacity(this, 1d, 120, 60),
                    ModAnimation.AaDouble(i => TransformPos.Y += (double)i,
                        -TransformPos.Y, 300, 60, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaDouble(i => TransformRotate.Angle += (double)i,
                        -TransformRotate.Angle, 300, 60,
                        new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak))
                }, "MyMsgBox " + uuid);
            // 记录日志
            ModBase.Log($"[Control] 正版验证弹窗：{LabTitle.Text}\r\n{LabCaption.Text}");
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Launch.Account.LoginDialog.Error.Load"),
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Launch.Account.LoginDialog.Error.Load"));
        }
    }

    private void Close()
    {
        // 动画
        ModAnimation.AniStart(new[]
        {
            ModAnimation.AaCode(() =>
            {
                if (!ModMain.WaitingMyMsgBox.Any())
                    ModAnimation.AniStart(ModAnimation.AaColor(ModMain.frmMain.PanMsgBackground,
                        BlurBorder.BackgroundProperty,
                        new ModBase.MyColor(0d, 0d, 0d, 0d) - ModMain.frmMain.PanMsgBackground.Background, 200,
                        ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)));
            }, 30),
            ModAnimation.AaOpacity(this, -Opacity, 80, 20),
            ModAnimation.AaDouble(i => TransformPos.Y += (double)i, 20d - TransformPos.Y,
                150, 0, new ModAnimation.AniEaseOutFluent()),
            ModAnimation.AaDouble(i => TransformRotate.Angle += (double)i,
                6d - TransformRotate.Angle, 150, 0, new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)),
            ModAnimation.AaCode(() => ((Grid)Parent).Children.Remove(this), after: true)
        }, "MyMsgBox " + uuid);
    }

    // 实现回车和 Esc 的接口（#4857）
    public void Btn1_Click(object sender, MouseButtonEventArgs e)
    {
    }

    public void Btn3_Click(object sender, MouseButtonEventArgs e)
    {
        Finished(new ThreadInterruptedException());
    }

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        // On Error Resume Next
        if (e.GetPosition(ShapeLine).Y <= 2d)
            ModMain.frmMain.DragMove();
    }

    #endregion
}
