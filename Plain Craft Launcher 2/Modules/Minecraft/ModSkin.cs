using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualBasic;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft.Profile;
using PCL.Core.UI;
using PCL.Core.Utils;
using PCL.Network;

namespace PCL;

public static class ModSkin
{
    public struct McSkinInfo
    {
        public bool IsSlim;
        public string LocalFile;
        public bool IsVaild;
    }

    /// <summary>
    ///     要求玩家选择一个皮肤文件，并进行相关校验。
    /// </summary>
    public static McSkinInfo McSkinSelect()
    {
        var fileName = SystemDialogs.SelectFile(Lang.Text("Launch.Skin.FileDialog.Filter"), Lang.Text("Launch.Skin.FileDialog.Title"));

        // 验证有效性
        if (string.IsNullOrEmpty(fileName))
            return new McSkinInfo { IsVaild = false };
        try
        {
            var image = new MyBitmap(fileName);
            if (image.pic.Width != 64 || !(image.pic.Height == 32 || image.pic.Height == 64))
            {
                HintService.Hint(Lang.Text("Launch.Skin.InvalidSize"), HintType.Error);
                return new McSkinInfo { IsVaild = false };
            }

            var fileInfo = new FileInfo(fileName);
            if (fileInfo.Length > 24 * 1024)
            {
                HintService.Hint(Lang.Text("Launch.Skin.FileTooLarge", Lang.Number(fileInfo.Length / 1024d, "N2")),
                    HintType.Error);
                return new McSkinInfo { IsVaild = false };
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Launch.Skin.File.Error"),
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Launch.Skin.File.Error"));
            return new McSkinInfo { IsVaild = false };
        }

        // 获取皮肤种类
        var isSlim = ModMain.MyMsgBox(Lang.Text("Launch.Skin.Model.SelectMessage"), Lang.Text("Launch.Skin.Model.SelectTitle"), Lang.Text("Launch.Skin.Model.Steve"), Lang.Text("Launch.Skin.Model.Alex"), Lang.Text("Common.Option.IDontKnow"),
            highLight: false);
        if (isSlim == 3)
        {
            HintService.Hint(Lang.Text("Launch.Skin.Model.UnknownHint"));
            return new McSkinInfo { IsVaild = false };
        }

        return new McSkinInfo { IsVaild = true, IsSlim = isSlim == 2, LocalFile = fileName };
    }

    /// <summary>
    ///     获取 Uuid 对应的皮肤文件地址，失败将抛出异常。
    /// </summary>
    public static string McSkinGetAddress(string uuid, string type)
    {
        if (string.IsNullOrEmpty(uuid))
            throw new ArgumentException(Lang.Text("Minecraft.Skin.Error.UuidEmpty"), nameof(uuid));

        if (uuid.StartsWith("00000"))
            throw new InvalidOperationException(Lang.Text("Minecraft.Skin.Error.OfflineNoSkin"));

        // 尝试读取缓存
        var cachePath = Path.Combine(ModBase.pathTemp, $"Cache\\Skin\\Index{type}.ini");
        var cacheSkinAddress = ModBase.ReadIni(cachePath, uuid);
        if (!string.IsNullOrEmpty(cacheSkinAddress))
            return cacheSkinAddress;

        // 获取皮肤地址
        var url = type switch
        {
            "Mojang" => "https://sessionserver.mojang.com/session/minecraft/profile/",
            "Ms" => "https://sessionserver.mojang.com/session/minecraft/profile/",
            "Auth" => (ProfileService.Current?.Server ?? string.Empty).Replace("/authserver", "") +
                      "/sessionserver/session/minecraft/profile/",
            _ => throw new ArgumentException(Lang.Text("Minecraft.Skin.Error.InvalidSkinType", type ?? "null"))
        };

        var skinString = ModNet.NetGetCodeByRequestRetry(url + uuid);
        if (string.IsNullOrEmpty((string?)skinString))
            throw new InvalidDataException(Lang.Text("Minecraft.Skin.Error.SkinReturnEmpty"));

        // 解析皮肤 Property
        string skinValue = null;
        try
        {
            var json = (JsonObject)ModBase.GetJson((string)skinString);
            foreach (var property in json["properties"].AsArray())
                if (property["name"]?.ToString() == "textures")
                {
                    skinValue = property["value"]?.ToString();
                    break;
                }

            if (skinValue is null)
                throw new InvalidDataException(Lang.Text("Minecraft.Skin.Error.PropertyNotFound"));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex,
                $"无法完成解析的皮肤返回值，可能是未设置自定义皮肤的用户：{skinString}",
                ModBase.LogLevel.Developer);
            throw new InvalidDataException(Lang.Text("Minecraft.Skin.Error.NoSkinData"), ex);
        }

        // 解码 Base64 并解析 JSON
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(skinValue));
        var skinJson = (JsonObject)ModBase.GetJson(decoded.ToLowerInvariant());

        if (skinJson["textures"]?["skin"]?["url"] is null)
            throw new InvalidDataException(Lang.Text("Minecraft.Skin.Error.NoCustomSkin"));

        var skinUrl = skinJson["textures"]["skin"]["url"].ToString();
        skinUrl = skinUrl.Contains("minecraft.net/") ? skinUrl.Replace("http://", "https://") : skinUrl;

        // 保存缓存
        ModBase.WriteIni(cachePath, uuid, skinUrl);
        ModBase.Log($"[Skin] UUID {uuid} 对应的皮肤文件为 {skinUrl}");

        return skinUrl;
    }

    private static readonly object mcSkinDownloadLock = new();

    /// <summary>
    ///     从 Url 下载皮肤。返回本地文件路径，失败将抛出异常。
    /// </summary>
    public static string McSkinDownload(string address)
    {
        var skinName = ModBase.GetFileNameFromPath(address);
        var fileAddress = ModBase.pathTemp + @"Cache\Skin\" + ModBase.GetHash(address) + ".png";
        lock (mcSkinDownloadLock)
        {
            if (!File.Exists(fileAddress))
            {
                FileDownloader.DownloadAsync(address, fileAddress + ModNet.netDownloadEnd).GetAwaiter().GetResult();
                File.Delete(fileAddress);
                FileSystem.Rename(fileAddress + ModNet.netDownloadEnd, fileAddress);
                ModBase.Log("[Minecraft] 皮肤下载成功：" + fileAddress);
            }

            return fileAddress;
        }
    }

    /// <summary>
    ///     获取 Uuid 对应的皮肤，返回"Steve"或"Alex"。
    /// </summary>
    public static string McSkinSex(string uuid)
    {
        if (uuid.Length != 32)
            return "Steve";
        var a = int.Parse(uuid[7].ToString(), NumberStyles.AllowHexSpecifier);
        var b = int.Parse(uuid[15].ToString(), NumberStyles.AllowHexSpecifier);
        var c = int.Parse(uuid[23].ToString(), NumberStyles.AllowHexSpecifier);
        var d = int.Parse(uuid[31].ToString(), NumberStyles.AllowHexSpecifier);
        return ((a ^ b ^ c ^ d) % 2) != 0 ? "Alex" : "Steve";
        // Math.floorMod(uuid.hashCode(), 18)

        // Public Function hashCode(ByVal str As String) As Integer
        // Dim hash As Integer = 0
        // Dim n As Integer = str.Length
        // If n = 0 Then
        // Return hash
        // End If
        // For i As Integer = 0 To n - 1
        // hash = hash + Asc(str(i)) * (1 << (n - i - 1))
        // Next
        // Return hash
        // End Function
    }
}
