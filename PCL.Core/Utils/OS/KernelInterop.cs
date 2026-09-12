using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace PCL.Core.Utils.OS;

public static partial class KernelInterop
{
    // ReSharper disable InconsistentNaming, UnusedMember.Local

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThreadId", SetLastError = true)]
    private static partial uint _GetCurrentThreadId();

    [LibraryImport("kernel32.dll", EntryPoint = "ExitProcess", SetLastError = false)]
    private static partial void _ExitProcess(uint statusCode);

    [LibraryImport("kernel32.dll", EntryPoint = "GetNamedPipeClientProcessId", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool _GetNamedPipeClientProcessId(IntPtr pipeHandle, out uint clientProcessId);

    [LibraryImport("kernel32.dll", EntryPoint = "GetNamedPipeServerProcessId", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool _GetNamedPipeServerProcessId(IntPtr pipeHandle, out uint serverProcessId);

    [LibraryImport("kernel32.dll", EntryPoint = "GetLogicalProcessorInformationEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool _GetLogicalProcessorInformationEx(
        LOGICAL_PROCESSOR_RELATIONSHIP relationshipType,
        IntPtr buffer,
        ref uint returnLength);

    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    
    private enum LOGICAL_PROCESSOR_RELATIONSHIP : uint
    {
        RelationProcessorCore    = 0,
        RelationNumaNode         = 1,
        RelationCache            = 2,
        RelationProcessorPackage = 3,
        RelationGroup            = 4,
        RelationAll              = 0xffff
    }

    private static MEMORYSTATUSEX CreateStatus() => new() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private const int ERROR_ACCESS_DENIED = 5;

    [LibraryImport("kernel32.dll", EntryPoint = "AllocConsole")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool _AllocConsole();

    [LibraryImport("kernel32.dll", EntryPoint = "FreeConsole")]
    private static partial void _FreeConsole();

    [LibraryImport("kernel32.dll", EntryPoint = "GetConsoleWindow")]
    private static partial nint _GetConsoleWindow();

    // ReSharper restore InconsistentNaming, UnusedMember.Local

    private static void _ThrowLastWin32Error(int? errorCode = null) => throw new Win32Exception(errorCode ?? Marshal.GetLastWin32Error());

    /// <summary>
    /// 获取当前线程的 Win32 Thread ID。若无特殊情况请用 <see cref="Thread.ManagedThreadId"/> 而不是这个方法。
    /// </summary>
    public static uint CurrentNativeThreadId => _GetCurrentThreadId();

    /// <summary>
    /// 直接结束当前进程。若无特殊情况请使用 <see cref="PCL.Core.App.IoC.Lifecycle.Shutdown"/>
    /// </summary>
    /// <param name="statusCode">退出状态码 (返回值)</param>
    public static void ExitProcess(int statusCode = 0) => _ExitProcess((uint)statusCode);

    /// <summary>
    /// 获取指定命名管道当前连接的客户端进程 ID
    /// </summary>
    /// <param name="pipeHandle">命名管道句柄</param>
    /// <returns>获取到的进程 ID</returns>
    public static uint GetNamedPipeClientProcessId(IntPtr pipeHandle)
    {
        if (!_GetNamedPipeClientProcessId(pipeHandle, out var clientProcessId)) _ThrowLastWin32Error();
        return clientProcessId;
    }

    /// <summary>
    /// 获取指定命名管道当前连接的服务端进程 ID
    /// </summary>
    /// <param name="pipeHandle">命名管道句柄</param>
    /// <returns>获取到的进程 ID</returns>
    public static uint GetNamedPipeServerProcessId(IntPtr pipeHandle)
    {
        if (!_GetNamedPipeServerProcessId(pipeHandle, out var serverProcessId)) _ThrowLastWin32Error();
        return serverProcessId;
    }

    /// <summary>
    /// 获取仅包含性能核（P-core）的逻辑处理器数量。
    /// 在不支持 EfficiencyClass（旧 OS 或非混合架构）时，会退回到 Environment.ProcessorCount。
    /// </summary>
    public static int GetPerformanceLogicalProcessorCount()
    {
        var cores = QueryProcessorCoreRelationships();
        if (cores.Count == 0)
        {
            // 不支持 EfficiencyClass
            return Environment.ProcessorCount;
        }

        // 原理：性能核的 EfficiencyClass 一定比能效核大
        var maxEff = cores.Max(c => c.EfficiencyClass);

        // 统计所有效率等级为 maxEff 的核心的掩码位数
        return cores
            .Where(c => c.EfficiencyClass == maxEff)
            .Sum(c => CountSetBits(c.Mask));

        static int CountSetBits(ulong v)
        {
            var cnt = 0;
            while (v != 0)
            {
                cnt += (int)(v & 1);
                v >>= 1;
            }
            return cnt;
        }
    }

    /// <summary>
    /// 仅承载 EfficiencyClass 和 Mask 的简单 CPU 核心信息
    /// </summary>
    public sealed record ProcessorCore(byte EfficiencyClass, ulong Mask);

    /// <summary>
    /// 枚举 RelationProcessorCore 返回的所有物理核心关系信息
    /// </summary>
    // Partly generated by o4-mini-high (20250709)
    public static List<ProcessorCore> QueryProcessorCoreRelationships()
    {
        uint returnedLength = 0;

        // 第一次调用仅为了获取所需缓冲区大小
        if (!_GetLogicalProcessorInformationEx(
                LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore,
                IntPtr.Zero,
                ref returnedLength)
            && Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var list = new List<ProcessorCore>();
        var buffer = Marshal.AllocHGlobal((int)returnedLength);
        try
        {
            if (!_GetLogicalProcessorInformationEx(
                    LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore,
                    buffer,
                    ref returnedLength))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var ptr = buffer;
            var end = IntPtr.Add(buffer, (int)returnedLength);

            // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX 头部：Relationship (4 字节) + Size (4 字节)
            const int headerSize = sizeof(uint) + sizeof(uint);
            // GROUP_AFFINITY 大小 = KAFFINITY (平台指针大小) + WORD Group + WORD[3] Reserved
            var groupAffinitySize = IntPtr.Size + 8;

            while (ptr.ToInt64() < end.ToInt64())
            {
                var relationship = (uint)Marshal.ReadInt32(ptr);
                var size = (uint)Marshal.ReadInt32(ptr, sizeof(uint));

                if (relationship == (uint)LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                {
                    // PROCESSOR_RELATIONSHIP 结构：
                    // Flags                BYTE @ offset 8
                    // EfficiencyClass      BYTE @ offset 9
                    // Reserved[20]         BYTE[20]
                    // GroupCount           WORD @ offset 30
                    // GroupMask[ANYSIZE]   GROUP_AFFINITY 从 offset 32 开始

                    var efficiencyClass = Marshal.ReadByte(ptr, headerSize + 1);
                    var groupCount = (ushort)Marshal.ReadInt16(ptr, headerSize + 2 + 20);
                    var maskBase = IntPtr.Add(ptr, headerSize + 2 + 20 + sizeof(ushort));

                    for (var i = 0; i < groupCount; i++)
                    {
                        var affinityPtr = IntPtr.Add(maskBase, i * groupAffinitySize);
                        // 只读取 Mask 部分，统计位数
                        var mask = (IntPtr.Size == 8 ? (ulong)Marshal.ReadInt64(affinityPtr) : (uint)Marshal.ReadInt32(affinityPtr));
                        list.Add(new ProcessorCore(efficiencyClass, mask));
                    }
                }

                // 移动到下一个记录
                ptr = IntPtr.Add(ptr, (int)size);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return list;
    }

    /// <summary>
    /// 获取系统可用物理内存 (<c>ullAvailPhys</c>) 的字节数
    /// </summary>
    public static ulong GetAvailablePhysicalMemoryBytes()
    {
        var status = CreateStatus();
        if (!GlobalMemoryStatusEx(ref status)) _ThrowLastWin32Error();
        return status.ullAvailPhys;
    }

    /// <summary>
    /// 获取系统可用物理内存 (<c>ullAvailPhys</c>) 和总物理内存 (<c>ullTotalPhys</c>) 的字节数
    /// </summary>
    public static (ulong Total, ulong Available) GetPhysicalMemoryBytes()
    {
        var status = CreateStatus();
        if (!GlobalMemoryStatusEx(ref status)) _ThrowLastWin32Error();
        return (status.ullTotalPhys, status.ullAvailPhys);
    }

    /// <summary>
    /// 获取以百分比表示的系统内存占用 (范围 0.0 ~ 100.0)
    /// </summary>
    public static double GetMemoryLoadPercent()
    {
        var status = CreateStatus();
        if (!GlobalMemoryStatusEx(ref status)) _ThrowLastWin32Error();
        return status.dwMemoryLoad;
    }

    /// <summary>
    /// 为当前进程新建终端窗口。<br/>
    /// 若进程已拥有终端窗口，该方法将无任何作用。若有需要，可在调用前使用
    /// <see cref="GetConsoleWindow"/> 来确认进程是否存在关联的终端窗口。
    /// </summary>
    public static void AllocateConsole()
    {
        if (_AllocConsole()) return;
        var lastError = Marshal.GetLastWin32Error();
        if (lastError != ERROR_ACCESS_DENIED) _ThrowLastWin32Error(lastError);
    }

    /// <summary>
    /// 释放当前进程的终端窗口。<br/>
    /// 若进程不存在关联的终端窗口，该方法将无任何作用。
    /// </summary>
    public static void FreeConsole() => _FreeConsole();

    /// <summary>
    /// 获取当前进程关联的终端窗口句柄。
    /// </summary>
    /// <returns>代表终端窗口的 HWND，若当前进程无关联的终端窗口，则该值为 <see cref="nint.Zero"/></returns>
    public static nint GetConsoleWindow() => _GetConsoleWindow();
}
