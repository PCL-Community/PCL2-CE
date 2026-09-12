using System.IO;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.Localization;
using PCL.Core.Utils.OS;
using PCL.Network;

namespace PCL
{
    public class CustomEvent
    {
        public EventType Type { get; set; } = EventType.None;
        public string Data { get; set; } = string.Empty;

        public CustomEvent() { }

        public CustomEvent(EventType type, string data)
        {
            Type = type;
            Data = data;
        }

        public void Raise() => Raise(Type, Data);

        /// <summary>
        /// 静态入口：根据 EventType 分发给对应的 action 执行。
        /// </summary>
        public static void Raise(EventType type, string arg)
        {
            if (type == EventType.None) return;
            ModBase.Log($"[Control] 执行自定义事件: {type}, {arg}");

            try
            {
                if (ActionMap.TryGetValue(type, out var action))
                    action(arg, type);
                else
                    ModMain.MyMsgBox(
                        Lang.Text("Event.Error.UnknownType", type.ToString()),
                        Lang.Text("Event.Error.Title"));
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Event.Error.ExecutionFailed", type, arg),
                    ModBase.LogLevel.Msgbox,
                    userSummary: Lang.Text("Event.Error.ExecutionFailed", type, arg));
            }
        }

        public static string GetCustomVariable(string name, string defaultValue = "") =>
            States.CustomVariables.GetValueOrDefault(name, defaultValue);

        /// <summary>
        /// 将 arg 按 '|' 分割为参数数组，空串统一返回 [""]。
        /// </summary>
        private static string[] SplitArgs(string arg) => arg.Split('|');

        /// <summary>
        /// 将 \\n 替换为 Windows 换行符 \r\n。
        /// </summary>
        private static string FixNewlines(string s) => s.Replace("\\n", "\r\n");

        /// <summary>
        /// 禁止自定义主页写入的高危设置黑名单
        /// </summary>
        private static readonly HashSet<string> SecuritySensitiveSettingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { 
            "LaunchAdvanceRun", 
            "VersionAdvanceRun"
        };
        
        /// <summary>
        /// EventType → 执行逻辑 的字典映射，O(1) 分发。
        /// </summary>
        private static readonly Dictionary<EventType, Action<string, EventType>> ActionMap = new()
        {
            [EventType.OpenUrl] = _OpenUrl,
            [EventType.OpenFile] = _OpenFileOrCommand,
            [EventType.ExecuteCommand] = _OpenFileOrCommand,
            [EventType.LaunchGame] = _LaunchGame,
            [EventType.CopyText] = _CopyText,
            [EventType.RefreshHomepage] = _Refresh,
            [EventType.RefreshPage] = _Refresh,
            [EventType.DailyFortune] = _DailyFortune,
            [EventType.ClearTrash] = _ClearTrash,
            [EventType.ShowDialog] = _ShowDialog,
            [EventType.ShowHint] = _ShowHint,
            [EventType.SwitchPage] = _SwitchPage,
            [EventType.ImportModpack] = _ModpackInstall,
            [EventType.InstallModpack] = _ModpackInstall,
            [EventType.DownloadFile] = _DownloadFile,
            [EventType.ModifySetting] = _WriteSetting,
            [EventType.WriteSetting] = _WriteSetting,
            [EventType.ModifyVariable] = _WriteVariable,
            [EventType.WriteVariable] = _WriteVariable,
            [EventType.OpenHelp] = _OpenHelp,
        };

        /// <summary>
        /// 打开网页。校验 https?:// 前缀，非 file 协议。
        /// </summary>
        private static void _OpenUrl(string arg, EventType type)
        {
            arg = arg.Replace('\\', '/');
            if (!arg.Contains("://") || arg.StartsWithF("file", true))
            {
                ModMain.MyMsgBox(Lang.Text("Event.Error.UrlRequired"), Lang.Text("Event.Error.Title"));
                return;
            }
            HintService.Hint(Lang.Text("Event.OpenUrl.Opening", arg));
            ModBase.RunInThread(() => ModBase.OpenWebsite(arg));
        }

