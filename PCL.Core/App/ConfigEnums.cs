namespace PCL.Core.App;

/// <summary>
/// 联机协议偏好
/// </summary>
public enum LinkProtocolPreference
{
    Tcp,
    Udp
}

/// <summary>
/// 主题模式（亮/暗/系统）
/// </summary>
public enum ColorMode
{
    Light = 0,
    Dark = 1,
    System = 2
}

/// <summary>
/// 配色主题
/// </summary>
public enum ColorTheme
{
    SkyBlue = 0,
    CatBlue = 1,
    DeathBlue = 2,
    HmclBlue = 3
}

/// <summary>
/// 更新通道
/// </summary>
public enum UpdateChannel
{
    Release = 0,
    Beta = 1,
    Dev = 2
}

/// <summary>
/// 游戏窗口大小模式
/// </summary>
public enum GameWindowSizeMode
{
    Fullscreen = 0,
    Default = 1,
    Launcher = 2,
    Custom = 3,
    Maximized = 4
}

/// <summary>
/// 游戏进程优先级
/// </summary>
public enum GameProcessPriority
{
    AboveNormal = 0,
    Normal = 1,
    BelowNormal = 2,
    High = 3,
    RealTime = 4
}

/// <summary>
/// 删除实例时的个人文件备份模式。
/// </summary>
public enum PersonalFilesBackupMode
{
    Disabled = 0,
    AskEveryTime = 1,
    Always = 2
}

/// <summary>
/// 游戏启动后启动器可见性
/// </summary>
public enum LauncherVisibility
{
    ExitImmediately = 0,
    ObsoleteCaseDoNotUse = 1,
    HideAndExit = 2,
    HideAndReopen = 3,
    MinimizeAndReopen = 4,
    DoNothing = 5
}

/// <summary>
/// JVM 优先 IP 栈类型
/// </summary>
public enum JvmPreferredIpStack
{
    PreferV4 = 0,
    Default = 1,
    PreferV6 = 2
}

/// <summary>
/// 联机中继行为
/// </summary>
public enum LinkRelayBehavior
{
    Default = 0,
    ForceRelay = 1
}

/// <summary>
/// 启动器更新行为
/// </summary>
public enum LauncherAutoUpdateBehavior
{
    DownloadAndInstall = 0,
    DownloadAndAnnounce = 1,
    AnnounceOnly = 2,
    Disable = 3
}

public enum LauncherTitleType
{
    None = 0,
    Default = 1,
    Text = 2,
    Image = 3
}
