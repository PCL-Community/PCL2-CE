using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xaml;
using System.Xml.Linq;
using Microsoft.VisualBasic;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.IO;
using PCL.Core.Logging;
using PCL.Core.Utils;
using PCL.Core.Utils.Codecs;
using PCL.Core.Utils.Hash;
using PCL.Core.Utils.OS;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Size = System.Windows.Size;

namespace PCL;

public static class ModBase
{
    #region 声明

    // 下列版本信息由更新器自动修改
    public static readonly string versionBaseName = Basics.VersionName;
    public static readonly string versionStandardCode = Basics.Metadata.Version.StandardVersion;
    public static readonly string upstreamVersion = Basics.Metadata.Version.UpstreamVersion;
    public static readonly string commitHash = Basics.Metadata.Version.Commit;
    public static readonly string commitHashShort = Basics.Metadata.Version.CommitDigest;
    public static readonly int versionCode = Basics.VersionCode;

#if DEBUG
    public const string versionBranchName = "Debug";
    public const string versionBranchCode = "100";
#elif DEBUGCI
    public const string versionBranchName = "CI";
    public const string versionBranchCode = "50";
#else
    public const string versionBranchName = "Publish";
    public const string versionBranchCode = "0";
#endif
    /// <summary>
    ///     主窗口句柄。
    /// </summary>
    public static nint frmHandle;

