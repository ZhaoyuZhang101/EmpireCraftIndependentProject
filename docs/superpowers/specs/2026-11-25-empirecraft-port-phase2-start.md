# 帝国盒子移植：阶段 2 起步(2026-11-25)

接 `2026-11-25-empirecraft-port-phase1.md`。阶段 1(不靠改写身份就能编译的 141 个源文件、11,782 行)已在 `20ee199`、`0288d5c`。本文记阶段 2 的头一步：实体层改造、身份改写规则，以及一次全量调查得出的待办排行。**这一步还没有把任何新系统搬进来。**

模组来源提交：`e770dbbe`。

## 一、这一步做了什么

**实体层改成对象引用**

- 阶段 0 的国、城、人、宗族互相用整数编号引用(`Realm.Capital` 是城的编号)。模组的代码全是对象引用的写法(`city.kingdom == owner`、`kingdom.capital`、`HashSet<City>`)，要机械改写过来，实体就得是对象图。
- 现在 `Realm.Capital` 是 `City`、`Realm.Ruler` 是 `Person`、`Realm.Cities` 是 `List<City>`、`City.Realm` 是 `Realm`，人的父母配偶子女和宗族也都是对象。`Id` 保留：仍是仓库里的下标，存档和史书只写 `Id`。
- 改了 `Entities`、`EmpireWorld`、`WorldOps`(参数和返回值换成对象)、`SkeletonRules`、`EmpireBootstrap`、`EmpireInvariants`、`EmpireSave`(读时先建对象、读完再连引用；存档字节格式不变)、`RealmAccounts` 和测试。
- **行为没变**：新增 `--fingerprint` 命令，打印固定世界跑固定年数后的存档哈希。改造前后两组(256 世界 75 年、512 世界 120 年)逐字节相同。

**身份改写规则**(`port_tool.py` 的 `--identity`，按文件启用，记在清单里，`sync` 时同样应用)

- 类型：`Kingdom`→`Realm`、`Actor`→`Person`、`MapBox`→`EmpireWorld`、`World.world`→`EmpireWorld.Current`。
- 成员，一个标识符换一个标识符：`isRekt()`→`IsGone()`、`isAlive()`→`Alive`、`king`→`Ruler`、`capital`→`Capital`、`cities`→`Cities`、`kingdom`→`Realm`、`hasKing()`→`HasRuler()`、`getID()`→`Id`、`data.name`→`Name`。
- 历法：`Date.getMonthsSince`→`GameClock.MonthsSince` 等。`GameClock` 是新写的：时间戳以"世界的月"为单位。
- 模组自己的类里也有叫 `capital`、`cities` 的字段(各 4、22 处声明)，用 `--keep title.capital` 这样的名单按文件保护。
- 顺带：模组里 `Layer.Empire` 这种拿命名空间的一段当限定词的写法，改写时折掉。

**其它**

- 宿主层的数据目录类从 `EmpireData` 改名 `GameData`：模组自己有个 `EmpireData`(帝国的存档数据)，重名。
- `EmpireWorld.Current`：搬来的系统和模组里一样是静态的，靠它找到世界；`EmpireSimulation` 每步之前设好。(这一条现在只留了位置，还没有系统在用。)
- `closure.py` 加了 `--identity`。

## 二、发现：不能一个系统一个系统地搬

拿城稳定(CityStabilitySystem)、国库(TreasurySystem、TreasuryHoardSystem)三个文件试：身份改写之后，方法体里还有 324 处不通过——引用了约 50 个别的系统(人口、税蚀、君主资质、军团、周制、多京……)和约 60 个原版对象上的成员。

模组的系统是一张互相勾连的网，每个系统都点名调用几十个别的系统。按计划原来写的"先搬人口城市、再搬宗族"一块一块来，每一块都会卡在别的块上。

