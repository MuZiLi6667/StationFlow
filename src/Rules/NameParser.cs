using System;
using System.Collections.Generic;
using StationFlow.PluginCore;
using StationFlow.VanillaBridge;

namespace StationFlow.Rules
{
    /// <summary>名字解析结果。</summary>
    public class ParsedName
    {
        public List<int> ItemIds = new List<int>();       // 识别到的物品 id（可多个）
        public List<string> ItemNames = new List<string>(); // 对应物品规范名
        public int Role;             // 0=未指定 1=供 2=需 3=供需 4=储（声明仓储、不参与配对）
        public string GroupTag;      // 第一个标签段（分组标签候选）
        public List<string> Tags = new List<string>();

        public bool HasItem { get { return ItemIds.Count > 0; } }
        public bool HasRole { get { return Role != 0; } }

        public string RoleName
        {
            get
            {
                switch (Role)
                {
                    case 1: return "供";
                    case 2: return "需";
                    case 3: return "供需";
                    case 4: return "储";
                    default: return "-";
                }
            }
        }
    }

    /// <summary>
    /// 名字解析器（模式一·命名规则驱动）：
    /// 从塔名字中提取 [物品][角色][分组标签] 语义，支持任意用户格式——
    /// 段内包含物品名即识别（长名优先、支持一段多物品）；角色段需精确匹配角色词。
    /// </summary>
    public static class NameParser
    {
        // 物品名索引（懒构建；按名字长度降序，保证长名优先匹配）
        private static List<KeyValuePair<string, int>> itemIndex; // name → itemId

        /// <summary>使缓存失效（切换语言/物品原型变化时调用）。</summary>
        public static void InvalidateCache()
        {
            itemIndex = null;
        }

        private static void EnsureItemIndex()
        {
            if (itemIndex != null) return;
            var list = new List<KeyValuePair<string, int>>();
            try
            {
                foreach (var kv in StationOps.AllItemNames())
                {
                    list.Add(new KeyValuePair<string, int>(kv.Value, kv.Key));
                }
                list.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            }
            catch (Exception ex)
            {
                StationFlowPlugin.LogWarn($"NameParser 索引构建失败: {ex.Message}");
            }
            itemIndex = list;
        }

        /// <summary>解析塔名字。</summary>
        public static ParsedName Parse(string name)
        {
            var result = new ParsedName();
            if (string.IsNullOrEmpty(name)) return result;
            EnsureItemIndex();

            // 1. 提取段：[xxx] 为方括号段；其余为自由文本段
            var segments = SplitSegments(name, out var bracketed);

            // 2. 逐段分类（先角色精确匹配，再物品包含匹配，最后归为标签）
            for (int i = 0; i < segments.Count; i++)
            {
                string seg = segments[i].Trim();
                if (seg.Length == 0) continue;

                if (result.Role == 0)
                {
                    int role = MatchRole(seg);
                    if (role != 0)
                    {
                        result.Role = role;
                        continue;
                    }
                }

                var found = MatchItems(seg);
                if (found.Count > 0)
                {
                    foreach (var kv in found)
                    {
                        if (!result.ItemIds.Contains(kv.Value))
                        {
                            result.ItemIds.Add(kv.Value);
                            result.ItemNames.Add(kv.Key);
                        }
                    }
                    continue;
                }

                // 未识别的方括号段 → 标签
                if (bracketed[i] && seg.Length > 0)
                {
                    result.Tags.Add(seg);
                    if (result.GroupTag == null) result.GroupTag = seg;
                }
            }
            return result;
        }

        /// <summary>切分段：返回文本段 + 每段是否来自方括号。</summary>
        private static List<string> SplitSegments(string name, out List<bool> bracketed)
        {
            var segs = new List<string>();
            var brk = new List<bool>();
            int i = 0;
            while (i < name.Length)
            {
                int lb = name.IndexOf('[', i);
                if (lb < 0)
                {
                    segs.Add(name.Substring(i));
                    brk.Add(false);
                    break;
                }
                if (lb > i)
                {
                    segs.Add(name.Substring(i, lb - i));
                    brk.Add(false);
                }
                int rb = name.IndexOf(']', lb + 1);
                if (rb < 0)
                {
                    segs.Add(name.Substring(lb)); // 无闭合括号：剩余整段当自由文本
                    brk.Add(false);
                    break;
                }
                segs.Add(name.Substring(lb + 1, rb - lb - 1));
                brk.Add(true);
                i = rb + 1;
            }
            bracketed = brk;
            return segs;
        }

        /// <summary>角色词精确匹配（「储」= 声明仓储，不参与配对，独立于「供需」）。</summary>
        private static int MatchRole(string seg)
        {
            switch (seg.ToLowerInvariant())
            {
                case "供": case "供应": case "供应商": case "supply": case "sup": return 1;
                case "需": case "需求": case "需求方": case "demand": case "dem": return 2;
                case "供需": case "both": case "supdem": return 3;
                case "储": case "仓储": case "storage": case "store": return 4;
                default: return 0;
            }
        }

        /// <summary>
        /// 段内物品名匹配（长名优先、命中后从段中移除防子串重复，支持一段多物品）。
        /// 返回：物品名 → itemId。
        /// </summary>
        private static List<KeyValuePair<string, int>> MatchItems(string seg)
        {
            var found = new List<KeyValuePair<string, int>>();
            if (itemIndex == null || itemIndex.Count == 0) return found;
            string rest = seg;
            foreach (var kv in itemIndex)
            {
                if (rest.IndexOf(kv.Key, StringComparison.Ordinal) < 0) continue;
                found.Add(kv);
                rest = rest.Replace(kv.Key, " ");
                if (rest.Trim().Length == 0) break;
            }
            return found;
        }
    }
}