    // 龙猫味石山小记: 用最不靠谱的实现写出能跑的代码 (AppDomain.CurrentDomain.SetupInformation.ApplicationBase 获取到的是当前工作目录而不是可执行文件所在目录)
    /// <summary>
    ///     程序可执行文件所在目录，以“\”结尾。
    /// </summary>
    public static readonly string exePath = (Basics.ExecutableDirectory.EndsWith(@"\")
        ? Basics.ExecutableDirectory
        : Basics.ExecutableDirectory + @"\");

    /// <summary>
    ///     程序内嵌图片文件夹路径，以“/”结尾。
    /// </summary>
    public static readonly string pathImage = "pack://application:,,,/Plain Craft Launcher 2;component/Images/";

    /// <summary>
    ///     当前程序的语言。
    /// </summary>
    public static string currentLang = "zh_CN";

    /// <summary>
    ///     设置对象。
    /// </summary>
    public static ModSetup setup = new();

    /// <summary>
    ///     程序的打开计时。
    /// </summary>
    public static long applicationStartTick = TimeUtils.GetTimeTick();

    /// <summary>
    ///     程序打开时的时间。
    /// </summary>
    public static DateTime applicationOpenTime = DateTime.Now;

    /// <summary>
    ///     程序是否已结束。
    /// </summary>
    public static bool isProgramEnded = false;

    /// <summary>
    ///     程序的缓存文件夹路径，以 \ 结尾。
    /// </summary>
    public static string pathTemp = Paths.Temp + @"\";

    /// <summary>
    ///     AppData 中的 PCL 文件夹路径，以 \ 结尾。
    /// </summary>
    public static string pathAppdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL") + @"\";

    /// <summary>
    ///     AppData 中的 PCLCE 配置文件夹路径，以 \ 结尾。
    /// </summary>
    public static string pathAppdataConfig = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) +
                                             (versionBranchName == "Debug" ? @"\.pclcedebug\" : @"\.pclce\");


    #endregion

    #region 自定义类

    /// <summary>
    ///     支持小数与常见类型隐式转换的颜色。
    /// </summary>
    public class MyColor
    {
        public double a = 255d;
        public double b;
        public double g;
        public double r;

        // 构造函数
        public MyColor()
        {
        }

        public MyColor(Color col)
        {
            a = col.A;
            r = col.R;
            g = col.G;
            b = col.B;
        }

        public MyColor(string hexString)
        {
            var stringColor = (Color)ColorConverter.ConvertFromString(hexString);
            a = stringColor.A;
            r = stringColor.R;
            g = stringColor.G;
            b = stringColor.B;
        }

        public MyColor(double newA, MyColor col)
        {
            a = newA;
            r = col.r;
            g = col.g;
            b = col.b;
        }

        public MyColor(double newR, double newG, double newB)
        {
            a = 255d;
            r = newR;
            g = newG;
            b = newB;
        }

        public MyColor(double newA, double newR, double newG, double newB)
        {
            a = newA;
            r = newR;
            g = newG;
            b = newB;
        }

        public MyColor(Brush brush)
        {
            var color = ((SolidColorBrush)brush).Color;
            a = color.A;
            r = color.R;
            g = color.G;
            b = color.B;
        }

        public MyColor(SolidColorBrush brush)
        {
            var color = brush.Color;
            a = color.A;
            r = color.R;
            g = color.G;
            b = color.B;
        }

        public MyColor(object obj)
        {
            if (obj is null)
            {
                a = 255d;
                r = 255d;
                g = 255d;
                b = 255d;
            }
            else if (obj is SolidColorBrush)
            {
                // 避免反复获取 Color 对象造成性能下降
                var color = ((SolidColorBrush)obj).Color;
                a = color.A;
                r = color.R;
                g = color.G;
                b = color.B;
            }
            else
            {
                a = Convert.ToDouble(((dynamic)obj).A);
                r = Convert.ToDouble(((dynamic)obj).R);
                g = Convert.ToDouble(((dynamic)obj).G);
                b = Convert.ToDouble(((dynamic)obj).B);
            }
        }

        // 类型转换
        public static implicit operator MyColor(string str)
        {
            return new MyColor(str);
        }

        public static implicit operator MyColor(Color col)
        {
            return new MyColor(col);
        }

        public static implicit operator Color(MyColor conv)
        {
            return Color.FromArgb(MathByte(conv.a), MathByte(conv.r), MathByte(conv.g), MathByte(conv.b));
        }

        public static implicit operator System.Drawing.Color(MyColor conv)
        {
            return System.Drawing.Color.FromArgb(MathByte(conv.a), MathByte(conv.r), MathByte(conv.g),
                MathByte(conv.b));
        }

        public static implicit operator MyColor(SolidColorBrush bru)
        {
            return new MyColor(bru.Color);
        }

        public static implicit operator SolidColorBrush(MyColor conv)
        {
            return new SolidColorBrush(Color.FromArgb(MathByte(conv.a), MathByte(conv.r), MathByte(conv.g),
                MathByte(conv.b)));
        }

        public static implicit operator MyColor(Brush bru)
        {
            return new MyColor(bru);
        }

        public static implicit operator Brush(MyColor conv)
        {
            return new SolidColorBrush(Color.FromArgb(MathByte(conv.a), MathByte(conv.r), MathByte(conv.g),
                MathByte(conv.b)));
        }

        // 颜色运算
        public static MyColor operator +(MyColor a, MyColor b)
        {
            return new MyColor { a = a.a + b.a, b = a.b + b.b, g = a.g + b.g, r = a.r + b.r };
        }

        public static MyColor operator -(MyColor a, MyColor b)
        {
            return new MyColor { a = a.a - b.a, b = a.b - b.b, g = a.g - b.g, r = a.r - b.r };
        }

        public static MyColor operator *(MyColor a, double b)
        {
            return new MyColor { a = a.a * b, b = a.b * b, g = a.g * b, r = a.r * b };
        }

        public static MyColor operator /(MyColor a, double b)
        {
            return new MyColor { a = a.a / b, b = a.b / b, g = a.g / b, r = a.r / b };
        }

        public static bool operator ==(MyColor a, MyColor b)
        {
            if (a is null && b is null)
                return true;
            if (a is null || b is null)
                return false;
            return a.a == b.a && a.r == b.r && a.g == b.g && a.b == b.b;
        }

        public static bool operator !=(MyColor a, MyColor b)
        {
            if (a is null && b is null)
                return false;
            if (a is null || b is null)
                return true;
            return !(a.a == b.a && a.r == b.r && a.g == b.g && a.b == b.b);
        }

        // HSL
        public double Hue(double v1, double v2, double vH)
        {
            if (vH < 0d)
                vH += 1d;
            if (vH > 1d)
                vH -= 1d;
            if (vH < 0.16667d)
                return v1 + (v2 - v1) * 6d * vH;
            if (vH < 0.5d)
                return v2;
            if (vH < 0.66667d)
                return v1 + (v2 - v1) * (4d - vH * 6d);
            return v1;
        }

        public MyColor FromHSL(double sH, double sS, double sL)
        {
            if (sS == 0d)
            {
                r = sL * 2.55d;
                g = r;
                b = r;
            }
            else
            {
                var h = sH / 360d;
                var s = sS / 100d;
                var l = sL / 100d;
                s = l < 0.5d ? s * l + l : s * (1.0d - l) + l;
                l = 2d * l - s;
                r = 255d * Hue(l, s, h + 1d / 3d);
                g = 255d * Hue(l, s, h);
                b = 255d * Hue(l, s, h - 1d / 3d);
            }

            a = 255d;
            return this;
        }

        public MyColor FromHSL2(double sH, double sS, double sL)
        {
            if (sS == 0d)
            {
                r = sL * 2.55d;
                g = r;
                b = r;
            }
            else
            {
                // 初始化
                sH = (sH + 3600000d) % 360d;
                var cent = new[]
                {
                    +0.1d, -0.06d, -0.3d, -0.19d, -0.15d, -0.24d, -0.32d, -0.09d, +0.18d, +0.05d, -0.12d, -0.02d, +0.1d,
                    -0.06d
                }; // 0, 30, 60
                // 90, 120, 150
                // 180, 210, 240
                // 270, 300, 330
                // 最后两位与前两位一致，加是变亮，减是变暗
                // 计算色调对应的亮度片区
                var center = sH / 30.0d;
                var intCenter = (int)Math.Round(Math.Floor(center)); // 亮度片区编号
                center = 50d -
                         ((1d - center + intCenter) * cent[intCenter] + (center - intCenter) * cent[intCenter + 1]) *
                         sS;
                // center = 50 + (cent(intCenter) + (center - intCenter) * (cent(intCenter + 1) - cent(intCenter))) * sS
                sL = (sL < center ? sL / center : 1d + (sL - center) / (100d - center)) * 50d;
                FromHSL(sH, sS, sL);
            }

            a = 255d;
            return this;
        }

        public MyColor Alpha(double sA)
        {
            a = sA;
            return this;
        }

        public override string ToString()
        {
            return "(" + a + "," + r + "," + g + "," + b + ")";
        }

        public override bool Equals(object obj)
        {
            return obj is MyColor other && a == other.a && r == other.r && g == other.g && b == other.b;
        }
    }

    /// <summary>
    ///     支持负数与浮点数的矩形。
    /// </summary>
    public class MyRect
    {
        // 构造函数
        public MyRect()
        {
        }

        public MyRect(double left, double top, double width, double height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        // 属性
        public double Width { get; set; }
        public double Height { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
    }

    /// <summary>
    ///     模块加载状态枚举。
    /// </summary>
    public enum LoadState
    {
        Waiting,
        Loading,
        Finished,
        Failed,
        Aborted
    }

    /// <summary>
    ///     执行返回值。
    /// </summary>
    public enum ProcessReturnValues
    {
        /// <summary>
        ///     执行成功，或进程被中断。
        /// </summary>
        Aborted = -1,

        /// <summary>
        ///     执行成功。
        /// </summary>
        Success = 0,

        /// <summary>
        ///     执行失败。
        /// </summary>
        Fail = 1,

        /// <summary>
        ///     执行时出现未经处理的异常。
        /// </summary>
        Exception = 2,

        /// <summary>
        ///     执行超时。
        /// </summary>
        Timeout = 3,

        /// <summary>
        ///     取消执行。可能是由于不满足执行的前置条件。
        /// </summary>
        Cancel = 4,

        /// <summary>
        ///     任务成功完成。
        /// </summary>
        TaskDone = 5
    }

    /// <summary>
    ///     可以使用 Equals 和等号的 List。
    /// </summary>
    public class EqualableList<T> : List<T>
    {
        public override bool Equals(object obj)
        {
            if (obj as List<T> is null)
                // 类型不同
                return false;

            // 类型相同
            var objList = (List<T>)obj;
            if (objList.Count != Count)
                return false;
            for (int i = 0, loopTo = objList.Count - 1; i <= loopTo; i++)
                if (!objList[i].Equals(this[i]))
                    return false;
            return true;
        }

        public static bool operator ==(EqualableList<T> left, EqualableList<T> right)
        {
            return EqualityComparer<EqualableList<T>>.Default.Equals(left, right);
        }

        public static bool operator !=(EqualableList<T> left, EqualableList<T> right)
        {
            return !(left == right);
        }
    }

    #endregion

    #region 数学

    /// <summary>
    ///     2~65 进制的转换。
    /// </summary>
    public static string RadixConvert(string input, int fromRadix, int toRadix)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz/+=";
        // 零与负数的处理
        if (string.IsNullOrEmpty(input))
            return "0";
        var isNegative = input.StartsWithF("-");
        if (isNegative)
            input = input.TrimStart('-');
        // 转换为十进制
        var realNum = 0L;
        var scale = 1L;
        foreach (var digit in input.Reverse().Select(l => digits.IndexOfF(l.ToString())))
        {
            realNum += digit * scale;
            scale *= fromRadix;
        }

        // 转换为指定进制
        var result = "";
        while (realNum > 0L)
        {
            var newNum = (int)(realNum % toRadix);
            realNum = (long)Math.Round((realNum - newNum) / (double)toRadix);
            result = digits[newNum] + result;
        }

        // 负数的结束处理与返回
        return (isNegative ? "-" : "") + result;
    }

    /// <summary>
    ///     计算二阶贝塞尔曲线。
    /// </summary>
    public static double MathBezier(double x, double x1, double y1, double x2, double y2, double acc = 0.01d)
    {
        if (x <= 0d || double.IsNaN(x)) return 0d;
        if (x >= 1d) return 1d;
        double a, b;
        a = x;
        do
        {
            b = 3 * a * ((0.33333333 + x1 - x2) * a * a + (x2 - 2 * x1) * a + x1);
            a += (x - b) * 0.5;
        } while (!(Math.Abs(b - x) < acc)); // 精度

        return 3 * a * ((0.33333333 + y1 - y2) * a * a + (y2 - 2 * y1) * a + y1);
    }

    /// <summary>
    ///     将一个数字限制为 0~255 的 Byte 值。
    /// </summary>
    public static byte MathByte(double d)
    {
        if (d < 0d)
            d = 0d;
        if (d > 255d)
            d = 255d;
        return (byte)Math.Round(Math.Round(d));
    }

    /// <summary>
    ///     提供 MyColor 类型支持的 Math.Round。
    /// </summary>
    public static MyColor MathRound(MyColor col, int w = 0)
    {
        return new MyColor
            { a = Math.Round(col.a, w), r = Math.Round(col.r, w), g = Math.Round(col.g, w), b = Math.Round(col.b, w) };
    }

    /// <summary>
    ///     获取两数间的百分比。小数点精确到 6 位。
    /// </summary>
    /// <returns></returns>
    public static double MathPercent(double valueA, double valueB, double percent)
    {
        return Math.Round(valueA * (1d - percent) + valueB * percent, 6); // 解决 Double 计算错误
    }

    /// <summary>
    ///     获取两颜色间的百分比，根据 RGB 计算。小数点精确到 6 位。
    /// </summary>
    public static MyColor MathPercent(MyColor valueA, MyColor valueB, double percent)
    {
        return MathRound(valueA * (1d - percent) + valueB * percent, 6); // 解决Double计算错误
    }

    /// <summary>
    ///     将数值限定在某个范围内。
    /// </summary>
    public static double MathClamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    /// <summary>
    ///     符号函数。
    /// </summary>
    public static int MathSgn(double value)
    {
        if (value == 0d) return 0;

        if (value > 0d) return 1;

        return -1;
    }

    #endregion

    #region 文件

    // =============================
    // ini
    // =============================

    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> iniCache = new();

    /// <summary>
    ///     清除某 ini 文件的运行时缓存。
    /// </summary>
    /// <param name="fileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
    public static void IniClearCache(string fileName)
    {
        if (!fileName.Contains(@":\"))
            fileName = $@"{exePath}PCL\{fileName}.ini";
        if (iniCache.ContainsKey(fileName))
            iniCache.Remove(fileName, out _);
    }

    /// <summary>
    ///     获取 ini 文件缓存。如果没有，则新读取 ini 文件内容。
    ///     在文件不存在或读取失败时返回 Nothing。
    /// </summary>
    /// <param name="fileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
    private static ConcurrentDictionary<string, string> IniGetContent(string fileName)
    {
        try
        {
            // 还原文件路径
            if (!fileName.Contains(@":\"))
                fileName = $@"{exePath}PCL\{fileName}.ini";
            // 检索缓存
            if (iniCache.ContainsKey(fileName))
                return iniCache[fileName];
            // 读取文件
            if (!File.Exists(fileName))
                return null;
            var ini = new ConcurrentDictionary<string, string>();
            foreach (var line in ReadFile(fileName)
                         .Split("\r\n".ToArray(), StringSplitOptions.RemoveEmptyEntries))
            {
                var index = line.IndexOfF(":");
                if (index > 0)
                    ini[line.Substring(0, index)] = line.Substring(index + 1); // 可能会有重复键，见 #3616
            }

            iniCache[fileName] = ini;
            return ini;
        }
        catch (Exception ex)
        {
            Log(ex, $"生成 ini 文件缓存失败（{fileName}）", LogLevel.Hint);
            return null;
        }
    }

    /// <summary>
    ///     读取 ini 文件。这可能会使用到缓存。
    /// </summary>
    /// <param name="fileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
    /// <param name="key">键。</param>
    /// <param name="defaultValue">没有找到键时返回的默认值。</param>
    public static string ReadIni(string fileName, string key, string defaultValue = "")
    {
        var content = IniGetContent(fileName);
        if (content is null || !content.ContainsKey(key))
            return defaultValue;
        return content[key];
    }

    /// <summary>
    ///     判断 ini 文件中是否包含某个键。这可能会使用到缓存。
    /// </summary>
    public static bool HasIniKey(string fileName, string key)
    {
        var content = IniGetContent(fileName);
        return content is not null && content.ContainsKey(key);
    }

    /// <summary>
    ///     从 ini 文件中移除某个键。这会更新缓存。
    /// </summary>
    public static void DeleteIniKey(string fileName, string key)
    {
        WriteIni(fileName, key, null);
    }

    /// <summary>
    ///     写入 ini 文件，这会更新缓存。
    ///     若 Value 为 Nothing，则删除该键。
    /// </summary>
    /// <param name="fileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    /// <remarks></remarks>
    public static void WriteIni(string fileName, string key, string value)
    {
        try
        {
            // 预处理
            if (key.Contains(":"))
                throw new Exception($"尝试写入 ini 文件 {fileName} 的键名中包含了冒号：{key}");
            key = key.Replace("\r", "").Replace("\n", "");
            value = value?.Replace("\r", "").Replace("\n", "");
            // 防止争用
            lock (writeIniLock)
            {
                // 获取目前文件
                var content = IniGetContent(fileName);
                if (content is null)
                    content = new ConcurrentDictionary<string, string>();
                // 更新值
                if (value is null)
                {
                    if (!content.ContainsKey(key))
                        return; // 无需处理
                    content.Remove(key, out _);
                }
                else
                {
                    if (content.ContainsKey(key) && (content[key] ?? "") == (value ?? ""))
                        return; // 无需处理
                    content[key] = value;
                }

                // 写入文件
                var fileContent = new StringBuilder();
                foreach (var pair in content)
                {
                    fileContent.Append(pair.Key);
                    fileContent.Append(":");
                    fileContent.Append(pair.Value);
                    fileContent.Append("\r\n");
                }

                if (!fileName.Contains(@":\"))
                    fileName = $@"{exePath}PCL\{fileName}.ini";
                WriteFile(fileName, fileContent.ToString());
            }
        }
        catch (Exception ex)
        {
            Log(ex, $"写入文件失败（{fileName} → {key}:{value}）", LogLevel.Hint);
        }
    }

    private static readonly object writeIniLock = new();

    // 路径处理
    /// <summary>
    ///     从文件路径或者 Url 获取不包含文件名的路径，或获取文件夹的父文件夹路径。
    ///     取决于原路径格式，路径以 / 或 \ 结尾。
    ///     不包含路径将会抛出异常。
    /// </summary>
    public static string GetPathFromFullPath(string filePath)
    {
        string getPathFromFullPathRet = default;
        if (!(filePath.Contains(@"\") || filePath.Contains("/")))
            throw new Exception("不包含路径：" + filePath);
        if (filePath.EndsWithF(@"\") || filePath.EndsWithF("/"))
        {
            // 是文件夹路径
            var isRight = filePath.EndsWithF(@"\");
            filePath = filePath.Substring(0, filePath.Length - 1);
            getPathFromFullPathRet = filePath.Substring(0, filePath.LastIndexOfAny(new[] { '\\', '/' })) +
                                     (isRight ? @"\" : "/");
        }
        else
        {
            // 是文件路径
            getPathFromFullPathRet = filePath.Substring(0, filePath.LastIndexOfAny(new[] { '\\', '/' }) + 1);
            if (string.IsNullOrEmpty(getPathFromFullPathRet))
                throw new Exception("不包含路径：" + filePath);
        }

        return getPathFromFullPathRet;
    }

    /// <summary>
    ///     从文件路径或者 Url 获取不包含路径的文件名。不包含文件名将会抛出异常。
    /// </summary>
    public static string GetFileNameFromPath(string filePath)
    {
        filePath = filePath.Replace("/", @"\");
        if (filePath.EndsWithF(@"\"))
            throw new Exception("不包含文件名：" + filePath);
        if (filePath.Contains("?"))
            filePath = filePath.Substring(0, filePath.IndexOfF("?")); // 去掉网络参数后的 ?
        if (filePath.Contains(@"\"))
            filePath = filePath.Substring(filePath.LastIndexOfF(@"\") + 1);
        var length = filePath.Length;
        if (length == 0)
            throw new Exception("不包含文件名：" + filePath);
        if (length > 250)
            throw new PathTooLongException("文件名过长：" + filePath);
        return filePath;
    }

    /// <summary>
    ///     从文件路径或者 Url 获取不包含路径与扩展名的文件名。不包含文件名将会抛出异常。
    /// </summary>
    public static string GetFileNameWithoutExtentionFromPath(string filePath)
    {
        return Path.GetFileNameWithoutExtension(filePath);
    }

    /// <summary>
    ///     从文件夹路径获取文件夹名。
    /// </summary>
    public static string GetFolderNameFromPath(string folderPath)
    {
        if (folderPath.EndsWithF(@":\") || folderPath.EndsWithF(@":\\"))
            return folderPath.Substring(0, 1);
        if (folderPath.EndsWithF(@"\") || folderPath.EndsWithF("/"))
            folderPath = folderPath.Substring(0, folderPath.Length - 1);
        return GetFileNameFromPath(folderPath);
    }

    // 读取、写入、复制文件
    /// <summary>
    ///     复制文件。会自动创建文件夹、会覆盖已有的文件。
    /// </summary>
    public static void CopyFile(string fromPath, string toPath)
    {
        try
        {
            // 还原文件路径
            if (!fromPath.Contains(@":\"))
                fromPath = exePath + fromPath;
            if (!toPath.Contains(@":\"))
                toPath = exePath + toPath;
            // 如果复制同一个文件则跳过
            if ((fromPath ?? "") == (toPath ?? ""))
                return;
            // 确保目录存在
            Directory.CreateDirectory(GetPathFromFullPath(toPath));
            // 复制文件
            File.Copy(fromPath, toPath, true);
        }
        catch (Exception ex)
        {
            throw new Exception("复制文件出错：" + fromPath + " → " + toPath, ex);
        }
    }

    /// <summary>
    ///     读取文件，如果失败则返回空数组。
    /// </summary>
    public static byte[] ReadFileBytes(string filePath, Encoding encoding = null)
    {
        try
        {
            // 还原文件路径
            if (!filePath.Contains(@":\"))
                filePath = exePath + filePath;
            if (File.Exists(filePath))
                using (var readStream =
                       new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using (var ms = new MemoryStream())
                    {
                        readStream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }

            Log("[System] 欲读取的文件不存在，已返回空内容：" + filePath);
            return Array.Empty<byte>();
        }
        catch (Exception ex)
        {
            Log(ex, "读取文件出错：" + filePath);
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    ///     读取文件，如果失败则返回空字符串。
    /// </summary>
    /// <param name="filePath">文件完整或相对路径。</param>
    public static string ReadFile(string filePath, Encoding encoding = null)
    {
        string readFileRet = default;
        var fileBytes = ReadFileBytes(filePath);
        readFileRet = encoding is null ? DecodeBytes(fileBytes) : encoding.GetString(fileBytes);
        return readFileRet;
    }

    /// <summary>
    ///     读取流中的所有文本。
    /// </summary>
    public static string ReadFile(Stream stream, Encoding encoding = null)
    {
        try
        {
            var readedContent = new MemoryStream();
            stream.CopyTo(readedContent);
            var bts = readedContent.ToArray();
            return (encoding ?? EncodingDetector.DetectEncoding(bts)).GetString(bts);
        }
        catch (Exception ex)
        {
            Log(ex, "读取流出错");
            return "";
        }
    }

    /// <summary>
    ///     写入文件。
    /// </summary>
    /// <param name="filePath">文件完整或相对路径。</param>
    /// <param name="text">文件内容。</param>
    /// <param name="append">是否将文件内容追加到当前文件，而不是覆盖它。</param>
    public static void WriteFile(string filePath, string text, bool append = false, Encoding? encoding = null)
    {
        // 处理相对路径
        if (!filePath.Contains(@":\"))
            filePath = exePath + filePath;
        // 确保目录存在
        Directory.CreateDirectory(GetPathFromFullPath(filePath));
        // 写入文件
        if (append)
            // 追加目前文件
            using (var writer = new StreamWriter(filePath, true,
                       encoding ?? EncodingDetector.DetectEncoding(ReadFileBytes(filePath))))
            {
                writer.Write(text);
            }
            else
            {
                // 直接写入字节
                var bytes = encoding is null ? new UTF8Encoding(false).GetBytes(text) : encoding.GetBytes(text);
                var tempPath = filePath + ".pcltmp." + Guid.NewGuid().ToString("N");
                File.WriteAllBytes(tempPath, bytes);
                File.Move(tempPath, filePath, true);
            }
    }

    /// <summary>
    ///     写入文件。
    ///     如果 CanThrow 设置为 False，返回是否写入成功。
    /// </summary>
    /// <param name="filePath">文件完整或相对路径。</param>
    /// <param name="content">文件内容。</param>
    /// <param name="append">是否将文件内容追加到当前文件，而不是覆盖它。</param>
    public static void WriteFile(string filePath, byte[] content, bool append = false)
    {
        // 处理相对路径
        if (!filePath.Contains(@":\"))
            filePath = exePath + filePath;
        // 确保目录存在
        Directory.CreateDirectory(GetPathFromFullPath(filePath));
        // 写入文件
        File.WriteAllBytes(filePath, content);
    }

    /// <summary>
    ///     将流写入文件。
    /// </summary>
    /// <param name="filePath">文件完整或相对路径。</param>
    public static bool WriteFile(string filePath, Stream stream)
    {
        try
        {
            // 还原文件路径
            if (!filePath.Contains(@":\"))
                filePath = exePath + filePath;
            // 确保目录存在
            Directory.CreateDirectory(GetPathFromFullPath(filePath));
            // 读取流
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                fs.SetLength(0L);
                stream.CopyTo(fs);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log(ex, "保存流出错");
            return false;
        }
    }

    /// <summary>
    ///     解码 Bytes。
    /// </summary>
    public static string DecodeBytes(byte[] bytes)
    {
        var length = bytes.Length;
        if (length < 3)
            return Encoding.UTF8.GetString(bytes);
        // 根据 BOM 判断编码
        if (bytes[0] >= 0xEF)
        {
            // 有 BOM 类型
            if (bytes[0] == 0xEF && bytes[1] == 0xBB) return Encoding.UTF8.GetString(bytes, 3, length - 3);

            if (bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 3, length - 3);

            if (bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 3, length - 3);

            return Encoding.GetEncoding("GB18030").GetString(bytes, 3, length - 3);
        }

        // 无 BOM 文件：GB18030（ANSI）或 UTF8
        var uTF8 = Encoding.UTF8.GetString(bytes);
        var errorChar = Encoding.UTF8.GetString(new[] { (byte)239, (byte)191, (byte)189 }).ToCharArray()[0];
        if (uTF8.Contains(errorChar)) return Encoding.GetEncoding("GB18030").GetString(bytes);

        return uTF8;
    }

    public static object GetHexString(Memory<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var c in bytes.Span)
            sb.Append(c.ToString("x2"));

        return sb.ToString();
    }

    // 文件校验
    /// <summary>
    ///     获取文件 MD5，若失败则返回空字符串。
    /// </summary>
    public static string GetFileMD5(string filePath)
    {
        var retry = false;
        Re: ;

        try
        {
            // 获取 MD5
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return (string)GetHexString(MD5Provider.Instance.ComputeHash(fs));
            }
        }
        catch (Exception ex)
        {
            if (retry || ex is FileNotFoundException)
            {
                Log(ex, "获取文件 MD5 失败：" + filePath);
                return "";
            }

            retry = true;
            Log(ex, "获取文件 MD5 可重试失败：" + filePath, LogLevel.Normal);
            Thread.Sleep(RandomUtils.NextInt(200, 500));
            goto Re;
        }
    }

    /// <summary>
    ///     获取文件 SHA512，若失败则返回空字符串。
    /// </summary>
    public static string GetFileSHA512(string filePath)
    {
        var retry = false;
        Re: ;

        try
        {
            // '检测该文件是否在下载中，若在下载则放弃检测
            // If IgnoreOnDownloading AndAlso NetManage.Files.ContainsKey(FilePath) AndAlso NetManage.Files(FilePath).State <= NetState.Merge Then Return ""
            // 获取 SHA512
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return (string)GetHexString(SHA512Provider.Instance.ComputeHash(fs));
            }
        }
        catch (Exception ex)
        {
            if (retry || ex is FileNotFoundException)
            {
                Log(ex, "获取文件 SHA512 失败：" + filePath);
                return "";
            }

            retry = true;
            Log(ex, "获取文件 SHA512 可重试失败：" + filePath, LogLevel.Normal);
            Thread.Sleep(RandomUtils.NextInt(200, 500));
            goto Re;
        }
    }

    /// <summary>
    ///     获取文件 SHA256，若失败则返回空字符串。
    /// </summary>
    public static string GetFileSHA256(string filePath)
    {
        var retry = false;
        Re: ;

        try
        {
            // '检测该文件是否在下载中，若在下载则放弃检测
            // If IgnoreOnDownloading AndAlso NetManage.Files.ContainsKey(FilePath) AndAlso NetManage.Files(FilePath).State <= NetState.Merge Then Return ""
            // 获取 SHA256
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return (string)GetHexString(SHA256Provider.Instance.ComputeHash(fs));
            }
        }
        catch (Exception ex)
        {
            if (retry || ex is FileNotFoundException)
            {
                Log(ex, "获取文件 SHA256 失败：" + filePath);
                return "";
            }

            retry = true;
            Log(ex, "获取文件 SHA256 可重试失败：" + filePath, LogLevel.Normal);
            Thread.Sleep(RandomUtils.NextInt(200, 500));
            goto Re;
        }
    }

    /// <summary>
    ///     获取文件 SHA1，若失败则返回空字符串。
    /// </summary>
    public static string GetFileSHA1(string filePath)
    {
        var retry = false;
        Re: ;

        try
        {
            // 获取 SHA1
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return (string)GetHexString(SHA1Provider.Instance.ComputeHash(fs));
            }
        }
        catch (Exception ex)
        {
            if (retry || ex is FileNotFoundException)
            {
                Log(ex, "获取文件 SHA1 失败：" + filePath);
                return "";
            }

            retry = true;
            Log(ex, "获取文件 SHA1 可重试失败：" + filePath, LogLevel.Normal);
            Thread.Sleep(RandomUtils.NextInt(200, 500));
            goto Re;
        }
    }

    /// <summary>
    ///     获取流的 SHA1，若失败则返回空字符串。
    /// </summary>
    public static string GetAuthSHA1(Stream inputStream)
    {
        try
        {
            return (string)GetHexString(SHA1Provider.Instance.ComputeHash(inputStream));
        }
        catch (Exception ex)
        {
            Log(ex, "获取流 SHA1 失败");
            return "";
        }
    }

    /// <summary>
    ///     文件的校验规则。
    /// </summary>
    public class FileChecker
    {
        /// <summary>
        ///     文件的准确大小。
        ///     不检查则为 -1。
        /// </summary>
        public long actualSize = -1;

        /// <summary>
        ///     是否可以使用已经存在的文件。
        /// </summary>
        public bool canUseExistsFile = true;

        /// <summary>
        ///     文件的 MD5、SHA1 或 SHA256。会根据输入字符串的长度自动判断种类。
        ///     不检查则为 Nothing。
        /// </summary>
        public string hash;

        /// <summary>
        ///     是否要求为 JSON 文件。
        ///     即，开头结尾必须为 {} 或 []。
        /// </summary>
        public bool isJson;

        /// <summary>
        ///     文件的最小大小。
        ///     不检查则为 -1。
        /// </summary>
        public long minSize = -1;

        public FileChecker(long minSize = -1, long actualSize = -1, string hash = null, bool canUseExistsFile = true,
            bool isJson = false)
        {
            this.actualSize = actualSize;
            this.minSize = minSize;
            this.hash = hash;
            this.canUseExistsFile = canUseExistsFile;
            this.isJson = isJson;
        }

        /// <summary>
        ///     检查文件。若成功则返回 Nothing，失败则返回错误的描述文本，描述文本不以句号结尾。不会抛出错误。
        /// </summary>
        public string Check(string localPath)
        {
            try
            {
                Log($"[Checker] 开始校验文件 {localPath}", LogLevel.Developer);
                var info = new FileInfo(localPath);
                if (!info.Exists)
                    return "文件不存在：" + localPath;
                var fileSize = info.Length;
                var errorMessage = new List<string>();
                var allowIgnore = false; // 允许相信哈希正确但是大小不正确
                if (!string.IsNullOrEmpty(hash))
                {
                    if (hash.Length < 35) // MD5
                    {
                        var computedHash = GetFileMD5(localPath);
                        if ((hash.ToLowerInvariant() ?? "") != (computedHash ?? ""))
                            errorMessage.Add("文件 MD5 应为 " + hash + "，实际为 " + computedHash);
                    }
                    else if (hash.Length == 64) // SHA256
                    {
                        var computedHash = GetFileSHA256(localPath);
                        if ((hash.ToLowerInvariant() ?? "") != (computedHash ?? ""))
                            errorMessage.Add("文件 SHA256 应为 " + hash + "，实际为 " + computedHash);
                    }
                    else // SHA1 (40)
                    {
                        var computedHash = GetFileSHA1(localPath);
                        if ((hash.ToLowerInvariant() ?? "") != (computedHash ?? ""))
                            errorMessage.Add("文件 SHA1 应为 " + hash + "，实际为 " + computedHash);
                    }

                    allowIgnore = errorMessage.Count == 0;
                }

                if (actualSize >= 0L && actualSize != fileSize && !allowIgnore) // 不允许忽略大小不正确的情况
                    errorMessage.Add($"文件大小应为 {actualSize} B，实际为 {fileSize} B" +
                                     (fileSize < 2000L ? "，内容为" + ReadFile(localPath) : ""));

                if (minSize >= 0L && minSize > fileSize)
                    errorMessage.Add($"文件大小应大于 {minSize} B，实际为 {fileSize} B" +
                                     (fileSize < 2000L ? "，内容为：" + ReadFile(localPath) : ""));

                if (isJson)
                {
                    var content = ReadFile(localPath);
                    if (string.IsNullOrEmpty(content))
                        throw new Exception("读取到的文件为空");
                    try
                    {
                        GetJson(content);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(Lang.Text("Common.Error.InvalidJson"), ex);
                    }
                }

                if (errorMessage.Count != 0)
                {
                    errorMessage.Insert(0, $"实际校验地址：{localPath}");
                    return errorMessage.Join(";");
                }

                return null;
            }
            catch (Exception ex)
            {
                Log(ex, "检查文件出错");
                return ex.ToString();
            }
        }
    }

    /// <summary>
    ///     等待文件就绪可读，在指定超时时间内轮询检查文件是否存在且内容非空。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="timeoutMs">超时时间（毫秒）。</param>
    public static void WaitForFileReady(string filePath, int timeoutMs = 2000)
    {
        WaitForFileReady(filePath, timeoutMs, false);
    }

    /// <summary>
    ///     等待文件就绪可读，在指定超时时间内轮询检查文件是否存在且内容非空。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="timeoutMs">超时时间（毫秒）。</param>
    /// <param name="requireJson">是否要求文件为合法 JSON。</param>
    public static void WaitForFileReady(string filePath, int timeoutMs, bool requireJson)
    {
        filePath = filePath.Contains(@":\") ? filePath : exePath + filePath;
        var start = Environment.TickCount;
        long lastSize = -1;
        while (Environment.TickCount - start < timeoutMs)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    var info = new FileInfo(filePath);
                    var size = info.Length;
                    if (size <= 0)
                        continue;
                    if (!requireJson)
                    {
                        if (size == lastSize)
                            return;
                        lastSize = size;
                    }
                    else
                    {
                        var content = ReadFile(filePath);
                        if (!string.IsNullOrEmpty(content) && content.Trim().StartsWith("{"))
                            return;
                    }
                }
                catch (IOException)
                {
                }
            }
            Thread.Sleep(50);
        }
    }

    /// <summary>
    ///     尝试根据后缀名判断文件种类并解压文件，支持 gz 与 zip，会尝试将 Jar 以 zip 方式解压。
    ///     会尝试创建，但不会清空目标文件夹。
    /// </summary>
    public static void ExtractFile(string compressFilePath, string destDirectory, Encoding encode = null,
        Action<double> progressIncrementHandler = null)
    {
        Directory.CreateDirectory(destDirectory);
        destDirectory = Path.GetFullPath(destDirectory);
        if (!destDirectory.EndsWith(Path.DirectorySeparatorChar.ToString()))
            destDirectory += Path.DirectorySeparatorChar.ToString();
        if (compressFilePath.EndsWithF(".gz", true))
            // 以 gz 方式解压
            using (var compressedFile = new FileStream(compressFilePath, FileMode.Open, FileAccess.Read))
            {
                using (var decompressStream = new GZipStream(compressedFile, CompressionMode.Decompress))
                {
                    using (var extractFileStream =
                           new FileStream(
                               Path.Combine(destDirectory,
                                   GetFileNameFromPath(compressFilePath).ToLower().Replace(".tar", "")
                                       .Replace(".gz", "")), FileMode.OpenOrCreate, FileAccess.Write))
                    {
                        decompressStream.CopyTo(extractFileStream);
                    }
                }
            }
        else
            // 以 zip 方式解压
            using (var archive = ZipFile.Open(compressFilePath, ZipArchiveMode.Read,
                       encode ?? Encoding.GetEncoding("GB18030")))
            {
                var totalCount = archive.Entries.Count;
                foreach (var entry in archive.Entries)
                {
                    if (progressIncrementHandler is not null)
                        progressIncrementHandler(1d / totalCount);
                    var destinationPath = Path.GetFullPath(Path.Combine(destDirectory, entry.FullName));
                    if (!destinationPath.StartsWithF(destDirectory))
                        throw new Exception(
                            $"解压文件 {entry.FullName} 错误：解压文件路径 {destinationPath} 不在目标目录 {destDirectory} 内");
                    if (destinationPath.EndsWithF(@"\") || destinationPath.EndsWithF("/"))
                    {
                    }
                    else
                    {
                        Directory.CreateDirectory(GetPathFromFullPath(destinationPath));
                        entry.ExtractToFile(destinationPath, true);
                    }
                }
            }
    }

    /// <summary>
    ///     删除文件夹，返回删除的文件个数。通过参数选择是否抛出异常。
    /// </summary>
    public static int DeleteDirectory(string path, bool ignoreIssue = false)
    {
        if (!Directory.Exists(path))
            return 0;
        var deletedCount = 0;
        string[] files;
        try
        {
            files = Directory.GetFiles(path);
        }
        catch (DirectoryNotFoundException ex) // #4549
        {
            Log(ex, $"疑似为孤立符号链接，尝试直接删除（{path}）", LogLevel.Developer);
            Directory.Delete(path);
            return 0;
        }

        foreach (var filePath in files)
        {
            var retriedFile = false;
            RetryFile: ;

            try
            {
                File.Delete(filePath);
                deletedCount += 1;
            }
            catch (Exception ex)
            {
                if (!retriedFile)
                {
                    retriedFile = true;
                    Log(ex, $"删除文件失败，将在 0.3s 后重试（{filePath}）");
                    Thread.Sleep(300);
                    goto RetryFile;
                }

                if (ignoreIssue)
                    Log(ex, "删除单个文件可忽略地失败");
                else
                    throw;
            }
        }

        foreach (var str in Directory.GetDirectories(path))
            DeleteDirectory(str, ignoreIssue);
        var retriedDir = false;
        RetryDir: ;

        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            if (!retriedDir && !RunInUi())
            {
                retriedDir = true;
                Log(ex, $"删除文件夹失败，将在 0.3s 后重试（{path}）");
                Thread.Sleep(300);
                goto RetryDir;
            }

            if (ignoreIssue)
                Log(ex, "删除单个文件夹可忽略地失败");
            else
                throw;
        }

        return deletedCount;
    }

    /// <summary>
    ///     复制文件夹，失败会抛出异常。
    /// </summary>
    public static void CopyDirectory(string fromPath, string toPath, Action<double> progressIncrementHandler = null)
    {
        fromPath = fromPath.Replace("/", @"\");
        if (!fromPath.EndsWithF(@"\"))
            fromPath += @"\";
        toPath = toPath.Replace("/", @"\");
        if (!toPath.EndsWithF(@"\"))
            toPath += @"\";
        var allFiles = EnumerateFiles(fromPath).ToList();
        var fileCount = allFiles.Count;
        foreach (var file in allFiles)
        {
            CopyFile(file.FullName, file.FullName.Replace(fromPath, toPath));
            if (progressIncrementHandler is not null)
                progressIncrementHandler(1d / fileCount);
        }
    }

    /// <summary>
    ///     遍历文件夹中的所有文件。
    /// </summary>
    public static IEnumerable<FileInfo> EnumerateFiles(string directory)
    {
        var info = new DirectoryInfo(ShortenPath(directory));
        if (!info.Exists)
            return new List<FileInfo>();
        return info.EnumerateFiles("*", SearchOption.AllDirectories);
    }

    /// <summary>
    ///     若路径长度大于指定值，则将长路径转换为短路径。
    /// </summary>
    public static string ShortenPath(string longPath, int shortenThreshold = 247)
    {
        if (longPath.Length <= shortenThreshold)
            return longPath;
        var shortPath = new StringBuilder(260);
        GetShortPathName(longPath, shortPath, 260);
        return shortPath.ToString();
    }

    public static void MoveDirectory(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);
        foreach (var filePath in Directory.GetFiles(sourceDir))
        {
            var fileName = GetFileNameFromPath(filePath);
            File.Move(filePath, Path.Combine(targetDir, fileName));
        }

        foreach (var dirPath in Directory.GetDirectories(sourceDir))
        {
            var dirName = GetFolderNameFromPath(dirPath);
            MoveDirectory(dirPath, Path.Combine(targetDir, dirName));
        }
    }

    [DllImport("kernel32", EntryPoint = "GetShortPathNameA")]
    private static extern int GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

    public static void CreateSymbolicLink(string linkPath, string targetPath, int flags)
    {
        var cMDProcess = new Process();
        var linkDPath = ModLaunch.ExtractLinkD();
        {
            var withBlock = cMDProcess.StartInfo;
            withBlock.FileName = linkDPath;
            withBlock.Arguments = $"\"{linkPath}\" \"{targetPath}\"";
            withBlock.CreateNoWindow = true;
            withBlock.UseShellExecute = false;
        }
        cMDProcess.Start();
        while (!cMDProcess.HasExited)
        {
        }
    }

    #endregion

    #region 文本

    public static char vbLQ = Convert.ToChar(8220);
    public static char vbRQ = Convert.ToChar(8221);

    /// <summary>
    ///     返回一个枚举对应的字符串。
    /// </summary>
    /// <param name="enumData">一个已经实例化的枚举类型。</param>
    public static string GetStringFromEnum(Enum enumData)
    {
        return Enum.GetName(enumData.GetType(), enumData);
    }

    /// <summary>
    ///     将文件大小转化为适合的文本形式，如“1.28 M”。
    /// </summary>
    /// <param name="fileSize">以字节为单位的大小表示。</param>
    public static string GetString(long fileSize)
    {
        return ByteStream.GetReadableLength(fileSize, provider: Lang.Culture);
    }

    /// <summary>
    ///     获取 JSON 对象。
    /// </summary>
    public static JsonNode GetJson(string data)
    {
        try
        {
            return JsonCompat.ParseNode(data);
        }
        catch (Exception ex)
        {
            var dataText = data ?? "";
            var length = dataText.Length;
            throw new Exception("格式化 JSON 失败：" + (length > 2000
                ? dataText.Substring(0, 500) + $"...(全长 {length} 个字符)..." + dataText.Substring(length - 500)
                : dataText), ex);
        }
    }

    /// <summary>
    ///     将第一个字符转换为大写，其余字符转换为小写。
    /// </summary>
    public static string Capitalize(this string word)
    {
        if (string.IsNullOrEmpty(word))
            return word;
        return word.Substring(0, 1).ToUpperInvariant() + word.Substring(1).ToLowerInvariant();
    }

    /// <summary>
    ///     将字符串统一至某个长度，过短则以 Code 将其右侧填充，过长则截取靠左的指定长度。
    /// </summary>
    public static string StrFill(string str, string code, byte length)
    {
        if (str.Length > length)
            return str.Substring(0, length);
        return str.PadRight(length, code[0]).Substring(str.Length) + str;
    }

    /// <summary>
    ///     将一个小数显示为固定的小数点后位数形式，将向零取整。
    ///     如 12 保留 2 位则输出 12.00，而 95.678 保留 2 位则输出 95.67。
    /// </summary>
    public static string StrFillNum(double num, int length)
    {
        return Lang.Number(num, $"F{length}");
    }

    /// <summary>
    ///     移除字符串首尾的标点符号、回车，以及括号中、冒号后的补充说明内容。
    /// </summary>
    public static object StrTrim(string str, bool removeQuote = true)
    {
        if (removeQuote)
            str = str.Split("（")[0].Split("：")[0].Split("(")[0].Split(":")[0];
        return str.Trim('.', '。', '！', ' ', '!', '?', '？', '\r',
            '\n');
    }

    /// <summary>
    ///     连接字符串。
    /// </summary>
    public static string Join(this IEnumerable list, string split)
    {
        var builder = new StringBuilder();
        var isFirst = true;
        foreach (var element in list)
        {
            if (isFirst)
                isFirst = false;
            else
                builder.Append(split);
            if (element is not null)
                builder.Append(element);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     分割字符串。
    /// </summary>
    public static string[] Split(this string fullStr, string splitStr)
    {
        if (splitStr.Length == 1) return fullStr.Split(splitStr[0]);

        return fullStr.Split(new[] { splitStr }, StringSplitOptions.None);
    }

    /// <summary>
    ///     获取字符串哈希值。
    /// </summary>
    public static ulong GetHash(string str)
    {
        ulong getHashRet = default;
        getHashRet = 5381UL;
        for (int i = 0, loopTo = str.Length - 1; i <= loopTo; i++)
            getHashRet = (getHashRet << 5) ^ getHashRet ^ str[i];
        return getHashRet ^ 0xA98F501BC684032FUL;
    }

    /// <summary>
    ///     获取字符串 MD5。
    /// </summary>
    public static string GetStringMD5(string str)
    {
        return (string)GetHexString(MD5Provider.Instance.ComputeHash(str));
    }

    /// <summary>
    ///     检查字符串中的字符是否均为 ASCII 字符。
    /// </summary>
    public static bool IsASCII(this string input)
    {
        return input.All(c => c < 128);
    }

    /// <summary>
    ///     获取在子字符串第一次出现之前的部分，例如对 2024/11/08 拆切 / 会得到 2024。
    ///     如果未找到子字符串则不裁切。
    /// </summary>
    public static string BeforeFirst(this string str, string text, bool ignoreCase = false)
    {
        var pos = string.IsNullOrEmpty(text) ? -1 : str.IndexOfF(text, ignoreCase);
        if (pos >= 0) return str.Substring(0, pos);

        return str;
    }

    /// <summary>
    ///     获取在子字符串最后一次出现之前的部分，例如对 2024/11/08 拆切 / 会得到 2024/11。
    ///     如果未找到子字符串则不裁切。
    /// </summary>
    public static string BeforeLast(this string str, string text, bool ignoreCase = false)
    {
        var pos = string.IsNullOrEmpty(text) ? -1 : str.LastIndexOfF(text, ignoreCase);
        if (pos >= 0) return str.Substring(0, pos);

        return str;
    }

    /// <summary>
    ///     获取在子字符串第一次出现之后的部分，例如对 2024/11/08 拆切 / 会得到 11/08。
    ///     如果未找到子字符串则不裁切。
    /// </summary>
    public static string AfterFirst(this string str, string text, bool ignoreCase = false)
    {
        var pos = string.IsNullOrEmpty(text) ? -1 : str.IndexOfF(text, ignoreCase);
        if (pos >= 0) return str.Substring(pos + text.Length);

        return str;
    }

    /// <summary>
    ///     获取在子字符串最后一次出现之后的部分，例如对 2024/11/08 拆切 / 会得到 08。
    ///     如果未找到子字符串则不裁切。
    /// </summary>
    public static string AfterLast(this string str, string text, bool ignoreCase = false)
    {
        var pos = string.IsNullOrEmpty(text) ? -1 : str.LastIndexOfF(text, ignoreCase);
        if (pos >= 0) return str.Substring(pos + text.Length);

        return str;
    }

    /// <summary>
    ///     获取处于两个子字符串之间的部分，裁切尽可能多的内容。
    ///     等效于 AfterLast 后接 BeforeFirst。
    ///     如果未找到子字符串则不裁切。
    /// </summary>
    public static string Between(this string str, string after, string before, bool ignoreCase = false)
    {
        var startPos = string.IsNullOrEmpty(after) ? -1 : str.LastIndexOfF(after, ignoreCase);
        if (startPos >= 0)
            startPos += after.Length;
        else
            startPos = 0;
        var endPos = string.IsNullOrEmpty(before) ? -1 : str.IndexOfF(before, startPos, ignoreCase);
        if (endPos >= 0) return str.Substring(startPos, endPos - startPos);

        if (startPos > 0) return str.Substring(startPos);

        return str;
    }

    /// <summary>
    ///     高速的 StartsWith。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool StartsWithF(this string str, string prefix, bool ignoreCase = false)
    {
        return str.StartsWith(prefix, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    ///     高速的 EndsWith。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EndsWithF(this string str, string suffix, bool ignoreCase = false)
    {
        return str.EndsWith(suffix, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    ///     支持可变大小写判断的 Contains。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ContainsF(this string str, string subStr, bool ignoreCase = false)
    {
        return str.IndexOf(subStr, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    ///     高速的 IndexOf。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IndexOfF(this string str, string subStr, bool ignoreCase = false)
    {
        return str.IndexOf(subStr, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    ///     高速的 IndexOf。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IndexOfF(this string str, string subStr, int startIndex, bool ignoreCase = false)
    {
        return str.IndexOf(subStr, startIndex,
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    ///     高速的 LastIndexOf。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LastIndexOfF(this string str, string subStr, bool ignoreCase = false)
    {
        return str.LastIndexOf(subStr, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    ///     高速的 LastIndexOf。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LastIndexOfF(this string str, string subStr, int startIndex, bool ignoreCase = false)
    {
        return str.LastIndexOf(subStr, startIndex,
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    ///     不会报错的 Val。
    ///     如果输入有误，返回 0。
    /// </summary>
    public static double Val(object str)
    {
        try
        {
            return str is "&" ? 0d : Conversion.Val(str);
        }
        catch
        {
            return 0d;
        }
    }

    // 转义
    /// <summary>
    ///     为字符串进行 XML 转义。
    /// </summary>
    public static string EscapeXML(string str)
    {
        if (str.StartsWithF("{"))
            str = "{}" + str; // #4187
        return str.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&apos;")
            .Replace("\"", "&quot;").Replace("\r\n", "&#xa;");
    }

    /// <summary>
    ///     为字符串进行 Like 关键字转义。
    /// </summary>
    public static string EscapeLikePattern(string input)
    {
        var sb = new StringBuilder();
        foreach (var c in input)
            switch (c)
            {
                case '[':
                case ']':
                case '*':
                case '?':
                case '#':
                {
                    sb.Append('[').Append(c).Append(']');
                    break;
                }

                default:
                {
                    sb.Append(c);
                    break;
                }
            }

        return sb.ToString();
    }

    // 正则
    /// <summary>
    ///     搜索字符串中的所有正则匹配项。
    /// </summary>
    public static List<string> RegexSearch(this string str, string regex, RegexOptions options = RegexOptions.None)
    {
        List<string> regexSearchRet = default;
        try
        {
            regexSearchRet = new List<string>();
            var regexSearchRes = new Regex(regex, options).Matches(str);
            if (regexSearchRes is null)
                return regexSearchRet;
            foreach (Match item in regexSearchRes)
                regexSearchRet.Add(item.Value);
        }
        catch (Exception ex)
        {
            Log(ex, "正则匹配全部项出错");
            return new List<string>();
        }

        return regexSearchRet;
    }
    
    /// <summary>
    /// 搜索字符串中的所有正则匹配项。
    /// </summary>
    /// <param name="str">要搜索的字符串</param>
    /// <param name="regex">正则表达式对象</param>
    /// <returns>所有匹配项的列表</returns>
    public static List<string> RegexSearch(this string str, Regex regex)
    {
        try
        {
            var result = new List<string>();
            foreach (Match item in regex.Matches(str))
            {
                result.Add(item.Value);
            }
            return result;
        }
        catch (Exception ex)
        {
            Log(ex, "正则匹配全部项出错");
            return new List<string>();
        }
    }
    
    /// <summary>
    ///     获取字符串中的第一个正则匹配项，若无匹配则返回 Nothing。
    /// </summary>
    public static string RegexSeek(this string str, string regex, RegexOptions options = RegexOptions.None)
    {
        try
        {
            var result = Regex.Match(str, regex, options).Value;
            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch (Exception ex)
        {
            Log(ex, "正则匹配第一项出错");
            return null;
        }
    }

    /// <summary>
    ///     获取字符串中的第一个正则匹配项，若无匹配则返回 Nothing。
    /// </summary>
    public static string RegexSeek(this string str, Regex regex, RegexOptions options = RegexOptions.None)
    {
        try
        {
            var result = regex.Match(str, (int)options).Value;
            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch (Exception ex)
        {
            Log(ex, "正则匹配第一项出错");
            return null;
        }
    }

    /// <summary>
    ///     检查字符串是否匹配某正则模式。
    /// </summary>
    public static bool RegexCheck(this string str, string regex, RegexOptions options = RegexOptions.None)
    {
        try
        {
            return Regex.IsMatch(str, regex, options);
        }
        catch (Exception ex)
        {
            Log(ex, "正则检查出错");
            return false;
        }
    }

    /// <summary>
    ///     进行正则替换，会抛出错误。
    /// </summary>
    public static string RegexReplace(this string allContents, string searchRegex, string replaceTo,
        RegexOptions options = RegexOptions.None)
    {
        return Regex.Replace(allContents, searchRegex, replaceTo, options);
    }

    /// <summary>
    ///     对每个正则匹配分别进行替换，会抛出错误。
    /// </summary>
    public static string RegexReplaceEach(this string allContents, string searchRegex, MatchEvaluator replaceTo,
        RegexOptions options = RegexOptions.None)
    {
        return Regex.Replace(allContents, searchRegex, replaceTo, options);
    }

    #endregion

    #region 搜索

    /// <summary>
    ///     获取搜索文本的相似度。
    /// </summary>
    /// <param name="source">被搜索的长内容。</param>
    /// <param name="query">用户输入的搜索文本。</param>
    private static double SearchSimilarity(string source, string query)
    {
        var qp = 0;
        var lenSum = 0d;
        source = source.ToLower().Replace(" ", "");
        query = query.ToLower().Replace(" ", "");
        var sourceLength = source.Length;
        var queryLength = query.Length; // 用于计算最后因数的长度缓存
        while (qp < queryLength)
        {
            // 对 qp 作为开始位置计算
            var sp = 0;
            var lenMax = 0;
            var spMax = 0;
            // 查找以 qp 为头的最大子串
            while (sp < source.Length)
            {
                // 对每个 sp 作为开始位置计算最大子串
                var len = 0;
                while (qp + len < queryLength && sp + len < source.Length && source[sp + len] == query[qp + len])
                    len += 1;
                // 存储 len
                if (len > lenMax)
                {
                    lenMax = len;
                    spMax = sp;
                }

                // 根据结果增加 sp
                sp += Math.Max(1, len);
            }

            if (lenMax > 0)
            {
                source = source.Substring(0, spMax) +
                         (source.Count() > spMax + lenMax
                             ? source.Substring(spMax + lenMax)
                             : string.Empty); // 将源中的对应字段替换空
                // 存储 lenSum
                var incWeight = Math.Pow(1.4d, 3 + lenMax) - 3.6d; // 根据长度加成
                incWeight *= 1d + 0.3d * Math.Max(0, 3 - Math.Abs(qp - spMax)); // 根据位置加成
                lenSum += incWeight;
            }

            // 根据结果增加 qp
            qp += Math.Max(1, lenMax);
        }

        // 计算结果：重复字段量 × 源长度影响比例
        return lenSum / queryLength * (3d / Math.Pow(sourceLength + 15, 0.5d)) *
               (queryLength <= 2 ? 3 - queryLength : 1);
    }

    /// <summary>
    ///     获取多段文本加权后的相似度。
    /// </summary>
    private static double SearchSimilarityWeighted(List<SearchSource> source, string query)
    {
        var totalWeight = 0d;
        var sum = 0d;
        foreach (var pair in source)
        {
            if (pair.aliases.Any())
                sum += pair.aliases.Max(a => SearchSimilarity(a, query)) * pair.weight;
            totalWeight += pair.weight;
        }

        return sum / totalWeight;
    }

    /// <summary>
    ///     用于搜索的项目。
    /// </summary>
    public class SearchEntry<T>
    {
        /// <summary>
        ///     是否完全匹配。
        /// </summary>
        public bool absoluteRight;

        /// <summary>
        ///     该项目对应的源数据。
        /// </summary>
        public T item;

        /// <summary>
        ///     该项目用于搜索的文本源。
        ///     在搜索时，会对每个文本源单独加权，但单个文本源内的多个别名只取最高的一个的相似度。
        /// </summary>
        public List<SearchSource> searchSource;

        /// <summary>
        ///     相似度。
        /// </summary>
        public double similarity;
    }

    /// <summary>
    ///     单个用于搜索的文本源。
    /// </summary>
    public class SearchSource
    {
        public string[] aliases;
        public double weight;

        public SearchSource(string[] aliases, double weight = 1)
        {
            this.aliases = aliases;
            this.weight = weight;
        }

        public SearchSource(string text, double weight = 1)
        {
            aliases = new[] { text };
            this.weight = weight;
        }
    }

    /// <summary>
    ///     本地搜索返回的最大模糊结果数。
    /// </summary>
    public const int MaxLocalSearchDepth = 25;

    /// <summary>
    ///     进行多段文本加权搜索，获取相似度较高的数项结果。
    /// </summary>
    /// <param name="maxBlurCount">返回的最大模糊结果数。</param>
    /// <param name="minBlurSimilarity">返回结果要求的最低相似度。</param>
    public static List<SearchEntry<T>> Search<T>(List<SearchEntry<T>> entries, string query, int maxBlurCount = 5,
        double minBlurSimilarity = 0.1d)
    {
        var resultList = new List<SearchEntry<T>>();

        if (entries is null || !entries.Any()) return resultList;

        // Preprocess query into parts
        var queryParts = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (queryParts.Length == 0)
        {
            resultList.AddRange(entries);
            return resultList;
        }

        // Precompute query parts in lowercase for case-insensitive comparison
        var queryPartsLower = queryParts.Select(q => q.ToLower()).ToArray();

        // Process each entry to compute similarity and absolute match status
        foreach (var entry in entries)
        {
            entry.similarity = SearchSimilarityWeighted(entry.searchSource, query);

            // Preprocess search source keys: remove spaces and convert to lowercase
            var processedSources = entry.searchSource.Select(s =>
            {
                for (var i = 0; i < s.aliases.Length; i++)
                    s.aliases[i] = s.aliases[i].Replace(" ", "").ToLower();
                return s.aliases;
            }).ToList();

            // Check if all query parts are matched exactly by at least one source
            var isAbsoluteRight = true;
            foreach (var qp in queryPartsLower)
            {
                var found = false;
                foreach (var ps in processedSources)
                    if (ps.Any(p => p.Contains(qp)))
                    {
                        found = true;
                        break;
                    }

                if (!found)
                {
                    isAbsoluteRight = false;
                    break;
                }
            }

            entry.absoluteRight = isAbsoluteRight;
        }

        // Sort by absolute match (descending), then by similarity (descending)
        var sortedEntries = entries.OrderByDescending(e => e.absoluteRight).ThenByDescending(e => e.similarity)
            .ToList();

        // Build the final result list
        var blurCount = 0;
        foreach (var entry in sortedEntries)
            if (entry.absoluteRight)
            {
                resultList.Add(entry);
            }
            else
            {
                if (entry.similarity < minBlurSimilarity || blurCount >= maxBlurCount) break;
                resultList.Add(entry);
                blurCount += 1;
            }

        return resultList;
    }

    #endregion

    #region 系统

    public static bool IsUtf8CodePage()
    {
        return Encoding.Default.CodePage == 65001;
    }

    /// <summary>
    ///     线程安全的 List。
    ///     通过在 For Each 循环中使用一个浅表副本规避多线程操作或移除自身导致的异常。
    /// </summary>
    public class SafeList<T> : IEnumerable<T>, IDisposable, ICollection<T>
    {
        private readonly List<T> _internalList;
        private readonly ReaderWriterLockSlim _lock = new();

        public SafeList()
        {
            _internalList = new List<T>();
        }

        public SafeList(IEnumerable<T> data)
        {
            _internalList = new List<T>(data);
        }

        public T this[int index]
        {
            get => _internalList[index];
            set => _internalList[index] = value;
        }

        public void Add(T item)
        {
            _lock.EnterWriteLock();
            try
            {
                _internalList.Add(item);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool Remove(T item)
        {
            _lock.EnterWriteLock();
            try
            {
                return _internalList.Remove(item);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _internalList.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _internalList.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        public bool IsReadOnly => ((ICollection<T>)_internalList).IsReadOnly;

        public bool Contains(T item)
        {
            return ((ICollection<T>)_internalList).Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            ((ICollection<T>)_internalList).CopyTo(array, arrayIndex);
        }

        public void Dispose()
        {
            _lock.Dispose();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return ToList().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public List<T> ToList()
        {
            _lock.EnterReadLock();
            try
            {
                return _internalList.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void RemoveAt(int index)
        {
            _lock.EnterWriteLock();
            try
            {
                _internalList.RemoveAt(index);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }

    /// <summary>
    ///     可用于临时存放文件的，不含任何特殊字符的文件夹路径，以“\”结尾。
    /// </summary>
    public static string pathPure = GetPureASCIIDir();

    private static string GetPureASCIIDir()
    {
        if (exePath.IsASCII()) return exePath + @"PCL\";

        if (pathAppdata.IsASCII()) return pathAppdata;

        if (pathTemp.IsASCII()) return pathTemp;

        return Path.Combine(SystemPaths.DriveLetter, "ProgramData", "PCL");
    }

    /// <summary>
    ///     指示接取到这个异常的函数进行重试。
    /// </summary>
    public class RestartException : Exception
    {
    }

    /// <summary>
    ///     指示用户手动取消了操作，或用户已知晓操作被取消的原因。
    /// </summary>
    public class CancelledException : Exception
    {
    }

    /// <summary>
    ///     判断对象是否为某个泛型类型的实例。
    /// </summary>
    public static bool IsInstanceOfGenericType(this Type genericType, object obj)
    {
        if (obj is null)
            return false;
        var t = obj.GetType();
        while (t is not null)
        {
            if (t.IsGenericType && ReferenceEquals(t.GetGenericTypeDefinition(), genericType))
                return true;
            t = t.BaseType;
        }

        return false;
    }

    private static int uuid = 1;
    private static object uuidLock;

    /// <summary>
    ///     获取一个全程序内不会重复的数字（伪 Uuid）。
    /// </summary>
    public static int GetUuid()
    {
        if (uuidLock is null)
            uuidLock = new object();
        lock (uuidLock)
        {
            uuid += 1;
            return uuid;
        }
    }

    /// <summary>
    ///     将元素与 List 的混合体拆分为元素组。
    /// </summary>
    public static List<T> GetFullList<T>(IList data)
    {
        List<T> getFullListRet = default;
        getFullListRet = new List<T>();
        for (int i = 0, loopTo = data.Count - 1; i <= loopTo; i++)
            if (data[i] is ICollection)
                getFullListRet.AddRange((IEnumerable<T>)data[i]);
            else
                getFullListRet.Add((T)data[i]);

        return getFullListRet;
    }

    /// <summary>
    ///     数组去重。
    /// </summary>
    public static List<T> Distinct<T>(this ICollection<T> arr, ComparisonBoolean<T> isEqual)
    {
        var resultArray = new List<T>();
        for (int i = 0, loopTo = arr.Count - 1; i <= loopTo; i++)
        {
            for (int ii = i + 1, loopTo1 = arr.Count - 1; ii <= loopTo1; ii++)
                if (isEqual(arr.ElementAtOrDefault(i), arr.ElementAtOrDefault(ii)))
                    goto NextElement;
            resultArray.Add(arr.ElementAtOrDefault(i));
            NextElement: ;
        }

        return resultArray;
    }

    /// <summary>
    ///     对集合的每个元素执行指定操作。
    /// </summary>
    public static IEnumerable<T> ForEach<T>(this IEnumerable<T> collection, Action<T> action)
    {
        foreach (var item in collection)
            action(item);
        return collection;
    }

    /// <summary>
    ///     用于储存 RaiseByMouse 的 EventArgs。
    /// </summary>
    public sealed class RouteEventArgs : EventArgs
    {
        public bool handled = false;
        public bool raiseByMouse;

        public RouteEventArgs(bool raiseByMouse = false)
        {
            this.raiseByMouse = raiseByMouse;
        }
    }

    /// <summary>
    ///     前台运行文件。
    /// </summary>
    /// <param name="fileName">文件名。可以为“notepad”等缩写。</param>
    /// <param name="arguments">运行参数。</param>
    public static void ShellOnly(string fileName, string arguments = "")
    {
        try
        {
            fileName = ShortenPath(fileName);
            using (var program = new Process())
            {
                program.StartInfo.Arguments = arguments;
                program.StartInfo.FileName = fileName;
                program.StartInfo.UseShellExecute = true;
                Log("[System] 执行外部命令：" + fileName + " " + arguments);
                program.Start();
            }
        }
        catch (Exception ex)
        {
            Log(
                ex,
                "打开文件或程序失败：" + fileName,
                LogLevel.Msgbox,
                userSummary: Lang.Text("SystemDialog.File.OpenFailed.Message", fileName));
        }
    }

    /// <summary>
    ///     前台运行文件并返回返回值。
    /// </summary>
    /// <param name="fileName">文件名。可以为“notepad”等缩写。</param>
    /// <param name="arguments">运行参数。</param>
    /// <param name="timeout">等待该程序结束的最长时间（毫秒）。超时会返回 Result.Timeout。</param>
    public static ProcessReturnValues ShellAndGetExitCode(string fileName, string arguments = "", int timeout = 1000000)
    {
        try
        {
            using (var program = new Process())
            {
                program.StartInfo.Arguments = arguments;
                program.StartInfo.FileName = fileName;
                Log("[System] 执行外部命令并等待返回码：" + fileName + " " + arguments);
                program.Start();
                if (program.WaitForExit(timeout)) return (ProcessReturnValues)program.ExitCode;

                return ProcessReturnValues.Timeout;
            }
        }
        catch (Exception ex)
        {
            Log(ex, "执行命令失败：" + fileName, LogLevel.Msgbox);
            return ProcessReturnValues.Fail;
        }
    }

    /// <summary>
    ///     静默运行文件并返回输出流字符串。执行失败会抛出异常。
    /// </summary>
    /// <param name="fileName">文件名。可以为“notepad”等缩写。</param>
    /// <param name="arguments">运行参数。</param>
    /// <param name="timeout">等待该程序结束的最长时间（毫秒）。超时会抛出错误。</param>
    public static string ShellAndGetOutput(string fileName, string arguments = "", int timeout = 1000000,
        string workingDirectory = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // 设置工作目录（如果提供）
        if (!string.IsNullOrEmpty(workingDirectory)) info.WorkingDirectory = workingDirectory.TrimEnd('\\');

        Log("[System] 执行外部命令并等待返回结果：" + fileName + " " + arguments);

        using (var program = new Process { StartInfo = info })
        {
            program.Start();

            // 异步读取输出和错误流
            var outputTask = program.StandardOutput.ReadToEndAsync();
            var errorTask = program.StandardError.ReadToEndAsync();

            // 等待进程退出或超时
            if (program.WaitForExit(timeout))
            {
                // 确保异步读取完成
                Task.WaitAll(outputTask, errorTask);
            }
            else
            {
                // 超时后终止进程
                program.Kill();
                // 仍然尝试获取已输出的内容
                Task.WaitAll(outputTask, errorTask);
            }

            // 合并结果并返回
            return outputTask.Result + errorTask.Result;
        }
    }

    /// <summary>
    ///     在新的工作线程中执行代码。
    /// </summary>
    public static Thread RunInNewThread(Action action, string name = null,
        ThreadPriority priority = ThreadPriority.Normal)
    {
        var th = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (ThreadInterruptedException ex)
            {
                Log(name + "：线程已中止");
            }
            catch (Exception ex)
            {
                Log(ex, name + "：线程执行失败", LogLevel.Feedback);
            }
        }) { Name = name ?? "Runtime New Invoke " + GetUuid() + "#", Priority = priority };
        th.Start();
        return th;
    }

    /// <summary>
    ///     确保在 UI 线程中执行代码。
    ///     如果当前并非 UI 线程，则会阻断当前线程，直至 UI 线程执行完毕。
    ///     为防止线程互锁，请仅在开始加载动画、从 UI 获取输入时使用！
    /// </summary>
    public static Output RunInUiWait<Output>(Func<Output> action)
    {
        if (RunInUi()) return action();

        return System.Windows.Application.Current.Dispatcher.Invoke(action);
    }

    /// <summary>
    ///     确保在 UI 线程中执行代码。
    ///     如果当前并非 UI 线程，则会阻断当前线程，直至 UI 线程执行完毕。
    ///     为防止线程互锁，请仅在开始加载动画、从 UI 获取输入时使用！
    /// </summary>
    public static void RunInUiWait(Action action)
    {
        if (System.Windows.Application.Current is null)
            return;
        if (RunInUi())
            action();
        else
            System.Windows.Application.Current.Dispatcher.Invoke(action);
    }

    /// <summary>
    ///     确保在 UI 线程中执行代码，代码按触发顺序执行。
    ///     如果当前并非 UI 线程，也不阻断当前线程的执行。
    /// </summary>
    public static void RunInUi(Action action, bool forceWaitUntilLoaded = false)
    {
        if (System.Windows.Application.Current is null)
            return;
        if (RunInUi())
            action();
        else
            System.Windows.Application.Current.Dispatcher.InvokeAsync(action,
                forceWaitUntilLoaded ? DispatcherPriority.Loaded : DispatcherPriority.Normal);
    }

    /// <summary>
    ///     确保在工作线程中执行代码。
    /// </summary>
    public static void RunInThread(Action action)
    {
        if (RunInUi())
            RunInNewThread(action, "Runtime Invoke " + GetUuid() + "#");
        else
            action();
    }

    /// <summary>
    ///     使用优化的归并排序算法进行稳定排序。
    /// </summary>
    /// <param name="sortRule">传入两个对象，若第一个对象应该排在前面，则返回 True。</param>
    public static List<T> Sort<T>(this IList<T> list, ComparisonBoolean<T> sortRule)
    {
        // 创建原列表的副本以避免修改原始列表
        var tempList = new List<T>(list);
        if (tempList.Count <= 1)
            return tempList;

        // 使用归并排序核心算法
        MergeSort_Sort(ref tempList, 0, tempList.Count - 1, sortRule);
        return tempList;
    }

    private static void MergeSort_Sort<T>(ref List<T> array, int left, int right, ComparisonBoolean<T> comparator)
    {
        if (left >= right)
            return;

        var mid = (left + right) / 2;
        MergeSort_Sort(ref array, left, mid, comparator);
        MergeSort_Sort(ref array, mid + 1, right, comparator);
        MergeSort_Merge(ref array, left, mid, right, comparator);
    }

    private static void MergeSort_Merge<T>(ref List<T> array, int left, int mid, int right,
        ComparisonBoolean<T> comparator)
    {
        var leftArray = new List<T>();
        var rightArray = new List<T>();

        for (int i = left, loopTo = mid; i <= loopTo; i++)
            leftArray.Add(array[i]);

        for (int j = mid + 1, loopTo1 = right; j <= loopTo1; j++)
            rightArray.Add(array[j]);

        var leftPtr = 0;
        var rightPtr = 0;
        var current = left;

        while (leftPtr < leftArray.Count && rightPtr < rightArray.Count)
        {
            // 保持稳定性的关键比较逻辑：当相等时优先取左数组元素
            if (comparator(leftArray[leftPtr], rightArray[rightPtr]))
            {
                array[current] = leftArray[leftPtr];
                leftPtr += 1;
            }
            else
            {
                array[current] = rightArray[rightPtr];
                rightPtr += 1;
            }

            current += 1;
        }

        while (leftPtr < leftArray.Count)
        {
            array[current] = leftArray[leftPtr];
            leftPtr += 1;
            current += 1;
        }

        while (rightPtr < rightArray.Count)
        {
            array[current] = rightArray[rightPtr];
            rightPtr += 1;
            current += 1;
        }
    }

    public delegate bool ComparisonBoolean<T>(T left, T right);

    /// <summary>
    ///     返回列表的浅表副本。
    /// </summary>
    public static IList<T> Clone<T>(this IList<T> list)
    {
        return new List<T>(list);
    }

    /// <summary>
    ///     尝试从字典中获取某项，如果该项不存在，则返回默认值。
    /// </summary>
    public static TValue GetOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key,
        TValue defaultValue = default)
    {
        if (dict.ContainsKey(key)) return dict[key];

        return defaultValue;
    }

    /// <summary>
    ///     将某项添加到以列表作为值的字典中。
    /// </summary>
    public static void AddToList<TKey, TValue>(this Dictionary<TKey, List<TValue>> dict, TKey key, TValue value)
    {
        if (dict.ContainsKey(key))
            dict[key].Add(value);
        else
            dict.Add(key, new List<TValue> { value });
    }

    /// <summary>
    ///     获取程序启动参数。
    /// </summary>
    /// <param name="name">参数名。</param>
    /// <param name="defaultValue">默认值。</param>
    public static object GetProgramArgument(string name, object defaultValue = null)
    {
        var allArguments = Interaction.Command().Split(" ");
        for (int i = 0, loopTo = allArguments.Length - 1; i <= loopTo; i++)
            if ((allArguments[i] ?? "") == ("-" + name ?? ""))
            {
                if (allArguments.Length == i + 1 || allArguments[i + 1].StartsWithF("-"))
                    return true;
                return allArguments[i + 1];
            }

        return defaultValue;
    }

    /// <summary>
    ///     打开网页。
    /// </summary>
    public static void OpenWebsite(string url)
    {
        try
        {
            if (!url.StartsWithF("http", true) && !url.StartsWithF("minecraft://", true))
                throw new Exception(url + " 不是一个有效的网址，它必须以 http 开头！");
            Log("[System] 正在打开网页：" + url);
            var psi = new ProcessStartInfo(url)
            {
                UseShellExecute = true,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log(ex, "无法打开网页（" + url + "）");
            ClipboardSet(url, false);
            var message = ExceptionDetails.Compose(
                Lang.Text("SystemDialog.Browser.OpenFailed.Message", url),
                ex);
            ModMain.MyMsgBox(
                message,
                Lang.Text("SystemDialog.Browser.OpenFailed.Title"));
        }
    }

    /// <summary>
    ///     打开 explorer。
    ///     若不以 \ 结尾，则将视作文件路径，打开并选中此文件。
    /// </summary>
    public static void OpenExplorer(string location)
    {
        try
        {
            location = ShortenPath(location.Replace("/", @"\").Trim(' ', '"'));
            Log("[System] 正在打开资源管理器：" + location);
            if (location.EndsWithF(@"\"))
                ShellOnly(location);
            else
                ShellOnly("explorer", $"/select,\"{location}\"");
        }
        catch (Exception ex)
        {
            Log(
                ex,
                "打开资源管理器失败，请尝试关闭安全软件（如 360 安全卫士）",
                LogLevel.Msgbox,
                userSummary: Lang.Text("SystemDialog.Folder.OpenFailed.Message", location));
        }
    }

    /// <summary>
    ///     设置剪贴板。将在另一线程运行，且不会抛出异常。
    /// </summary>
    public static void ClipboardSet(string text, bool showSuccessHint = true)
    {
        RunInThread(() =>
        {
            var success = false;

            for (var attempt = 0; attempt <= 5; attempt++)
                try
                {
                    RunInUi(() => Clipboard.SetText(text));
                    success = true;
                    break;
                }
                catch (Exception ex) when (attempt < 5)
                {
                    Thread.Sleep(20);
                }
                catch (Exception finalEx)
                {
                    Log(
                        finalEx,
                        "剪贴板被占用，文本复制失败",
                        LogLevel.Hint,
                        userSummary: Lang.Text("Common.Hint.CopyFailed"));
                }

            if (success && showSuccessHint)
                RunInUi(() => HintService.Hint(Lang.Text("Common.Hint.Copied"), HintType.Success));
        });
    }

    /// <summary>
    ///     从剪切板粘贴文件或文件夹
    /// </summary>
    /// <param name="dest">目标文件夹</param>
    /// <param name="copyFile">是否粘贴文件</param>
    /// <param name="copyDir">是否粘贴文件夹</param>
    /// <returns>总共粘贴的数量</returns>
    public static int PasteFileFromClipboard(string dest, bool copyFile = true, bool copyDir = true)
    {
        Log("[System] 从剪贴板粘贴文件到：" + dest);
        try
        {
            var files = Clipboard.GetFileDropList();
            if (files.Count.Equals(0))
            {
                Log("[System] 剪贴板内无文件可粘贴");
                return 0;
            }

            var copiedFiles = 0;
            var copiedFolders = 0;
            foreach (var i in files)
            {
                if (copyFile && File.Exists(i)) // 文件
                    try
                    {
                        var thisDest = dest + GetFileNameFromPath(i);
                        if (File.Exists(thisDest))
                        {
                            Log("[System] 已存在同名文件：" + thisDest);
                        }
                        else
                        {
                            File.Copy(i, thisDest);
                            copiedFiles += 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(ex, "[System] 复制文件时出错");
                        continue;
                    }

                if (copyDir && Directory.Exists(i)) // 文件夹
                    try
                    {
                        var thisDest = dest + GetFolderNameFromPath(i);
                        if (Directory.Exists(thisDest))
                        {
                            Log("[System] 已存在同名文件夹：" + thisDest);
                        }
                        else
                        {
                            CopyDirectory(i, thisDest);
                            copiedFolders += 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(ex, "[System] 复制文件时出错");
                    }
            }

            HintService.Hint(Lang.Text("Common.Hint.FilesPasted", copiedFiles, copiedFolders));
        }
        catch (Exception ex)
        {
            Log(ex, "[System] 从剪切板粘贴文件失败", LogLevel.Hint);
        }

        return 0;
    }

    /// <summary>
    ///     获取程序打包资源的输入流。该资源必须声明为 <c>Resource</c> 类型，否则将会报错，<c>Images</c>
    ///     和 <c>Resources</c> 目录已默认声明该类型。
    /// </summary>
    public static Stream GetResourceStream(string path)
    {
        var resourceInfo =
            System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/{path}", UriKind.Absolute));
        return resourceInfo?.Stream;
    }

    #endregion

    /// <summary>
    ///     检查是否拥有某一文件夹的 I/O 权限。如果文件夹不存在，会返回 False。
    /// </summary>
    public static bool CheckPermission(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
                return false;
            if (!path.EndsWithF(@"\"))
                path += @"\";
            if (path.EndsWithF(@":\System Volume Information\") || path.EndsWithF(@":\$RECYCLE.BIN\"))
                return false;
            if (!Directory.Exists(path))
                return false;
            var fileName = "CheckPermission" + GetUuid();
            if (File.Exists(path + fileName))
                File.Delete(path + fileName);
            File.Create(path + fileName).Dispose();
            File.Delete(path + fileName);
            return true;
        }
        catch (Exception ex)
        {
            Log(ex, "没有对文件夹 " + path + " 的权限，请尝试以管理员权限运行 PCL");
            return false;
        }
    }

    /// <summary>
    ///     检查是否拥有某一文件夹的 I/O 权限。如果出错，则抛出异常。
    /// </summary>
    public static void CheckPermissionWithException(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentNullException("文件夹名不能为空！");
        if (!path.EndsWithF(@"\"))
            path += @"\";
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException("文件夹不存在！");
        if (File.Exists(path + "CheckPermission"))
            File.Delete(path + "CheckPermission");
        File.Create(path + "CheckPermission").Dispose();
        File.Delete(path + "CheckPermission");
    }

    #region UI

    public static void SetLaunchFont(string fontName = null)
    {
        try
        {
            LocalizationFontService.ApplyLaunchFont(fontName, LocalizationService.CurrentLanguage);
        }
        catch (Exception ex)
        {
            Log(ex, "设置字体失败", LogLevel.Hint);
        }
    }

    // 边距改变
    /// <summary>
    ///     相对增减控件的左边距。
    /// </summary>
    public static void DeltaLeft(FrameworkElement control, double newValue)
    {
        // 安全性检查
        DebugAssert(!double.IsNaN(newValue));
        DebugAssert(!double.IsInfinity(newValue));

        if (control is Window)
            // 窗口改变
            ((Window)control).Left += newValue;
        else
            // 根据 HorizontalAlignment 改变数值
            switch (control.HorizontalAlignment)
            {
                case HorizontalAlignment.Left:
                case HorizontalAlignment.Stretch:
                {
                    control.Margin = new Thickness(control.Margin.Left + newValue, control.Margin.Top,
                        control.Margin.Right, control.Margin.Bottom);
                    break;
                }
                case HorizontalAlignment.Right:
                {
                    // control.Margin = New Thickness(control.Margin.Left, control.Margin.Top, CType(control.Parent, Object).ActualWidth - control.ActualWidth - newValue, control.Margin.Bottom)
                    control.Margin = new Thickness(control.Margin.Left, control.Margin.Top,
                        control.Margin.Right - newValue, control.Margin.Bottom);
                    break;
                }

                default:
                {
                    DebugAssert(false);
                    break;
                }
            }
    }

    /// <summary>
    ///     设置控件的左边距。（仅针对置左控件）
    /// </summary>
    public static void SetLeft(FrameworkElement control, double newValue)
    {
        DebugAssert(control.HorizontalAlignment == HorizontalAlignment.Left);
        control.Margin = new Thickness(newValue, control.Margin.Top, control.Margin.Right, control.Margin.Bottom);
    }

    /// <summary>
    ///     相对增减控件的上边距。
    /// </summary>
    public static void DeltaTop(FrameworkElement control, double newValue)
    {
        // 安全性检查
        DebugAssert(!double.IsNaN(newValue));
        DebugAssert(!double.IsInfinity(newValue));

        if (control is Window)
            // 窗口改变
            ((Window)control).Top += newValue;
        else
            // 根据 VerticalAlignment 改变数值
            switch (control.VerticalAlignment)
            {
                case VerticalAlignment.Top:
                {
                    control.Margin = new Thickness(control.Margin.Left, control.Margin.Top + newValue,
                        control.Margin.Right, control.Margin.Bottom);
                    break;
                }
                case VerticalAlignment.Bottom:
                {
                    // control.Margin = New Thickness(control.Margin.Left, control.Margin.Top, CType(control.Parent, Object).ActualWidth - control.ActualWidth - newValue, control.Margin.Bottom)
                    control.Margin = new Thickness(control.Margin.Left, control.Margin.Top, control.Margin.Right,
                        control.Margin.Bottom - newValue);
                    break;
                }

                default:
                {
                    DebugAssert(false);
                    break;
                }
            }
    }

    /// <summary>
    ///     设置控件的顶边距。（仅针对置上控件）
    /// </summary>
    public static void SetTop(FrameworkElement control, double newValue)
    {
        DebugAssert(control.VerticalAlignment == VerticalAlignment.Top);
        control.Margin = new Thickness(control.Margin.Left, newValue, control.Margin.Right, control.Margin.Bottom);
    }

    // DPI 转换
    public static readonly int dpi = (int)Math.Round(Graphics.FromHwnd(nint.Zero).DpiX);

    /// <summary>
    ///     将经过 DPI 缩放的 WPF 尺寸转化为实际的像素尺寸。
    /// </summary>
    public static double GetPixelSize(double wPFSize)
    {
        return wPFSize / 96d * dpi;
    }

    /// <summary>
    ///     将实际的像素尺寸转化为经过 DPI 缩放的 WPF 尺寸。
    /// </summary>
    public static double GetWPFSize(double pixelSize)
    {
        return pixelSize * 96d / dpi;
    }

    // UI 截图
    /// <summary>
    ///     将某个控件的呈现转换为图片。
    /// </summary>
    public static ImageBrush ControlBrush(FrameworkElement uI)
    {
        var width = uI.ActualWidth;
        var height = uI.ActualHeight;
        if (width < 1d || height < 1d)
            return new ImageBrush();
        var bmp = new RenderTargetBitmap((int)Math.Round(GetPixelSize(width)), (int)Math.Round(GetPixelSize(height)),
            dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(uI);
        return new ImageBrush(bmp);
    }

    /// <summary>
    ///     将某个控件的模拟呈现转换为图片。
    /// </summary>
    public static ImageBrush ControlBrush(FrameworkElement uI, double width, double height, double left = 0d,
        double top = 0d)
    {
        uI.Measure(new Size(width, height));
        uI.Arrange(new Rect(0d, 0d, width, height));
        var bmp = new RenderTargetBitmap((int)Math.Round(GetPixelSize(width)), (int)Math.Round(GetPixelSize(height)),
            dpi, dpi, PixelFormats.Default);
        bmp.Render(uI);
        if (left != 0d || top != 0d)
            uI.Arrange(new Rect(left, top, width, height));
        return new ImageBrush(bmp);
    }

    /// <summary>
    ///     将 UI 内容固定为图片并进行 Clear。
    /// </summary>
    public static void ControlFreeze(Panel uI)
    {
        uI.Background = ControlBrush(uI);
        uI.Children.Clear();
    }

    /// <summary>
    ///     将 UI 内容固定为图片并进行 Clear。
    /// </summary>
    public static void ControlFreeze(Border uI)
    {
        uI.Background = ControlBrush(uI);
        uI.Child = null;
    }

    /// <summary>
    ///     将 XElement 转换为对应 UI 对象（不返回 XAML 清理结果）。
    /// </summary>
    public static object GetObjectFromXML(XElement str)
    {
        return GetObjectFromXML(str.ToString());
    }

    /// <summary>
    ///     将 XML 转换为对应 UI 对象。
    /// </summary>
    public static object GetObjectFromXML(string str)
    {
        return GetObjectFromXML(str, out _);
    }

    /// <summary>
    ///     将 XML 转换为对应 UI 对象，并输出 XAML 清理结果。
    /// </summary>
    public static object GetObjectFromXML(string str, out XamlEventSanitizer.SanitizeResult sanitizeResult)
    {
        str = str. // 兼容旧版自定义事件写法
            Replace("EventType=\"", "local:CustomEventService.EventType=\"").
            Replace("EventData=\"", "local:CustomEventService.EventData=\"").
            Replace("Property=\"EventType\"", "Property=\"local:CustomEventService.EventType\"").
            Replace("Property=\"EventData\"", "Property=\"local:CustomEventService.EventData\"");
        // 修复因上述替换导致重复前缀的情况：local:CustomEventService.local:CustomEventService.EventType
        str = str.Replace("local:CustomEventService.local:CustomEventService.", "local:CustomEventService.");

        sanitizeResult = XamlEventSanitizer.Sanitize(str);
        str = sanitizeResult.SanitizedXaml;
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(str)))
        {
            // 类型检查
            using (var reader = new XamlXmlReader(stream))
            {
                while (reader.Read())
                {
                    foreach (var blackListType in new[]
                             {
                                 typeof(WebBrowser), typeof(Frame), typeof(MediaElement), typeof(ObjectDataProvider),
                                 typeof(XamlReader), typeof(Window), typeof(XmlDataProvider),
                                 typeof(Process), typeof(ProcessStartInfo)
                             })
                    {
                        if (reader.Type is not null && blackListType.IsAssignableFrom(reader.Type.UnderlyingType))
                            throw new UnauthorizedAccessException($"不允许使用 {blackListType.Name} 类型。");
                        if (reader.Value is not null && Equals(reader.Value, blackListType.Name))
                            throw new UnauthorizedAccessException($"不允许使用 {blackListType.Name} 值。");
                    }

                    foreach (var blackListAssembly in new[] { "Microsoft.Xaml.Behaviors" })
                    {
                        if (reader.Type?.UnderlyingType?.Assembly.GetName().Name?.StartsWith(blackListAssembly, StringComparison.Ordinal) == true)
                            throw new UnauthorizedAccessException($"不允许使用 {blackListAssembly} 程序集中的类型。");
                        if (reader.Member?.DeclaringType?.UnderlyingType?.Assembly.GetName().Name?.StartsWith(blackListAssembly, StringComparison.Ordinal) == true)
                            throw new UnauthorizedAccessException($"不允许使用 {blackListAssembly} 程序集中的成员。");
                        if (reader.Value is string value && value.Contains(blackListAssembly, StringComparison.Ordinal))
                            throw new UnauthorizedAccessException($"不允许引用 {blackListAssembly} 程序集。");
                    }

                    foreach (var blackListMember in new[] { "Code", "FactoryMethod", "Static" })
                        if (reader.Member is not null && (reader.Member.Name ?? "") == (blackListMember ?? ""))
                            throw new UnauthorizedAccessException($"不允许使用 {blackListMember} 成员。");
                }
            }

            // 实际的加载
            stream.Position = 0L;
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(str);
                writer.Flush();
                stream.Position = 0L;
                return System.Windows.Markup.XamlReader.Load(stream);
            }
        }
    }

    private static readonly int uiThreadId = Thread.CurrentThread.ManagedThreadId;

    /// <summary>
    ///     当前线程是否为主线程。
    /// </summary>
    public static bool RunInUi()
    {
        return Thread.CurrentThread.ManagedThreadId == uiThreadId;
    }

    #endregion

    #region Debug

    public static bool modeDebug = false;

    // Log
    public enum LogLevel
    {
        /// <summary>
        ///     不提示，只记录日志。
        /// </summary>
        Normal = 0,

        /// <summary>
        ///     只提示开发者。
        /// </summary>
        Developer = 1,

        /// <summary>
        ///     只提示开发者与调试模式用户。
        /// </summary>
        Debug = 2,

        /// <summary>
        ///     弹出提示所有用户。
        /// </summary>
        Hint = 3,

        /// <summary>
        ///     弹窗，不要求反馈。
        /// </summary>
        Msgbox = 4,

        /// <summary>
        ///     弹窗，要求反馈。
        /// </summary>
        Feedback = 5,

        /// <summary>
        ///     弹出 Windows 原生弹窗，要求反馈。在无法保证 WPF 窗口能正常运行时使用此级别。
        ///     在第二次触发后会直接结束程序。
        /// </summary>
        Critical = 6
    }

    private static bool isCriticalErrorTriggered;

    /// <summary>
    ///     输出 Log。
    /// </summary>
    /// <param name="title">如果要求弹窗，指定弹窗的标题。</param>
    public static void Log(
        string text,
        LogLevel level = LogLevel.Normal,
        string? title = null,
        string? userSummary = null)
    {
        // On Error Resume Next
        // 放在最后会导致无法显示极端错误下的弹窗（如无法写入日志文件）
        // 处理错误会导致再次调用 Log() 导致无限循环

        // 输出日志
        if (new[] { LogLevel.Msgbox, LogLevel.Hint }.Contains(level))
            LogWrapper.Warn(text);
        else if (LogLevel.Feedback == level)
            LogWrapper.Error(text);
        else if (LogLevel.Critical == level)
            LogWrapper.Fatal(text);
        else if (LogLevel.Debug == level)
            LogWrapper.Debug(text);
        else if (LogLevel.Developer == level)
            LogWrapper.Trace(text);
        else
            LogWrapper.Info(text);

        if (isProgramEnded || level == LogLevel.Normal)
            return;

        var userDetails = text.RegexReplace(@"\[[^\]]+?\] ", "");
        var userMessage = string.IsNullOrWhiteSpace(userSummary)
            ? Lang.Text("SystemDialog.Error.UserVisible.Message", userDetails)
            : userSummary;
        var dialogTitle = _GetUserDialogTitle(title);

        switch (level)
        {
            case LogLevel.Developer:
                break;

            case LogLevel.Debug:
                if (modeDebug)
                    HintService.Hint("[调试模式] " + text, HintType.Info, false);
                break;

            case LogLevel.Hint:
                HintService.Hint(userMessage, HintType.Error, false);
                break;

            case LogLevel.Msgbox:
                ModMain.MyMsgBox(userMessage, dialogTitle, isWarn: true);
                break;

            case LogLevel.Feedback:
                _ShowFeedbackPrompt(userMessage, dialogTitle, false);
                break;

            case LogLevel.Critical:
                _ShowFeedbackPrompt(userMessage, dialogTitle, true);
                break;
        }
    }

    /// <summary>
    ///     输出错误信息。
    /// </summary>
    /// <param name="desc">错误描述，仅用于日志和错误详情。</param>
    /// <param name="userSummary">可选的本地化用户摘要；不会写入日志。</param>
    public static void Log(
        Exception ex,
        string desc,
        LogLevel level = LogLevel.Debug,
        string? title = null,
        string? userSummary = null)
    {
        // On Error Resume Next
        if (ex is ThreadInterruptedException)
            return;

        // 输出日志
        if (new[] { LogLevel.Msgbox, LogLevel.Hint }.Contains(level))
            LogWrapper.Warn(ex, desc);
        else if (LogLevel.Feedback == level)
            LogWrapper.Error(ex, desc);
        else if (LogLevel.Critical == level)
            LogWrapper.Fatal(ex, desc);
        else if (LogLevel.Debug == level)
            LogWrapper.Debug($"{desc}:{ex}");
        else if (LogLevel.Developer == level)
            LogWrapper.Trace($"{desc}:{ex}");
        else
            LogWrapper.Error(ex, desc);

        if (isProgramEnded)
            return;

        var userMessage = _GetUserExceptionMessage(desc, ex, userSummary);
        var dialogTitle = _GetUserDialogTitle(title);

        switch (level)
        {
            case LogLevel.Normal or LogLevel.Developer:
                break;

            case LogLevel.Debug:
                if (modeDebug)
                    HintService.Hint("[调试模式] " + desc + "：" + ex, HintType.Info, false);
                break;

            case LogLevel.Hint:
                HintService.Hint(userMessage, HintType.Error, false);
                break;

            case LogLevel.Msgbox:
                ModMain.MyMsgBox(userMessage, dialogTitle, isWarn: true);
                break;

            case LogLevel.Feedback:
                _ShowFeedbackPrompt(userMessage, dialogTitle, false);
                break;

            case LogLevel.Critical:
                _ShowFeedbackPrompt(userMessage, dialogTitle, true);
                break;
        }
    }

    private static string _GetUserDialogTitle(string? title)
    {
        return string.IsNullOrWhiteSpace(title)
            ? Lang.Text("SystemDialog.Error.Title")
            : title;
    }

    private static string _GetUserExceptionMessage(
        string desc,
        Exception ex,
        string? userSummary)
    {
        if (!string.IsNullOrWhiteSpace(userSummary))
            return ExceptionDetails.Compose(userSummary, ex);

        return ex.GetType() == typeof(Win32Exception)
            ? Lang.Text("SystemDialog.Error.OperationFailed.RuntimeMessage", desc, ex.ToString())
            : Lang.Text("SystemDialog.Error.OperationFailed.Message", desc, ex.ToString());
    }

    private static void _ShowFeedbackPrompt(
        string userMessage,
        string title,
        bool isCritical)
    {
        if (isCritical && isCriticalErrorTriggered)
        {
            FormMain.EndProgramForce(ProcessReturnValues.Exception);
            return;
        }

        if (isCritical)
            isCriticalErrorTriggered = true;

        if (CanFeedback(false))
        {
            var message = Lang.Text("Setup.Feedback.ErrorPrompt.Submit.Message", userMessage);
            var shouldSend = isCritical
                ? Interaction.MsgBox(
                    message,
                    (MsgBoxStyle)((int)MsgBoxStyle.Critical + (int)MsgBoxStyle.YesNo),
                    title) == MsgBoxResult.Yes
                : ModMain.MyMsgBox(
                    message,
                    title,
                    Lang.Text("Setup.Feedback.ErrorPrompt.Submit.Action"),
                    Lang.Text("Common.Action.Cancel"),
                    isWarn: true) == 1;

            if (shouldSend)
                Feedback(false, true);
            return;
        }

        var updateMessage = Lang.Text("Setup.Feedback.ErrorPrompt.Update.Message", userMessage);
        if (isCritical)
            Interaction.MsgBox(updateMessage, MsgBoxStyle.Critical, title);
        else
            ModMain.MyMsgBox(updateMessage, title, isWarn: true);
    }

    public static string Base64Decode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var decodedBytes = Convert.FromBase64String(text);
        return Encoding.UTF8.GetString(decodedBytes);
    }

    public static string Base64Encode(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(bytes);
    }

    public static string Base64Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes);
    }

    // 反馈
    public static void Feedback(bool showMsgbox = true, bool forceOpenLog = false)
    {
        // On Error Resume Next
        FeedbackInfo();
        var currentDate = DateTime.Now.ToString("yyyy-M-dd", CultureInfo.InvariantCulture);

        if (forceOpenLog || (showMsgbox &&
                             ModMain.MyMsgBox(
                                 Lang.Text("Setup.Feedback.Reminder.Message", currentDate),
                                 Lang.Text("Setup.Feedback.Reminder.Title"),
                                 Lang.Text("Common.Action.OpenFolder"),
                                 Lang.Text("Setup.Feedback.Reminder.NotNeeded")) ==
                             1)) OpenExplorer(exePath + @"PCL\Log\");
        OpenWebsite("https://github.com/PCL-Community/PCL2-CE/issues/");
    }

    public static bool CanFeedback(bool showHint)
    {
        var stat = UpdateManager.GetVersionStatus();
        if (stat == UpdateEnums.VersionStatus.Latest) return true;

        if (!showHint) return false;

        if (ModMain.MyMsgBox(
                stat == UpdateEnums.VersionStatus.NotLatest
                    ? Lang.Text("Setup.Feedback.Unavailable.NotLatest.Message")
                    : Lang.Text("Setup.Feedback.Unavailable.CheckFailed.Message"),
                Lang.Text("Setup.Feedback.Unavailable.Title"),
                stat == UpdateEnums.VersionStatus.NotLatest
                    ? Lang.Text("Setup.Feedback.Unavailable.NotLatest.Action")
                    : Lang.Text("Setup.Feedback.Unavailable.CheckFailed.Action"),
                Lang.Text("Common.Action.Cancel")) == 1)
            ModMain.frmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupUpdate);

        return false;
    }

    /// <summary>
    ///     在日志中输出系统诊断信息。
    /// </summary>
    public static void FeedbackInfo()
    {
        try
        {
            // Get system memory info
            var phyRam = KernelInterop.GetPhysicalMemoryBytes();

            // Calculate memory and DPI scale
            var availableMb = phyRam.Available / 1024 / 1024;
            var totalMb = phyRam.Total / 1024 / 1024;
            var dpiScale = Math.Round(dpi / 96.0, 2);

            // Build diagnostic information string
            var info = $"""
                [System] Diagnostic Information:
                OS: {RuntimeInformation.OSDescription} (32-bit: {SystemInfo.Is32BitSystem})
                Memory: {availableMb} MiB / {totalMb} MiB
                DPI: {dpi} ({dpiScale * 100}%)
                MC Folder: {ModFolder.mcFolderSelected ?? "Nothing"}
                Executable Path: {exePath}
                """;

            LogWrapper.Info(info);
        }
        catch (Exception ex)
        {
            // Basic fail-safe to replace "On Error Resume Next"
            LogWrapper.Error(ex, "Failed to collect feedback information");
        }
    }

    // 断言
    public static void DebugAssert(bool exp)
    {
        if (!exp)
            throw new Exception("断言命中");
    }

    // 获取当前的堆栈信息
    public static string GetStackTrace()
    {
        var stack = new StackTrace();
        return stack.GetFrames().Skip(1).Select(f => f.GetMethod())
            .Select(f => f.Name + "(" + f.GetParameters().Select(p => p.ToString()).ToList().Join(", ") + ") - " +
                         f.Module).ToList().Join("\r\n")
            .Replace("\r\n" + "\r\n", "\r\n");
    }

    #endregion
}

#region WPF

/// <summary>
///     对数据绑定进行加法运算，使用参数决定加数。
/// </summary>
public class AdditionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return 0;
        double before;
        if (!double.TryParse(value.ToString(), out before))
            return 0;
        var scale = 1d;
        if (parameter is not null)
            double.TryParse(parameter.ToString(), out scale);
        return before + scale;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return Binding.DoNothing;
        double before;
        if (!double.TryParse(value.ToString(), out before))
            return Binding.DoNothing;
        var scale = 1d;
        if (parameter is not null)
            double.TryParse(parameter.ToString(), out scale);
        if (scale == 0d)
            return Binding.DoNothing;
        return before - scale;
    }
}

/// <summary>
///     对数据绑定进行乘法运算，使用参数决定乘数。
/// </summary>
public class MultiplicationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return 0;
        double before;
        if (!double.TryParse(value.ToString(), out before))
            return 0;
        var scale = 1d;
        if (parameter is not null)
            double.TryParse(parameter.ToString(), out scale);
        return before * scale;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return Binding.DoNothing;
        double before;
        if (!double.TryParse(value.ToString(), out before))
            return Binding.DoNothing;
        var scale = 1d;
        if (parameter is not null)
            double.TryParse(parameter.ToString(), out scale);
        if (scale == 0d)
            return Binding.DoNothing;
        return before / scale;
    }
}

/// <summary>
///     将取反的 Boolean 绑定到 Visibility。
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return Visibility.Visible;
        bool boolValue;
        return bool.TryParse(value.ToString(), out boolValue)
            ? boolValue ? Visibility.Collapsed : Visibility.Visible
            : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return false;
        return value is Visibility
            ? (Visibility)value != Visibility.Visible
            : false;
    }
}

/// <summary>
///     将 Boolean 取反。
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return false;
        bool boolValue;
        return bool.TryParse(value.ToString(), out boolValue) ? !boolValue : false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return false;

        if (bool.TryParse(value.ToString(), out var result)) return !result;

        return false;
    }
}

#endregion
