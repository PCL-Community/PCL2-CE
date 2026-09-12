using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PCL;

/// <summary>
///     内嵌模组（Jar-in-Jar）解析结果的持久化缓存。每实例一个文件，位于该实例的
///     <c>PCL\JarInJar.bin</c>（gzip 压缩的紧凑 JSON，与 config.v1.yml 同级）。按 Mod 文件路径 + (最后修改时间, 大小) 指纹判断
///     有效性，避免每次加载都重新递归解析嵌套 jar；也为后续依赖/级联分析提供可查询的内嵌索引。
///     使用前须先 <see cref="UseInstance" /> 注册目标实例，用毕 <see cref="Flush" /> 落盘。
///     "当前实例"按线程记录（<c>[ThreadStatic]</c>），模组列表加载与崩溃导出各自的线程互不干扰。
/// </summary>
public static class ModJarInJarCache
{
    /// <summary>缓存数据结构变化时递增此值以令旧缓存失效（改动 JIJ 解析/节点字段后务必升此值）。</summary>
    private const int FormatVersion = 9;

    // 缓存落盘为 gzip 压缩的紧凑 JSON（.bin）：省略缩进/空集合/空字段后再压缩，
    // 相比美化 JSON 体积降至约 1/15（几百 mod 的实例从 300+KB 降到 ~20KB）。
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { _DropEmptyCollections } }
    };

    // 空集合（无依赖/无内嵌/无冲突等，占绝大多数节点）不写入，压缩前先削掉这部分体积
    private static void _DropEmptyCollections(JsonTypeInfo info)
    {
        foreach (var prop in info.Properties)
            if (typeof(ICollection).IsAssignableFrom(prop.PropertyType))
                prop.ShouldSerialize = static (_, value) => value is ICollection { Count: > 0 };
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<string, _Store> _stores = new(StringComparer.OrdinalIgnoreCase);

    // 每线程各自的"当前实例"：列表加载线程与崩溃导出线程并发时互不干扰，
    // 避免一个线程 UseInstance 切走 _current 后，另一个线程路由回退写进错误实例的缓存
    [ThreadStatic] private static _Store _current;

    private class _Store
    {
        public string InstancePath;
        public string CachePath;
        public Dictionary<string, CacheEntry> Entries; // null = 未加载
        public bool Dirty;
    }

    public class CacheEntry
    {
        public long LastModified { get; set; }
        public long Size { get; set; }

        /// <summary>顶层 Mod 自身的关系元数据（ModId/版本/加载器/依赖/可选/冲突/别名），使缓存自包含、可纯离线做关系分析。</summary>
        public EmbeddedModNode Self { get; set; }

        public List<EmbeddedModNode> Tree { get; set; } = new();
    }

    private class CacheFile
    {
        public int Version { get; set; }
        public Dictionary<string, CacheEntry> Entries { get; set; } = new();
    }

    /// <summary>
    ///     注册并切换到某实例的缓存（<paramref name="instancePath" />\PCL\JarInJar.bin）。
    ///     传空表示后续路由不到的读写不走缓存。
    /// </summary>
    public static void UseInstance(string instancePath)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(instancePath))
            {
                _current = null;
                return;
            }

            var key = instancePath.TrimEnd('\\', '/');
            if (!_stores.TryGetValue(key, out var store))
            {
                store = new _Store
                {
                    InstancePath = key,
                    CachePath = Path.Combine(key, "PCL", "JarInJar.bin")
                };
                _stores[key] = store;
            }

            _current = store;
        }
    }

    // 启/禁用是纯改名、mtime 不变；剥 .disabled 后复用同键避免白白重扫。
    // 保留 .old（.old 可与新文件并存，剥了会键冲突）。
    private static string _NormalizeKey(string path)
    {
        if (path is null) return null;
        return path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? path.Substring(0, path.Length - ".disabled".Length)
            : path;
    }

    private static void _EnsureLoaded(_Store store)
    {
        if (store.Entries is not null) return;
        store.Entries = new Dictionary<string, CacheEntry>();
        try
        {
            if (!File.Exists(store.CachePath)) return;
            using var fs = File.OpenRead(store.CachePath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gz);
            var root = doc.RootElement;
            // 格式版本不符：丢整表（避免旧结构的错值按指纹命中）
            if (!root.TryGetProperty("Version", out var ver) || ver.GetInt32() != FormatVersion) return;
            if (!root.TryGetProperty("Entries", out var entries) || entries.ValueKind != JsonValueKind.Object) return;
            // 逐条容错：单条损坏只丢那条，不丢整表
            foreach (var prop in entries.EnumerateObject())
                try
                {
                    var entry = prop.Value.Deserialize<CacheEntry>();
                    if (entry is not null) store.Entries[prop.Name] = entry;
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "跳过损坏的 Jar-in-Jar 缓存条目：" + prop.Name, ModBase.LogLevel.Developer);
                }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "读取 Jar-in-Jar 缓存失败，已重置", ModBase.LogLevel.Developer);
        }
    }

    /// <summary>指纹匹配时返回缓存的内嵌树，否则返回 null。</summary>
    public static List<EmbeddedModNode> TryGet(string path, long lastModified, long size)
    {
        // [ThreadStatic] 读取无需锁；未启用缓存的线程（如 UI 惰性加载）直接 bypass，
        // 不必排队等别的线程在锁内做的读盘/落盘重 IO
        var store = _current;
        if (store is null) return null;
        lock (_lock)
        {
            _EnsureLoaded(store);
            if (store.Entries.TryGetValue(_NormalizeKey(path), out var e) && e.LastModified == lastModified &&
                e.Size == size)
                return e.Tree;
            return null;
        }
    }

    public static void Set(string path, long lastModified, long size, List<EmbeddedModNode> tree, EmbeddedModNode self)
    {
        var store = _current;
        if (store is null) return;
        lock (_lock)
        {
            _EnsureLoaded(store);
            store.Entries[_NormalizeKey(path)] =
                new CacheEntry { LastModified = lastModified, Size = size, Self = self, Tree = tree };
            store.Dirty = true;
        }
    }

    /// <summary>
    ///     清理当前实例存储中已不存在的文件条目（删除/改名后残留），<paramref name="keepPaths" /> 为本次
    ///     扫描到的全部 Mod 文件路径。应由模组列表加载器在扫描完成后调用。
    /// </summary>
    public static void Prune(IEnumerable<string> keepPaths)
    {
        var store = _current;
        if (store is null) return;
        lock (_lock)
        {
            var list = keepPaths as ICollection<string> ?? keepPaths.ToList();
            _EnsureLoaded(store);
            var keep = new HashSet<string>(list.Select(_NormalizeKey), StringComparer.OrdinalIgnoreCase);
            var stale = store.Entries.Keys.Where(k => !keep.Contains(k)).ToList();
            if (stale.Count == 0) return;
            foreach (var k in stale) store.Entries.Remove(k);
            store.Dirty = true;
        }
    }

    /// <summary>将全部实例的变更原子写入磁盘（临时文件 + 移动）。批量加载结束后调用一次即可。</summary>
    public static void Flush()
    {
        lock (_lock)
        {
            foreach (var store in _stores.Values)
                _FlushStore(store);
        }
    }

    private static void _FlushStore(_Store store)
    {
        if (!store.Dirty || store.Entries is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(store.CachePath)!);
            var tmp = store.CachePath + ".tmp";
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                    JsonSerializer.Serialize(gz,
                        new CacheFile { Version = FormatVersion, Entries = store.Entries }, _jsonOpts);
                bytes = ms.ToArray();
            }

            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(store.CachePath)) File.Delete(store.CachePath);
            File.Move(tmp, store.CachePath);
            // 清理旧版明文缓存（.json → .bin 迁移后残留）
            var legacy = Path.Combine(Path.GetDirectoryName(store.CachePath)!, "JarInJar.json");
            if (File.Exists(legacy)) File.Delete(legacy);
            store.Dirty = false;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "写入 Jar-in-Jar 缓存失败", ModBase.LogLevel.Developer);
        }
    }
}

