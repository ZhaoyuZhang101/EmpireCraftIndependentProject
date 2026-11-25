# 帝国盒子移植：阶段 1 进度(2026-11-25)

接 `docs/superpowers/plans/2026-11-19-empirecraft-port-plan.md`。阶段 0(实体层、世界操作、冒烟)已在 `80a3111`。本文记阶段 1 第一批：搬了什么、怎么搬、怎么验、下一批是什么。

模组来源提交：`55d6d4f8`。

## 一、这批进了什么

**搬迁工具 `Tools/PortTool/port_tool.py`**

- 只从模组**已提交**的版本拷，不碰模组工作区；每个文件登记在 `Port/manifest.json`(模组路径、提交号、日期、去处、行数、工具改了什么、手改了什么)。
- 机械改写：命名空间统一成 `EmpireCraft.Core`；`using EmpireCraft.Scripts.*`、`UnityEngine`、`NeoModLoader` 去掉；`Mathf`、`Time`、`Color` 用 using 别名指到宿主层；几处固定文字替换(数据目录、语言、游戏速度)。
- 正文逐行不动(文件级命名空间只改成一对花括号，不重新缩进)。这样模组以后改了，`sync` 能三方合并：旧提交的改写结果作基、新提交的改写结果作对方、这边手改过的文件作己方。
- `extract`：只从大文件里抽出某个枚举、常量或纯函数(例如 `RegimeType` 住在 564 行的 `RegimeSystem.cs` 里)，不必等整个系统。
- 依赖还没搬来的几个方法用 `#if EC_PENDING … #endif` 包住，等对应阶段搬来再放开。
- 命令：`add`、`extract`、`data`、`remove`、`note`、`status`、`diff`、`sync`。模组仓库路径用 `--mod` 或环境变量 `EMPIRECRAFT_MOD_REPO` 给。

**宿主层 `Assets/EmpireCraft/Core/Host/Host.cs`(新写)**

搬来的规则向"运行它的东西"要的几样服务，不引用引擎：帧时钟 `FrameClock`、数学 `EcMath`、颜色 `EcColor`、日志 `LogService`、文字表 `LM`、数据目录 `EmpireData`、分段计时 `FrameProfiler`。Unity 客户端和 dotnet 测试各自驱动它。

**搬来的源码：45 个文件，4,076 行**

| 去处 | 内容 |
|---|---|
| `Core/Rules/` | 城稳定度(CityStabilityRules/Math)、财政账(TreasuryRules)、腐败(CorruptionRules)、王朝周期(DynasticCycleRules)、制度(InstitutionRules、InstitutionDefinitionRegistry、理念与生产两条制度路径)、官品(OfficialRankRules)、爵位继承(PeerageSuccessionRules)、朝堂危机(CourtCrisisRules)、开放党禁(PartyOpeningRules)、理念谱系(IdeologyFamilies)、社会阶层(ClassSystem)、人口结算规则 |
| `Core/State/` | 各系统挂的数据结构：TreasuryData、CityPopulationData、InstitutionState、InstitutionConfig、CultureRules(文化配置)、WalletReserve、人口经济账 |
| `Core/Enums/` | 模组 `Enums/` 整个目录，加上诉求类型、政体类型、派系类型、政党理念、改朝换代三档 |
| `Core/Scheduling/` | 帧预算 FrameJobs、SimulationFrameBudget |

**搬来的规则数据：47 个文件**(`Assets/EmpireCraft/Data/`，目录结构同模组)

制度树 7 条线(757 个节点)加公共模板和设置、诉求规则、宪法模板、理念衙署、宗教教义、科技树三个文件、27 个文化的 `CultureRule.jsonc`、三种语言的文字表(13,616 条)。

**新写的接线 `Core/Systems/RealmAccounts.cs`**

- `RealmAccounts`：把搬来的规则跑在骨架世界上。每月每城算自然稳定度、驻军与治理开销，收税、扣腐败截留、国库付账、稳定度朝自然值走；每年各城吏治浮动、王朝积弊增长、改朝换代时清积弊、正统随积弊下降。账记在 `EmpireWorld.Ledger`，随存档(存档版本升到 2，版本 1 仍能读)。
- 它**只记账，不改归属**：稳定度再低也不起事，世界进程和阶段 0 完全一样(有测试对照)。忠诚、人头税、主官品行这几样输入是占位的，阶段 2、3 搬来真系统后替换。
- `EmpireSimulation.Month`：整步模拟的统一入口。
- `EmpireRunner`：按 ORBIS 日历(30 天一月)推进，走 FrameJobs 帧预算，落后时一帧多补几个月。

**Unity 侧 `Assets/EmpireCraft/Presentation/`**

- `EmpireCoreDriver`：场景加载后自动建一个对象，`FlatWorldApp` 有世界时在它上面开一个 `EmpireWorld`，每帧跟着 ORBIS 的 `Day` 推进。不画东西，窗口和图层从 `EmpireCoreDriver.Instance.World` 读。没有改 ORBIS 的任何文件。
- `Editor/EmpireCoreCheck`：批处理验证入口(也在菜单 EmpireCraft/Check Core)。

## 二、核实后没进这批的

计划里阶段 1 列的 15 项，核实后有 4 项并不独立，往后放：

| 文件 | 卡在哪 | 放到 |
|---|---|---|
| ClaimRules、ClaimTuning | 判定要读帝国(`Empire`)和成员国 | 阶段 4(诉求与决议)，要等阶段 6 的帝国实体或先立帝国的身份 |
| CourtIssues、CourtDoctrine | 要 `FixedFaction`、`TemporaryFaction` 两个大类 | 阶段 4(朝堂) |