**改成这样做**：把剩下的全部文件一起看，统计它们向实体层要什么；按次数从多到少补实体的成员和世界操作；每补一批，跑一遍 `closure.py --identity`，把能编译的那一圈成批搬进来。计划里各阶段的先后，改当"先补哪一类成员"的优先级用。

## 三、全量调查

工具：`Tools/PortTool/survey.py` 加 `Validation/PortSurvey`(用编译器的语义分析逐个查"这个成员访问解析得了吗"，不要求编译通过)。结果存 `Port/survey.json`，以后每补一批重跑对账。

对象：模组核心七个目录里还没搬的 556 个文件、21.2 万行(含 `GameClassExtensions` 那几个扩展大文件)。

- 解析不了的成员访问 **22,419 处**(1,216 种)，解析不了的名字 **9,769 处**(378 种)。
- 完全解析的只有 25 个文件、2,842 行。
- 落在实体上的占 11,828 处(53%)，而且很集中：

| 接收者 | 处 | 种 | 覆盖八成只需 | 最多的几个 |
|---|---:|---:|---:|---|
| Person(人) | 3,988 | 117 | 15 种 | id 569、getName 557、data 556、Realm 464、city 187、isKing 172、hasTrait 120、isAdult 118 |
| Realm(国) | 3,719 | 79 | 9 种 | data 1,164、id 978、religion 164、isInWarWith 158、wild 149、getWars 113、getColor 104、hasEnemies 97 |
| City(城) | 2,809 | 103 | 12 种 | data 826、id 313、zones 258、getTile 207、units 139、city_center 131、neighbours_cities 100 |
| EmpireWorld(世界) | 1,286 | 46 | 9 种 | kingdoms 431、units 151、zone_calculator 91、getCurWorldTime 75、wars 73、diplomacy 71 |

也就是约 45 种成员就解决实体缺口的八成。

- 不在实体上的大头，是这个仓库还没有对应物的原版概念：

| 概念 | 成员访问 | 当名字用 |
|---|---:|---:|
| TileZone(地图分区) | 1,898 | 1,554 |
| War(战争) | 944 | 457 |
| WorldTile(地块) | 778 | 501 |
| Religion(宗教) | 297 | 517 |
| Building、BuildingAsset(建筑) | 450 | 279 |

- 模组自己的两个类也大量解析不了，因为它们继承原版的元对象基类：`Empire`(帝国)2,245 处、`KingdomTitle`(法理头衔)512 处。要给它们换个自己的基类。
- 名字里另有 `ModClass` 603(模组入口上的全局开关)、`UnityEngine` 508、`Vector2`/`Vector2Int`/`Color32`/`Random` 等引擎类型约 800。

## 四、下一步的顺序

1. `id` 和 `data`：三种实体上合计 4,400 处。`id`→`Id` 加进改写规则；`data` 先看都读了哪些子字段(名字、颜色、建立时间、自定义键值)，在实体上给出对应字段。
2. 人与国、城的关系：`Person.Realm`、`Person.city`、`isKing`、`isCityLeader`、`Realm.units`、`City.units`、`City.leader`——要先定"人"在新游戏里是谁(只有名人，还是也有背景人口的代表)。
3. 扩展数据：`GetOrCreate()`(模组把自己的状态挂在原版对象上的那套)，在实体上做一张按 `Id` 的表，随存档；`CityExtraData`、`KingdomExtraData`、`ActorExtraData` 从扩展大文件里抽出来。
4. 世界上的集合：`kingdoms`、`units`、`wars`、`religions`、`clans`。
5. 给 `Empire`、`KingdomTitle` 换基类。
6. 每做完一项跑 `closure.py --identity`，把通过的一圈搬进来、接进月结、补测试。
7. 战争、宗教、地图分区三个概念各自是一大块，对应计划的阶段 5、6 和"地图模型"；轮到它们时先定自己的类型再回来跑闭包。

## 五、验证

