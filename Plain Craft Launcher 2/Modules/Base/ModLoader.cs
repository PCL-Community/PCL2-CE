using Microsoft.VisualBasic.CompilerServices;
using PCL.Core.App;
using PCL.Core.Utils;
using System.Collections;
using System.IO;
using System.Windows.Shell;
using PCL.Network;
using PCL.Network.Loaders;

using PCL.Core.App.Localization;
namespace PCL;

public static class ModLoader
{
    public enum LoaderFolderRunType
    {
        RunOnUpdated,
        ForceRun,
        UpdateOnly
    }

    // 任务栏进度条
    public static ModBase.SafeList<LoaderBase> loaderTaskbar = new();
    public static double loaderTaskbarProgress; // 平滑后的进度
    private static TaskbarItemProgressState loaderTaskbarProgressLast = TaskbarItemProgressState.None;

    // 文件夹刷新类委托
    private static readonly Dictionary<LoaderBase, LoaderFolderDictionaryEntry> loaderFolderDictionary = new();

    public static void LoaderTaskbarAdd<T>(LoaderCombo<T> loader)
    {
        if (ModMain.frmSpeedLeft is not null)
            ModMain.frmSpeedLeft.TaskRemove(loader);
        loaderTaskbar.Add(loader);
        ModBase.Log($"[Taskbar] {loader.name} 已加入任务列表");
    }

