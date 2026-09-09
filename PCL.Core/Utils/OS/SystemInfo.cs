using System;
using System.Runtime.InteropServices;

namespace PCL.Core.Utils.OS;

public static class SystemInfo
{
    /// <summary>
    /// 是否为 32 位系统。
    /// </summary>
    public static readonly bool Is32BitSystem = !Environment.Is64BitOperatingSystem;

    /// <summary>
    /// 是否为 ARM64 架构。
    /// </summary>
    public static readonly bool IsArm64System = RuntimeInformation.OSArchitecture == Architecture.Arm64;

    /// <summary>
    /// 是否使用 GBK 编码。
    /// </summary>
    public static readonly bool IsGBKEncoding = KernelInterop.GetACP() == 936;

    /// <summary>
    /// 系统信息描述，例如 Microsoft Windows 11 专业工作站版 10.0.22635.0
    /// </summary>
    public static readonly string OSInfo = $"{RuntimeInformation.OSDescription} {Environment.OSVersion.Version}";
}