## 三、验证

**dotnet(.NET 8，语言版本卡在 C# 9，和 Unity 6 一致)**，仓库根下：

```
dotnet run --project Validation/EmpireCraft -c Release
dotnet run --project Validation/EmpireCraft -c Release -- --empire-smoke 1024 200 ValidationOutput/EmpireCraft
```

| 测试组 | 断言 | 结果 |
|---|---:|---|
| 世界操作(阶段 0) | 12 | 过 |
| 骨架 60+15 年(阶段 0) | 78 | 过 |
| 搬来的规则 | 134 | 过 |
| 帧预算 | 123 | 过：被一个占满预算的高档作业压着时，低档作业 300 帧里跑 3 次，最长等 89 帧(上限 90) |
| 规则数据 | 779 | 过：7 条制度线 757 节点、27 个文化、13,616 条文字；制度数据自带 16 条"树状图分层"提示，不是错误 |
| 账本 60+15 年(256 世界) | 1,012 | 过：同种子两次逐字节一致；存档读回一致、续跑一致；版本 1 的存档段能读；国库一年的变动等于账上十二个月的收支；世界进程与只跑骨架时相同 |
| 驱动器 | 3 | 过：每帧 2 天、60 帧每秒跑 40 年，从不落后日历；日历跳 20 年，20 帧补完 |

1024×512 世界 200+10 年冒烟：过(203 项)。

- 开局 165 ms；每个模拟年中位 2.83 ms、95 分位 5.88 ms、最大 12.57 ms。其中骨架规则 1.57 ms/年，每月账本合计 1.44 ms/年，年度结账 0.026 ms/年。
- 结束时 76 国、341 城、4,946 格有主；平均稳定度 85.2(没有城低于 40)、腐败 6%、王朝积弊 23.0、正统 61，9 国欠饷；存档 500 KiB。
- 地图生成 11.6 秒(底图那条线的事，不在这批范围)。

**Unity 6000.0.55f1 批处理**(编辑器要先关)：

```
Unity.exe -batchmode -nographics -projectPath <仓库> -logFile <日志> -executeMethod EmpireCraft.EditorTools.EmpireCoreCheck.Run [-ecCoreWidth 512] [-ecCoreYears 60]
```

- 工程编译 0 错误(`EmpireCraft.Core` 程序集不引用引擎，Newtonsoft 用 Unity 自带的包)。
- 256×128 世界 40 年(跑了两遍)：数据加载齐全，开局 10~12 ms，7,200 帧里最慢一帧 6.80 / 8.98 ms，从不落后日历，存档往返一致，PASS。
- 512×256 世界(游戏默认大小)60 年：开局 22 ms，最慢一帧 6.40 ms，PASS。

**没验证的**

- 没有进游戏窗口看过：`EmpireCoreDriver` 跟着 `FlatWorldApp` 实际推进这条路只编译过，批处理检查走的是同一个 `EmpireRunner`，但不经过场景。
- 1024 世界在 Unity 里的帧耗时没量(编辑器里生成 1024 地图要几分钟)。最慢的一帧是年初那一帧：骨架的年度规则整批算，512 世界约 6 ms，超过帧预算的 1~4 ms；城多了会更长。骨架规则到阶段 2、3 被替换时要按月错开。
- 同一种子在 .NET 8 和 Unity(Mono)下结果是否逐字节相同，没比。
- `sync`(模组改动后的三方合并)只拿一个文件试过：SimulationFrameBudget.cs 从模组 11-15 的版本(4587c414)搬入、做同样的手改、再 `sync` 到最新，结果和直接搬最新版逐字节相同。有冲突时的处理没试过。

## 四、已知的缺口

- Unity 里核心的状态还没跟 ORBIS 存档一起存：每次开世界重新开局。
- 改地形(笔刷)不会重新核对城址。
- 规则数据在编辑器里从 `Assets/EmpireCraft/Data` 读；打包出的游戏要先把它拷到 StreamingAssets 才读得到，这一步没做。
- 界面(另一条线在做的 HUD)还读旧的 `FlatSimulation` 文明数据，没接 `EmpireWorld`。
- 账本的数值(人头税、忠诚)是占位，没有平衡过。
- 命名库(各文化的词库 csv 约 1,400 个文件)没搬，阶段 3 起名时再搬。

## 五、下一批

**阶段 1 续(机械，可以马上做)**：第 0 档还剩约 1.9 万行没搬。照这批的办法，按依赖清单把"一处原版对象都不碰"的系统成批 `add`、试编译、缺什么补什么：`Data/` 下其余存档结构、国债、族群等第、帝国权威、选帝、草原规则、宪法与议会法案、党派财政与党际关系、舆论。

**阶段 2：人口与城市**(计划里约 6.5 千行)

- CityStabilitySystem、TreasurySystem 与 TreasuryHoardSystem：替换 `RealmAccounts` 里对应的占位部分，稳定度开始有后果。
- CityPopulationSystem、LandEconomySystem、RuralSettlementSystem、RealisticAgeSystem：粮食和肥力改用 ORBIS 的气候、水文、地表数据，城的辖区按地块算。
- 这些系统读写原版的城、国、人，要按计划第四节第 3 步做身份改写(`Kingdom`→`Realm` 等)，PortTool 还要加这一组改写规则。
- Unity：疆域图层、城标、点城看基本信息；核心状态并进存档。