        /// <summary>
        /// 打开文件 / 执行命令。复用 GetAbsoluteUrls 解析路径后 ProcessInterop.Start 启动。
        /// </summary>
        private static void _OpenFileOrCommand(string arg, EventType type)
        {
            var args = SplitArgs(arg);
            ModBase.RunInThread(() =>
            {
                try
                {
                    var urls = GetAbsoluteUrls(args[0], type);
                    if (!EventSafetyConfirm($"{urls[0]}{(args.Length >= 2 ? " " + args[1] : "")}"))
                        return;
                    ProcessInterop.Start(urls[0], args.Length >= 2 ? args[1] : "");
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        Lang.Text("Event.Error.ExecutionFailed", type, arg),
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Event.Error.ExecutionFailed", type, arg));
                }
            });
        }

        /// <summary>
        /// 启动游戏。支持 \current 指代当前选中实例，可选 ServerIp。
        /// </summary>
        private static void _LaunchGame(string arg, EventType type)
        {
            var args = SplitArgs(arg);
            if (args[0] == "\\current")
            {
                if (ModInstanceList.McMcInstanceSelected is null)
                    throw new InvalidOperationException(Lang.Text("Event.LaunchGame.SelectVersion"));
                args[0] = ModInstanceList.McMcInstanceSelected.Name;
            }
            ModBase.RunInUi(() =>
            {
                var launchOptions = new ModLaunch.McLaunchOptions
                {
                    ServerIp = args.Length >= 2 ? args[1] : null,
                    instance = new McInstance(args[0])
                };
                if (ModLaunch.McLaunchStart(launchOptions))
                    HintService.Hint(Lang.Text("Event.LaunchGame.Starting", args[0]));
            });
        }

        /// <summary>
        /// 复制文本到剪贴板。
        /// </summary>
        private static void _CopyText(string arg, EventType _) => ModBase.ClipboardSet(arg);

        /// <summary>
        /// 刷新主页 / 刷新当前页面。要求当前 pageRight 实现 IRefreshable。
        /// </summary>
        private static void _Refresh(string arg, EventType _)
        {
            if (ModMain.frmMain?.pageRight is IRefreshable refreshable)
            {
                ModBase.RunInUiWait(() => refreshable.Refresh());
                if (string.IsNullOrEmpty(arg))
                    HintService.Hint(Lang.Text("Event.Refresh.Success"), HintType.Success);
            }
            else
                HintService.Hint(Lang.Text("Event.Refresh.NotSupported"), HintType.Error);
        }

        /// <summary>
        /// 今日人品。
        /// </summary>
        private static void _DailyFortune(string _, EventType __) => PageToolsTest.Jrrp();

        /// <summary>
        /// 清理垃圾。异步执行 RubbishClear。
        /// </summary>
        private static void _ClearTrash(string _, EventType __) =>
            ModBase.RunInThread(PageToolsTest.RubbishClear);

        /// <summary>
        /// 弹出消息框。参数：Title|Content[|ButtonText]。
        /// </summary>
        private static void _ShowDialog(string arg, EventType type)
        {
            var args = SplitArgs(arg);
            if (args.Length == 1)
                throw new ArgumentException(Lang.Text("Event.Error.MissingArgs", type.ToString(), "Title|Content"));
            ModMain.MyMsgBox(
                FixNewlines(args[1]),
                FixNewlines(args[0]),
                args.Length > 2 ? args[2] : Lang.Text("Common.Action.Confirm"));
        }

        /// <summary>
        /// 弹出提示条。参数：Message[|HintType]（HintType = Info / Success / Error，兼容旧版 Finish / Critical）。
        /// </summary>
        private static void _ShowHint(string arg, EventType _)
        {
            var args = SplitArgs(arg);
            var hintType = args.Length == 1
                ? HintType.Info
                : (HintType)Enum.Parse(typeof(HintType), NormalizeHintTypeName(args[1]), true);
            HintService.Hint(FixNewlines(args[0]), hintType);
        }

        /// <summary>
        /// 将旧版 HintType 名称映射到当前名称，保持对旧主页的兼容。
        /// </summary>
        private static string NormalizeHintTypeName(string name) => name.ToLowerInvariant() switch
        {
            "finish" => nameof(HintType.Success),
            "critical" => nameof(HintType.Error),
            _ => name
        };

        /// <summary>
        /// 切换页面。参数：PageType[|PageSubType]。
        /// </summary>
        private static void _SwitchPage(string arg, EventType _)
        {
            var args = SplitArgs(arg);
            ModBase.RunInUi(() =>
            {
                var page = (FormMain.PageType)Enum.Parse(typeof(FormMain.PageType), args[0], true);
                var sub = args.Length == 1
                    ? FormMain.PageSubType.Default
                    : (FormMain.PageSubType)Enum.Parse(typeof(FormMain.PageSubType), args[1], true);
                ModMain.frmMain?.PageChange(page, sub);
            });
        }

