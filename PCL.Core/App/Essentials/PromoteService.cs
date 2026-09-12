using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using PCL.Core.App.IoC;
using PCL.Core.IO;
using PCL.Core.Logging;
using PCL.Core.Utils.OS;
using PCL.Core.Utils;

namespace PCL.Core.App.Essentials;

public delegate string? PromoteOperationFunction(string? arg);

/// <summary>
/// 标记一个方法，使其能够被提权进程调用，方法签名需符合 <see cref="PromoteOperationFunction"/>。
/// </summary>
/// <param name="name">提权操作名</param>
[DependencyCollector<PromoteOperationFunction>("promote", AttributeTargets.Method)]
[AttributeUsage(AttributeTargets.Method)]
public sealed class PromoteOperationAttribute(string name) : Attribute;

[LifecycleService(LifecycleState.BeforeLoading, Priority = -10)]
[LifecycleScope("promote", "提权服务", false)]
public sealed partial class PromoteService
{
    private static Process? _promoteProcess;
    private static NamedPipeServerStream? _promotePipeServer;
    
    private static readonly ConcurrentQueue<PromoteOperation> _PendingOperations = [];
    
    private readonly record struct PromoteOperation(string Command, Action<string?>? Callback, bool DetailLog);
    
    /// <summary>
    /// 提权进程是否正在运行。
    /// </summary>
    public static bool IsPromoteProcessRunning => _promoteProcess is not null;
    
    /// <summary>
    /// 当前进程是否是提权进程。
    /// </summary>
    public static bool IsCurrentProcessPromoted { get; private set; }
    
    private static string _GetPromotePipeName(int processId) => $"PCLCE_PM@{processId}";

    private static readonly Dictionary<string, PromoteOperationFunction> _OperationFunctions = new();

    /// <summary>
    /// 添加提权操作，仅在提权进程中有效。
    /// </summary>
    /// <param name="name">操作名</param>
    /// <param name="operation">操作实现，接收参数并返回结果，返回值会被自动压缩为单行</param>
    /// <returns>是否添加成功，若在主进程中调用或已存在相同操作名，则为 <c>false</c></returns>
    public static bool AddOperationFunction(string name, PromoteOperationFunction operation)
    {
        return IsCurrentProcessPromoted && _OperationFunctions.TryAdd(name, operation);
    }

    /// <summary>
    /// 添加自动将参数 JSON 反序列化的提权操作，仅在提权进程中有效。
    /// </summary>
    /// <param name="name">操作名</param>
    /// <param name="operation">操作实现，接收反序列化的并返回结果，返回值会被自动压缩为单行</param>
    /// <typeparam name="TValue">反序列化的目标类型</typeparam>
    /// <returns>是否添加成功，若在主进程中调用或已存在相同操作名，则为 <c>false</c></returns>
    public static bool AddJsonOperationFunction<TValue>(string name, Func<TValue?, string?> operation)
    {
        return AddOperationFunction(name, arg =>
        {
            if (arg is null) return OperationErrEmpty;
            var obj = JsonSerializer.Deserialize<TValue>(arg, JsonCompat.SerializerOptions);
            return operation(obj);
        });
    }
    
    private const string OperationErrNotFound = "ERR_OPERATION_NOT_FOUND";
    private const string OperationErrInvalidArgument = "ERR_ILLEGAL_ARGUMENT";
    private const string OperationErrExceptionThrown = "ERR_UNHANDLED_EXCEPTION";
    private const string OperationErrEmpty = "ERR_EMPTY";
    
    /// <summary>
    /// 提权进程接收到操作请求时触发的事件，接收一个字符串作为操作命令并返回一个字符串作为结果。<br/>
    /// <b>注意：如果你不知道这是做什么的，请勿覆盖默认实现。</b>请使用 <see cref="AddOperationFunction"/>。
    /// </summary>
    public static Func<string, string?> Operate {
        private get => field ??= command =>
        {
            var split = command.Split([' '], 2);
            _OperationFunctions.TryGetValue(split[0], out var operation);
            if (operation is null) return OperationErrNotFound;
            try
            {
                return operation(split.Length > 1 ? split[1] : null) ?? OperationErrEmpty;
            }
            catch (Exception ex)
            {
                Context.Warn("操作出错", ex);
                return OperationErrExceptionThrown;
            }
        };
        set;
    } = null!;
    
