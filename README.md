# TapTap 聚光灯 Game Jam 2026

第三届 TapTap 聚光灯 · 21 天游戏创作挑战参赛项目。

| 项目 | 内容 |
|---|---|
| 开发窗口 | 2026-10-01 ~ 2026-10-21 |
| 试玩评审 | 2026-10-25 ~ 2026-12-03 |
| 命题公布 | 2026-10-01 12:00 |
| 项目代号 | 待定 |

## 文档

- **[开发文档](docs/开发文档.md)** — 团队单一事实来源（管线 / 规范 / 排期 / 提审清单）
- **[更新日志](CHANGELOG.md)** — 按分支/日期记录每次改动（含每条提交号与"待策划确认"清单）
- **[卡牌数值定稿 v3.0](docs/卡牌数值定稿_v3.0.md)** — 策划定稿的卡表正文
- **[缺口报告](docs/卡牌数值_v3.0_缺口报告.md)** — 未实现项与待确认项，**动卡表之前先看这个**
- [核对表](docs/卡牌数值_v3.0_核对表.md) / [差异报告](docs/卡牌数值_v3.0_差异报告.md) — 卡表与正文逐条比对
- **[规则引擎架构](docs/规则引擎架构.md)** — 层数 / 附魔 / 结算的代码结构
- **[商店与卡牌管线 v3.0](docs/商店与卡牌管线_v3.0.md)**
- **[3D 桌面整合说明](docs/整合_v21进3D桌面.md)** — 规则怎么接进桌面、已知限制
- **[程序结构](docs/程序结构.md)** — 分层与依赖方向
- **[探针阶段索引](docs/探针阶段索引.md)** — `AutoPlayHarness` 377 个自动化阶段对照表（该文件 9614 行，占 Assets 的 23.4%，仅编辑器用）
- `docs/GDD.md` — 游戏设计文档（命题公布后创建）
- `docs/meetings/` — 会议记录

## 当前状态（2026-10-13）

- Unity 工程已接入 3D 桌面回合流程，默认启用规则模式；旧牌组与旧规则仍可通过设置切换体验。
- 当前卡牌定稿为 v3.0：**31 张素材、8 张法术**（`cards_v21.json`）；规则解析、表现和未实现项见[缺口报告](docs/卡牌数值_v3.0_缺口报告.md)。
- **附魔规则（2026-10-12 策划定稿）**：火焰 / 冰霜 / 酸蚀 一律附魔 **1 层**，且**不再随回合衰减**。
- **商店管线（2026-10-12 新增）**：`ShopV30.cs` + `shop_v30.json`（商店数 / 法术数 / 稀有度权重 / 价格 / 3 个商人）。
- 规则模式的开局牌组窗口只列出使用当前卡表素材的牌组，避免误选旧版数据后规则无法触发。
- **离线断言 659 项全过**（`Tools/RuleProbe`），不需要开 Unity。
- 试玩入口：`UnityProject/Assets/Scenes/Bootstrap.unity`（Unity 2022.3.62f3c1）。

实现记录见 [CHANGELOG.md](CHANGELOG.md)，规则接入与已知限制见[3D 桌面整合说明](docs/整合_v21进3D桌面.md)。

## 快速开始

```bash
git clone <repo-url>
cd taptapgamejam2026
```

用 Unity Hub 打开 `UnityProject`，等待资源导入后运行 `Assets/Scenes/Bootstrap.unity`。规则与卡牌数据位于 `UnityProject/Assets/Resources/Config/`。

**改规则之前先跑一遍离线断言** —— 规则内核是纯逻辑，不用开 Unity、不占工程锁、不用等编辑器加载：

```powershell
pwsh -File Tools/RuleProbe/run.ps1      # 规则内核，659 项断言
pwsh -File Tools/SimProbe/run.ps1       # 旧版杯内模拟，20 项断言
```

## 目录结构

```text
.
├── docs/              # 文档（开发文档 / 规则正文 / 架构说明 / 美术交付说明）
├── UnityProject/      # Unity 工程（2022.3.62f3c1）
│   ├── Assets/Scripts/Data/        # 数据层：卡牌 / 属性 / 效果 / 配置 DTO（不碰渲染）
│   ├── Assets/Scripts/Rules/       # 卡牌规则内核：层数账本 / 附魔规则表 / 中文解析 / 结算引擎
│   ├── Assets/Scripts/Table/       # 3D 桌面表现：相机构图 / 卡牌工厂 / HUD / 交互 / 量筒…
│   ├── Assets/Scripts/Prototype/   # 旧原型流程与配置加载（GameConfig / CardSpecs）
│   └── Assets/Resources/Config/    # 策划可改：game_config.json、cards_v21.json、shop_v30.json
├── Tools/             # 离线工具：RuleProbe（规则断言 659 项）、SimProbe（旧模拟探针 20 项）
├── Builds/            # 构建产物（不进 Git）
└── CHANGELOG.md       # 更新日志
```

## 分支

| 分支 | 用途 |
|---|---|
| `main` | 集成线。2026-10-13 已快进到最新，与 `feat/rules-v21-turnloop` 同一提交。 |
| `feat/rules-v21-turnloop` | 规则与卡牌主线（v2.1 / v3.0 卡表、规则引擎、商店）。 |
| `art/ui-res-1004` | 美术与 UI 资源线。 |

**动手之前先确认自己在哪条分支上** —— 三条线并行推进，工作区默认检出的是其中一条，不是 `main`。

## 官方链接

- [开发者中心](https://developer.taptap.cn/)
- [新建和管理游戏](https://developer.taptap.cn/docs/store/release/publish/create-game/)
- [游戏物料要求](https://developer.taptap.cn/docs/store/release/publish/material/)
- [审核规范细则](https://developer.taptap.cn/docs/store/release/publish/agree/)
