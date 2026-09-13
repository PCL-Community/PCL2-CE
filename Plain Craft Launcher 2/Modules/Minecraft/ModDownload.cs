using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.IO.Net.Http;
using PCL.Core.Utils;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

public static class ModDownload
{
    #region DlClient* | Minecraft 客户端

    /// <summary>
    ///     返回某 Minecraft 版本对应的原版主 Jar 文件的下载信息，要求对应依赖实例已存在。
    ///     失败则抛出异常，不需要下载则返回 Nothing。
    /// </summary>
    public static DownloadFile DlClientJarGet(McInstance version, bool returnNothingOnFileUseable)
    {
        // 获取底层继承实例
        try
        {
            while (!string.IsNullOrEmpty(version.InheritInstanceName))
                version = new McInstance(version.InheritInstanceName);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "获取底层继承实例失败");
        }

        // 检查 Json 是否标准
        if (version.JsonObject["downloads"] is null || version.JsonObject["downloads"]["client"] is null ||
            version.JsonObject["downloads"]["client"]["url"] is null)
            throw new Exception(Lang.Text("Minecraft.Download.Error.NoJarDownloadInfo", version.Name));
        // 检查文件
        var checker = new ModBase.FileChecker(1024L, (long)(version.JsonObject["downloads"]["client"]["size"] ?? -1),
            (string)version.JsonObject["downloads"]["client"]["sha1"]);
        if (returnNothingOnFileUseable && checker.Check(version.PathInstance + version.Name + ".jar") is null)
            return null; // 通过校验
        // 返回下载信息
        var jarUrl = (string)version.JsonObject["downloads"]["client"]["url"];
        return new DownloadFile(DlSourceLauncherOrMetaGet(jarUrl), version.PathInstance + version.Name + ".jar",
            checker);
    }

    /// <summary>
    ///     返回某 Minecraft 版本对应的原版主 AssetIndex 文件的下载信息，要求对应依赖实例已存在。
    ///     若未找到，则会返回 Legacy 资源文件或 Nothing。
    /// </summary>
    public static DownloadFile DlClientAssetIndexGet(McInstance version)
    {
        // 获取底层继承实例
        while (!string.IsNullOrEmpty(version.InheritInstanceName))
            version = new McInstance(version.InheritInstanceName);
        // 获取信息
        var indexInfo = ModAssets.McAssetsGetIndex(version, true, true);
        var indexAddress = Path.Combine(ModFolder.mcFolderSelected, "assets", "indexes", indexInfo["id"] + ".json");
        ModBase.Log("[Download] 实例 " + version.Name + " 对应的资源文件索引为 " + indexInfo["id"]);
        var indexUrl = (string)(indexInfo["url"] ?? "");
        if (string.IsNullOrEmpty(indexUrl)) return null;

        // 避免下载器先发一次 Range 探测请求，再回退到顺序下载
        var indexSize = indexInfo["size"] is null ? -1L : (long)indexInfo["size"];
        return new DownloadFile(DlSourceLauncherOrMetaGet(indexUrl), indexAddress,
            new ModBase.FileChecker(actualSize: indexSize, canUseExistsFile: false));
    }

    /// <summary>
    ///     构造补全某 Minecraft 版本的所有文件的加载器列表。失败会抛出异常。
    /// </summary>
    public static List<ModLoader.LoaderBase> DlClientFix(McInstance version, bool checkAssetsHash,
        AssetsIndexExistsBehaviour assetsIndexBehaviour)
    {
        var loaders = new List<ModLoader.LoaderBase>();

        #region 下载支持库文件

        if (ModLibrary.ShouldIgnoreFileCheck(version))
        {
            ModBase.Log("[Download] 已跳过所有 Libraries 检查");
        }
        else
        {
            var loadersLib = new List<ModLoader.LoaderBase>
            {
                new ModLoader.LoaderTask<string, List<DownloadFile>>(
                    Lang.Text("Minecraft.Download.Stage.AnalyzeMissingLibraries"),
                    task => task.output = ModLibrary.McLibNetFilesFromInstance(version)) { ProgressWeight = 1d },
                new LoaderDownload(Lang.Text("Minecraft.Download.Stage.DownloadLibraries"), new List<DownloadFile>())
                    { ProgressWeight = 15d }
            };
            // 构造加载器
            loaders.Add(
                new ModLoader.LoaderCombo<string>(Lang.Text("Minecraft.Download.Stage.DownloadLibraries.MainLoader"),
                        loadersLib)
                { block = false, show = false, ProgressWeight = 16d });
        }

        #endregion

        #region 下载资源文件

        if (ModLibrary.ShouldIgnoreFileCheck(version))
        {
            ModBase.Log("[Download] 已跳过所有 Assets 检查");
        }
        else
        {
            var loadersAssets = new List<ModLoader.LoaderBase>();
            // 获取资源文件索引地址
            loadersAssets.Add(new ModLoader.LoaderTask<string, List<DownloadFile>>(
                Lang.Text("Minecraft.Download.Stage.AnalyzeAssetsIndex"), task =>
            {
                try
                {
                    var indexFile = DlClientAssetIndexGet(version);
                    var indexFileInfo = new FileInfo(indexFile.LocalPath);
                    if (assetsIndexBehaviour != AssetsIndexExistsBehaviour.AlwaysDownload &&
                        indexFile.Check.Check(indexFile.LocalPath) is null)
                        task.output = new List<DownloadFile>();
                    else
                        task.output = new List<DownloadFile> { indexFile };
                }
                catch (Exception ex)
                {
                    throw new Exception(Lang.Text("Minecraft.Download.Error.AssetIndexAnalysisFailed"), ex);
                }
            }) { ProgressWeight = 0.5d, show = false });
            // 下载资源文件索引
            loadersAssets.Add(new LoaderDownload(Lang.Text("Minecraft.Download.Stage.DownloadAssetsIndex"),
                    new List<DownloadFile>())
                { ProgressWeight = 2d });
            // 要求独立更新索引
            if (assetsIndexBehaviour == AssetsIndexExistsBehaviour.DownloadInBackground)
            {
                var loadersAssetsUpdate = new List<ModLoader.LoaderBase>();
                string tempAddress = null;
                string realAddress = null;
                loadersAssetsUpdate.Add(new ModLoader.LoaderTask<string, List<DownloadFile>>(
                    Lang.Text("Minecraft.Download.Stage.AnalyzeAssetsIndex.Background"), task =>
                {
                    var backAssetsFile = DlClientAssetIndexGet(version);
                    realAddress = backAssetsFile.LocalPath;
                    tempAddress = ModBase.pathTemp + @"Cache\" + backAssetsFile.LocalName;
                    backAssetsFile.LocalPath = tempAddress;
                    task.output = new List<DownloadFile> { backAssetsFile };
                    // 检查是否需要更新：每天只更新一次
                    if (File.Exists(realAddress) &&
                        Math.Abs((File.GetLastWriteTime(realAddress).Date - DateTime.Now.Date).TotalDays) < 1d)
                    {
                        ModBase.Log("[Download] 无需更新资源文件索引，取消");
                        task.Abort();
                    }
                }));
                loadersAssetsUpdate.Add(new LoaderDownload(
                    Lang.Text("Minecraft.Download.Stage.DownloadAssetsIndex.Background"), new List<DownloadFile>()));
                loadersAssetsUpdate.Add(new ModLoader.LoaderTask<List<DownloadFile>, string>(
                    Lang.Text("Minecraft.Download.Stage.CopyAssetsIndex.Background"), task =>
                {
                    ModBase.CopyFile(tempAddress, realAddress);
                    ModLaunch.McLaunchLog("后台更新资源文件索引成功：" + tempAddress);
                }));
                var updater = new ModLoader.LoaderCombo<string>(
                    Lang.Text("Minecraft.Download.Stage.UpdateAssetsIndex.Background"), loadersAssetsUpdate);
                ModBase.Log("[Download] 开始后台检查资源文件索引");
                updater.Start();
            }

            // 获取资源文件地址
            loadersAssets.Add(new ModLoader.LoaderTask<string, List<DownloadFile>>(
                Lang.Text("Minecraft.Download.Stage.AnalyzeMissingAssets"), task =>
            {
                ModLoader.LoaderBase argprogressFeed = task;
                task.output = ModAssets.McAssetsFixList(version, checkAssetsHash, ref argprogressFeed);
                task = (ModLoader.LoaderTask<string, List<DownloadFile>>)argprogressFeed;
            })
            {
                ProgressWeight = 3d
            });
            // 下载资源文件
            loadersAssets.Add(
                new LoaderDownload(Lang.Text("Minecraft.Download.Stage.DownloadAssets"), new List<DownloadFile>())
                    { ProgressWeight = 25d });
            // 构造加载器
            loaders.Add(
                new ModLoader.LoaderCombo<string>(Lang.Text("Minecraft.Download.Stage.DownloadAssets.MainLoader"),
                        loadersAssets)
                { block = false, show = false, ProgressWeight = 30.5d });
        }

        #endregion

        return loaders;
    }

    public enum AssetsIndexExistsBehaviour
    {
        /// <summary>
        ///     如果文件存在，则不进行下载。
        /// </summary>
        DontDownload,

        /// <summary>
        ///     如果文件存在，则启动新的下载加载器进行独立的更新。
        /// </summary>
        DownloadInBackground,

        /// <summary>
        ///     如果文件存在，也同样进行下载。
        /// </summary>
        AlwaysDownload
    }

    #endregion

    #region DlClientList | Minecraft 客户端 版本列表

    /// <summary>
    ///     所有正式版的 Minecraft Drop 序数。
    ///     若从未完成过获取，返回 Nothing；否则必定存在元素，且从高到低排列。
    /// </summary>
    public static List<int> AllDrops
    {
        get
        {
            lock (_allDropsLock)
            {
                if (field is null)
                {
                    var rawData = States.Game.Drops;
                    if (string.IsNullOrEmpty(rawData))
                        field = new List<int>();
                    else
                        field = rawData.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(d => (int)Math.Round(ModBase.Val(d))).ToList();
                }

                return field.Count != 0 ? field : null;
            }
        }
        set
        {
            lock (_allDropsLock)
            {
                field = value;
                States.Game.Drops = value.Join(",");
            }
        }
    }

    private static readonly object _allDropsLock = new();

    // 主加载器
    public struct DlClientListResult
    {
        /// <summary>
        ///     数据来源名称，如“Mojang”，“BMCLAPI”。
        /// </summary>
        public string SourceName;

        /// <summary>
        ///     是否为官方的实时数据。
        /// </summary>
        public bool IsOfficial;

        /// <summary>
        ///     获取到的 Json 数据。
        /// </summary>
        public JsonObject Value;
        // ''' <summary>
        // ''' 官方源的失败原因。若没有则为 Nothing。
        // ''' </summary>
        // Public OfficialError As Exception
    }

    /// <summary>
    ///     Minecraft 客户端 版本列表，主加载器。
    ///     若要求镜像源必须包含某个版本，则将该版本 ID 作为输入（#5195）。
    /// </summary>
    public static ModLoader.LoaderTask<string, DlClientListResult> dlClientListLoader =
        new("DlClientList Main", DlClientListMain);

    private static void DlClientListMain(ModLoader.LoaderTask<string, DlClientListResult> loader)
    {
        switch (Config.Download.VersionListSource)
        {
            case 0:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<string, DlClientListResult>, int>>
                        { new(dlClientListBmclapiLoader, 30), new(dlClientListMojangLoader, 30 + 60) },
                    loader.isForceRestarting);
                break;
            }
            case 1:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<string, DlClientListResult>, int>>
                        { new(dlClientListMojangLoader, 5), new(dlClientListBmclapiLoader, 5 + 30) },
                    loader.isForceRestarting);
                break;
            }

            default:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<string, DlClientListResult>, int>>
                        { new(dlClientListMojangLoader, 60), new(dlClientListBmclapiLoader, 60 + 60) },
                    loader.isForceRestarting);
                break;
            }
        }

        // 提取所有 Drop 序数
        var drops = new List<int>();
        foreach (JsonObject version in loader.output.Value["versions"].AsArray())
            drops.Add(McInstanceInfo.VersionToDrop((string)version["id"]));
        AllDrops = drops.Distinct().OrderByDescending(d => d).ToList();
    }

    // 各个下载源的分加载器
    /// <summary>
    ///     Minecraft 客户端 版本列表，Mojang 官方源加载器。
    /// </summary>
    public static ModLoader.LoaderTask<string, DlClientListResult> dlClientListMojangLoader =
        new("DlClientList Mojang", DlClientListMojangMain);

    private static bool isNewClientVersionHinted = false;

    // MC 更新提示
    private static bool _DlClientListMojangMain_IsHinted;

    private static void DlClientListMojangMain(ModLoader.LoaderTask<string, DlClientListResult> loader)
    {
        var startTime = TimeUtils.GetTimeTick();
        var json = (JsonObject)Requester.FetchJson("https://launchermeta.mojang.com/mc/game/version_manifest.json");
        try
        {
            var versions = (JsonArray)json["versions"];
            if (versions.Count < 200)
                throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Mojang", json));
            // 添加 UVMC 项
            var cacheFilePath = ModBase.pathTemp + @"Cache\uvmc-download.json";
            if (!File.Exists(cacheFilePath))
                try
                {
                    var unlistedJson = (JsonObject)Requester.FetchJson(
                        "https://alist.8mi.tech/d/mirror/unlisted-versions-of-minecraft/Auto/version_manifest.json");
                    File.WriteAllText(cacheFilePath, unlistedJson.ToString());
                }
                catch (Exception ex)
                {
                    ModBase.Log("[Download] 未列出的版本官方源下载失败: " + ex.Message);
                }

            try
            {
                var cachedJson = (JsonObject)ModBase.GetJson(ModBase.ReadFile(cacheFilePath));
                versions.Merge(cachedJson["versions"]);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[Download] UVMC 列表加载失败，忽略列表内容");
            }

            // 确定官方源是否可用
            if (!dlPreferMojang)
            {
                var deltaTime = TimeUtils.GetTimeTick() - startTime;
                dlPreferMojang = deltaTime < 4000;
                ModBase.Log($"[Download] Mojang 官方源加载耗时：{deltaTime}ms，{(dlPreferMojang ? "可优先使用官方源" : "不优先使用官方源")}");
            }

            // 添加 PCL 特供项
            // 这个社区版下不了
            // If File.Exists(PathTemp & "Cache\download.json") Then Versions.Merge(GetJson(ReadFile(PathTemp & "Cache\download.json")))
            // 返回
            loader.output = new DlClientListResult
                { IsOfficial = true, SourceName = Lang.Text("Download.Source.MojangOfficial"), Value = json };
            string version;
            // 快照版
            version = (string)json["latest"]["snapshot"];
            if (Config.Tool.SnapshotNotification &&
                                      States.Tool.LastSnapshot != "" &&
                                      States.Tool.LastSnapshot != version &&
                                      !_DlClientListMojangMain_IsHinted)
            {
                _DlClientListMojangMain_IsHinted = true;
                McDownloadClientUpdateHint(version, json);
            }

            States.Tool.LastSnapshot = version ?? "Nothing";
            // 正式版
            version = (string)json["latest"]["release"];
            if (Config.Tool.ReleaseNotification &&
                                      States.Tool.LastRelease != "" &&
                                      States.Tool.LastRelease != version &&
                                      !_DlClientListMojangMain_IsHinted)
            {
                _DlClientListMojangMain_IsHinted = true;
                McDownloadClientUpdateHint(version, json);
            }

            States.Tool.LastRelease = version;
        }
        catch (Exception ex)
        {
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Mojang", ""), ex);
        }
    }

    /// <summary>
    ///     Minecraft 客户端 版本列表，BMCLAPI 源加载器。
    /// </summary>
    public static ModLoader.LoaderTask<string, DlClientListResult> dlClientListBmclapiLoader =
        new("DlClientList Bmclapi", DlClientListBmclapiMain);

    private static void DlClientListBmclapiMain(ModLoader.LoaderTask<string, DlClientListResult> loader)
    {
        var json = (JsonObject)Requester.FetchJson(
            "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json");
        try
        {
            var versions = (JsonArray)json["versions"];
            if (versions.Count < 200)
                throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "BMCLAPI", json));
            // 添加 UVMC 项
            var cacheFilePath = ModBase.pathTemp + @"Cache\uvmc-download.json";
            if (!File.Exists(cacheFilePath))
                try
                {
                    var unlistedJson = (JsonObject)Requester.FetchJson(
                        "https://alist.8mi.tech/d/mirror/unlisted-versions-of-minecraft/Auto/version_manifest.json");
                    File.WriteAllText(cacheFilePath, unlistedJson.ToString());
                }
                catch (Exception ex)
                {
                    ModBase.Log("[Download] 未列出的版本镜像源下载失败: " + ex.Message);
                }

            try
            {
                var cachedJson = (JsonObject)ModBase.GetJson(ModBase.ReadFile(cacheFilePath));
                versions.Merge(cachedJson["versions"]);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[Download] UVMC 列表加载失败，忽略列表内容");
            }

            // 检查是否有要求的版本（#5195）
            if (!string.IsNullOrEmpty(loader.input))
            {
                var id = loader.input;
                if (dlClientListLoader.output.Value is not null &&
                    !dlClientListLoader.output.Value["versions"].AsArray().Any(v => (string)v["id"] == id))
                    throw new Exception(Lang.Text("Minecraft.Download.Error.BmclapiMissingTargetVersion", id));
            }

            // 返回
            loader.output = new DlClientListResult { IsOfficial = false, SourceName = "BMCLAPI", Value = json };
        }
        catch (Exception ex)
        {
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "BMCLAPI", json), ex);
        }
    }

    /// <summary>
    ///     获取某个版本的 Json 下载地址，若失败则返回 Nothing。必须在工作线程执行。
    /// </summary>
    public static object DlClientListGet(string id)
    {
        try
        {
            // 确认版本格式标准
            id = id.Replace("_", "-"); // 1.7.10_pre4 在版本列表中显示为 1.7.10-pre4
            if (id != "1.0" && id.EndsWithF(".0"))
                id = id.Substring(0, id.Length - 2); // OptiFine 1.8 的下载会触发此问题，显示版本为 1.8.0
            // 获取 Minecraft 版本列表
            switch (dlClientListLoader.State)
            {
                case ModBase.LoadState.Finished:
                {
                    // 从当前的结果获取目标版本…
                    foreach (JsonObject Version in dlClientListLoader.output.Value["versions"].AsArray())
                        if ((string)Version["id"] == id)
                            return Version["url"].ToString();
                    // …如果没有，则重新尝试获取（在版本刚更新时可能出现这种情况，#5195）
                    dlClientListLoader.WaitForExit(id, isForceRestart: true);
                    break;
                }
                case ModBase.LoadState.Loading:
                {
                    dlClientListLoader.WaitForExit(id);
                    break;
                }
                case ModBase.LoadState.Failed:
                case ModBase.LoadState.Aborted:
                case ModBase.LoadState.Waiting:
                {
                    dlClientListLoader.WaitForExit(id, isForceRestart: true);
                    break;
                }
            }

            // 重新查找版本
            foreach (JsonObject Version in dlClientListLoader.output.Value["versions"].AsArray())
                if ((string)Version["id"] == id)
                    return Version["url"].ToString();
            ModBase.Log($"未发现版本 {id} 的 json 下载地址，版本列表返回为：{"\r\n"}{dlClientListLoader.output.Value}",
                ModBase.LogLevel.Debug);
            return null;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"获取版本 {id} 的 json 下载地址失败");
            return null;
        }
    }

    #endregion

    #region DlOptiFineList | OptiFine 版本列表

    public struct DlOptiFineListResult
    {
        /// <summary>
        ///     数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
        public string sourceName;

        /// <summary>
        ///     是否为官方的实时数据。
        /// </summary>
        public bool isOfficial;

        /// <summary>
        ///     获取到的数据。
        /// </summary>
        public List<DlOptiFineListEntry> Value;
    }

    public class DlOptiFineListEntry
    {
        /// <summary>
        ///     显示名称，已去除 HD_U 字样，如“1.12.2 C8”。
        /// </summary>
        public string DisplayName;

        /// <summary>
        ///     是否为测试版。
        /// </summary>
        public bool IsPreview;

        /// <summary>
        ///     原始文件名称，如“preview_OptiFine_1.11_HD_U_E1_pre.jar”。
        /// </summary>
        public string NameFile;

        /// <summary>
        ///     对应的版本名称，如“1.13.2-OptiFine_HD_U_E6”。
        /// </summary>
        public string NameVersion;

        /// <summary>
        ///     发布时间，格式为“yyyy/mm/dd”。OptiFine 源无此数据。
        /// </summary>
        public string ReleaseTime;

        /// <summary>
        ///     需要的最低 Forge 版本。空字符串为无限制，Nothing 为不兼容，“28.1.56” 表示版本号，“1161” 表示版本号的最后一位。
        /// </summary>
        public string RequiredForgeVersion;

        /// <summary>
        ///     对应的 Minecraft 版本，如“1.12.2”。
        /// </summary>
        public string Inherit
        {
            get => field;
            set
            {
                if (value.EndsWithF(".0"))
                    value = value.Substring(0, value.Length - 2);
                field = value;
            }
        }
    }

    /// <summary>
    ///     OptiFine 版本列表，主加载器。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlOptiFineListResult> dlOptiFineListLoader =
        new("DlOptiFineList Main", DlOptiFineListMain);

    private static void DlOptiFineListMain(ModLoader.LoaderTask<int, DlOptiFineListResult> loader)
    {
        switch (Config.Download.VersionListSource)
        {
            case 0:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlOptiFineListResult>, int>>
                        { new(dlOptiFineListBmclapiLoader, 30), new(dlOptiFineListOfficialLoader, 30 + 60) },
                    loader.isForceRestarting);
                break;
            }
            case 1:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlOptiFineListResult>, int>>
                        { new(dlOptiFineListOfficialLoader, 5), new(dlOptiFineListBmclapiLoader, 5 + 30) },
                    loader.isForceRestarting);
                break;
            }

            default:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlOptiFineListResult>, int>>
                        { new(dlOptiFineListOfficialLoader, 60), new(dlOptiFineListBmclapiLoader, 60 + 60) },
                    loader.isForceRestarting);
                break;
            }
        }
    }

    /// <summary>
    ///     OptiFine 版本列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlOptiFineListResult> dlOptiFineListOfficialLoader =
        new("DlOptiFineList Official", DlOptiFineListOfficialMain);

    private static void DlOptiFineListOfficialMain(ModLoader.LoaderTask<int, DlOptiFineListResult> loader)
    {
        string result = "";
        using var resp = HttpRequest
            .Create("https://optifine.net/downloads")
            .WithHeader("Accept", "application/json, text/javascript, */*; q=0.01")
            .WithHeader("Accept-Language", "en-US,en;q=0.5")
            .WithHeader("X-Requested-With", "XMLHttpRequest")
            .SendAsync()
            .GetAwaiter()
            .GetResult();
        resp.EnsureSuccessStatusCode();
        result = resp.AsString();
        if (result.Length < 200)
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "OptiFine", result));
        try
        {
            var forge = result.RegexSearch("(?<=colForge'>)[^<]*");
            var releaseTime = result.RegexSearch("(?<=colDate'>)[^<]+");
            var name = result.RegexSearch("(?<=OptiFine_)[0-9A-Za-z_.]+(?=.jar\")");
            if (releaseTime.Count != name.Count)
                throw new Exception(Lang.Text("Minecraft.Download.Error.OptiFineTimeDataMismatch"));
            if (forge.Count != name.Count)
                throw new Exception(Lang.Text("Minecraft.Download.Error.OptiFineForgeCompatMismatch"));
            if (releaseTime.Count < 10)
                throw new Exception(
                    Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "OptiFine", result));
            // 转化为列表输出
            var versions = new List<DlOptiFineListEntry>();
            for (int i = 0, loopTo = releaseTime.Count - 1; i <= loopTo; i++)
            {
                name[i] = name[i].Replace("_", " ");
                var releaseDate = DateTime.ParseExact(releaseTime[i],
                    ["d.M.yyyy", "dd.M.yyyy", "d.MM.yyyy", "dd.MM.yyyy"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None);
                var entry = new DlOptiFineListEntry
                {
                    DisplayName = name[i].Replace("HD U ", "").Replace(".0 ", " "),
                    ReleaseTime = Lang.Date(releaseDate, "d"),
                    IsPreview = name[i].ContainsF("pre", true),
                    Inherit = name[i].Split(" ")[0],
                    NameFile = (name[i].ContainsF("pre", true) ? "preview_" : "") + "OptiFine_" +
                               name[i].Replace(" ", "_") + ".jar",
                    RequiredForgeVersion = forge[i].Replace("Forge ", "").Replace("#", "")
                };
                if (entry.RequiredForgeVersion.Contains("N/A"))
                    entry.RequiredForgeVersion = null;
                entry.NameVersion = entry.Inherit + "-OptiFine_" +
                                    name[i].Replace(" ", "_").Replace(entry.Inherit + "_", "");
                versions.Add(entry);
            }

            loader.output = new DlOptiFineListResult
                { isOfficial = true, sourceName = Lang.Text("Download.Source.OptiFineOfficial"), Value = versions };
        }
        catch (Exception ex)
        {
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "OptiFine", result),
                ex);
        }
    }

    /// <summary>
    ///     OptiFine 版本列表，BMCLAPI。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlOptiFineListResult> dlOptiFineListBmclapiLoader =
        new("DlOptiFineList Bmclapi", DlOptiFineListBmclapiMain);

    private static void DlOptiFineListBmclapiMain(ModLoader.LoaderTask<int, DlOptiFineListResult> loader)
    {
        var json = (JsonArray)Requester.FetchJson("https://bmclapi2.bangbang93.com/optifine/versionList");
        try
        {
            var versions = new List<DlOptiFineListEntry>();
            foreach (JsonObject Token in json)
            {
                var entry = new DlOptiFineListEntry
                {
                    DisplayName =
                        (Token["mcversion"] + Token["type"].ToString().Replace("HD_U", "").Replace("_", " ") + " " +
                         Token["patch"]).Replace(".0 ", " "),
                    ReleaseTime = "",
                    IsPreview = Token["patch"].ToString().ContainsF("pre", true),
                    Inherit = Token["mcversion"].ToString(),
                    NameFile = Token["filename"].ToString(),
                    RequiredForgeVersion = (Token["forge"] ?? "").ToString().Replace("Forge ", "").Replace("#", "")
                };
                if (entry.RequiredForgeVersion.Contains("N/A"))
                    entry.RequiredForgeVersion = null;
                entry.NameVersion = entry.Inherit + "-OptiFine_" + (Token["type"] + " " + Token["patch"])
                    .Replace(".0 ", " ").Replace(" ", "_").Replace(entry.Inherit + "_", "");
                versions.Add(entry);
            }

            loader.output = new DlOptiFineListResult { isOfficial = false, sourceName = "BMCLAPI", Value = versions };
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "OptiFine BMCLAPI", json), ex);
        }
    }

    #endregion

    #region DlForgeList | Forge Minecraft 版本列表

    public struct DlForgeListResult
    {
        /// <summary>
        ///     数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
        public string sourceName;

        /// <summary>
        ///     是否为官方的实时数据。
        /// </summary>
        public bool isOfficial;

        /// <summary>
        ///     获取到的数据。
        /// </summary>
        public List<string> Value;
    }

    /// <summary>
    ///     Forge 版本列表，主加载器。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlForgeListResult> dlForgeListLoader =
        new("DlForgeList Main", DlForgeListMain);

    private static void DlForgeListMain(ModLoader.LoaderTask<int, DlForgeListResult> loader)
    {
        switch (Config.Download.VersionListSource)
        {
            case 0:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlForgeListResult>, int>>
                        { new(dlForgeListBmclapiLoader, 30), new(dlForgeListOfficialLoader, 30 + 60) },
                    loader.isForceRestarting);
                break;
            }
            case 1:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlForgeListResult>, int>>
                        { new(dlForgeListOfficialLoader, 5), new(dlForgeListBmclapiLoader, 5 + 30) },
                    loader.isForceRestarting);
                break;
            }

            default:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlForgeListResult>, int>>
                        { new(dlForgeListOfficialLoader, 60), new(dlForgeListBmclapiLoader, 60 + 60) },
                    loader.isForceRestarting);
                break;
            }
        }
    }

    /// <summary>
    ///     Forge 版本列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlForgeListResult> dlForgeListOfficialLoader =
        new("DlForgeList Official", DlForgeListOfficialMain);

    private static void DlForgeListOfficialMain(ModLoader.LoaderTask<int, DlForgeListResult> loader)
    {
        var result = Requester.FetchString(
            "https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_1.2.4.html", new RequestParam
            {
                Encoding = Encoding.Default,
                UseBrowserUserAgent = true
            });
        if (result.Length < 200)
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Forge", result));
        // 获取所有版本信息
        var names = result.RegexSearch("(?<=a href=\"index_)[0-9.]+(_pre[0-9]?)?(?=.html)");
        names.Add("1.2.4"); // 1.2.4 不会被匹配上
        if (names.Count < 10)
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Forge", result));
        loader.output = new DlForgeListResult
            { isOfficial = true, sourceName = Lang.Text("Download.Source.ForgeOfficial"), Value = names };
    }

    /// <summary>
    ///     Forge 版本列表，BMCLAPI。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlForgeListResult> dlForgeListBmclapiLoader =
        new("DlForgeList Bmclapi", DlForgeListBmclapiMain);

    private static void DlForgeListBmclapiMain(ModLoader.LoaderTask<int, DlForgeListResult> loader)
    {
        var result =
            Requester.FetchJson("https://bmclapi2.bangbang93.com/forge/minecraft",
                new RequestParam
                {
                    Encoding = Encoding.Default,
                })?.ToString() ?? "";
        if (result.Length < 200)
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Forge BMCLAPI",
                result));
        // 获取所有版本信息
        var names = result.RegexSearch("[0-9.]+(_pre[0-9]?)?");
        if (names.Count < 10)
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Forge BMCLAPI",
                result));
        loader.output = new DlForgeListResult { isOfficial = false, sourceName = "BMCLAPI", Value = names };
    }

    #endregion

    #region DlForgeVersion | Forge 版本列表

    public abstract class DlForgelikeEntry : IComparable<DlForgelikeEntry>
    {
        public enum ForgelikeType
        {
            Forge,
            NeoForge,
            Cleanroom
        }

        /// <summary>
        ///     Forgelike 种类。Forge、NeoForge、Cleanroom。
        /// </summary>
        public ForgelikeType forgeType;

        /// <summary>
        ///     对应的 Minecraft 版本，如“1.12.2”。
        /// </summary>
        public string Inherit;

        /// <summary>
        ///     标准化后的版本号，仅可用于比较与排序。
        ///     格式：Major.Minor.Build.Revision
        ///     Forge：如 “50.1.9.0”（最后一位固定为 0）、“14.22.1.2478”（Legacy）。
        ///     NeoForge：如 “20.4.30.0”（最后一位固定为 0）、“19.47.1.99”（Legacy：第一位固定为 19）。
        ///     Cleanroom：如 “0.2.4.1”（Alpha：最后一位固定为 1）。
        /// </summary>
        public Version version;

        /// <summary>
        ///     可对玩家显示的非格式化版本名。
        ///     Forge：如 “50.1.9”、“14.22.1.2478”（Legacy）。
        ///     NeoForge：如 “20.4.30-beta”、“47.1.99”（Legacy）。
        ///     Cleanroom：如 “0.2.4-alpha”。
        /// </summary>
        public string VersionName;

        /// <summary>
        ///     加载器名称。Forge / NeoForge / Cleanroom。
        /// </summary>
        public string LoaderName => forgeType.ToString();

        /// <summary>
        ///     文件扩展名。不以小数点开头。
        /// </summary>
        public string FileExtension
        {
            get
            {
                if (forgeType == 0) return ((DlForgeVersionEntry)this).Category == "installer" ? "jar" : "zip";

                return "jar";
            }
        }

        /// <summary>
        ///     Forge：MC 版本是否小于 1.13。
        ///     NeoForge：MC 版本是否为 1.20.1。
        ///     Cleanroom：固定为 False。
        /// </summary>
        public bool IsLegacy
        {
            get
            {
                // Cleanroom 始终为 False
                if ((int)forgeType == 2)
                    return false;
                // 虽然很抽象，但确实可以这样判断
                // Forge：1.13+ 的版本号首位都大于 20
                // NeoForge：1.20.1 的版本号首位人为规定为 19 开头
                return version.Major < 20;
            }
        }

        public int CompareTo(DlForgelikeEntry other)
        {
            if (version != other.version) return version.CompareTo(other.version);

            return McVersionComparer.CompareVersion(VersionName, other.VersionName);
        }
    }

    public class DlForgeVersionEntry : DlForgelikeEntry
    {
        /// <summary>
        ///     安装类型。有 installer、client、universal 三种。
        /// </summary>
        public string Category;

        /// <summary>
        ///     用于下载的文件版本名。可能在 Version 的基础上添加了分支。
        /// </summary>
        public string FileVersion;

        /// <summary>
        ///     文件的 MD5 或 SHA1（BMCLAPI 的老版本是 MD5，新版本是 SHA1；官方源总是 MD5）。
        /// </summary>
        public string Hash;

        /// <summary>
        ///     是否为推荐版本。
        /// </summary>
        public bool IsRecommended;

        /// <summary>
        ///     发布时间，格式为“yyyy/MM/dd HH:mm”。
        /// </summary>
        public string ReleaseTime;

        public DlForgeVersionEntry(string version, string branch, string inherit)
        {
            // 司马版本的特殊处理
            if (version == "11.15.1.2318" || version == "11.15.1.1902" || version == "11.15.1.1890")
                branch = "1.8.9";
            if (branch is null && inherit == "1.7.10" && double.Parse(version.Split(".")[3]) >= 1300d)
                branch = "1.7.10";
            // 为 DlForgelikeEntry 提供所有信息
            forgeType = ForgelikeType.Forge;
            VersionName = version;
            this.version = new Version(version);
            this.Inherit = inherit;
            FileVersion = version + (branch is null ? "" : "-" + branch);
        }
    }

    /// <summary>
    ///     Forge 版本列表，主加载器。
    /// </summary>
    public static void DlForgeVersionMain(ModLoader.LoaderTask<string, List<DlForgeVersionEntry>> loader)
    {
        var dlForgeVersionOfficialLoader =
            new ModLoader.LoaderTask<string, List<DlForgeVersionEntry>>("DlForgeVersion Official",
                DlForgeVersionOfficialMain);
        var dlForgeVersionBmclapiLoader =
            new ModLoader.LoaderTask<string, List<DlForgeVersionEntry>>("DlForgeVersion Bmclapi",
                DlForgeVersionBmclapiMain);
        switch (Config.Download.VersionListSource)
        {
            case 0:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<string, List<DlForgeVersionEntry>>, int>>
                        { new(dlForgeVersionBmclapiLoader, 30), new(dlForgeVersionOfficialLoader, 30 + 60) },
                    loader.isForceRestarting);
                break;
            }
            case 1:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<string, List<DlForgeVersionEntry>>, int>>
                        { new(dlForgeVersionOfficialLoader, 5), new(dlForgeVersionBmclapiLoader, 5 + 30) },
                    loader.isForceRestarting);
                break;
            }

            default:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<string, List<DlForgeVersionEntry>>, int>>
                        { new(dlForgeVersionOfficialLoader, 60), new(dlForgeVersionBmclapiLoader, 60 + 60) },
                    loader.isForceRestarting);
                break;
            }
        }
    }

    /// <summary>
    ///     Forge 版本列表，官方源。
    /// </summary>
    public static void DlForgeVersionOfficialMain(ModLoader.LoaderTask<string, List<DlForgeVersionEntry>> loader)
    {
        string result;
        try
        {
            result = Requester.FetchString(
                "https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_" +
                loader.input.Replace("-", "_") + ".html", new RequestParam
                {
                    UseBrowserUserAgent = true
                }); // 兼容 Forge 1.7.10-pre4，#4057
        }
        catch (WebException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("(404)")) throw new Exception(Lang.Text("Minecraft.Download.Error.NotFound"));

            throw;
        }

        if (result.Length < 1000)
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Forge", result));
        var versions = new List<DlForgeVersionEntry>();
        try
        {
            // 分割版本信息
            var versionCodes = result.Substring(0, result.LastIndexOfF("</table>"))
                .Split("<td class=\"download-version");
            // 获取所有版本信息
            for (int i = 1, loopTo = versionCodes.Count() - 1; i <= loopTo; i++)
            {
                var versionCode = versionCodes[i];
                try
                {
                    // 基础信息获取
                    var name = versionCode.RegexSeek(@"(?<=[^(0-9)]+)[0-9\.]+");
                    var isRecommended = versionCode.Contains("fa promo-recommended");
                    var inherit = loader.input;
                    // 分支获取
                    var branch = versionCode.RegexSeek($"(?<=-{name}-)[^-\"]+(?=-[a-z]+.[a-z]{{3}})");
                    if (string.IsNullOrWhiteSpace(branch))
                        branch = null;
                    // 发布时间获取
                    var releaseTimeOriginal = versionCode.RegexSeek("(?<=\"download-time\" title=\")[^\"]+");
                    // Dim ReleaseTimeSplit = ReleaseTimeOriginal.Split(" -:".ToCharArray) '原格式："2021-02-15 03:24:02"
                    var releaseDate =
                        DateTime.Parse(releaseTimeOriginal, null, DateTimeStyles.AssumeUniversal); // 以 UTC 时间作为标准
                    var releaseTime = Lang.Date(releaseDate.ToLocalTime(), "g"); // 时区与格式转换
                    // 分类与 MD5 获取
                    string mD5;
                    string category;
                    if (versionCode.Contains("classifier-installer\""))
                    {
                        // 类型为 installer.jar，支持范围 ~753 (~ 1.6.1 部分), 738~684 (1.5.2 全部)
                        versionCode = versionCode.Substring(versionCode.IndexOfF("installer.jar"));
                        mD5 = versionCode.RegexSeek("(?<=MD5:</strong> )[^<]+");
                        category = "installer";
                    }
                    else if (versionCode.Contains("classifier-universal\""))
                    {
                        // 类型为 universal.zip，支持范围 751~449 (1.6.1 部分), 682~183 (1.5.1 ~ 1.3.2 部分)
                        versionCode = versionCode.Substring(versionCode.IndexOfF("universal.zip"));
                        mD5 = versionCode.RegexSeek("(?<=MD5:</strong> )[^<]+");
                        category = "universal";
                    }
                    else if (versionCode.Contains("client.zip"))
                    {
                        // 类型为 client.zip，支持范围 182~ (1.3.2 部分 ~)
                        versionCode = versionCode.Substring(versionCode.IndexOfF("client.zip"));
                        mD5 = versionCode.RegexSeek("(?<=MD5:</strong> )[^<]+");
                        category = "client";
                    }
                    else
                    {
                        // 没有任何下载（1.6.4 有一部分这种情况）
                        continue;
                    }

                    // 添加进列表
                    versions.Add(new DlForgeVersionEntry(name, branch, inherit)
                    {
                        Category = category, IsRecommended = isRecommended,
                        Hash = mD5.Trim('\r', '\n'),
                        ReleaseTime = releaseTime
                    });
                }
                catch (Exception ex)
                {
                    throw new Exception(
                        Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Forge", versionCode), ex);
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Forge", result), ex);
        }

        if (!versions.Any())
            throw new Exception(Lang.Text("Minecraft.Download.Error.NotFound"));
        loader.output = versions;
    }

    /// <summary>
    ///     Forge 版本列表，BMCLAPI。
    /// </summary>
    public static void DlForgeVersionBmclapiMain(ModLoader.LoaderTask<string, List<DlForgeVersionEntry>> loader)
    {
        var json = (JsonArray)Requester.FetchJson(
            "https://bmclapi2.bangbang93.com/forge/minecraft/" +
            loader.input.Replace("-", "_")); // 兼容 Forge 1.7.10-pre4，#4057
        var versions = new List<DlForgeVersionEntry>();
        try
        {
            var recommended = ModDownloadLib.McDownloadForgeRecommendedGet(loader.input);
            foreach (JsonObject Token in json)
            {
                // 分类与 Hash 获取
                string hash = null;
                var category = "unknown";
                var proi = -1;
                foreach (JsonObject File in Token["files"].AsArray())
                    switch (File["category"].ToString() ?? "")
                    {
                        case "installer":
                        {
                            if (File["format"].ToString() == "jar")
                            {
                                // 类型为 installer.jar，支持范围 ~753 (~ 1.6.1 部分), 738~684 (1.5.2 全部)
                                hash = (string)File["hash"];
                                category = "installer";
                                proi = 2;
                            }

                            break;
                        }
                        case "universal":
                        {
                            if (proi <= 1 && File["format"].ToString() == "zip")
                            {
                                // 类型为 universal.zip，支持范围 751~449 (1.6.1 部分), 682~183 (1.5.1 ~ 1.3.2 部分)
                                hash = (string)File["hash"];
                                category = "universal";
                                proi = 1;
                            }

                            break;
                        }
                        case "client":
                        {
                            if (proi <= 0 && File["format"].ToString() == "zip")
                            {
                                // 类型为 client.zip，支持范围 182~ (1.3.2 部分 ~)
                                hash = (string)File["hash"];
                                category = "client";
                                proi = 0;
                            }

                            break;
                        }
                    }

                // 获取 Entry
                var branch = (string)Token["branch"];
                var name = (string)Token["version"];
                // 基础信息获取
                var entry = new DlForgeVersionEntry(name, branch, loader.input)
                    { Hash = hash, Category = category, IsRecommended = (recommended ?? "") == (name ?? "") };
                var timeSplit = Token["modified"].ToString().Split('-', 'T', ':', '.', ' ', '/');
                entry.ReleaseTime = Lang.Date(Token["modified"].ToObject<DateTime>().ToLocalTime(), "g");
                // 添加项
                versions.Add(entry);
            }
        }
        catch (Exception ex)
        {
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Forge BMCLAPI", json),
                ex);
        }

        if (!versions.Any())
            throw new Exception(Lang.Text("Minecraft.Download.Error.NotFound"));
        loader.output = versions;
    }

    #endregion

    #region DlNeoForgeList | NeoForge 版本列表

    public struct DlNeoForgeListResult
    {
        /// <summary>
        ///     数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
        public string sourceName;

        /// <summary>
        ///     是否为官方的实时数据。
        /// </summary>
        public bool isOfficial;

        /// <summary>
        ///     所有版本的列表。已经按从新到老排序。
        /// </summary>
        public List<DlNeoForgeListEntry> Value;
    }

    public class DlNeoForgeListEntry : DlForgelikeEntry
    {
        /// <summary>
        ///     API 使用的原始版本字符串，如 “20.4.30-beta”、“1.20.1-47.1.99”（Legacy）。
        /// </summary>
        public string ApiName;

        /// <summary>
        ///     是否是 Beta 版。
        /// </summary>
        public bool IsBeta;

        public DlNeoForgeListEntry(string apiName)
        {
            forgeType = ForgelikeType.NeoForge;
            this.ApiName = apiName;
            IsBeta = apiName.Contains("beta") || apiName.Contains("alpha");
            if (apiName.Contains("1.20.1")) // 1.20.1-47.1.99
            {
                VersionName = apiName.Replace("1.20.1-", "");
                version = new Version("19." + VersionName);
                Inherit = "1.20.1";
            }
            else if (apiName.StartsWith("0.")) // 0.25w14craftmine.3-beta
            {
                VersionName = apiName;
                var segments = apiName.BeforeFirst("-").Split('.');
                version = new Version(0, 0, int.Parse(segments.Last()));
                Inherit = segments[1];
            }
            else // 20.4.30-beta；26.1.0.0-alpha.1+snapshot-1
            {
                VersionName = apiName;
                version = new Version(apiName.BeforeFirst("-"));
                if (version.Major >= 24)
                    Inherit = $"{version.Major}.{version.Minor}{(version.Build > 0 ? $".{version.Build}" : "")}";
                else
                    Inherit = "1." + version.Major + (version.Minor > 0 ? "." + version.Minor : "");
                if (VersionName.Contains("+"))
                    Inherit += "-" + VersionName.AfterFirst("+");
            }
        }

        /// <summary>
        ///     文件在官网的基础地址，不包含后缀。
        /// </summary>
        public string UrlBase
        {
            get
            {
                var packageName = IsLegacy ? "forge" : "neoforge";
                return
                    $"https://maven.neoforged.net/releases/net/neoforged/{packageName}/{ApiName}/{packageName}-{ApiName}";
            }
        }
    }

    /// <summary>
    ///     NeoForge 版本列表，主加载器。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlNeoForgeListResult> dlNeoForgeListLoader =
        new("DlNeoForgeList Main", DlNeoForgeListMain);

    private static void DlNeoForgeListMain(ModLoader.LoaderTask<int, DlNeoForgeListResult> loader)
    {
        switch (Config.Download.VersionListSource)
        {
            case 0:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlNeoForgeListResult>, int>>
                        { new(dlNeoForgeListBmclapiLoader, 30), new(dlNeoForgeListOfficialLoader, 30 + 60) },
                    loader.isForceRestarting);
                break;
            }
            case 1:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlNeoForgeListResult>, int>>
                        { new(dlNeoForgeListOfficialLoader, 5), new(dlNeoForgeListBmclapiLoader, 5 + 30) },
                    loader.isForceRestarting);
                break;
            }

            default:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlNeoForgeListResult>, int>>
                        { new(dlNeoForgeListOfficialLoader, 60), new(dlNeoForgeListBmclapiLoader, 60 + 60) },
                    loader.isForceRestarting);
                break;
            }
        }
    }

    /// <summary>
    ///     NeoForge 版本列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlNeoForgeListResult> dlNeoForgeListOfficialLoader =
        new("DlNeoForgeList Official", DlNeoForgeListOfficialMain);

    private static void DlNeoForgeListOfficialMain(ModLoader.LoaderTask<int, DlNeoForgeListResult> loader)
    {
        // 获取版本列表 JSON
        var resultLatest = Requester.FetchJson(
            "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge",
            new RequestParam
            {
                UseBrowserUserAgent = true
            }).ToString();
        var resultLegacy = Requester.FetchJson(
            "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge",
            new RequestParam
            {
                UseBrowserUserAgent = true
            }).ToString();
        if (resultLatest.Length < 100 || resultLegacy.Length < 100)
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "NeoForge",
                resultLatest + "\r\n\r\n" + resultLegacy));
        // 解析
        try
        {
            loader.output = new DlNeoForgeListResult
            {
                isOfficial = true,
                sourceName = Lang.Text("Download.Source.NeoForgeOfficial"),
                Value = GetNeoForgeEntries(resultLatest, resultLegacy)
            };
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "NeoForge",
                    resultLatest + "\r\n\r\n" + resultLegacy), ex);
        }
    }

    /// <summary>
    ///     NeoForge 版本列表，BMCLAPI。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlNeoForgeListResult> dlNeoForgeListBmclapiLoader =
        new("DlNeoForgeList Bmclapi", DlNeoForgeListBmclapiMain);

    public static void DlNeoForgeListBmclapiMain(ModLoader.LoaderTask<int, DlNeoForgeListResult> loader)
    {
        // 获取版本列表 JSON
        var resultLatest = Requester.FetchJson(
            "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge",
            new RequestParam
            {
                UseBrowserUserAgent = true
            }).ToString();
        var resultLegacy = Requester.FetchJson(
            "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/forge",
            new RequestParam
            {
                UseBrowserUserAgent = true
            }).ToString();
        if (resultLatest.Length < 100 || resultLegacy.Length < 100)
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "NeoForge BMCLAPI",
                resultLatest + "\r\n\r\n" + resultLegacy));
        // 解析
        try
        {
            loader.output = new DlNeoForgeListResult
            {
                isOfficial = true,
                sourceName = "BMCLAPI",
                Value = GetNeoForgeEntries(resultLatest, resultLegacy)
            };
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "NeoForge BMCLAPI",
                    resultLatest + "\r\n\r\n" + resultLegacy),
                ex);
        }
    }

    private static List<DlNeoForgeListEntry> GetNeoForgeEntries(string latestJson, string latestLegacyJson)
    {
        var versionNames = ModBase.RegexSearch(latestLegacyJson + latestJson, RegexPatterns.DlNeoForgeVersion);
        var versions = versionNames.Where(name => name != "47.1.82").Select(name => new DlNeoForgeListEntry(name))
            .OrderByDescending(a => a).ToList(); // 这个版本虽然在版本列表中，但不能下载
        if (!versions.Any())
            throw new Exception(Lang.Text("Minecraft.Download.Error.NotFound"));
        return versions;
    }

    #endregion

    #region DlCleanroomList | Cleanroom 版本列表

    public struct DlCleanroomListResult
    {
        /// <summary>
        ///     数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
        public string sourceName;

        /// <summary>
        ///     是否为官方的实时数据。
        /// </summary>
        public bool isOfficial;

        /// <summary>
        ///     所有版本的列表。已经按从新到老排序。
        /// </summary>
        public List<DlCleanroomListEntry> Value;
    }

    public class DlCleanroomListEntry : DlForgelikeEntry
    {
        /// <summary>
        ///     API 使用的原始版本字符串，如 “0.2.4-alpha”。
        /// </summary>
        public string ApiName;

        /// <summary>
        ///     是否是 Beta 版。
        /// </summary>
        public bool IsBeta;

        public DlCleanroomListEntry(string apiName)
        {
            forgeType = ForgelikeType.Cleanroom;
            this.ApiName = apiName;
            IsBeta = apiName.Contains("alpha");
            VersionName = apiName;
            version = new Version(apiName.BeforeFirst("-"));
            Inherit = "1.12.2";
        }

        /// <summary>
        ///     文件在官网的基础地址，不包含后缀。
        /// </summary>
        public string UrlBase =>
            $"https://github.com/CleanroomMC/Cleanroom/releases/download/{ApiName}/cleanroom-{ApiName}";
    }

    /// <summary>
    ///     Cleanroom 版本列表，主加载器。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlCleanroomListResult> dlCleanroomListLoader =
        new("DlCleanroomList Main", DlCleanroomListMain);

    private static void DlCleanroomListMain(ModLoader.LoaderTask<int, DlCleanroomListResult> loader)
    {
        switch (Config.Download.VersionListSource)
        {
            case 0:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlCleanroomListResult>, int>>
                        { new(dlCleanroomListOfficialLoader, 30) }, loader.isForceRestarting);
                break;
            }
            case 1:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlCleanroomListResult>, int>>
                        { new(dlCleanroomListOfficialLoader, 5) }, loader.isForceRestarting);
                break;
            }

            default:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlCleanroomListResult>, int>>
                        { new(dlCleanroomListOfficialLoader, 60) }, loader.isForceRestarting);
                break;
            }
        }
    }

    /// <summary>
    ///     Cleanroom 版本列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlCleanroomListResult> dlCleanroomListOfficialLoader =
        new("DlCleanroomList Official", DlCleanroomListOfficialMain);

    private static void DlCleanroomListOfficialMain(ModLoader.LoaderTask<int, DlCleanroomListResult> loader)
    {
        // 获取版本列表 JSON
        var resultLatest = Requester.FetchJson(
            "https://api.github.com/repos/CleanroomMC/Cleanroom/releases", new RequestParam
            {
                UseBrowserUserAgent = true
            }).ToString();
        if (resultLatest.Length < 100)
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Cleanroom",
                resultLatest));
        // 解析
        try
        {
            loader.output = new DlCleanroomListResult
            {
                isOfficial = true,
                sourceName = Lang.Text("Download.Source.CleanroomOfficial"),
                Value = GetCleanroomEntries(resultLatest)
            };
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Cleanroom", resultLatest), ex);
        }
    }

    private static List<DlCleanroomListEntry> GetCleanroomEntries(string latestJson)
    {
        var versions = new List<DlCleanroomListEntry>();
        var json = JsonArray.Parse(latestJson);
        foreach (JsonObject Token in json.AsArray())
            versions.Add(new DlCleanroomListEntry(Token["tag_name"].ToString())
                { forgeType = (DlForgelikeEntry.ForgelikeType)2 });
        if (!versions.Any())
            throw new Exception(Lang.Text("Minecraft.Download.Error.NoAvailableVersion"));
        versions = versions.OrderByDescending(a => a.version).ToList();
        return versions;
    }

    #endregion

    #region DlLiteLoaderList | LiteLoader 版本列表

    public struct DlLiteLoaderListResult
    {
        /// <summary>
        ///     数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
        public string sourceName;

        /// <summary>
        ///     是否为官方的实时数据。
        /// </summary>
        public bool isOfficial;

        /// <summary>
        ///     获取到的数据。
        /// </summary>
        public List<DlLiteLoaderListEntry> Value;

        /// <summary>
        ///     官方源的失败原因。若没有则为 Nothing。
        /// </summary>
        public Exception officialError;
    }

    public class DlLiteLoaderListEntry
    {
        /// <summary>
        ///     实际的文件名，如“liteloader-installer-1.12-00-SNAPSHOT.jar”。
        /// </summary>
        public string FileName;

        /// <summary>
        ///     对应的 Minecraft 版本，如“1.12.2”。
        /// </summary>
        public string Inherit;

        /// <summary>
        ///     是否为 1.7 及更早的远古版。
        /// </summary>
        public bool IsLegacy;

        /// <summary>
        ///     是否为测试版。
        /// </summary>
        public bool IsPreview;

        /// <summary>
        ///     对应的 Json 项。
        /// </summary>
        public JsonNode jsonToken;

        /// <summary>
        ///     文件的 MD5。
        /// </summary>
        public string MD5;

        /// <summary>
        ///     发布时间，格式为“yyyy/mm/dd HH:mm”。
        /// </summary>
        public string ReleaseTime;
    }

    /// <summary>
    ///     LiteLoader 版本列表，主加载器。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlLiteLoaderListResult> dlLiteLoaderListLoader =
        new("DlLiteLoaderList Main", DlLiteLoaderListMain);

    private static void DlLiteLoaderListMain(ModLoader.LoaderTask<int, DlLiteLoaderListResult> loader)
    {
        switch (Config.Download.VersionListSource)
        {
            case 0:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlLiteLoaderListResult>, int>>
                    {
                        new(dlLiteLoaderListBmclapiLoader, 30), new(dlLiteLoaderListOfficialLoader, 30 + 60)
                    }, loader.isForceRestarting);
                break;
            }
            case 1:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlLiteLoaderListResult>, int>>
                    {
                        new(dlLiteLoaderListOfficialLoader, 5), new(dlLiteLoaderListBmclapiLoader, 5 + 30)
                    }, loader.isForceRestarting);
                break;
            }

            default:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlLiteLoaderListResult>, int>>
                    {
                        new(dlLiteLoaderListOfficialLoader, 60), new(dlLiteLoaderListBmclapiLoader, 60 + 60)
                    }, loader.isForceRestarting);
                break;
            }
        }
    }

    /// <summary>
    ///     LiteLoader 版本列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlLiteLoaderListResult> dlLiteLoaderListOfficialLoader =
        new("DlLiteLoaderList Official", DlLiteLoaderListOfficialMain);

    private static void DlLiteLoaderListOfficialMain(ModLoader.LoaderTask<int, DlLiteLoaderListResult> loader)
    {
        var result =
            (JsonObject)Requester.FetchJson("https://dl.liteloader.com/versions/versions.json");
        try
        {
            var json = (JsonObject)result["versions"];
            var versions = new List<DlLiteLoaderListEntry>();
            foreach (var Pair in json)
            {
                if (Pair.Key.StartsWithF("1.6") || Pair.Key.StartsWithF("1.5"))
                    continue;
                var realEntry =
                    (Pair.Value["artefacts"] ?? Pair.Value["snapshots"])["com.mumfrey:liteloader"]["latest"];
                versions.Add(new DlLiteLoaderListEntry
                {
                    Inherit = Pair.Key,
                    IsLegacy = double.Parse(Pair.Key.Split(".")[1]) < 8d,
                    IsPreview = realEntry["stream"].ToString().ToLower() == "snapshot",
                    FileName = "liteloader-installer-" + Pair.Key +
                               (Pair.Key == "1.8" || Pair.Key == "1.9" ? ".0" : "") + "-00-SNAPSHOT.jar",
                    MD5 = (string)realEntry["md5"],
                    ReleaseTime = TimeUtils.FormatUnixTimestamp(long.Parse(realEntry["timestamp"].ToString())),
                    jsonToken = realEntry
                });
            }

            loader.output = new DlLiteLoaderListResult
                { isOfficial = true, sourceName = Lang.Text("Download.Source.LiteLoaderOfficial"), Value = versions };
        }
        catch (Exception ex)
        {
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "LiteLoader", result),
                ex);
        }
    }

    /// <summary>
    ///     LiteLoader 版本列表，BMCLAPI。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlLiteLoaderListResult> dlLiteLoaderListBmclapiLoader =
        new("DlLiteLoaderList Bmclapi", DlLiteLoaderListBmclapiMain);

    private static void DlLiteLoaderListBmclapiMain(ModLoader.LoaderTask<int, DlLiteLoaderListResult> loader)
    {
        var result =
            (JsonObject)Requester.FetchJson(
                "https://bmclapi2.bangbang93.com/maven/com/mumfrey/liteloader/versions.json");
        try
        {
            var json = (JsonObject)result["versions"];
            var versions = new List<DlLiteLoaderListEntry>();
            foreach (var Pair in json)
            {
                if (Pair.Key.StartsWithF("1.6") || Pair.Key.StartsWithF("1.5"))
                    continue;
                var realEntry =
                    (Pair.Value["artefacts"] ?? Pair.Value["snapshots"])["com.mumfrey:liteloader"]["latest"];
                versions.Add(new DlLiteLoaderListEntry
                {
                    Inherit = Pair.Key,
                    IsLegacy = double.Parse(Pair.Key.Split(".")[1]) < 8d,
                    IsPreview = realEntry["stream"].ToString().ToLower() == "snapshot",
                    FileName = "liteloader-installer-" + Pair.Key +
                               (Pair.Key == "1.8" || Pair.Key == "1.9" ? ".0" : "") + "-00-SNAPSHOT.jar",
                    MD5 = (string)realEntry["md5"],
                    ReleaseTime = TimeUtils.FormatUnixTimestamp(long.Parse((string)realEntry["timestamp"])),
                    jsonToken = realEntry
                });
            }

            loader.output = new DlLiteLoaderListResult { isOfficial = false, sourceName = "BMCLAPI", Value = versions };
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "LiteLoader BMCLAPI", result), ex);
        }
    }

    #endregion

    #region DlFabricList | Fabric 列表

    public struct DlFabricListResult
    {
        /// <summary>
        ///     数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
        public string sourceName;

        /// <summary>
        ///     是否为官方的实时数据。
        /// </summary>
        public bool isOfficial;

        /// <summary>
        ///     获取到的数据。
        /// </summary>
        public JsonObject Value;
    }

    /// <summary>
    ///     Fabric 列表，主加载器。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlFabricListResult> dlFabricListLoader =
        new("DlFabricList Main", DlFabricListMain);

    private static void DlFabricListMain(ModLoader.LoaderTask<int, DlFabricListResult> loader)
    {
        switch (Config.Download.VersionListSource)
        {
            case 0:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlFabricListResult>, int>>
                        { new(dlFabricListBmclapiLoader, 30), new(dlFabricListOfficialLoader, 30 + 60) },
                    loader.isForceRestarting);
                break;
            }
            case 1:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlFabricListResult>, int>>
                        { new(dlFabricListOfficialLoader, 5), new(dlFabricListBmclapiLoader, 5 + 30) },
                    loader.isForceRestarting);
                break;
            }

            default:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlFabricListResult>, int>>
                        { new(dlFabricListOfficialLoader, 60), new(dlFabricListBmclapiLoader, 60 + 60) },
                    loader.isForceRestarting);
                break;
            }
        }
    }

    /// <summary>
    ///     Fabric 列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlFabricListResult> dlFabricListOfficialLoader =
        new("DlFabricList Official", DlFabricListOfficialMain);

    private static void DlFabricListOfficialMain(ModLoader.LoaderTask<int, DlFabricListResult> loader)
    {
        var result = (JsonObject)Requester.FetchJson("https://meta.fabricmc.net/v2/versions");
        try
        {
            var output = new DlFabricListResult
                { isOfficial = true, sourceName = Lang.Text("Download.Source.FabricOfficial"), Value = result };
            if (output.Value["game"] is null || output.Value["loader"] is null || output.Value["installer"] is null)
                throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Fabric", result));
            loader.output = output;
        }
        catch (Exception ex)
        {
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Fabric", result), ex);
        }
    }

    /// <summary>
    ///     Fabric 列表，BMCLAPI。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlFabricListResult> dlFabricListBmclapiLoader =
        new("DlFabricList Bmclapi", DlFabricListBmclapiMain);

    private static void DlFabricListBmclapiMain(ModLoader.LoaderTask<int, DlFabricListResult> loader)
    {
        var result = (JsonObject)Requester.FetchJson("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions");
        try
        {
            var output = new DlFabricListResult { isOfficial = false, sourceName = "BMCLAPI", Value = result };
            if (output.Value["game"] is null || output.Value["loader"] is null || output.Value["installer"] is null)
                throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Fabric BMCLAPI",
                    result));
            loader.output = output;
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Fabric BMCLAPI", result), ex);
        }
    }

    /// <summary>
    ///     Fabric API 列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, List<ModComp.CompFile>> dlFabricApiLoader = new("Fabric API List Loader",
        task => task.output = ModComp.CompFilesGet("fabric-api", false));

    /// <summary>
    ///     OptiFabric 列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, List<ModComp.CompFile>> dlOptiFabricLoader =
        new("OptiFabric List Loader", task => task.output = ModComp.CompFilesGet("322385", true));

    #endregion

    #region DlLabyModList | LabyMod 列表

    public struct DlLabyModListResult
    {
        /// <summary>
        ///     获取到的数据。
        /// </summary>
        public JsonObject Value;
    }

    /// <summary>
    ///     LabyMod 列表，主加载器。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlLabyModListResult> dlLabyModListLoader =
        new("DlLabyModList Main", DlLabyModListMain);

    private static void DlLabyModListMain(ModLoader.LoaderTask<int, DlLabyModListResult> loader)
    {
        switch (Config.Download.VersionListSource)
        {
            case 0:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlLabyModListResult>, int>>
                        { new(dlLabyModListOfficialLoader, 30), new(dlLabyModListOfficialLoader, 60) },
                    loader.isForceRestarting);
                break;
            }
            case 1:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlLabyModListResult>, int>>
                        { new(dlLabyModListOfficialLoader, 5), new(dlLabyModListOfficialLoader, 35) },
                    loader.isForceRestarting);
                break;
            }

            default:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlLabyModListResult>, int>>
                        { new(dlLabyModListOfficialLoader, 60), new(dlLabyModListOfficialLoader, 60) },
                    loader.isForceRestarting);
                break;
            }
        }
    }

    /// <summary>
    ///     LabyMod 列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlLabyModListResult> dlLabyModListOfficialLoader =
        new("DlLabyModList Official", DlLabyModListOfficialMain);

    private static void DlLabyModListOfficialMain(ModLoader.LoaderTask<int, DlLabyModListResult> loader)
    {
        JsonObject resultProduction;
        using (var productionResponse = HttpRequest
                   .Create("https://releases.r2.labymod.net/api/v1/manifest/production/latest.json")
                   .WithHttpVersionOption(HttpVersion.Version20)
                   .SendAsync()
                   .GetAwaiter()
                   .GetResult())
        {
            resultProduction = (JsonObject)ModBase.GetJson(productionResponse.AsString());
        }

        JsonObject resultSnapshot;
        using (var snapshotResponse = HttpRequest
                   .Create("https://releases.r2.labymod.net/api/v1/manifest/snapshot/latest.json")
                   .WithHttpVersionOption(HttpVersion.Version20)
                   .SendAsync()
                   .GetAwaiter()
                   .GetResult())
        {
            snapshotResponse.EnsureSuccessStatusCode();
            resultSnapshot = (JsonObject)ModBase.GetJson(snapshotResponse.AsString());
        }

        var result = new JsonObject();
        result.Add("production", resultProduction);
        result.Add("snapshot", resultSnapshot);
        try
        {
            var output = new DlLabyModListResult { Value = result };
            if (output.Value["production"]["labyModVersion"] is null ||
                output.Value["snapshot"]["labyModVersion"] is null)
                throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "LabyMod",
                    result));
            loader.output = output;
        }
        catch (Exception ex)
        {
            throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "LabyMod", result),
                ex);
        }
    }

    #endregion

    #region DlMod | Mod 镜像源请求

    /// <summary>
    ///     对可能涉及 Mod 镜像源的请求进行处理，返回字符串。
    ///     调用 NetGetCodeByRequest，会进行重试。
    /// </summary>
    public static string DlModRequest(string url) => DlModRequest<string>(url);

    /// <summary>
    ///     对可能涉及 Mod 镜像源的请求进行处理，返回字符串或 JSON 对象。
    ///     调用 NetGetCodeByRequest，会进行重试。
    /// </summary>
    public static T DlModRequest<T>(string url)
    {
        var urls = new List<KeyValuePair<string, int>>();
        var mcimUrl = DlSourceModGet(url);
        if ((mcimUrl ?? "") != (url ?? ""))
            switch (Config.Download.Comp.CompSourceSolution)
            {
                case 0:
                {
                    urls.Add(new KeyValuePair<string, int>(mcimUrl, 5));
                    urls.Add(new KeyValuePair<string, int>(mcimUrl, 10));
                    urls.Add(new KeyValuePair<string, int>(url, 15));
                    break;
                }
                case 1:
                {
                    urls.Add(new KeyValuePair<string, int>(url, 5));
                    urls.Add(new KeyValuePair<string, int>(mcimUrl, 5));
                    urls.Add(new KeyValuePair<string, int>(url, 15));
                    urls.Add(new KeyValuePair<string, int>(mcimUrl, 10));
                    break;
                }

                default:
                {
                    urls.Add(new KeyValuePair<string, int>(url, 5));
                    urls.Add(new KeyValuePair<string, int>(url, 15));
                    urls.Add(new KeyValuePair<string, int>(mcimUrl, 10));
                    break;
                }
            }

        var exs = "";
        foreach (var Source in urls)
            try
            {
                var json = Requester.FetchString(Source.Key, new RequestParam
                {
                    Timeout = Source.Value * 1000,
                    UseBrowserUserAgent = true
                });
                if (typeof(T) == typeof(string)) return (T)(object)json;
                return (T)(object)ModBase.GetJson(json);
            }
            catch (Exception ex)
            {
                // 镜像源可能随机爆炸，忽略就好
                if (!ex.Message.ContainsF("mcimirror")) exs += ex.Message + "\r\n";
            }

        throw new Exception(exs);
    }

    /// <summary>
    ///     非泛型版本的 DlModRequest，返回 string
    ///     对可能涉及 Mod 镜像源的请求进行处理。
    ///     调用 NetRequest，会进行重试。
    /// </summary>
    public static string DlModRequest(string url, string method, string data, string contentType,
        bool allowMirror = false) => DlModRequest<string>(url, method, data, contentType, allowMirror);
    
    /// <summary>
    ///     对可能涉及 Mod 镜像源的请求进行处理。
    ///     调用 NetRequest，会进行重试。
    /// </summary>
    public static T DlModRequest<T>(string url, string method, string data, string contentType,
        bool allowMirror = false)
    {
        var urls = new List<KeyValuePair<string, int>>();
        var mcimUrl = DlSourceModGet(url);
        if ((mcimUrl ?? "") != (url ?? ""))
            switch (allowMirror ? Config.Download.Comp.CompSourceSolution : 2)
            {
                case 0:
                {
                    urls.Add(new KeyValuePair<string, int>(mcimUrl, 5));
                    urls.Add(new KeyValuePair<string, int>(mcimUrl, 10));
                    urls.Add(new KeyValuePair<string, int>(url, 15));
                    break;
                }
                case 1:
                {
                    urls.Add(new KeyValuePair<string, int>(url, 5));
                    urls.Add(new KeyValuePair<string, int>(mcimUrl, 5));
                    urls.Add(new KeyValuePair<string, int>(url, 15));
                    urls.Add(new KeyValuePair<string, int>(mcimUrl, 10));
                    break;
                }

                default:
                {
                    urls.Add(new KeyValuePair<string, int>(url, 5));
                    urls.Add(new KeyValuePair<string, int>(url, 15));
                    urls.Add(new KeyValuePair<string, int>(mcimUrl, 10));
                    break;
                }
            }

        var exs = "";
        foreach (var Source in urls)
            try
            {
                string json = Requester.Fetch(Source.Key, new FetchParam
                {
                    Method = method,
                    Content = data, 
                    ContentType = contentType,
                    Timeout = Source.Value * 1000
                });
                if (typeof(T) == typeof(string)) return (T)(object)json; // 沟槽的，为什么不能写 T is string
                return (T)(object)ModBase.GetJson(json);
            }
            catch (Exception ex)
            {
                if (!ex.Message.ContainsF("mcimirror")) exs += ex.Message + "\r\n";
            }

        throw new Exception(exs);
    }

    #endregion

    #region DlSource | 镜像下载源

    private static bool dlPreferMojang;

    /// <summary>
    ///     下载文件（而非获取版本列表）的时候，是否优先使用官方源。
    /// </summary>
    public static bool DlSourcePreferMojang =>
        Config.Download.FileSource == 2 ||
        (Config.Download.FileSource == 1 && dlPreferMojang);

    /// <summary>
    ///     下载文件（而非获取版本列表）的时候，根据是否优先使用官方源决定使用 Url 的顺序。
    /// </summary>
    public static IEnumerable<string> DlSourceOrder(IEnumerable<string> officialUrls, IEnumerable<string> mirrorUrls)
    {
        return DlSourcePreferMojang ? officialUrls.Union(mirrorUrls) : mirrorUrls.Union(officialUrls);
    }

    /// <summary>
    ///     获取版本列表（而非下载文件）的时候，是否优先使用官方源。
    /// </summary>
    public static bool DlVersionListPreferMojang =>
        Config.Download.VersionListSource == 2 ||
        (Config.Download.VersionListSource == 1 && dlPreferMojang);

    /// <summary>
    ///     获取版本列表（而非下载文件）的时候，根据是否优先使用官方源决定使用 Url 的顺序。
    /// </summary>
    public static IEnumerable<string> DlVersionListOrder(IEnumerable<string> officialUrls,
        IEnumerable<string> mirrorUrls)
    {
        return DlVersionListPreferMojang ? officialUrls.Union(mirrorUrls) : mirrorUrls.Union(officialUrls);
    }


    /// <summary>
    ///     下载 Assets 文件。
    /// </summary>
    public static IEnumerable<string> DlSourceAssetsGet(string original)
    {
        original = original.Replace("http://resources.download.minecraft.net",
            "https://resources.download.minecraft.net");
        return DlSourceOrder(new[] { original },
            new[]
            {
                original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/assets")
                    .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/assets")
                    .Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets")
            });
    }

    /// <summary>
    ///     下载 Libraries 文件。
    /// </summary>
    public static IEnumerable<string> DlSourceLibraryGet(string original)
    {
        if (new[] { "minecraftforge", "fabricmc", "neoforged" }.Any(k => original.Contains(k))) // 不添加原版源
            return new[]
            {
                original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/maven")
                    .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/maven")
                    .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven")
                    .Replace("https://zkitefly.github.io/unlisted-versions-of-minecraft",
                        "https://alist.8mi.tech/d/mirror/unlisted-versions-of-minecraft/Auto"),
                original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/libraries")
                    .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/libraries")
                    .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/libraries")
                    .Replace("https://zkitefly.github.io/unlisted-versions-of-minecraft",
                        "https://alist.8mi.tech/d/mirror/unlisted-versions-of-minecraft/Auto")
            };

        return DlSourceOrder(new[] { original },
            new[]
            {
                original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/maven")
                    .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/maven")
                    .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven")
                    .Replace("https://zkitefly.github.io/unlisted-versions-of-minecraft",
                        "https://alist.8mi.tech/d/mirror/unlisted-versions-of-minecraft/Auto"),
                original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/libraries")
                    .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/libraries")
                    .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/libraries")
                    .Replace("https://zkitefly.github.io/unlisted-versions-of-minecraft",
                        "https://alist.8mi.tech/d/mirror/unlisted-versions-of-minecraft/Auto"),
                original
            });
    }

    /// <summary>
    ///     下载 Launcher 或 Meta 文件。
    ///     不应使用它来获取版本列表（因为它只使用文件下载源设置来决定源顺序）。
    /// </summary>
    public static IEnumerable<string> DlSourceLauncherOrMetaGet(string original)
    {
        if (original is null)
            throw new Exception(Lang.Text("Minecraft.Download.Error.NoJsonDownloadAddress"));
        return DlSourceOrder(new[] { original },
            new[]
            {
                original.Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com")
                    .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com")
                    .Replace("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com")
                    .Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com")
                    .Replace("https://zkitefly.github.io/unlisted-versions-of-minecraft",
                        "https://alist.8mi.tech/d/mirror/unlisted-versions-of-minecraft/Auto"),
                original
            });
    }

    /// <summary>
    ///     Mod Api 镜像源
    /// </summary>
    /// <param name="original"></param>
    /// <returns></returns>
    public static string DlSourceModGet(string original)
    {
        return original.Replace("https://api.modrinth.com", "https://mod.mcimirror.top/modrinth")
            .Replace("https://api.curseforge.com", "https://mod.mcimirror.top/curseforge");
    }

    /// <summary>
    ///     Mod 下载镜像源
    /// </summary>
    /// <param name="original"></param>
    /// <returns></returns>
    public static List<string> DlSourceModDownloadGet(string original)
    {
        var res = new List<string>();
        var mirrorDl = original.Replace("https://cdn.modrinth.com", "https://mod.mcimirror.top")
            .Replace("https://edge.forgecdn.net",
                "https://mod.mcimirror.top"); // like https://cdn.modrinth.com/data/P7dR8mSH/versions/X2hTodix/fabric-api-0.129.0%2B1.21.8.jar
        // like https://edge.forgecdn.net/files/6767/951/jei-1.21.5-neoforge-21.4.0.27.jar
        switch (Config.Download.Comp.CompSourceSolution)
        {
            case 0: // 镜像源
            {
                res.Add(mirrorDl);
                res.Add(mirrorDl);
                break;
            }
            case 1: // 平衡
            {
                res.Add(original);
                res.Add(mirrorDl);
                break;
            }
            case 2: // 官方源
            {
                res.Add(original);
                res.Add(original); // 错误
                break;
            }

            default:
            {
                Config.Download.Comp.CompSourceSolution = 1;
                res.Add(original);
                break;
            }
        }

        res.Add(original);
        return res;
    }

    // Loader 自动切换
    private static void DlSourceLoader<InputType, OutputType>(ModLoader.LoaderTask<InputType, OutputType> mainLoader,
        List<KeyValuePair<ModLoader.LoaderTask<InputType, OutputType>, int>> loaderList, bool isForceRestart = false)
    {
        var waitCycle = 0;
        while (true)
        {
            // 检查状态
            var beforeLoadersAllFailed = true;
            foreach (var SubLoader in loaderList)
            {
                if (waitCycle == 0) // 判断是否可以不加载，直接使用已经加载好的结果
                {
                    if (isForceRestart)
                        continue; // 强制刷新，不行
                    if (SubLoader.Key.input is null ^ mainLoader.input is null || (SubLoader.Key.input is not null &&
                            !SubLoader.Key.input.Equals(mainLoader.input)))
                        continue; // 父子加载器的输入不一样，也不行
                }

                if (SubLoader.Key.State != ModBase.LoadState.Failed)
                    beforeLoadersAllFailed = false;
                if (SubLoader.Key.State == ModBase.LoadState.Finished)
                {
                    // 检查加载器成功
                    mainLoader.output = SubLoader.Key.output;
                    DlSourceLoaderAbort(loaderList);
                    return;
                }

                if (beforeLoadersAllFailed)
                    // 此前的加载器全部失败，直接启动后续加载器
                    if (waitCycle < SubLoader.Value * 100)
                        waitCycle = SubLoader.Value * 100;
            }

            // 第一轮时：既然不直接使用已经加载好的结果，那就启动第一个加载器
            if (waitCycle == 0)
            {
                loaderList.First().Key.Start(mainLoader.input, isForceRestart);
                foreach (var Loader in loaderList.Skip(1))
                    Loader.Key.State = ModBase.LoadState.Waiting; // 将其他源标记为未启动，以确保可以切换下载源（#184）
            }

            // 检查加载器失败或超时
            for (int i = 0, loopTo = loaderList.Count - 1; i <= loopTo; i++)
            {
                if (waitCycle != loaderList[i].Value * 100)
                    continue;
                if (i < loaderList.Count - 1 && !loaderList.All(l => l.Key.State == ModBase.LoadState.Failed))
                {
                    // 若还有下一个源，则启动下一个源
                    loaderList[i + 1].Key.Start(mainLoader.input, isForceRestart);
                }
                else
                {
                    // 若没有，则失败
                    Exception errorInfo = null;
                    for (int ii = 0, loopTo1 = loaderList.Count - 1; ii <= loopTo1; ii++)
                    {
                        loaderList[ii].Key.input = default; // 重置输入，以免以同样的输入“重试加载”时直接失败
                        if (loaderList[ii].Key.Error is null) continue;
                        if (errorInfo is null || loaderList[ii].Key.Error.Message
                                .Contains(Lang.Text("Minecraft.Download.Error.NotFound")))
                            errorInfo = loaderList[ii].Key.Error;
                    }

                    errorInfo ??= new TimeoutException(Lang.Text("Minecraft.Download.Error.Timeout"));
                    DlSourceLoaderAbort(loaderList);
                    throw errorInfo;
                }

                break;
            }

            // 计时
            Thread.Sleep(10);
            waitCycle += 1;
            // 检查父加载器中断
            if (mainLoader.IsAborted)
            {
                DlSourceLoaderAbort(loaderList);
                return;
            }
        }
    }

    private static void DlSourceLoaderAbort<InputType, OutputType>(
        List<KeyValuePair<ModLoader.LoaderTask<InputType, OutputType>, int>> loaderList)
    {
        foreach (var Loader in loaderList)
            if (Loader.Key.State == ModBase.LoadState.Loading)
                Loader.Key.Abort();
    }

    #endregion

    #region DlLegacyFabricList | LegacyFabric 列表

    public struct DlLegacyFabricListResult
    {
        /// <summary>
        ///     数据来源名称，如“Official”，“BMCLAPI”。
        /// </summary>
        public string sourceName;

        /// <summary>
        ///     是否为官方的实时数据。
        /// </summary>
        public bool isOfficial;

        /// <summary>
        ///     获取到的数据。
        /// </summary>
        public JsonObject Value;
    }

    /// <summary>
    ///     LegacyFabric 列表，主加载器。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlLegacyFabricListResult> dlLegacyFabricListLoader =
        new("DlLegacyFabricList Main", DlLegacyFabricListMain);

    private static void DlLegacyFabricListMain(ModLoader.LoaderTask<int, DlLegacyFabricListResult> loader)
    {
        switch (Config.Download.VersionListSource)
        {
            case 0:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlLegacyFabricListResult>, int>>
                        { new(dlLegacyFabricListOfficialLoader, 30) }, loader.isForceRestarting);
                break;
            }
            case 1:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlLegacyFabricListResult>, int>>
                        { new(dlLegacyFabricListOfficialLoader, 5) }, loader.isForceRestarting);
                break;
            }

            default:
            {
                DlSourceLoader(loader,
                    new List<KeyValuePair<ModLoader.LoaderTask<int, DlLegacyFabricListResult>, int>>
                        { new(dlLegacyFabricListOfficialLoader, 60) }, loader.isForceRestarting);
                break;
            }
        }
    }

    /// <summary>
    ///     LegacyFabric 列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, DlLegacyFabricListResult> dlLegacyFabricListOfficialLoader =
        new("DlLegacyFabricList Official", DlLegacyFabricListOfficialMain);

    private static void DlLegacyFabricListOfficialMain(ModLoader.LoaderTask<int, DlLegacyFabricListResult> loader)
    {
        var result =
            (JsonObject)Requester.FetchJson("https://meta.legacyfabric.net/v2/versions");
        try
        {
            var output = new DlLegacyFabricListResult
                { isOfficial = true, sourceName = Lang.Text("Download.Source.LegacyFabricOfficial"), Value = result };
            if (output.Value["game"] is null || output.Value["loader"] is null || output.Value["installer"] is null)
                throw new Exception(Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "LegacyFabric",
                    result));
            loader.output = output;
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "LegacyFabric", result), ex);
        }
    }

    /// <summary>
    ///     Legacy Fabric API 列表，官方源。
    /// </summary>
    public static ModLoader.LoaderTask<int, List<ModComp.CompFile>> dlLegacyFabricApiLoader =
        new("Legacy Fabric API List Loader", task => task.output = ModComp.CompFilesGet("legacy-fabric-api", false));

    #endregion

    /// <summary>
    ///     发送 Minecraft 更新提示。
    /// </summary>
    public static void McDownloadClientUpdateHint(string versionName, JsonObject json)
    {
        try
        {
            // 获取对应版本
            JsonNode version = null;
            foreach (var Token in json["versions"].AsArray())
                if (Token["id"] is not null && (Token["id"].ToString() ?? "") == (versionName ?? ""))
                {
                    version = Token;
                    break;
                }

            // 进行提示
            if (version is null)
                return;
            var time = version["releaseTime"].ToObject<DateTime>();
            var msgBoxText = Lang.Text("Minecraft.Update.NewVersion", versionName) + "\r\n" +
                             ((DateTime.Now - time).TotalDays > 1d
                                 ? Lang.Text("Minecraft.Update.UpdateTime") + Lang.Date(time)
                                 : Lang.Text("Minecraft.Update.UpdatedAt") + Lang.TimeSpan(time - DateTime.Now));
            var msgResult = ModMain.MyMsgBox(msgBoxText, Lang.Text("Minecraft.Update.Title"),
                Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Download"),
                (DateTime.Now - time).TotalHours > 3d ? Lang.Text("Common.Action.UpdateLog") : "",
                button3Action: () => ModDownloadLib.McUpdateLogShow(version));
            // 弹窗结果
            if (msgResult == 2)
                // 下载
                ModBase.RunInUi(() =>
                {
                    PageDownloadInstall.mcVersionWaitingForSelect = versionName;
                    ModMain.frmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall);
                });
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Minecraft.Error.UpdateNotify", versionName ?? "Nothing"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Minecraft.Error.UpdateNotify", versionName ?? "Nothing"));
        }
    }
}