        /// <summary>
        /// 导入 / 安装整合包。触发 ModModpack.ModpackInstall()。
        /// </summary>
        private static void _ModpackInstall(string _, EventType __) =>
            ModBase.RunInUi(ModModpack.ModpackInstall);

        /// <summary>
        /// 下载文件。参数：Url[|SavePath[|FileName]]，校验 http/https 前缀并弹安全确认。
        /// </summary>
        private static void _DownloadFile(string arg, EventType _)
        {
            var args = SplitArgs(arg);
            args[0] = args[0].Replace('\\', '/');
            if (!args[0].StartsWithF("http://", true) && !args[0].StartsWithF("https://", true))
            {
                ModMain.MyMsgBox(Lang.Text("Event.Error.DownloadUrlRequired"), Lang.Text("Event.Error.Title"));
                return;
            }
            if (!EventSafetyConfirm(Lang.Text("Event.Download.Confirm", args[0])))
                return;

            try
            {
                PageToolsTest.StartCustomDownload(args[0],
                    args.Length >= 2 ? args[1] : ModBase.GetFileNameFromPath(args[0]),
                    args.Length >= 3 ? args[2] : null);
            }
            catch
            {
                PageToolsTest.StartCustomDownload(args[0], Lang.Text("Common.State.Unknown"));
            }
        }

        /// <summary>
        /// 写入 / 修改设置。参数：SettingName|Value。
        /// </summary>
        private static void _WriteSetting(string arg, EventType type)
        {
            var args = SplitArgs(arg);
            if (args.Length == 1)
                throw new ArgumentException(Lang.Text("Event.Error.MissingArgs", type.ToString(), "SettingName|Value"));
            if (ConfigService.TryGetConfigItemNoType(args[0], out var item) && item.Source != ConfigSource.SharedEncrypt)
            {
                if (!CanWriteSettingFromCustomEvent(args[0]))
                    return;
                item.SetValueNoType(args[1], ModInstanceList.McMcInstanceSelected?.PathInstance);
            }
            if (args.Length == 2)
                HintService.Hint(Lang.Text("Event.Setting.Written", args[0], args[1]), HintType.Success);
        }
        private static bool CanWriteSettingFromCustomEvent(string key)
        {
            if (!SecuritySensitiveSettingKeys.Contains(key))
                return true;
            ModBase.Log($"[Control] 已阻止自定义事件写入高危设置：{key}", ModBase.LogLevel.Developer);
            HintService.Hint(Lang.Text("Event.Safety.DangerousSettingBlocked", key), HintType.Error);
            return false;
        }

        /// <summary>
        /// 写入 / 修改自定义变量。参数：VariableName|Value。
        /// </summary>
        private static void _WriteVariable(string arg, EventType type)
        {
            var args = SplitArgs(arg);
            if (args.Length == 1)
                throw new ArgumentException(Lang.Text("Event.Error.MissingArgs", type.ToString(), "VariableName|Value"));
            States.CustomVariables[args[0]] = args[1];
            States.CustomVariables = States.CustomVariables; // 触发 ConfigPropertyChanged
            if (args.Length == 2)
                HintService.Hint(Lang.Text("Event.Variable.Written", args[0], args[1]), HintType.Success);
        }