    public static void LoaderTaskbarProgressRefresh()
    {
        try
        {
            TaskbarItemProgressState newState;
            var newProgress = LoaderTaskbarProgressGet();
            // 若单个任务已中止，或全部任务已完成，则刷新并移除
            foreach (var Task in loaderTaskbar)
                if (Task.State != ModBase.LoadState.Loading)
                {
                    ModMain.frmSpeedLeft?.TaskRefresh(Task);
                    loaderTaskbar.Remove(Task);
                    ModBase.Log($"[Taskbar] {Task.name} 已移出任务列表");
                }

            // 更新平滑后的进度
            if (newProgress <= 0d || newProgress >= 1d || loaderTaskbarProgress > newProgress)
                loaderTaskbarProgress = newProgress;
            else
                loaderTaskbarProgress = loaderTaskbarProgress * 0.9d + newProgress * 0.1d;
            ModBase.RunInUi(() => ModMain.frmMain.BtnExtraDownload.Progress = loaderTaskbarProgress);
            // 更新任务栏信息
            if (!loaderTaskbar.Any() || loaderTaskbarProgress == 1d)
            {
                newState = TaskbarItemProgressState.None;
            }
            else if (loaderTaskbarProgress < 0.015d)
            {
                newState = TaskbarItemProgressState.Indeterminate;
            }
            else
            {
                newState = TaskbarItemProgressState.Normal;
                ModMain.frmMain.TaskbarItemInfo.ProgressValue = loaderTaskbarProgress;
            }

            if (loaderTaskbarProgressLast != newState)
            {
                loaderTaskbarProgressLast = newState;
                ModMain.frmMain.TaskbarItemInfo.ProgressState = newState;
                ModMain.frmMain.BtnExtraDownload.ShowRefresh();
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "刷新任务栏进度显示失败",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Application.Loader.Error.OperationFailed"));
        }
    }

    public static double LoaderTaskbarProgressGet()
    {
        try
        {
            if (!loaderTaskbar.Any())
                return 1d;

            return ModBase.MathClamp(
                loaderTaskbar.Select(l => l.Progress).Average(),
                0,
                1
            );
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "获取任务栏进度出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Application.Loader.Error.OperationFailed"));
            return 0.5d;
        }
    }

    /// <summary>
    ///     执行以文件夹检测作为输入的加载器。加载器需以文件夹路径为输入值。
    ///     返回是否执行了加载器。
    /// </summary>
    /// <param name="extraPath">用于检查文件夹修改的额外路径。该路径不会传入加载器。</param>
    /// <param name="loaderInput">如果不想要文件夹路径为输入值，则传入期望数据</param>
    public static bool LoaderFolderRun(LoaderBase loader, string folderPath, LoaderFolderRunType type, int maxDepth = 0,
        string extraPath = "", bool waitForExit = false, object loaderInput = null)
    {
        DirectoryInfo folderInfo;
        var value = new LoaderFolderDictionaryEntry { folderPath = folderPath + extraPath, lastCheckTime = default };
        try
        {
            // 获取数据
            folderInfo = new DirectoryInfo(folderPath + extraPath);
            value.lastCheckTime = folderInfo.Exists ? GetActualLastWriteTimeUtc(folderInfo, maxDepth) : null;
            // 如果已经检查过，则跳过
            if (type == LoaderFolderRunType.RunOnUpdated && loaderFolderDictionary.ContainsKey(loader))
            {
                if (folderInfo.Exists)
                {
                    if (loaderFolderDictionary[loader].lastCheckTime is not null &&
                        value.Equals(loaderFolderDictionary[loader]))
                        return false;
                }
                else if (loaderFolderDictionary[loader].lastCheckTime is null)
                {
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "文件夹加载器启动检测出错");
        }

        // 写入检查数据
        loaderFolderDictionary[loader] = value;
        // 开始检查
        if (type == LoaderFolderRunType.UpdateOnly)
            return false;
        if (waitForExit)
            loader.WaitForExit(loaderInput ?? folderPath, isForceRestart: true);
        else
            loader.Start(loaderInput ?? folderPath, true);
        return true;
    }

    private static DateTime GetActualLastWriteTimeUtc(DirectoryInfo folderInfo, int maxDepth)
    {
        var time = folderInfo.LastWriteTimeUtc;
        if (maxDepth > 0)
            foreach (var Folder in folderInfo.EnumerateDirectories())
            {
                var folderTime = GetActualLastWriteTimeUtc(Folder, maxDepth - 1);
                if (folderTime > time)
                    time = folderTime;
            }

        return time;
    }

    // 各类加载器
    /// <summary>
    ///     加载器的统一基类。
    /// </summary>
    public abstract class LoaderBase : ILoadingTrigger
    {
        public delegate void OnStateChangedThreadEventHandler(LoaderBase loader, ModBase.LoadState newState,
            ModBase.LoadState oldState);

        public delegate void OnStateChangedUiEventHandler(LoaderBase loader, ModBase.LoadState newState,
            ModBase.LoadState oldState);

        public delegate void PreviewFinishEventHandler(LoaderBase loader);

        // 等待结束
        public const string waitForExitTimeoutMessage = "等待加载器执行超时。";

        /// <summary>
        ///     用于状态改变检测的同步锁。
        /// </summary>
        public readonly object lockState = new();



        /// <summary>
        ///     使用 LoaderCombo 加载时，该任务是否会阻碍后续任务的进行。
        /// </summary>
        public bool block = true;

        public bool hasOnStateChangedThread;

        /// <summary>
        ///     当前加载器是否由 IsForceRestart 强制调起。
        ///     这个属性自身不会干任何事，而是提供给加载器执行的函数，使得加载器调用另一个加载器时，可以继承强制重启属性。
        /// </summary>
        public bool isForceRestarting;

        /// <summary>
        ///     加载器的名称。
        /// </summary>
        public string name;

        /// <summary>
        ///     父加载器。
        /// </summary>
        public LoaderBase parent;

        /// <summary>
        ///     该加载器是否显示在列表中。
        /// </summary>
        public bool show = true;

        // 基础属性
        /// <summary>
        ///     加载器的标识编号。
        /// </summary>
        public int Uuid = ModBase.GetUuid();

        public LoaderBase()
        {
            name = "未命名任务 " + Uuid + "#";
        }

        /// <summary>
        ///     最上级的加载器。
        /// </summary>
        public LoaderBase RealParent
        {
            get
            {
                LoaderBase realParentRet = default;
                try
                {
                    realParentRet = parent;
                    while (realParentRet is not null && realParentRet.parent is not null)
                        realParentRet = realParentRet.parent;
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        "获取父加载器失败（" + name + "）",
                        ModBase.LogLevel.Feedback,
                        userSummary: Lang.Text("Application.Loader.Error.OperationFailed"));
                    return null;
                }

                return realParentRet;
            }
        }

        /// <summary>
        ///     简易的在 UI 线程添加触发事件的方式。主要用于在新建 Loader 时直接使用 With 绑定事件，以及进行老代码兼容。
        /// </summary>
        public Action<LoaderBase> OnStateChanged
        {
            set { OnStateChangedUi += (loader, newState, oldState) => value(loader); }
        }

        // 状态监控
        /// <summary>
        ///     加载器的状态。
        /// </summary>
        public ModBase.LoadState State
        {
            get => field;
            set
            {
                if (field == value)
                    return;
                var oldState = field;
                if (value == ModBase.LoadState.Finished && Config.Debug.AddRandomDelay)
                    Thread.Sleep(RandomUtils.NextInt(100, 2000));
                field = value;
                ModBase.Log("[Loader] 加载器 " + name + " 状态改变：" + ModBase.GetStringFromEnum(value));
                // 实现 ILoadingTrigger 接口与 OnStateChanged 回调
                ModBase.RunInUi(() =>
                {
                    switch (value)
                    {
                        case ModBase.LoadState.Loading:
                        {
                            LoadingState = MyLoading.MyLoadingState.Run;
                            break;
                        }
                        case ModBase.LoadState.Failed:
                        {
                            LoadingState = MyLoading.MyLoadingState.Error;
                            break;
                        }

                        default:
                        {
                            LoadingState = MyLoading.MyLoadingState.Stop;
                            break;
                        }
                    }

                    OnStateChangedUi?.Invoke(this, value, oldState);
                });
                if (hasOnStateChangedThread)
                    ModBase.RunInThread(() => OnStateChangedThread?.Invoke(this, value, oldState));
            }
        } = ModBase.LoadState.Waiting;

        /// <summary>
        ///     若加载器出错，可提供给外部参考的异常。
        /// </summary>
        public Exception Error { get; set; }

        // 进度监控
        /// <summary>
        ///     加载器的执行进度，为 0 至 1 的小数。
        /// </summary>
        public virtual double Progress
        {
            get
            {
                switch (State)
                {
                    case ModBase.LoadState.Waiting:
                    {
                        return 0d;
                    }
                    case ModBase.LoadState.Loading:
                    {
                        return field == -1 ? 0.02d : field;
                    }

                    default:
                    {
                        return 1d;
                    }
                }
            }
            set
            {
                if (field == value)
                    return;
                var oldValue = field;
                field = value;
                ProgressChanged?.Invoke(value, oldValue);
            }
        } = -1;

        /// <summary>
        ///     计算总进度时的权重。它应该为预计时间（秒）。
        /// </summary>
        public double ProgressWeight { get; set; } = 1d;

        public bool IsLoader { get; } = true;

        public MyLoading.MyLoadingState LoadingState
        {
            get => field;
            set
            {
                if (field == value)
                    return;
                var oldState = field;
                field = value;
                LoadingStateChanged?.Invoke(value, oldState);
            }
        } = MyLoading.MyLoadingState.Stop;

        public event ILoadingTrigger.LoadingStateChangedEventHandler? LoadingStateChanged;
        public event ILoadingTrigger.ProgressChangedEventHandler? ProgressChanged;

        public virtual void InitParent(LoaderBase parent)
        {
            this.parent = parent;
        }

        // 事件

        /// <summary>
        ///     当状态改变时，在工作线程触发代码。在添加事件后，必须将 HasOnStateChangedThread 设为 True。
        /// </summary>
        public event OnStateChangedThreadEventHandler? OnStateChangedThread;

        /// <summary>
        ///     当状态改变时，在 UI 线程触发代码。
        /// </summary>
        public event OnStateChangedUiEventHandler? OnStateChangedUi;

        /// <summary>
        ///     在加载器目标事件执行完成，加载器状态即将变为 Finish 时调用。可以视为扩展加载器目标事件。
        /// </summary>
        public event PreviewFinishEventHandler? PreviewFinish;

        protected void RaisePreviewFinish()
        {
            PreviewFinish?.Invoke(this);
        }

        // 状态变化
        public abstract void Start(object? input = null, bool isForceRestart = false);
        public abstract void Abort();

        /// <summary>
        ///     无限期地等待加载器完成，直到结束或抛出异常。若加载器尚未开始，则会开始执行。
        /// </summary>
        public void WaitForExit(object input = null, LoaderBase loaderToSyncProgress = null,
            bool isForceRestart = false)
        {
            Start(input, isForceRestart);
            while (State == ModBase.LoadState.Loading)
            {
                if (loaderToSyncProgress is not null)
                    loaderToSyncProgress.Progress = Progress;
                Thread.Sleep(10);
            }

            if (State == ModBase.LoadState.Finished)
            {
            }
            else if (State == ModBase.LoadState.Aborted)
            {
                throw new ThreadInterruptedException("加载器执行已中断。");
            }
            else if (Error is null)
            {
                throw new InvalidOperationException("未知错误！");
            }
            else
            {
                throw Error;
            }
        }

        /// <summary>
        ///     等待加载器完成，直到结束、抛出异常或超时。若加载器尚未开始，则会开始执行。
        /// </summary>
        /// <param name="timeout">等待的超时时间，以毫秒为单位。</param>
        /// <param name="timeoutMessage">若执行超时，将会抛出的异常信息。</param>
        public void WaitForExitTime(int timeout, object input = null, string timeoutMessage = waitForExitTimeoutMessage,
            object loaderToSyncProgress = null, bool isForceRestart = false)
        {
            Start(input, isForceRestart);
            while (State == ModBase.LoadState.Loading)
            {
                if (loaderToSyncProgress is not null)
                    ((dynamic)loaderToSyncProgress).Progress = Progress;
                Thread.Sleep(10);
                timeout -= 10;
                if (timeout < 0)
                    throw new TimeoutException(timeoutMessage);
            }

            if (State == ModBase.LoadState.Finished)
            {
            }
            else if (State == ModBase.LoadState.Aborted)
            {
                throw new ThreadInterruptedException("加载器执行已中断。");
            }
            else if (Error is null)
            {
                throw new InvalidOperationException("未知错误！");
            }
            else
            {
                throw Error;
            }
        }

        // 相同重载
        public override bool Equals(object obj)
        {
            var @base = obj as LoaderBase;
            return @base is not null && Uuid == @base.Uuid;
        }
    }

    // 说实话，我真的觉得 C# 应该学学 VB 的那种近乎 Java 泛型擦除的兼容性，省掉一堆麻烦
    public abstract class LoaderTask : LoaderBase
    {
        /// <summary>
        ///     上次完成加载时的时间。
        /// </summary>
        public long lastFinishedTime;

        /// <summary>
        ///     最后一次运行加载器的线程。可能为 Nothing，或线程已结束。
        /// </summary>
        public Task? lastRunningTask;

        /// <summary>
        ///     在输入相同时使用原有结果的超时，单位为毫秒。
        /// </summary>
        public int reloadTimeout = -1;

        // 状态指示
        /// <summary>
        ///     当前执行线程是否应当中断。只应用在加载器的工作线程中判断，不可跨线程调用。
        /// </summary>
        public bool IsAborted => IsAbortedWithThread(Task.CurrentId ?? -1);

        /// <summary>
        ///     当前执行线程是否应当中断。需要手动提供加载器线程，用于需要跨线程检查的情况。
        /// </summary>
        public bool IsAbortedWithThread(int compareTaskId)
        {
            return lastRunningTask is null || compareTaskId != lastRunningTask.Id ||
                   State == ModBase.LoadState.Aborted;
        }

        public abstract bool ShouldStart(ref object? input, bool isForceRestart = false, bool ignoreReloadTimeout = false);

        // 装箱！装箱！装箱圣地！
        public abstract object? StartGetInputNoType(object? input = null, Func<object>? inputDelegate = null);

    }

    /// <summary>
    ///     用于异步执行并监控单一函数的加载器。
    /// </summary>
    public class LoaderTask<InputType, OutputType> : LoaderTask
    {
        // 输入输出
        public InputType input;
        protected internal Func<InputType?>? inputDelegate;

        // 执行事件
        protected internal Action<LoaderTask<InputType, OutputType>> loadDelegate;
        public OutputType output = default;

        private CancellationTokenSource? cancelToken;

        public CancellationToken AbortedToken => cancelToken?.Token ?? new CancellationToken(true);

        // 线程设定
        protected internal ThreadPriority threadPriority;

        public LoaderTask(string name, Action<LoaderTask<InputType, OutputType>> loadDelegate,
            Func<InputType?>? inputDelegate = null, ThreadPriority priority = ThreadPriority.Normal)
        {
            this.name = name;
            this.loadDelegate = loadDelegate;
            this.inputDelegate = inputDelegate;
        }

        // 获取输入
        public InputType? StartGetInput(InputType? input = default, Func<InputType?>? inputDelegate = null) // InputDelegate 参数存在匿名调用
        {
            inputDelegate ??= this.inputDelegate;
            // 按照龙猫的逻辑，此处将 input 与默认值直接进行等价比较，若相等则认为 input 未传入具体值，而调用 inputDelegate 获取
            // 这种逻辑未考虑值类型恰好传入 default 值 (如 double 传了 0.0) 的情况，这是一个陷阱，可能会产生 undefined behavior
            if (EqualityComparer<InputType>.Default.Equals(input, default) && inputDelegate is not null)
                ModBase.RunInUiWait(() => input = inputDelegate());
            return input;
        }

        public override object? StartGetInputNoType(object? input = null, Func<object?>? inputDelegate = null)
        {
            return StartGetInput(input is null ? default : (InputType?)input, inputDelegate is null ? null : () => (InputType?)inputDelegate());
        }

        // 代码执行
        public override bool ShouldStart(ref object? input, bool isForceRestart = false, bool ignoreReloadTimeout = false)
        {
            // 获取输入
            try
            {
                input = StartGetInput(Conversions.ToGenericParameter<InputType>(input));
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    "加载输入获取失败（" + name + "）",
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Application.Loader.Error.OperationFailed"));
                Error = ex;
                lock (lockState)
                {
                    State = ModBase.LoadState.Failed;
                }
            }

            // 检验输入以确定情况
            if (isForceRestart)
                return true; // 强制要求重启
            if (input is null != this.input is null || (input is not null && !input.Equals(this.input)))
                return true; // 输入不同
            if ((State == ModBase.LoadState.Loading || State == ModBase.LoadState.Finished) && (ignoreReloadTimeout ||
                    reloadTimeout == -1 || lastFinishedTime == 0L ||
                    TimeUtils.GetTimeTick() - lastFinishedTime < reloadTimeout)) // 正在加载或已结束
                // 没有超时
                return false; // 则不重试

            return true;
            // 需要开始
        }

        public override void Start(object input = null, bool isForceRestart = false)
        {
            // 确认是否开始加载
            if (ShouldStart(ref input, isForceRestart))
            {
                // 输入不同或失败，开始加载
                if (State == ModBase.LoadState.Loading)
                    TriggerThreadAbort();
                this.input = Conversions.ToGenericParameter<InputType>(input);
                lock (lockState)
                {
                    State = ModBase.LoadState.Loading;
                    Progress = -1;
                }
            }
            else return;

            // 如果线程是因为判断到 IsAborted 而提前中止，则代表已有新线程被重启，此时不应当改为 Aborted
            // 如果线程是在没有 IsAborted 时手动引发了 ThreadInterruptedException，则代表没有重启线程，这通常代表用户手动取消，应当改为 Aborted
            lastRunningTask = Task.Run(() =>
            {
                try
                {
                    isForceRestarting = isForceRestart;
                    if (ModBase.modeDebug)
                        ModBase.Log(
                            $"[Loader] 加载线程 {name} ({Task.CurrentId}) 已{(isForceRestarting ? "强制" : "")}启动");
                    loadDelegate(this);
                    if (IsAborted)
                    {
                        ModBase.Log(
                            $"[Loader] 加载线程 {name} ({Task.CurrentId}) 已中断但线程正常运行至结束，输出被弃用（最新线程：{(lastRunningTask is null ? -1 : lastRunningTask.Id)}）",
                            ModBase.LogLevel.Developer);
                        return;
                    }

                    if (ModBase.modeDebug)
                        ModBase.Log($"[Loader] 加载线程 {name} ({Task.CurrentId}) 已完成");
                    RaisePreviewFinish();
                    State = ModBase.LoadState.Finished;
                    lastFinishedTime = TimeUtils.GetTimeTick();
                }
                catch (ModBase.CancelledException ex)
                {
                    if (ModBase.modeDebug)
                        ModBase.Log(ex,
                            $"加载线程 {name} ({Task.CurrentId}) 已触发取消中断，已完成 {Math.Round(Progress * 100d)}%");
                    if (!IsAborted) State = ModBase.LoadState.Aborted;
                }
                catch (ThreadInterruptedException ex)
                {
                    if (ModBase.modeDebug)
                        ModBase.Log(ex,
                            $"加载线程 {name} ({Task.CurrentId}) 已触发线程中断，已完成 {Math.Round(Progress * 100d)}%");
                    if (!IsAborted) State = ModBase.LoadState.Aborted;
                }
                catch (Exception ex)
                {
                    if (IsAborted) return;
                    ModBase.Log(ex,
                        $"加载线程 {name} ({Task.CurrentId}) 出错，已完成 {Math.Round(Progress * 100d)}%",
                        ModBase.LogLevel.Developer);
                    Error = ex;
                    State = ModBase.LoadState.Failed;
                }
            }, (cancelToken ??= new CancellationTokenSource()).Token); // 未中断，本次输出有效
            // LastRunningTask.Start(); // 不能使用 RunInNewThread，否则在函数返回前线程就会运行完，导致误判 IsAborted
        }

        public override void Abort()
        {
            if (State != ModBase.LoadState.Loading)
                return;
            lock (lockState)
            {
                State = ModBase.LoadState.Aborted;
            }

            TriggerThreadAbort();
        }

        private void TriggerThreadAbort()
        {
            if (lastRunningTask is null) return;
            if (ModBase.modeDebug) ModBase.Log($"[Loader] 加载线程 {name} ({lastRunningTask.Id}) 已中断");
            if (!lastRunningTask.IsCompleted) cancelToken?.Cancel();
            lastRunningTask = null;
            cancelToken = null;
        }
    }

    /// <summary>
    ///     支持多个加载器连续运作的复合加载器。
    /// </summary>
    public class LoaderCombo : LoaderBase
    {
        public object? input;

        public List<LoaderBase> loaders = new();

        public LoaderCombo(string name, IEnumerable<LoaderBase> loaders)
        {
            this.loaders.Clear();
            foreach (var Loader in loaders)
                if (Loader is not null)
                {
                    this.loaders.Add(Loader);
                    Loader.OnStateChangedThread += SubTaskStateChanged;
                    Loader.hasOnStateChangedThread = true;
                }

            InitParent(null);
            this.name = name;
        }

        public override double Progress
        {
            get
            {
                switch (State)
                {
                    case ModBase.LoadState.Waiting:
                    {
                        return 0d;
                    }
                    case ModBase.LoadState.Loading:
                    {
                        var total = 0d;
                        var finished = 0d;
                        foreach (var Loader in loaders)
                        {
                            total += Loader.ProgressWeight;
                            finished += Loader.ProgressWeight * Loader.Progress;
                        }

                        if (total == 0d)
                            return 0d;
                        return finished / total;
                    }

                    default:
                    {
                        return 1d;
                    }
                }
            }
            set => throw new Exception("多重加载器不支持设置进度");
        }

        public override void InitParent(LoaderBase parent)
        {
            this.parent = parent;
            foreach (var Loader in loaders)
                Loader.InitParent(this);
        }

        public override void Start(object input = null, bool isForceRestart = false)
        {
            isForceRestarting = isForceRestart;
            lock (lockState)
            {
                if (State == ModBase.LoadState.Loading) return;

                State = ModBase.LoadState.Loading;
            }

            // 启动加载
            this.input = input;
            if (isForceRestart)
                foreach (var Loader in loaders)
                    Loader.State = ModBase.LoadState.Waiting;
            ModBase.RunInThread(Update);
        }

        public override void Abort()
        {
            lock (lockState)
            {
                if (State != ModBase.LoadState.Loading && State != ModBase.LoadState.Waiting)
                    return;
            }

            foreach (var Loader in loaders) Loader.Abort();

            lock (lockState)
            {
                if (State == ModBase.LoadState.Loading || State == ModBase.LoadState.Waiting)
                    State = ModBase.LoadState.Aborted;
            }
        }

        /// <summary>
        ///     子任务状态变更。
        /// </summary>
        private void SubTaskStateChanged(LoaderBase loader, ModBase.LoadState newState, ModBase.LoadState oldState)
        {
            switch (newState)
            {
                case ModBase.LoadState.Loading:
                {
                    break;
                }
                // 开始，啥都不干
                case ModBase.LoadState.Waiting:
                {
                    break;
                }
                // 子加载器可能由于外部输入改变而暂时变为 Waiting，之后会立即重新启动
                // 所以啥都不干就行
                case ModBase.LoadState.Finished:
                {
                    // 正常结束，触发刷新
                    Update();
                    break;
                }
                case ModBase.LoadState.Aborted:
                {
                    // 被中断，这个任务也中断
                    Abort();
                    break;
                }

                default:
                {
                    // 完蛋，出错了
                    lock (lockState)
                    {
                        if (State >= ModBase.LoadState.Finished)
                            return;
                        Error = new Exception(loader.name + "失败", loader.Error);
                        State = loader.State;
                    }

                    foreach (var currentLoader in loaders)
                    {
                        loader = currentLoader;
                        loader.Abort();
                    }

                    ModMain.frmMain.BtnExtraDownload.ShowRefresh();
                    return;
                }
            }
        }

        /// <summary>
        ///     触发一次更新，以启动新加载器或完成。
        /// </summary>
        private void Update()
        {
            if (State == ModBase.LoadState.Finished
                || State == ModBase.LoadState.Failed
                || State == ModBase.LoadState.Aborted)
                return;

            var isFinished = true;
            var blocked = false;
            object input = this.input;

            foreach (var loader in loaders)
                switch (loader.State)
                {
                    case ModBase.LoadState.Finished:
                    {
                        if (loader is LoaderTask task)
                        {
                            var genericArg = task.GetType().GenericTypeArguments.FirstOrDefault();
                            var shouldInput = input is not null && genericArg == input.GetType()
                                ? input
                                : null;

                            if (task.ShouldStart(ref shouldInput, false, true))
                            {
                                ModBase.Log("[Loader] 由于输入条件变更，重启已完成的加载器 " + loader.name);
                                goto Restart;
                            }

                            input = ((dynamic)loader).output; // 何意味啊，没法匹配 LoaderTask<,>
                        }

                        if (loader.block && !isFinished)
                            blocked = true;

                        break;
                    }

                    case ModBase.LoadState.Loading:
                    {
                        if (loader is LoaderTask task)
                        {
                            var genericArg = loader.GetType().GenericTypeArguments.FirstOrDefault();
                            var shouldInput = input is not null && genericArg == input.GetType()
                                ? input
                                : null;
                            if (task.ShouldStart(ref shouldInput, false, true))
                            {
                                ModBase.Log($"[Loader] 由于输入条件变更，重启进行中的加载器 {loader.name}",
                                    ModBase.LogLevel.Developer);
                                goto Restart;
                            }
                        }

                        isFinished = false;
                        blocked = true;
                        break;
                    }

                    default:

                        Restart:

                        isFinished = false;

                        if (blocked)
                            continue;

                        if (input is not null)
                        {
                            switch (loader)
                            {
                                case LoaderTask:
                                case LoaderCombo:
                                    var genericArg = loader.GetType().GenericTypeArguments.FirstOrDefault();

                                    loader.Start(
                                        genericArg == input.GetType() ? input : null,
                                        isForceRestarting);
                                    break;
                                case not null:
                                    loader.Start(
                                        input is List<DownloadFile> ? input : null,
                                        isForceRestarting);
                                    break;
                                default:
                                    throw new Exception($"未知的加载器类型（{loader?.GetType()}）");
                            }
                        }
                        else
                        {
                            loader.Start(isForceRestart: isForceRestarting);
                        }

                        if (loader.block)
                            blocked = true;

                        break;
                }

            if (isFinished)
            {
                RaisePreviewFinish();
                State = ModBase.LoadState.Finished;
                ModMain.frmMain.BtnExtraDownload.ShowRefresh();
            }
        }

        /// <summary>
        ///     获得最底层的，应被显示给用户的加载器列表，并追加于 List。
        /// </summary>
        public static void GetLoaderList(LoaderCombo loader, ref List<LoaderBase> list, bool requireShow = true)
        {
            foreach (var SubLoader in loader.loaders)
            {
                if (SubLoader.show || !requireShow)
                    list.Add(SubLoader);
                if (SubLoader is LoaderCombo combo)
                    GetLoaderList(combo, ref list);
            }
        }

        /// <summary>
        ///     获得最底层的，应被显示给用户的加载器列表，并追加于 List。
        /// </summary>
        public void GetLoaderList(ref List<LoaderBase> list, bool requireShow = true)
        {
            GetLoaderList(this, ref list, requireShow);
        }

        /// <summary>
        ///     获得最底层的，应被显示给用户的加载器列表。
        /// </summary>
        public List<LoaderBase> GetLoaderList(bool requireShow = true)
        {
            var list = new List<LoaderBase>();
            GetLoaderList(ref list, requireShow);
            return list;
        }
    }

    /// <summary>
    ///     支持多个加载器连续运作的复合加载器（泛型版本）。
    /// </summary>
    public class LoaderCombo<InputType> : LoaderCombo
    {
        public new InputType typedInput;

        public LoaderCombo(string name, IEnumerable<LoaderBase> loaders) : base(name, loaders) { }

        public override void Start(object input = null, bool isForceRestart = false)
        {
            this.typedInput = Conversions.ToGenericParameter<InputType>(input);
            base.Start(this.typedInput, isForceRestart);
        }
    }

    private struct LoaderFolderDictionaryEntry
    {
        public DateTime? lastCheckTime;
        public string folderPath;

        public override bool Equals(object obj)
        {
            if (obj is not LoaderFolderDictionaryEntry)
                return false;
            var entry = (LoaderFolderDictionaryEntry)obj;
            return EqualityComparer<DateTime?>.Default.Equals(lastCheckTime, entry.lastCheckTime) &&
                   (folderPath ?? "") == (entry.folderPath ?? "");
        }
    }
}
