using System.IO;
using System.IO.Compression;
using System.Text;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.Utils.Codecs;
using PCL.Core.Utils.OS;
using PCL.Core.Utils.Secret;

namespace PCL;

internal sealed class CrashReportExporter
{
    private const string ReportFolderName = "Report";

    private const string LaunchScriptFileName = "启动脚本.bat";
    private const string RawOutputFileName = "游戏崩溃前的输出.txt";
    private const string LauncherLogFileName = "PCL CE 启动器日志.txt";
    private const string EnvironmentFileName = "环境与启动信息.txt";
    private const string ModInfoFileName = "模组列表.txt";

    public void Export(
        CrashAnalysisContext context,
        string targetZipPath,
        IEnumerable<string>? extraFiles)
    {
        var targetFolder = Basics.GetParentPathOrEmpty(targetZipPath);
        Directory.CreateDirectory(targetFolder);

        if (File.Exists(targetZipPath))
            File.Delete(targetZipPath);

        ModBase.FeedbackInfo();

        var reportFolder = Path.Combine(context.TempFolder, ReportFolderName);

        if (Directory.Exists(reportFolder))
            CrashFileIo.DeleteDirectory(reportFolder);

        Directory.CreateDirectory(reportFolder);

        try
        {
            foreach (var outputFile in _CollectOutputFiles(context, extraFiles))
                _CopyFileToReport(reportFolder, outputFile);

            _WriteEnvironmentInfo(reportFolder);
            _WriteModInfo(reportFolder, context.Instance);

            ZipFile.CreateFromDirectory(reportFolder, targetZipPath);
        }
        finally
        {
            CrashFileIo.DeleteDirectory(reportFolder);
        }
    }

    private static void _CopyFileToReport(
        string reportFolder,
        string outputFile)
    {
        if (!File.Exists(outputFile))
            return;

        var fileName = _GetExportFileName(outputFile, out var fileEncoding);

        fileEncoding ??= EncodingDetector.DetectEncoding(CrashFileIo.ReadBytes(outputFile));

        var fileContent = CrashFileIo.ReadText(outputFile, fileEncoding);
        fileContent = _SanitizeFileContent(fileContent, fileName);

        CrashFileIo.WriteText(
            Path.Combine(reportFolder, fileName),
            fileContent,
            fileEncoding);

        LogWrapper.Info("Crash", $"导出文件：{fileName}，编码：{fileEncoding.HeaderName}");
    }