        /// <summary>
        /// 打开帮助或自定义主页页面
        /// </summary>
        private static void _OpenHelp(string arg, EventType type)
        {
            var args = SplitArgs(arg);
            if (!Uri.TryCreate(args[0], UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ModBase.OpenWebsite("https://docs.pclc.cc/ce");
                return;
            }

            ModBase.RunInThread(() =>
            {
                try
                {
                    var actualPaths = GetAbsoluteUrls(args[0], type);
                    var location = actualPaths[0];
                    var content = PageHelpDetail.LoadContent(location);
                    ModBase.RunInUiWait(() =>
                    {
                        var page = new PageHelpDetail(content);
                        ModMain.frmMain.PageChange(new FormMain.PageStackData
                        {
                            page = FormMain.PageType.HelpDetail,
                            helpPage = page
                        });
                    });
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        Lang.Text("Event.Error.ExecutionFailed", type, arg),
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Event.Error.ExecutionFailed", type, arg));
                }
            });
        }

        public static string[] GetAbsoluteUrls(string relativeUrl, EventType type)
        {
            if (type == EventType.OpenHelp &&
                Uri.TryCreate(relativeUrl, UriKind.Absolute, out var remoteUri) &&
                (remoteUri.Scheme == Uri.UriSchemeHttp || remoteUri.Scheme == Uri.UriSchemeHttps))
            {
                if (ModBase.RunInUi()) throw new Exception("能打开联网帮助页面的 MyListItem 必须手动设置 Title、Info 属性！");

                // 获取文件名
                string rawFileName;
                try
                {
                    rawFileName = Uri.UnescapeDataString(Path.GetFileName(remoteUri.AbsolutePath));
                    if (!Path.GetExtension(rawFileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
                        throw new Exception("未指向 .json 后缀的文件");
                    if (rawFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        throw new Exception("文件名包含非法字符");
                }
                catch (Exception e)
                {
                    throw new Exception("联网帮助页面须指向一个帮助 JSON 文件，并在同路径下包含相应 XAML 文件！\n" +
                                        "例如：\n" +
                                        " - https://www.baidu.com/test.json（填写这个路径）\n" +
                                        " - https://www.baidu.com/test.xaml（同时也需要包含这个文件）", e);
                }

                // 下载文件
                var tempFolder = ModMain.RequestTaskTempFolder();
                var jsonPath = Path.Combine(tempFolder, rawFileName);
                var xamlPath = Path.ChangeExtension(jsonPath, ".xaml");
                var xamlUri = new UriBuilder(remoteUri)
                {
                    Path = Path.ChangeExtension(remoteUri.AbsolutePath, ".xaml").Replace('\\', '/')
                }.Uri.AbsoluteUri;
                ModBase.Log($"[Control] 转换网络帮助资源：{relativeUrl} -> {jsonPath}");
                try
                {
                    Task.WhenAll(
                        ModNet.NetDownloadByClient(remoteUri.AbsoluteUri, jsonPath),
                        ModNet.NetDownloadByClient(xamlUri, xamlPath)
                    ).GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    throw new Exception("下载指定的文件失败！\n" +
                                        "注意，联网帮助页面须指向一个帮助 JSON 文件，并在同路径下包含相应 XAML 文件！\n" +
                                        "例如：\n" +
                                        " - https://www.baidu.com/test.json（填写这个路径）\n" +
                                        " - https://www.baidu.com/test.xaml（同时也需要包含这个文件）", e);
                }

                relativeUrl = jsonPath;
            }

            // 确认路径
            relativeUrl = relativeUrl.Replace('/', Path.DirectorySeparatorChar);
            var pclDir = Path.Combine(Basics.ExecutableDirectory, "PCL");

            if (Path.IsPathFullyQualified(relativeUrl)) // 绝对路径
            {
                ModBase.Log($"[Control] 自定义事件中由绝对路径 {type}: {relativeUrl}");
                return [relativeUrl, pclDir];
            }
            if (File.Exists(Path.Combine(pclDir, relativeUrl))) // 相对 PCL 文件夹的路径
            {
                var fullPath = Path.Combine(pclDir, relativeUrl);
                var resolved = Path.GetFullPath(fullPath);
                if (!resolved.StartsWith(pclDir, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException(Lang.Text("Event.Error.FileNotFound", relativeUrl));
                ModBase.Log($"[Control] 自定义事件中由相对 PCL 文件夹的路径 {type}: {fullPath}");
                return [fullPath, pclDir];
            }
            if (type is EventType.OpenFile or EventType.ExecuteCommand) // 直接使用原有路径启动程序
            {
                ModBase.Log($"[Control] 自定义事件中直接 {type}: {relativeUrl}");
                return [relativeUrl, pclDir];
            }
            throw new FileNotFoundException(Lang.Text("Event.Error.FileNotFound", relativeUrl), relativeUrl);
        }

        /// <summary>
        /// 安全确认对话框。已由用户勾选"不再询问"时跳过。
        /// </summary>
        private static bool EventSafetyConfirm(string message)
        {
            if (States.Hint.HomepageCommand)
                return true;

            return ModMain.MyMsgBox(
                Lang.Text("Event.Safety.ConfirmMessage", message),
                Lang.Text("Event.Safety.Title"),
                Lang.Text("Common.Action.Continue"),
                Lang.Text("Event.Safety.ContinueAlways"),
                Lang.Text("Common.Action.Cancel")) switch
            {
                1 => true,
                2 => States.Hint.HomepageCommand = true,
                _ => false,
            };
        }
    }
}