- **存档指纹**(`dotnet run --project Validation/EmpireCraft -c Release -- --fingerprint <宽> <年>`)，实体层改造前后相同：
  - 256×128 世界 75 年：`0675CEA4…FFD46CAB`(46,204 字节，15 国、30 城、528 人、98 条史书)
  - 512×256 世界 120 年：`9F86557D…775268B6`(133,545 字节，33 国、75 城、1,718 人、361 条史书)
- dotnet 八组测试全过(世界操作那组从 12 项加到 14 项：新增"都城不能是空"和"不能是这个世界没有的城")。
- Unity 6000.0.55f1 批处理：工程编译 0 错误；512×256 世界 60 年 PASS，最慢一帧 6.98 ms。结束时 29 国、75 城、460/723 人在世、平均稳定度 93.1、国库合计 323,885、存档 77 KiB——和实体层改造前那次跑出来的数字逐项相同。
- 身份改写规则只用一段样例和三个系统的试改写核对过；调查工具的结果没有另找办法交叉核对。

## 六、没做的

- 没有任何系统按身份改写搬进来；账本(RealmAccounts)里的占位部分原样还在。
- `--keep` 名单、`closure.py --identity` 只写了，没有在真实搬迁里用过。
- 调查按"成员访问解析不了"计数：重载对不上(参数类型不符)的不计，所以实际要改的地方比 22,419 多。
- `UnificationSystem` 里有个自己的嵌套类型叫 `Realm`，和实体重名，轮到它时要处理。

## 七、第二步：第一批实体成员与按类型改名(2026-11-25)

模组来源提交：`8f36692e`(已搬的 141 个文件到这个提交没有变化)。**仍然没有新系统搬进来**，原因见下面"成批试编译"。

**按类型改成员名**

`id`、`data`、`name` 这类名字，原版对象上有，模组自己的类上也到处有，不能按文字硬换。`Validation/PortSurvey` 加了 `fix` 模式：用语义分析看每个成员访问的接收者是什么类型，只在"接收者是实体、且这个成员解析不了"时按 `Port/identity-members.json` 改名；一次改名可能让后面的接收者有了类型，所以反复做到不再变化。`survey.py` 和 `closure.py --identity` 都在文字改写之后跑这一步。

映射表现有 43 条，例如 `Realm.id`→`Key`、`Realm.data`→`Record`、`Person.getName`→`FullName`、`Person.isKing`→`IsRuler`、`City.joinAnotherKingdom`→`JoinRealm`、`EmpireWorld.kingdoms.get`→`RealmByKey`。

**实体上补的成员**

- 国、城、人、宗族：`Key`(给搬来的系统当表键和存档键用的 long)、`Record`(模组里大量 `x?.data == null` 这种"有没有这个对象"的判断，落到它上)、建立或出生的时间戳。
- 国：`IsWild`、`IsNeutral()`、`IsCivilized()`(这里的国都是定居政权，无主之地不算国)。
- 城：`Leader`(城主，还没人任命，也还没进存档)、`HasLeader()`、`IsCapital()`、`PopulationCount()`。
- 人：`Throne`(所坐的王位，由世界操作维护、读档时重建，不进存档)、`Realm`(在位者属王位所在的国，其余人属所在城的国)、`IsRuler()`、`IsCityLeader()`、`FullName()`、`Name`、`Age()`、`IsAdult()`、`HasClan()`。
- 世界：`LiveRealms`(在世的国，按立国先后)、按键取国、城、人、宗族(已亡的返回空)、`Now()`。
- 世界操作：新增废君 `Dethrone`；另有一组挂在实体上的写法(`realm.SetRuler(person)`、`city.JoinRealm(realm)` 等)，作用在当前世界上，供搬来的系统调用。
- 不变量多查三条：在世国名单、君主与王位互相对得上。

行为没变：两组存档指纹仍与最初相同。

**顺带修的改写规则**

- 模组有四个文件声明了自己的辅助类型也叫 `Realm`(其中 `WorldMapHistory` 的是顶层类型)，改写时让它们改叫 `ModRealm`。
- `x?.list` 这种带问号的访问，去掉 `.list` 时连问号一起去掉。