    private static string _ShortenString(string str)
    {
#if TRACE
        const int maxLength = 40;
#else
        const int maxLength = 15;
#endif
        if (str.Length <= maxLength) return str;
        return str[..maxLength] + "...";
    }
    
    // 提权进程: 连接管道开始通信
    private static void _PerformAsPromoteProcess(string pid)
    {
        Context.Info("正在连接提权通信管道");
        var process = Process.GetProcessById(int.Parse(pid));
        // 验证来源
        var mainProcessPath = Path.GetFullPath(process.MainModule!.FileName);
        if (!string.Equals(mainProcessPath, Basics.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            Context.Error("来源验证失败，正在退出");
            return;
        }
        // 连接管道
        var pipeName = _GetPromotePipeName(process.Id);
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        pipe.Connect(10000);
        var serverProcessId = (int)KernelInterop.GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle());
        if (serverProcessId != process.Id)
        {
            Context.Error("管道服务端验证失败，正在退出");
            pipe.Dispose();
            return;
        }
        Context.Info("已连接，开始通信");
        var reader = new StreamReader(pipe);
        var writer = new StreamWriter(pipe);
        while (true)
        {
            var command = reader.ReadLine();
            if (string.IsNullOrEmpty(command))
            {
                Context.Info("管道已关闭，正在退出");
                break;
            }
            Context.Debug($"正在执行: {_ShortenString(command)}");
            var result = Operate(command) ?? OperationErrEmpty;
            Context.Trace($"返回结果: {_ShortenString(result)}");
            writer.WriteLine(result.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' '));
            writer.Flush();
            Context.Trace("返回成功");
        }
    }
    
    private static readonly AutoResetEvent _ActivateEvent = new(false);

    // 主进程: 管道连接回调
    private static bool _PromotePipeCallback(StreamReader reader, StreamWriter writer, Process? client)
    {
        while (IsPromoteProcessRunning)
        {
            if (!_PendingOperations.TryDequeue(out var operation))
            {
                _ActivateEvent.WaitOne();
                continue;
            }
            var command = operation.Command.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            var commandLog = operation.DetailLog ? command : _ShortenString(command);
            Context.Debug($"正在执行: {commandLog}");
            writer.WriteLine(command);
            writer.Flush();
            var result = reader.ReadLine();
            if (result is null)
            {
                Context.Warn("管道输入流已结束");
                break;
            }
            var resultLog = operation.DetailLog ? result : _ShortenString(result);
            Context.Trace($"执行结果: {resultLog}");
            if (result == OperationErrEmpty) result = null;
            operation.Callback?.Invoke(result);
        }
        return false;
    }

    // 主进程: 初始化提权后台服务
    private static bool _StartPromoteProcess()
    {
        // 启动提权进程
        _promoteProcess = ProcessInterop.Start(
            Basics.ExecutablePath, $"promote {Basics.CurrentProcessId}", true);
        if (_promoteProcess is null)
        {
            Context.Warn("提权进程启动失败");
            return false;
        }
        _promoteProcess.Exited += (_, _) => _promoteProcess = null;
        // 启动提权通信管道服务端
        _promotePipeServer ??= PipeComm.StartPipeServer(
            "Promote", _GetPromotePipeName(Basics.CurrentProcessId), _PromotePipeCallback,
            () => _promotePipeServer = null, true, [_promoteProcess.Id]);
        return true;
    }

    /// <summary>
    /// 向等待区添加操作。
    /// </summary>
    /// <param name="command">操作命令</param>
    /// <param name="callback">结果返回后的回调</param>
    /// <param name="detailLog">指定是否打印详细日志，若为 <c>false</c>，则日志仅保留前 40 或 15 字符（取决于是否为调试构建）</param>
    public static void Append(string command, Action<string?>? callback = null, bool detailLog = true)
    {
        _PendingOperations.Enqueue(new PromoteOperation(command, callback, detailLog));
    }

    /// <summary>
    /// 尝试启动提权进程并开始执行操作。
    /// </summary>
    /// <returns>是否成功开始执行，若提权进程启动失败则为 <c>false</c></returns>
    public static bool Activate()
    {
        if (!IsPromoteProcessRunning && !_StartPromoteProcess())
        {
            _PendingOperations.Clear();
            return false;
        }
        _ActivateEvent.Set();
        return true;
    }

    private static readonly Dictionary<string, Process> _RunningProcesses = new();
    
    // name: kill
    // arg: process-id [timeout]
    // return: kill result (false over timeout)
    [PromoteOperation("kill")]
    public static string? KillProcess(string? arg)
    {
        if (arg is null) return OperationErrInvalidArgument;
        var split = arg.Split(' ');
        if (!_RunningProcesses.TryGetValue(split[0], out var process)) return null;
        process.Kill();
        if (split.Length > 1)
        {
            int.TryParse(split[1], out var timeout);
            return process.WaitForExit(timeout).ToString();
        }
        process.WaitForExit();
        return true.ToString();
    }

    // name: start
    // arg: path\to\executable[.] ; arguments
    // return: process id
    [PromoteOperation("start")]
    public static string? StartProcess(string? arg)
    {
        if (arg is null) return OperationErrInvalidArgument;
        var split = arg.Split([" ; "], 2, StringSplitOptions.RemoveEmptyEntries);
        var createNoWindow = false;
        if (split[0].EndsWith('.'))
        {
            split[0] = split[0][..^1];
            createNoWindow = true;
        }
        var psi = new ProcessStartInfo(split[0]);
        if (createNoWindow)
        {
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
        }
        if (split.Length > 1) psi.Arguments = split[1];
        return _StartProcessWithInfo(psi);
    }

    // name: start-json
    // arg: {...}
    // return: process id
    private static string? _StartProcessWithInfo(ProcessStartInfo? info)
    {
        if (info is null) return OperationErrInvalidArgument;
        var process = Process.Start(info);
        if (process is null) return null;
        var id = process.Id.ToString();
        process.Exited += (_, _) => _RunningProcesses.Remove(id);
        _RunningProcesses[id] = process;
        return id;
    }

    [DependencyInjectionPoint("promote", false)]
    private static void _CollectOperationFunction(PromoteOperationFunction operation, string name)
        => AddOperationFunction(name, operation);

    [LifecycleStart]
    private static void _Start()
    {
        var args = Basics.CommandLineArguments;
        if (args is ["promote", _])
        {
            Context.Info("当前进程为提权进程");
            IsCurrentProcessPromoted = true;
            // 预定义操作
            Context.Info("正在加载提权操作");
            _CollectOperationFunction_InvokeInjection_Promote();
            AddJsonOperationFunction<ProcessStartInfo>("start-json", _StartProcessWithInfo);
            // 结束生命周期管理，启动提权操作线程
            Lifecycle.PendingLogFileName = "LastPending_Promote.log";
            LogWrapper.OnLog += (level, msg, module, ex) => Context.CustomLog($"[{module}] {msg}", ex, level);
            Context.Info("已接管通用日志");
            Context.Info("正在启动服务线程");
            new Thread(() => _PerformAsPromoteProcess(args[1])) { Name = "Promote" }.Start();
            Context.RequestStopLoading();
            Context.DeclareStopped();
        }
        else
        {
            Context.Info("当前进程为主进程");
            IsCurrentProcessPromoted = false;
            // TODO 提权进程自动启动
        }
    }

    [LifecycleStop]
    private static void _Stop()
    {
        if (_promotePipeServer is not null)
        {
            Context.Debug("正在结束提权管道服务");
            _promotePipeServer.Dispose();
        }
        if (_promoteProcess is not null && !_promoteProcess.WaitForExit(3000))
        {
            Context.Debug("正在结束提权进程");
            ProcessInterop.Kill(_promoteProcess, 0, true);
        }
    }
}
