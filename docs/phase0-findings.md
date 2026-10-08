# 阶段 0 验证报告：关键 API 反编译核实

> 日期：2026年10月08日
> 方法：ilspycmd 8.2.0 反编译 `Assembly-CSharp.dll`（DSP 0.10.35.29104）
> 反编译产物存于 `E:\Mod build\戴森球计划\reference\`（仅本地参考，不公开、不分发）

## 一、已验证结论总表

| # | 验证项 | 结论 | 状态 |
|---|---|---|---|
| 1 | 名字存储位置 | `PlanetFactory.WriteExtraInfoOnEntity(entityId, info)` / `ReadExtraInfoOnEntity(entityId)`；底层存 `digitalSystem.extraInfoes` 的 `ExtraInfoComponent.info`（原生存档自动保存） | ✅ 实证 |
| 2 | 分组写法 | 官方 UI 直接位操作 `station.remoteGroupMask ^= 1L << k`；位 0~N 对应分组按钮。**无需 setter，直接写字段** | ✅ 实证 |
| 3 | `RefreshTraffic` 语义（星际） | `GalacticTransport.RefreshTraffic(int keyStationGId = 0)`：**全量** ClearRemotePairs（所有站）+ RematchRemotePairs（所有站）；`keyStationGId <= 0` 时跳过"在途船订单修复"段；`=gid` 时只有该站的船执行修复 | ✅ 实证 |
| 4 | `RefreshStationTraffic` 语义（行星） | `PlanetTransport.RefreshStationTraffic(int keyStationId = 0)`：**全量** ClearLocalPairs + RematchLocalPairs；keyStationId 用途与星际版对称 | ✅ 实证 |
| 5 | `SetStationStorage` 签名与副作用 | 签名 `(stationId, storageIdx, itemId, itemCountMax, localLogic, remoteLogic, player)`；⚠️ remoteLogic 变化会触发一次全量 `RefreshTraffic`；⚠️ 换物品（itemId 变）会把存量退给 player 背包，**player=null 则直接丢弃**；非星际站强制 remoteLogic=None；max 自动夹取 | ✅ 实证 |
| 6 | P2P 写入 | `gt.station2stationRoutes`（public HashSet\<long\>），key = `CalculateStation2StationKey(gid0,gid1)` = `min | max<<32`；⚠️ **`AddStation2StationRoute` 每次调用都触发一次全量 RefreshTraffic** | ✅ 实证 |
| 7 | 航路写入 | `gt.astro2astroRoutes`（public Dictionary\<long, LogisticShipRoute\>），key = `planetId(22bit) | astroId(22bit)<<22 | itemId<<44`；`LogisticShipRoute.Init()` = `{enable=true, comment=""}`；⚠️ `AddAstro2AstroRoute` 新增 key 时也触发全量刷新；itemId=0 表示全部物品（排除 11901/11902/11903） | ✅ 实证 |
| 8 | 行为模式语义 | 桶段遍历（消费端）：**Ignore** → 仅段 0（无配对普通）；**Prioritize** → 段 1~5（P2P/行星航路/星系航路/分组/兜底）；**Only/Designated** → 段 1~4（不含兜底） | ✅ 实证 |
| 9 | 优先级锁 | `priorityLocks[idx].priorityIndex < 当前段 m && lockTick > 0` 时跳过（低优先级退让高优先级）；`lockTick` 每 tick 递减 | ✅ 实证 |
| 10 | 配对匹配范围 | `RematchRemotePairs` **只做跨星球配对**（同 planetId 跳过）——同星球塔之间不参与 remote 优先级体系（与"同星球内无法设优先级"社区反馈一致） | ✅ 实证 |
| 11 | Designated（专属）生成规则 | 专属塔：不生成段 0 普通配对、不生成段 5 兜底；对方是专属时同样不生成这两段 | ✅ 实证 |
| 12 | 枚举定义 | `ERemoteRoutePriority { Ignore=1, Prioritize=2, Only=3, Designated=4 }`；`ELogisticStorage { None=0, Supply=1, Demand=2 }` | ✅ 实证 |
| 13 | 名字 UI 限制 | 官方 UI 无长度校验（写入前仅 `.Trim()`）；无名字时官方 UI 禁用"设置"入口（仅查看已有配对）——**mod 应遵循"未命名不建 P2P"语义** | ✅ 实证 |

## 二、写入策略（据此修正后的最终方案）

**核心原则：批量直写容器/字段，最后统一刷新一次。**

```
批量执行序列（全部主线程）：
1. 名字：factory.WriteExtraInfoOnEntity(entityId, name)        // 无刷新成本
2. 分组：station.remoteGroupMask |= / &= ~ (1L << bit)          // 直接写，无刷新成本
3. 行为：station.routePriority = ...                             // 直接写，无刷新成本
4. 供需逻辑：station.storage[idx].remoteLogic = ...              // 直写字段（绕过 SetStationStorage 的每次刷新）
   ※ 若需改物品/max：走 SetStationStorage（语义完整），并接受其内部刷新，预览中提示
5. P2P：gt.station2stationRoutes.Add(CalculateStation2StationKey(a,b))       // 直写容器
6. 航路：gt.astro2astroRoutes[CalculateAstro2AstroKey(a,b,item)] = route    // 直写容器（new + Init()）
7. 【唯一重活】gt.RefreshTraffic()（不传参，跳过船修复；代价≈全站 Rematch，2101 站≈81ms）
   + 每受影响星球 planetTransport.RefreshStationTraffic()（仅当改了 remoteLogic）
※ 删除类操作（删 P2P/航路）同样直写容器移除 + 统一刷新
※ 若实测发现在途船订单残留问题，备选：对关键 gid 补 RefreshTraffic(gid)（代价高，仅必要时）
```

## 三、待实测项（需游戏内运行验证）

| # | 实测项 | 方法 |
|---|---|---|
| A | 名字闭环 | 用 mod 改塔名 → 原生 P2P 面板搜索能否找到；（预期：能，官方入口实证） |
| B | P2P 闭环 | 用 mod 建一条 P2P → 观察船按配对派发（诊断：dump remotePairOffsets[3]/[4] 桶计数变化） |
| C | RefreshTraffic 实测耗时 | DumpTool 计时 2000+ 站存档下的单次调用 |
| D | 分组位号与 UI 对应 | 写入 bit 0 → 打开原生面板确认高亮的是第 1 个按钮 |
| E | 批量刷新 vs 逐条刷新 | 直写容器+单次刷新 与 官方 Add 逐条调用 的耗时对比（预期差异巨大） |

## 四、遗留问题与备忘

- `stationCursor`：`GalacticTransport` / `PlanetTransport` 的站点数组游标（1 起），遍历用 `for (i=1; i<stationCursor; i++)` + `stationPool[i] != null && stationPool[i].id == i`（校验活站）
- `gameData.factories[]`：全星球工厂数组（按 planetId 索引）；星际塔的 `station.planetId / 100 * 100` = 行星系 id
- `GameMain.history.logisticShipCarries / logisticDroneCarries`：载具容量（配对计算用）
- `StationComponent` 初始化默认值：星际站 `routePriority = Prioritize`，非星际站 `= Ignore`
- 反编译编码：ilspycmd 输出的中文字符串常量会乱码（用于 Translate），不影响我们（我们从 `Locale/2052/base.txt` 拿文案）