/// <summary>内嵌模组的轻量序列化节点（供缓存与后续依赖分析使用）。</summary>
public class EmbeddedModNode
{
    public string FileName { get; set; }
    public string ModId { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }

    /// <summary>声明的加载器（Fabric/Quilt/Forge/NeoForge）。</summary>
    public string Loader { get; set; }

    /// <summary>声明的目标 Minecraft 版本范围。</summary>
    public string TargetMcVersion { get; set; }

    /// <summary>本内嵌 mod 声明的依赖（ModId → 原始版本约束），供其作为依赖方参与四态/级联分析。</summary>
    public Dictionary<string, string> Dependencies { get; set; } = new();

    /// <summary>其中被声明为可选的依赖 ModId 子集。</summary>
    public List<string> OptionalDeps { get; set; } = new();

    /// <summary>本内嵌 mod 声明的冲突关系（对方 ModId + 生效版本约束 + 是否硬冲突）。</summary>
    public List<EmbeddedConflict> Conflicts { get; set; } = new();

    /// <summary>本内嵌 mod 额外提供的别名 id（multi-mod 兄弟 / Fabric provides）。</summary>
    public List<string> ProvidedIds { get; set; } = new();

    public List<EmbeddedModNode> Children { get; set; } = new();
}

/// <summary>缓存中一条内嵌 mod 的冲突声明（用类而非元组以便 JSON 友好序列化）。</summary>
public class EmbeddedConflict
{
    public string Target { get; set; }
    public string Raw { get; set; }
    public bool Hard { get; set; }
}
