# 阶段 2 设计记录：模式一（命名规则驱动）+ 事务层

> 2026-10-08 完成并部署；同日二轮修正（"星际仓储"语义 + 单供应源策略）。
> 本文记录实现中的关键设计决策，供后续阶段参考。

## 模块清单

| 文件 | 职责 |
|---|---|
| `src/Rules/NameParser.cs` | 名字解析：提取物品（段内包含匹配，长名优先、一段多物品）、角色（精确词）、分组标签 |
| `src/Rules/PairingEngine.cs` | 供需配对：名字驱动 + 槽位一致性校验 → 缺口降序 / 距离升序 / 前 K / 防雨露均沾 / 单供应源策略 |
| `src/Exec/Executor.cs` | 事务层：分帧写入（60条/帧）→ 归并单次 RefreshTraffic → 反向操作记录（Undo）→ 报告 |
| `src/UI/RuleTab.cs` | 「规则配对」页签 UI：参数 / 生成 / 预览 / 执行进度 / 撤销 |

## ⭐ 官方机制实证（2026-10-08 二轮，反编译核对）

### 1. ELogisticStorage 与中文名称（UIStationStorage.GetLogisticText）
```
None   → 本地=None 显示"本地仓储"；星际=None 显示"星际仓储"；通用="仓储"
Supply → "本地供应" / "星际供应"
Demand → "本地需求" / "星际需求"
```
**"星际仓储" = remoteLogic == None(0)**。玩家存档中 rl=0 的槽位是**有意的中转/仓储设计**
（不是"忘设远程"）。新放物品默认：星际塔 remoteLogic=Supply、行星内塔 remoteLogic=None。

**结论：命名/图标/配对一律严格按 remoteLogic（星际逻辑），绝不回退本地逻辑。**

### 2. 配对分桶与行为模式消费（StationComponent.RematchRemotePairs + 消费端 2722 行）
段索引（消费端 `for m = num80..num81`，区间 `[offsets[m], offsets[m+1])`）：

| 段 | 写入方法 | 语义 |
|---|---|---|
| 0 | AddRemotePair | **普通配对**（全量自动，非 Designated 的所有跨星球供/需对） |
| 1 | AddRouteRemotePair(2) | P2P 点对点 |
| 2 | AddRouteRemotePair(3) | 星球航路 |
| 3 | AddRouteRemotePair(4) | 行星系航路 |
| 4 | AddRouteRemotePair(5) | 分组 |
| 5 | AddRouteRemotePair(6) | **兜底**（无 P2P/航路/分组时自动生成） |

行为模式（塔的 routePriority）消费范围：
- **Prioritize（默认值，dump 实证塔默认=2）**：段 1..5 —— 配了 P2P 走 P2P，没配走段5兜底
- **Only / Designated**：段 1..4 —— **无配置=停工**（没有兜底！）
- **Ignore**：段 0 —— 完全忽略配置的全量配对
- 任一塔 Designated → 该对不进段0/段5（构建时排除）

### 3. 单供应源策略（翘曲器场景的答案）
官方机制下，**所有跨星球供/需塔对默认就有兜底配对**（Prioritize 行为下生效）——
单个供应塔自动服务全部需求塔。因此**单供应源物品建 P2P 无增益**（多源才有"就近锁定"价值）。
→ 引擎规则：`supplies.Count == 1` 且（需求塔行为 ≤ Prioritize）且（供应塔非 Designated）→
跳过配对 + 输出聚合提示（◆）。
**例外照配**：需求塔行为 = Only/Designated（无兜底，不配会停工）、供应塔 Designated（普通配对被排除）。

### 4. 其他
- 同星球永不配 P2P（构建时 `planetId == planetId` 直接 continue），行星内物流自动覆盖
- 航路（astro2astro）默认不存在 → IsAstro2AstroRouteEnable=false；禁用机制（astro2astroBans）会拦截配对
- priorityLocks：官方"防漂移"锁——桩位用过某优先级后短期锁定，低优先级条目被跳过（防雨露均沾官方版）
- 刷新：`gt.RefreshTraffic()` 全量 ClearRemotePairs+RematchRemotePairs（约 81ms@2101 塔）

## 命名变量（NamingService）

| 变量 | 语义 |
|---|---|
| {物品} {角色} {星球} {gid} {序号:N} | 原有 |
| **{供}** | **星际供应**物品名（逗号分隔），仅 remoteLogic=1 |
| **{需}** | **星际需求**物品名，仅 remoteLogic=2 |
| **{储}** | **星际仓储**物品名，仅 remoteLogic=0 |
| **{星系}** | 恒星系名（如"母星系"，galaxy.StarById(planetId/100).displayName） |
| **{行星}** | 短行星名（"母星系 - IV号星" → "IV号星"） |

预设模板（4 个）：
1. 全称：`供{供}需{需} {星系}{行星}{序号}号塔`（用户指定格式）
2. 经典：`[{物品}][{角色}]-{序号:2}`
3. 星球：`[{星球}]-{物品}-{序号:2}`
4. 仓储：`储{储} {星系}{行星}{序号}号塔`

## 事务层要点

1. **P2P 边是塔对粒度**（key 只有两个 gid，无物品维度）；同塔对多物品在计划中合并
2. **K 额度按需求塔累计、跨物品共享**
3. **刷新归并**：写入阶段不刷新；Add/RemoveP2P、SetGroupBit、SetRemoteLogic 标记 needsRefresh，
   执行尾声统一一次 RefreshTraffic；Rename/SetBehavior 不触发
4. **Undo = 反向操作链**（执行时读旧值构造）；仅保留最近一次；空操作不记录
5. **幂等**：写入前比对目标状态，跳过无变化项（Skipped 计数）

## 待验证 / 已知限制

- [ ] 实测：Prioritize 塔在"仅单源、无 P2P"时确实从兜底段5 取货（预期官方已处理）
- [ ] 实测：执行后原生面板 P2P 页签显示数量 = 报告"生效"数量
- [ ] SetBehavior 不刷新是否即时生效（行为切换后派发更新？若否 → 加入 needsRefresh）
- [ ] 分组标签（GroupTag）解析已具备，自动分组写入（SetGroupBit）未接入 UI——计划阶段 3
- [ ] 预览列表为纯文本前 30 条；计划边数大时需升级虚拟滚动表格——视需要
