using System;
using System.Collections.Generic;
using System.Text;
using StationFlow.Data;

namespace StationFlow.Rules
{
    /// <summary>改名预览条目。</summary>
    public class RenamePlanItem
    {
        public StationRow Row;
        public string OldName;
        public string NewName;
    }

    /// <summary>
    /// 批量命名生成器：按模板为物流塔生成唯一名字。
    /// 模板变量：{物品} {角色} {星球} {gid} {序号} {序号:2}（补零位数）
    /// </summary>
    public static class NamingService
    {
        public enum ExistingNameMode
        {
            Skip,       // 已有名字的塔跳过（推荐，保守）
            Overwrite,  // 全部覆盖
        }

        /// <summary>
        /// 生成改名计划。
        /// 唯一性：对「全星区现有名字（排除本次将被改名的塔自身）」做 OrdinalIgnoreCase 去重，
        /// 冲突时递增序号再试。
        /// </summary>
        public static List<RenamePlanItem> Generate(List<StationRow> targets, List<StationRow> allRows,
            string template, ExistingNameMode mode)
        {
            var plan = new List<RenamePlanItem>();
            if (string.IsNullOrEmpty(template) || targets == null || targets.Count == 0) return plan;

            // 现有名字集合：
            //  - 非目标塔：全部占用；
            //  - 目标塔：将被改名（未命名，或 Overwrite）的不占用；Skip 模式下已命名塔保留原名 → 必须占用，
            //    否则新名字可能与这些"将被跳过"的塔重名。
            var targetGids = new HashSet<int>();
            foreach (var t in targets) targetGids.Add(t.Gid);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (allRows != null)
            {
                foreach (var r in allRows)
                {
                    if (string.IsNullOrEmpty(r.Name)) continue;
                    if (targetGids.Contains(r.Gid))
                    {
                        if (mode == ExistingNameMode.Skip) used.Add(r.Name.Trim());
                        continue;
                    }
                    used.Add(r.Name.Trim());
                }
            }

            int seq = 1;
            foreach (var row in targets)
            {
                if (mode == ExistingNameMode.Skip && row.HasName) continue;
                string name = null;
                for (int attempt = 0; attempt < 1000; attempt++)
                {
                    string candidate = Format(template, row, seq).Trim();
                    if (candidate.Length == 0) break;
                    if (used.Add(candidate))
                    {
                        name = candidate;
                        break;
                    }
                    seq++; // 冲突 → 序号递增重试
                }
                if (name == null) continue;
                plan.Add(new RenamePlanItem { Row = row, OldName = row.Name ?? "", NewName = name });
                seq++;
            }
            return plan;
        }

        /// <summary>
        /// 模板格式化（{序号:2} 表示 2 位补零）。
        /// 兼容全角括号 ｛｝ 与全角冒号 ：（中文输入法下常被误输）。
        /// </summary>
        public static string Format(string template, StationRow row, int seq)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '{' || c == '｛')
                {
                    char close = c == '｛' ? '｝' : '}';
                    int end = template.IndexOf(close, i);
                    if (end > i)
                    {
                        string token = template.Substring(i + 1, end - i - 1);
                        sb.Append(ResolveToken(token, row, seq));
                        i = end + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static string ResolveToken(string token, StationRow row, int seq)
        {
            int pad = 0;
            int colon = token.IndexOf(':');
            if (colon < 0) colon = token.IndexOf('：'); // 全角冒号
            if (colon >= 0)
            {
                int.TryParse(token.Substring(colon + 1), out pad);
                token = token.Substring(0, colon);
            }
            switch (token)
            {
                case "物品": case "item": return row.MainItemName ?? "";
                case "角色": case "role": return row.Role ?? "";
                case "星球": case "planet": return row.PlanetName ?? "";
                case "星系": case "galaxy": return row.GalaxyName ?? "";       // 恒星系名（如"母星系"）
                case "行星": case "planetshort": return row.ShortPlanetName ?? ""; // 短行星名（如"IV号星"）
                case "供": case "supply": return JoinSlots(row, 1);   // 星际供应物品（逗号分隔）
                case "需": case "demand": return JoinSlots(row, 2);   // 星际需求物品（逗号分隔）
                case "储": case "storage": return JoinSlots(row, 0);  // 星际仓储物品（无星际供需）
                case "gid": return row.Gid.ToString();
                case "序号": case "seq": return pad > 0 ? seq.ToString("D" + pad) : seq.ToString();
                default: return "{" + token + "}"; // 未知变量原样保留
            }
        }

        /// <summary>
        /// 按星际（远程）逻辑取物品名：wantLogic=1 供应 / 2 需求 / 0 仓储。
        /// 注意：严格按 remoteLogic（星际）判断，**不回退本地逻辑**——
        /// "本地供应/本地需求"是行星内物流，不应出现在星际命名里。
        /// </summary>
        private static string JoinSlots(StationRow row, int wantLogic)
        {
            if (row.Slots == null) return "";
            var sb = new StringBuilder();
            foreach (var s in row.Slots)
            {
                if (s.RemoteLogic != wantLogic) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(s.ItemName);
            }
            return sb.ToString();
        }
    }
}