**调查数字的变化**(同一批 556 个文件、21.2 万行)

| | 这一步之前 | 这一步之后 |
|---|---:|---:|
| 按类型改名 | — | 7,047 处 |
| 解析不了的成员访问 | 22,419 | 15,167 |
| 其中落在实体上 | 11,828 | 4,620 |
| 解析不了的名字 | 9,769 | 9,775 |
| 完全解析的文件 | 25 个 2,842 行 | 31 个 3,320 行 |

实体上剩下的 4,620 处，已经不是换个名字的事了，都是这个仓库还没有的东西：

| 实体 | 剩 | 最多的 |
|---|---:|---|
| 城 | 1,532 | zones 260、getTile 219、units 140、city_center 136、neighbours_cities 102、religion 86、buildings 61 |
| 人 | 1,250 | renown 134、hasTrait 120、asset 76、money 66、joinCity 64、plot 47 |
| 国 | 1,203 | religion 171、isInWarWith 162、getColor 117、getWars 113、hasEnemies 97、getAlliance 66、units 61 |
| 世界 | 626 | zone_calculator 91、wars 73、diplomacy 71、GetTile 58、religions 50 |

**成批试编译(`closure.py --identity`)的第一圈**

548 个候选里只有 5 个文件(330 行)通过，其中 3 个是阶段 1 就有意不搬的，有用的只有 `ExtraDataBase`、`FrameBudgetQueue` 两个，这一步没有搬它们。

原因：`KingdomExtension`、`CityExtension`、`ActorExtension` 三个扩展大文件(合计约 8,900 行)定义了别的系统到处调用的扩展方法，它们自己引用了几乎一切(战争、宗教、分区、帝国、建筑)，过不了；它们一倒，依赖它们的系统全部跟着倒(第 2、3、5 轮分别连带剔掉 295、150、83 个文件)。

## 八、接下来的顺序(修订)

1. **给 `Empire`、`KingdomTitle` 换基类**：这两个是模组自己的类，却继承原版的元对象基类，合计 2,763 处解析不了，种类只有十来种。写一个自己的基类，是眼下最划算的一项。
2. **扩展数据和三个扩展大文件**：`GetOrCreate()` 那套状态表落到实体上；三个大文件按成员拆开，能过的先过，过不了的挂起。这一项做完，闭包才会成圈。
3. **战争与外交**：`isInWarWith`、`getWars`、`hasEnemies`、联盟、`diplomacy.startWar`、`wars.endWar`，加上 `War` 类型本身，约 3,000 处。对应计划的阶段 5，要先定自己的战争记录长什么样。
4. **宗教与文化**当实体：约 1,400 处。
5. **人物属性**：名望、钱、特质、才干等，约 700 处；城里、国里"有哪些人"。
6. **地图分区**：`zones`、`getTile`、`city_center`、相邻的城，加上 `TileZone`、`WorldTile` 两个类型，约 6,700 处，是最大的一块，对应计划里"定自己的地图模型"。

每做完一项重跑 `survey.py` 对账、跑 `closure.py --identity` 看能搬进多少。

## 九、第二步的验证

- 存档指纹(256 世界 75 年、512 世界 120 年)仍与实体层改造之前相同。
- dotnet 八组测试全过；不变量每年多查在世国名单和王位两项。
- Unity 6000.0.55f1 批处理：工程编译 0 错误；512×256 世界 60 年 PASS，最慢一帧 6.61 ms，结束时的国数、城数、人口、稳定度、国库、存档大小与此前各次逐项相同。
- 按类型改名改了 7,047 处，只验证到"改完之后这些访问能解析"；改名后的代码除那 31 个文件外都还编译不过，没有运行过。
- `closure.py --identity` 找到的 5 个文件没有搬，所以"带身份改写的搬入、登记、同步"这条路还没有实际走过。