    private static string _GetExportFileName(
        string outputFile,
        out Encoding? fileEncoding)
    {
        fileEncoding = null;

        var fileName = Path.GetFileName(outputFile);

        switch (fileName)
        {
            case "LatestLaunch.bat":
                return LaunchScriptFileName;

            case "RawOutput.log":
                fileEncoding = Encoding.UTF8;
                return RawOutputFileName;
        }

        var currentLogFile = LogWrapper.CurrentLogger.CurrentLogFiles.LastOrDefault();
        var currentLogFileName = currentLogFile is null ? null : CrashText.AfterLast(currentLogFile, @"\");

        if (currentLogFileName != fileName) return fileName;

        fileEncoding = Encoding.UTF8;
        return LauncherLogFileName;
    }

    private static IReadOnlyList<string> _CollectOutputFiles(
        CrashAnalysisContext context,
        IEnumerable<string>? extraFiles)
    {
        if (extraFiles is not null)
            context.OutputFiles.AddRange(extraFiles);

        return context.OutputFiles;
    }

    private static string _SanitizeFileContent(
        string fileContent,
        string fileName)
    {
        var tokenMask = fileName == LaunchScriptFileName ? 'F' : '*';

        fileContent = McLogFilter.FilterAccessToken(fileContent, tokenMask);
        return McLogFilter.FilterUserName(fileContent, '*');
    }

    private static void _WriteEnvironmentInfo(string reportFolder)
    {
        var launcherLog = CrashText.BeforeFirst(
            CrashText.AfterLast(_ReadReportFile(reportFolder, LauncherLogFileName), "[Launch] ~ 基础参数 ~"),
            "开始 Minecraft 日志监控");

        var launchScript = _ReadReportFile(reportFolder, LaunchScriptFileName);

        var envInfo = new StringBuilder();

        _AppendLauncherInfo(envInfo);
        _AppendProfileInfo(envInfo, launcherLog);
        _AppendInstanceInfo(envInfo, launcherLog, launchScript);
        _AppendEnvironmentInfo(envInfo, launcherLog);

        CrashFileIo.WriteText(
            Path.Combine(reportFolder, EnvironmentFileName),
            envInfo.ToString(),
            Encoding.UTF8);
    }

    private static void _AppendLauncherInfo(StringBuilder builder)
    {
        builder.AppendLine(Lang.Text("Crash.Report.Environment.LauncherVersion", Basics.VersionName));
        builder.AppendLine(Lang.Text("Crash.Report.Environment.LauncherId", Identify.LauncherId));
        builder.AppendLine();
    }

    private static void _AppendProfileInfo(
        StringBuilder builder,
        string launcherLog)
    {
        builder.AppendLine(Lang.Text("Crash.Report.Environment.ProfileSection"));
        builder.AppendLine(
            Lang.Text(
                "Crash.Report.Environment.ProfileName",
                _ExtractLauncherValue(launcherLog, "玩家用户名："),
                _ExtractLauncherValue(launcherLog, "验证方式：")));
        builder.AppendLine();
    }

    private static void _AppendInstanceInfo(
        StringBuilder builder,
        string launcherLog,
        string launchScript)
    {
        builder.AppendLine(Lang.Text("Crash.Report.Environment.InstanceSection"));

        builder.AppendLine(
            Lang.Text(
                "Crash.Report.Environment.SelectedJava",
                _ExtractLauncherValue(launcherLog, "Java 信息：")));

        builder.AppendLine(
            Lang.Text(
                "Crash.Report.Environment.Log4j2NoLookups",
                !launchScript.Contains("-Dlog4j2.formatMsgNoLookups=false", StringComparison.OrdinalIgnoreCase)));

        builder.AppendLine(
            Lang.Text(
                "Crash.Report.Environment.MinecraftFolder",
                _ExtractLauncherValue(launcherLog, "MC 文件夹：")));

        builder.AppendLine();
    }

    private static void _AppendEnvironmentInfo(
        StringBuilder builder,
        string launcherLog)
    {
        builder.AppendLine(Lang.Text("Crash.Report.Environment.EnvironmentSection"));

        builder.AppendLine(
            Lang.Text(
                "Crash.Report.Environment.OperatingSystem",
                SystemInfo.OSInfo,
                !SystemInfo.Is32BitSystem,
                SystemInfo.IsArm64System));

        builder.AppendLine(
            Lang.Text(
                "Crash.Report.Environment.Cpu",
                HardwareInfo.CPUName));

        builder.AppendLine(
            Lang.Text(
                "Crash.Report.Environment.MemoryAllocation",
                _ExtractLauncherValue(launcherLog, "分配的内存："),
                Lang.Number(HardwareInfo.SystemMemorySize / 1024d, "N2"),
                Lang.Number(HardwareInfo.SystemMemorySize, "N0")));

        for (var i = 0; i < HardwareInfo.GPUs.Count; i++)
        {
            var gpu = HardwareInfo.GPUs[i];

            builder.AppendLine(
                Lang.Text(
                    "Crash.Report.Environment.Gpu",
                    i,
                    gpu.Name,
                    _FormatGpuMemory(gpu.Memory),
                    gpu.DriverVersion));
        }
    }

    private static string _ExtractLauncherValue(
        string launcherLog,
        string key)
    {
        return CrashText.Between(launcherLog, key, "[")
            .TrimEnd('[')
            .Trim();
    }

    private static string _FormatGpuMemory(long memory)
    {
        return memory >= 4095L
            ? ">= " + memory
            : memory.ToString();
    }

    private static string _ReadReportFile(
        string reportFolder,
        string fileName)
    {
        var filePath = Path.Combine(reportFolder, fileName);

        return File.Exists(filePath)
            ? CrashFileIo.ReadText(filePath)
            : "";
    }

    private static void _WriteModInfo(string reportFolder, McInstance? instance)
    {
        if (instance is null)
            return;

        try
        {
            ModJarInJarCache.UseInstance(instance.PathInstance);
            var modsFolderName = ModLocalComp.GetPathNameByCompType(ModComp.CompType.Mod);
            var modsFolder = instance.Info.HasLabyMod
                ? Path.Combine(instance.PathIndie, "labymod-neo", "fabric", instance.Info.VanillaName, modsFolderName)
                : Path.Combine(instance.PathIndie, modsFolderName);

            if (!Directory.Exists(modsFolder))
                return;

            // 老 Forge（Drop < 130）的启用 Mod 位于 mods/<版本名> 子目录，需一并扫描
            var scanFolders = new List<string> { modsFolder };
            if (instance.Info.HasForge && instance.Info.Drop < 130)
            {
                var versionSubFolder = Path.Combine(modsFolder, instance.Info.VanillaName);
                if (Directory.Exists(versionSubFolder))
                    scanFolders.Add(versionSubFolder);
            }

            var activeMods = new List<ModLocalComp.LocalCompFile>();
            foreach (var folder in scanFolders)
                foreach (var file in Directory.GetFiles(folder))
                {
                    if (!ModLocalComp.LocalCompFile.IsModFile(file)
                        || file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        || file.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var mod = new ModLocalComp.LocalCompFile(file);
                    mod.Load();
                    if (mod.State == ModLocalComp.LocalCompFile.LocalFileStatus.Fine)
                        activeMods.Add(mod);
                }

            activeMods = activeMods
                .OrderBy(m => m.Name ?? m.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();

            // 依赖警告与冲突关系放在最前，崩溃排查时第一眼可见
            _AppendDependencyIssues(sb, activeMods, instance.Info.VanillaName);

            var modsByModId = new Dictionary<string, List<ModLocalComp.LocalCompFile>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in activeMods)
            {
                if (string.IsNullOrEmpty(mod.ModId))
                    continue;
                if (!modsByModId.TryGetValue(mod.ModId, out var list))
                    modsByModId[mod.ModId] = list = new List<ModLocalComp.LocalCompFile>();
                list.Add(mod);
            }

            var duplicates = new List<string>();
            foreach (var host in activeMods)
                foreach (var embedded in _FlattenEmbedded(host.EmbeddedMods))
                {
                    if (string.IsNullOrEmpty(embedded.ModId)
                        || !modsByModId.TryGetValue(embedded.ModId, out var matches))
                        continue;
                    foreach (var other in matches)
                    {
                        if (ReferenceEquals(other, host))
                            continue;
                        duplicates.Add(Lang.Text("Crash.Report.JarInJarMod.DuplicateEntry", other.Name, other.FileName, host.Name, embedded.Name ?? embedded.ModId));
                    }
                }

            sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.DuplicateSection"));
            if (duplicates.Count == 0)
                sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.DuplicateNone"));
            else
            {
                sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.DuplicateDescription"));
                sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.DuplicateHeuristic"));
                foreach (var line in duplicates.Distinct())
                    sb.AppendLine("\t|-> " + line);
            }

            sb.AppendLine().AppendLine("----------------------------").AppendLine();

            sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.ModListSection"));
            sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.ModListDirectory", modsFolder));
            sb.AppendLine("|-> mods");
            foreach (var mod in activeMods)
            {
                var line = "|  |-> " + (mod.Name ?? mod.FileName);
                var version = _CleanVersion(mod.Version);
                if (!string.IsNullOrWhiteSpace(version))
                    line += $" ({version})";
                if (mod.Name != mod.FileName)
                    line += $" [{mod.FileName}]";
                sb.AppendLine(line);
            }

            sb.AppendLine().AppendLine("----------------------------").AppendLine();

            sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.JarInJarSection"));
            var hasJij = false;
            foreach (var mod in activeMods)
            {
                if (!mod.EmbeddedMods.Any())
                    continue;
                hasJij = true;
                sb.AppendLine(mod.Name ?? mod.FileName);
                _AppendEmbeddedMods(sb, mod.EmbeddedMods, 1);
                sb.AppendLine();
            }

            if (!hasJij)
                sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.JarInJarNone"));

            CrashFileIo.WriteText(Path.Combine(reportFolder, ModInfoFileName), sb.ToString(), Encoding.UTF8);
            ModJarInJarCache.Flush();
            LogWrapper.Info("Crash", "已导出模组列表及 Jar-in-Jar 信息");
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "Crash", "导出模组信息失败");
        }
        finally
        {
            // 复位本线程的"当前实例"：含 mods 目录不存在的 early return 与异常路径，
            // 否则本线程后续懒加载别实例的 Mod 会被路由进本实例缓存
            ModJarInJarCache.UseInstance(null);
        }
    }

    // 把模组关系页的「缺失前置/版本不符」警告与「不兼容/不建议共存」冲突一并写入崩溃模组信息
    private static void _AppendDependencyIssues(StringBuilder sb, List<ModLocalComp.LocalCompFile> activeMods,
        string mc)
    {
        var jij = new ModJarInJarIndex(activeMods, mc);

        sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.WarningSection"));
        var warned = false;
        foreach (var mod in activeMods)
        {
            var missing = new List<string>();
            var mismatch = new List<string>();
            var disabled = new List<string>();

            void Probe(ModLocalComp.LocalCompFile who, string loader)
            {
                foreach (var d in ModJarInJarIndex.BuildOwnDependencies(who, loader))
                {
                    if (ModJarInJarIndex.IsPlatform(d.DepId) || d.Optional) continue;
                    switch (jij.Analyze(who, d))
                    {
                        case JijDepStatus.Missing:
                            missing.Add(d.DepId);
                            break;
                        case JijDepStatus.VersionMismatch:
                            mismatch.Add(d.Raw is null ? d.DepId : d.DepId + " " + d.Raw);
                            break;
                        case JijDepStatus.Disabled:
                            disabled.Add(d.DepId); // 版本匹配的 provider 全被禁用：运行时同样不可用
                            break;
                    }
                }
            }

            Probe(mod, mod.DetectedLoader);
            foreach (var node in jij.GetLoadableEmbedded(mod)) Probe(node, node.JijLoader);
            if (missing.Count == 0 && mismatch.Count == 0 && disabled.Count == 0) continue;
            var parts = new List<string>();
            if (missing.Count > 0)
                parts.Add(Lang.Text("Crash.Report.JarInJarMod.WarningMissing", string.Join(", ", missing.Distinct())));
            if (mismatch.Count > 0)
                parts.Add(Lang.Text("Crash.Report.JarInJarMod.WarningMismatch",
                    string.Join(", ", mismatch.Distinct())));
            if (disabled.Count > 0)
                parts.Add(Lang.Text("Crash.Report.JarInJarMod.WarningDisabled",
                    string.Join(", ", disabled.Distinct())));
            sb.AppendLine("|-> " + (mod.Name ?? mod.FileName) + ": " + string.Join("; ", parts));
            warned = true;
        }

        if (!warned) sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.WarningNone"));

        sb.AppendLine().AppendLine("----------------------------").AppendLine();
        sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.ConflictSection"));
        var conflicts = jij.FindActiveConflicts();
        if (conflicts.Count == 0)
            sb.AppendLine(Lang.Text("Crash.Report.JarInJarMod.ConflictNone"));
        else
            foreach (var (a, b, hard) in conflicts)
                sb.AppendLine("|-> [" + Lang.Text(hard
                    ? "Crash.Report.JarInJarMod.ConflictIncompatible"
                    : "Crash.Report.JarInJarMod.ConflictDiscouraged") + "] " +
                    (a.Name ?? a.FileName) + " / " + (b.Name ?? b.FileName));

        sb.AppendLine().AppendLine("----------------------------").AppendLine();
    }

    private static IEnumerable<ModLocalComp.LocalCompFile> _FlattenEmbedded(List<ModLocalComp.LocalCompFile> mods)
    {
        foreach (var mod in mods)
        {
            yield return mod;
            if (mod.EmbeddedMods.Any())
                foreach (var child in _FlattenEmbedded(mod.EmbeddedMods))
                    yield return child;
        }
    }

    private static void _AppendEmbeddedMods(StringBuilder builder, List<ModLocalComp.LocalCompFile> mods, int depth)
    {
        var indent = new string('\t', depth);
        foreach (var mod in mods)
        {
            var line = indent + "|-> " + (mod.Name ?? mod.ModId ?? "?");
            var version = _CleanVersion(mod.Version);
            if (!string.IsNullOrWhiteSpace(version))
                line += $" ({version})";
            builder.AppendLine(line);
            if (mod.EmbeddedMods.Any())
                _AppendEmbeddedMods(builder, mod.EmbeddedMods, depth + 1);
        }
    }

    // 未解析的版本占位符（如 Fabric ${version}、Forge ${file.jarVersion}）显示为空，避免裸 token
    private static string _CleanVersion(string version)
        => string.IsNullOrEmpty(version) || version.Contains("${") ? null : version;
}