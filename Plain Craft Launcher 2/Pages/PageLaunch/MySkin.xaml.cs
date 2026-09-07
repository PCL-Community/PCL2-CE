using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft.Profile;
using PCL.Core.UI;
using PCL.Network;

namespace PCL;

public partial class MySkin
{
    public delegate void ClickEventHandler(object sender, MouseButtonEventArgs e);

    // 皮肤储存
    private bool isChanging;

    // 点击
    private bool isSkinMouseDown;
    public ModLoader.LoaderTask<ModBase.EqualableList<string>, string> loader;

    public MySkin()
    {
        InitializeComponent();
        MouseEnter += PanSkin_MouseEnter;
        MouseLeave += PanSkin_MouseLeave;
        MouseLeftButtonDown += PanSkin_MouseLeftButtonDown;
        MouseLeftButtonUp += PanSkin_MouseLeftButtonUp;
        // Handles
        BtnSkinSave.Click += BtnSkinSave_Click;
        BtnSkinSave.Checked += BtnSkinSave_Checked;
        BtnSkinRefresh.Click += RefreshClick;
        BtnSkinCape.Click += BtnSkinCape_Click;
    }

    public string Address
    {
        get => field;
        set
        {
            field = value;
            ToolTip = string.IsNullOrEmpty(field)
                ? Lang.Text("Common.State.Loading")
                : Lang.Text("Launch.Skin.Change.ToolTip");
        }
    }

    // 披风
    public bool HasCape
    {
        get => BtnSkinCape.Visibility == Visibility.Collapsed;
        set => BtnSkinCape.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    // 事件
    public event ClickEventHandler? Click;

    // 控件动画
    private void PanSkin_MouseEnter(object sender, MouseEventArgs e)
    {
        ModAnimation.AniStart(ModAnimation.AaOpacity(ShadowSkin, 0.8d - ShadowSkin.Opacity, 200, 100), "Skin Shadow");
    }

    private void PanSkin_MouseLeave(object sender, MouseEventArgs e)
    {
        ModAnimation.AniStart(ModAnimation.AaOpacity(ShadowSkin, 0.2d - ShadowSkin.Opacity, 200), "Skin Shadow");
        isSkinMouseDown = false;
        ModAnimation.AniStart(
            ModAnimation.AaScaleTransform(this, 1d - ((ScaleTransform)RenderTransform).ScaleX, 60,
                ease: new ModAnimation.AniEaseOutFluent()), "Skin Scale");
    }

    private void PanSkin_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        isSkinMouseDown = true;
        ModAnimation.AniStart(
            ModAnimation.AaScaleTransform(this, 0.9d - ((ScaleTransform)RenderTransform).ScaleX, 60,
                ease: new ModAnimation.AniEaseOutFluent()), "Skin Scale");
    }

