using System.Collections.Generic;
using PCL.Core.App.Localization;

namespace PCL;

public static class McVersionComparer
{
    public const string UNKNOWN_VERSION_KEY = "UnknownVersion";

    /// <summary>
    ///     比较两个版本名；等同 Left >= Right。
    ///     无法比较两个预发布版的大小。
    ///     支持的格式：未知版本, 1.13.2, 1.7.10-pre4, 1.8_pre, 1.14 Pre-Release 2, 1.14.4 C6
    /// </summary>
    public static bool CompareVersionGe(string left, string right)
    {
        return CompareVersion(left, right) >= 0;
    }

    /// <summary>
    ///     比较两个版本名，若 Left 较新则返回 1，相同则返回 0，Right 较新则返回 -1；等同 Left - Right。
    ///     无法比较两个预发布版的大小。
    ///     支持的格式：未知版本, 26.1-snapshot-1，1.13.2, 1.7.10-pre4, 1.8_pre, 1.14 Pre-Release 2, 1.14.4 C6
    /// </summary>
    public static int CompareVersion(string left, string right)
    {
        if (left == Lang.Text("Minecraft.Version.Unknown") || right == Lang.Text("Minecraft.Version.Unknown"))
        {
            if (left == Lang.Text("Minecraft.Version.Unknown") && right != Lang.Text("Minecraft.Version.Unknown"))
                return 1;
            if (left == Lang.Text("Minecraft.Version.Unknown") && right == Lang.Text("Minecraft.Version.Unknown"))
                return 0;
            if (left != Lang.Text("Minecraft.Version.Unknown") && right == Lang.Text("Minecraft.Version.Unknown"))
                return -1;
        }

        left = left.ToLowerInvariant();
        right = right.ToLowerInvariant();
        var lefts = left.Replace("快照", "snapshot").Replace("预览版", "pre").RegexSearch("[a-z]+|[0-9]+");
        var rights = right.Replace("快照", "snapshot").Replace("预览版", "pre").RegexSearch("[a-z]+|[0-9]+");
        var i = 0;
        while (true)
        {
            // 两边 token（含补零）全部相等：视为同一版本。
            // 不再回退原始串 ordinal 比较——那会把 "1.20" 判得小于 "1.20.0"（前缀关系≠更旧）
            if (lefts.Count - 1 < i && rights.Count - 1 < i)
                return 0;

            // 确定两边的数值
            var leftValue = lefts.Count - 1 < i ? "0" : lefts[i];
            var rightValue = rights.Count - 1 < i ? "0" : rights[i];
            if ((leftValue ?? "") == (rightValue ?? ""))
                goto NextEntry;
            if (leftValue == "rc")
                leftValue = (-1).ToString();
            if (leftValue == "pre")
                leftValue = (-2).ToString();
            if (leftValue == "snapshot")
                leftValue = (-3).ToString();
            if (leftValue == "experimental")
                leftValue = (-4).ToString();
            var leftValValue = ModBase.Val(leftValue);
            if (rightValue == "rc")
                rightValue = (-1).ToString();
            if (rightValue == "pre")
                rightValue = (-2).ToString();
            if (rightValue == "snapshot")
                rightValue = (-3).ToString();
            if (rightValue == "experimental")
                rightValue = (-4).ToString();
            var rightValValue = ModBase.Val(rightValue);
            if (leftValValue == 0d && rightValValue == 0d)
            {
                // 如果没有数值则直接比较字符串
                if (string.CompareOrdinal(leftValue, rightValue) > 0) return 1;

                if (string.CompareOrdinal(leftValue, rightValue) < 0) return -1;
            }
            // 如果有数值则比较数值
            // 这会使得一边是数字一边是字母时数字方更大
            else if (leftValValue > rightValValue)
            {
                return 1;
            }
            else if (leftValValue < rightValValue)
            {
                return -1;
            }

            NextEntry: ;

            i += 1;
        }

        return 0;
    }

    /// <summary>
    ///     比较两个版本名的排序器。
    /// </summary>
    public class VersionComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            return CompareVersion(x, y);
        }
    }
}