    private void PanSkin_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ModAnimation.AniStart(
            ModAnimation.AaScaleTransform(this, 1d - ((ScaleTransform)RenderTransform).ScaleX, 60,
                ease: new ModAnimation.AniEaseOutFluent()), "Skin Scale");
        if (!isSkinMouseDown) return;
        isSkinMouseDown = false;
        Click?.Invoke(sender, e);
    }

    // 保存皮肤
    public void BtnSkinSave_Click(object sender, RoutedEventArgs e)
    {
        Save(loader);
    }

    public static void Save(ModLoader.LoaderTask<ModBase.EqualableList<string>, string> loader)
    {
        var address = loader.output;
        if (loader.State != ModBase.LoadState.Finished)
        {
            HintService.Hint(Lang.Text("Launch.Skin.Fetching"), HintType.Error);
            if (loader.State != ModBase.LoadState.Loading)
                loader.Start();
            return;
        }

        try
        {
            var fileAddress = SystemDialogs.SelectSaveFile(Lang.Text("Launch.Skin.SaveDialog.Title"),
                ModBase.GetFileNameFromPath(address),
                Lang.Text("Launch.Skin.SaveDialog.Filter"));
            if (!fileAddress.Contains(@"\")) return;
            File.Delete(fileAddress);
            if (address.StartsWith(ModBase.pathImage))
            {
                var image = new MyBitmap(address);
                image.Save(fileAddress);
            }
            else
            {
                ModBase.CopyFile(address, fileAddress);
            }

            HintService.Hint(Lang.Text("Launch.Skin.SaveSuccess"), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Launch.Skin.Save.Error"),
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Launch.Skin.Save.Error"));
        }
    }

    private void BtnSkinSave_Checked(object sender, RoutedEventArgs e)
    {
        ((MyMenuItem)sender).IsEnabled = string.IsNullOrEmpty(Address);
    }

    /// <summary>
    ///     载入皮肤。
    /// </summary>
    public void Load()
    {
        try
        {
            // 检查文件存在
            Address = loader.output;
            if (string.IsNullOrEmpty(Address))
                throw new Exception("皮肤加载器 " + loader.name + " 没有输出");
            if (!Address.StartsWith(ModBase.pathImage) && !File.Exists(Address))
                throw new FileNotFoundException("皮肤文件未找到", Address);
            // 加载
            MyBitmap image;
            try
            {
                image = new MyBitmap(Address);
            }
            catch (Exception ex) // #2272
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Launch.Skin.Load.Error.Corrupted", Address),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Skin.Load.Error.Corrupted", Address));
                File.Delete(Address);
                return;
            }

            ImgBack.Tag = Address;
            // 大小检查
            var scale = (int)Math.Round(image.pic.Width / 64d);
            if (image.pic.Width < 32 || image.pic.Height < 32)
            {
                ImgFore.Source = null;
                ImgBack.Source = null;
                throw new Exception("图片大小不足，长为 " + image.pic.Height + "，宽为 " + image.pic.Width);
            }

            MyBitmap skinHead = null;
            // 头发层（附加层）
            if (image.pic.Width >= 64 && image.pic.Height >= 32)
            {
                if (image.pic.GetPixel(1, 1).A == 0 ||
                    image.pic.GetPixel(image.pic.Width - 1, image.pic.Height - 1).A == 0 ||
                    image.pic.GetPixel(image.pic.Width - 2, (int)Math.Round(image.pic.Height / 2d - 2d)).A == 0 ||
                    (image.pic.GetPixel(1, 1) != image.pic.GetPixel(scale * 41, scale * 9) &&
                     image.pic.GetPixel(image.pic.Width - 1, image.pic.Height - 1) !=
                     image.pic.GetPixel(scale * 41, scale * 9) &&
                     image.pic.GetPixel(image.pic.Width - 2, (int)Math.Round(image.pic.Height / 2d - 2d)) !=
                     image.pic.GetPixel(scale * 41, scale * 9))) // 如果图片中有任何透明像素（避免纯色白底）
                    // 或是头部颜色和透明区均不一样
                {
                    ImgFore.Source = image.Clip(scale * 40, scale * 8, scale * 8, scale * 8);
                    skinHead = image.Clip(scale * 40, scale * 8, scale * 8, scale * 8);
                }
                else
                {
                    ImgFore.Source = null;
                }
            }
            else
            {
                ImgFore.Source = null;
            }

            // 脸层
            ImgBack.Source = image.Clip(scale * 8, scale * 8, scale * 8, scale * 8);
            // 用于显示档案列表头像的图片
            var skinHeadId = Address.Between(new[] { Address.Contains("Images/Skins/") ? "Skins/" : @"Skin\" }[0],
                ".png");
            var cachePath = ModBase.pathTemp + $@"Cache\Skin\Head\{skinHeadId}.png";
            if (ProfileService.Current is { } profile)
            {
                var updated = profile.Clone();
                updated.SkinHeadId = skinHeadId;
                ProfileService.Update(profile, updated);
                ProfileService.Select(updated);
            }
            var completeHead = new Bitmap(56, 56);
            using (var g = Graphics.FromImage(completeHead))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (Bitmap faceBitmap = image.Clip(scale * 8, scale * 8, scale * 8, scale * 8))
                {
                    g.DrawImage(faceBitmap, new Rectangle(4, 4, 48, 48));
                }

                if (ImgFore.Source is not null)
                {
                    using Bitmap hairBitmap = image.Clip(scale * 40, scale * 8, scale * 8, scale * 8);
                    g.DrawImage(hairBitmap, new Rectangle(0, 0, 56, 56));
                }
            }

            if (!Directory.Exists(ModBase.pathTemp + @"Cache\Skin\Head"))
                Directory.CreateDirectory(ModBase.pathTemp + @"Cache\Skin\Head");
            completeHead.Save(cachePath, ImageFormat.Png);
            ModBase.Log("[Skin] 载入头像成功：" + loader.name);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Launch.Skin.Load.Error.Avatar", $"{(Address ?? "null")},{loader.name}"),
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Launch.Skin.Load.Error.Avatar", $"{(Address ?? "null")},{loader.name}"));
        }
    }

    private object ScaleToSize(Bitmap bitmap, int width, int height)
    {
        var scaledBitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(scaledBitmap);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bitmap, 0, 0, width, height);

        return scaledBitmap;
    }

    /// <summary>
    ///     清空皮肤。
    /// </summary>
    public void Clear()
    {
        Address = "";
        ImgFore.Source = null;
        ImgBack.Source = null;
    }

    // 刷新缓存
    public void RefreshClick(object sender, RoutedEventArgs e)
    {
        RefreshCache(loader);
    }

    /// <summary>
    ///     刷新皮肤缓存。
    /// </summary>
    public static void RefreshCache(ModLoader.LoaderTask<ModBase.EqualableList<string>, string> sender = null)
    {
        var hasLoaderRunning =
            PageLaunchLeft.skinLoaders.Any(skinLoader => skinLoader.State == ModBase.LoadState.Loading);

        if (ModMain.frmLaunchLeft is not null && hasLoaderRunning)
            // 由于 Abort 不是实时的，暂时不会释放文件，会导致删除报错，故只能取消执行
            HintService.Hint(Lang.Text("Launch.Skin.Refresh.Busy"));
        else
            // 清空缓存
            // 刷新控件
            ModBase.RunInThread(() =>
            {
                try
                {
                    HintService.Hint(Lang.Text("Launch.Skin.Refreshing"));
                    ModBase.Log("[Skin] 正在清空皮肤缓存");
                    if (Directory.Exists(ModBase.pathTemp + @"Cache\Skin"))
                        ModBase.DeleteDirectory(ModBase.pathTemp + @"Cache\Skin");
                    if (Directory.Exists(ModBase.pathTemp + @"Cache\Uuid"))
                        ModBase.DeleteDirectory(ModBase.pathTemp + @"Cache\Uuid");
                    ModBase.IniClearCache(ModBase.pathTemp + @"Cache\Skin\IndexMs.ini");
                    ModBase.IniClearCache(ModBase.pathTemp + @"Cache\Skin\IndexAuth.ini");
                    ModBase.IniClearCache(ModBase.pathTemp + @"Cache\Uuid\Mojang.ini");
                    foreach (var SkinLoader in sender is not null
                                 ? new[] { sender }
                                 : new[] { PageLaunchLeft.skinLegacy, PageLaunchLeft.skinMs })
                        SkinLoader.WaitForExit(isForceRestart: true);
                    HintService.Hint(Lang.Text("Launch.Skin.RefreshSuccess"), HintType.Success);
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        Lang.Text("Launch.Skin.Refresh.Error"),
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Launch.Skin.Refresh.Error"));
                }
            });
    }

    /// <summary>
    ///     在更换正版皮肤后，刷新正版皮肤。
    /// </summary>
    /// <param name="skinAddress">新的正版皮肤完整地址。</param>
    public static void ReloadCache(string skinAddress, string uuid)
    {
        // 更新缓存
        // 刷新控件
        // 完成提示
        ModBase.RunInThread(() =>
        {
            try
            {
                ModBase.WriteIni(ModBase.pathTemp + @"Cache\Skin\IndexMs.ini", uuid, skinAddress);
                ModBase.Log($"[Skin] 已写入皮肤地址缓存 {uuid} -> {skinAddress}");
                PageLaunchLeft.skinMs.WaitForExit(isForceRestart: true);
                HintService.Hint(Lang.Text("Launch.Skin.ChangeSuccess"), HintType.Success);
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Launch.Skin.Change.Error.MsRefresh"),
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Launch.Skin.Change.Error.MsRefresh"));
            }
        });
    }

    public void BtnSkinCape_Click(object sender, RoutedEventArgs e)
    {
        // 检查条件，获取新披风
        if (isChanging)
        {
            HintService.Hint(Lang.Text("Launch.Skin.Cape.Changing"));
            return;
        }

        if (ModLaunch.mcLoginMsLoader.State == ModBase.LoadState.Failed)
        {
            HintService.Hint(Lang.Text("Launch.Skin.Cape.LoginFailed"), HintType.Error);
            return;
        }

        HintService.Hint(Lang.Text("Launch.Skin.Cape.FetchingList"));
        isChanging = true;
        // 开始实际获取
        ModBase.RunInNewThread(() =>
        {
            try
            {
                // 获取登录信息
                if (ModLaunch.mcLoginMsLoader.State != ModBase.LoadState.Finished)
                    ModLaunch.mcLoginMsLoader.WaitForExit(ProfileUi.GetLoginData());
                if (ModLaunch.mcLoginMsLoader.State != ModBase.LoadState.Finished)
                {
                    HintService.Hint(Lang.Text("Launch.Skin.Cape.LoginFailed"), HintType.Error);
                    return;
                }

                var accessToken = ModLaunch.mcLoginMsLoader.output.AccessToken;
                var uuid = ModLaunch.mcLoginMsLoader.output.Uuid;
                var skinData = (JsonObject)ModBase.GetJson(ModLaunch.mcLoginMsLoader.output.ProfileJson);
                foreach (var itemSkin in skinData["capes"].AsArray())
                {
                    if (itemSkin["url"] is null)
                        continue;
                    var localFile = $@"{ModBase.pathTemp}Cache\Capes\{itemSkin["alias"]}.png";
                    var capeFrontFile = $@"{ModBase.pathTemp}Cache\Capes\{itemSkin["alias"]}-front.png";
                    if (File.Exists(localFile) && File.Exists(capeFrontFile))
                    {
                        itemSkin["url"] = capeFrontFile;
                        continue;
                    }

                    FileDownloader.DownloadByLoader(itemSkin["url"].ToString(), localFile);
                    var capeFrontRegion = new Rectangle(1, 0, 11, 17);
                    var capeFront = new Bitmap(capeFrontRegion.Width, capeFrontRegion.Height);
                    var capeImage = Image.FromFile(localFile);
                    var gra = Graphics.FromImage(capeFront);
                    gra.DrawImage(capeImage, capeFrontRegion, capeFrontRegion, GraphicsUnit.Pixel);
                    capeFront.Save(capeFrontFile);
                    itemSkin["url"] = capeFrontFile;
                }

                // 获取玩家的所有披风
                int? selId = null;
                ModBase.RunInUiWait(() =>
                {
                    try
                    {
                        var selectionControl = new List<IMyRadio>
                        {
                            new MyListItem
                            {
                                Title = Lang.Text("Launch.Skin.Cape.None"),
                                Info = "Null"
                            }
                        };
                        selectionControl.AddRange(from Cape in skinData["capes"].AsArray()
                            let CapeAlias = Cape["alias"].ToString()
                            let CapeName = _GetCapeDisplayName(CapeAlias)
                            let state = Cape["state"]
                            let active = state is not null && state.ToString().ToUpper().Equals("ACTIVE")
                            select new MyListItem
                            {
                                Title = CapeName,
                                Info = Cape["alias"].ToString(),
                                Checked = active,
                                Type = MyListItem.CheckType.RadioBox,
                                Logo = (string)Cape["url"],
                                LogoScale = 0.8d
                            });

                        selId = ModMain.MyMsgBoxSelect(selectionControl, Lang.Text("Launch.Skin.Cape.SelectTitle"),
                            Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel"));
                    }
                    catch (Exception ex)
                    {
                        ModBase.Log(
                            ex,
                            Lang.Text("Launch.Skin.Cape.Error.List"),
                            ModBase.LogLevel.Feedback,
                            userSummary: Lang.Text("Launch.Skin.Cape.Error.List"));
                    }
                });
                if (selId is null)
                    return;
                // 发送请求
                var result = Requester.Fetch("https://api.minecraftservices.com/minecraft/profile/capes/active",
                    new FetchParam
                    {
                        Method = selId is 0 ? "DELETE" : "PUT",
                        Content = selId is 0
                            ? ""
                            : new JsonObject { ["capeId"] = skinData["capes"][(int)(selId - 1)]["id"]?.ToString() }.ToJsonString(),
                        ContentType = "application/json",
                        Headers = new Dictionary<string, string> { { "Authorization", "Bearer " + accessToken } }
                    }
                );
                if (result.Contains("\"errorMessage\""))
                    HintService.Hint(
                        Lang.Text("Launch.Skin.Cape.ChangeFailedWithReason",
                            ((JsonObject)ModBase.GetJson(result))["errorMessage"]), HintType.Error);
                else
                    HintService.Hint(Lang.Text("Launch.Skin.Cape.ChangeSuccess"), HintType.Success);
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Launch.Skin.Cape.ChangeFailed"),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Skin.Cape.ChangeFailed"));
            }
            finally
            {
                isChanging = false;
            }
        }, "Cape Change");
    }

    private static string _GetCapeDisplayName(string capeAlias)
    {
        var safeName = capeAlias
            .Replace("-", "")
            .Replace(" ", "")
            .Replace("'", "");
        var key = $"Launch.Skin.Cape.Name.{safeName}";
        var name = Lang.Text(key);
        if (name == $"!{key}!" || name == key)
            return capeAlias;
        return name;
    }
}
