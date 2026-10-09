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
- `docs/GDD.md` — 游戏设计文档（命题公布后创建）
- `docs/meetings/` — 会议记录

## 当前状态（2026-10-09）

- Unity 工程已接入 3D 桌面回合流程，默认启用规则模式；旧牌组与旧规则仍可通过设置切换体验。
- 当前卡牌定稿为 v3.0：31 张素材、8 张法术；规则解析、表现和未实现项见[缺口报告](docs/卡牌数值_v3.0_缺口报告.md)。
- 规则模式的开局牌组窗口只列出使用当前卡表素材的牌组，避免误选旧版数据后规则无法触发。
- 试玩入口：`UnityProject/Assets/Scenes/Bootstrap.unity`（Unity 2022.3.62f3c1）。

实现记录见 [CHANGELOG.md](CHANGELOG.md)，规则接入与已知限制见[3D 桌面整合说明](docs/整合_v21进3D桌面.md)。

## 快速开始

```bash
git clone <repo-url>
cd taptapgamejam2026
```

用 Unity Hub 打开 `UnityProject`，等待资源导入后运行 `Assets/Scenes/Bootstrap.unity`。规则与卡牌数据位于 `UnityProject/Assets/Resources/Config/`。

## 目录结构

```text
.
├── docs/              # 文档（开发文档 / 规则正文 / 架构说明 / 美术交付说明）
├── UnityProject/      # Unity 工程（2022.3.62f3c1）
│   ├── Assets/Scripts/Data/        # 数据层：卡牌 / 属性 / 效果 / 配置 DTO（不碰渲染）
│   ├── Assets/Scripts/Rules/       # 卡牌规则内核：层数账本 / 附魔规则表 / 中文解析 / 结算引擎
│   ├── Assets/Scripts/Table/       # 3D 桌面表现：相机构图 / 卡牌工厂 / HUD / 交互 / 量筒…
│   ├── Assets/Scripts/Prototype/   # 旧原型流程与配置加载（GameConfig / CardSpecs）
│   └── Assets/Resources/Config/    # 策划可改：game_config.json、cards_v21.json
├── Tools/             # 离线工具：RuleProbe（规则断言）、SimProbe（旧模拟探针）
├── Builds/            # 构建产物（不进 Git）
└── CHANGELOG.md       # 更新日志
```

## 官方链接

- [开发者中心](https://developer.taptap.cn/)
- [新建和管理游戏](https://developer.taptap.cn/docs/store/release/publish/create-game/)
- [游戏物料要求](https://developer.taptap.cn/docs/store/release/publish/material/)
- [审核规范细则](https://developer.taptap.cn/docs/store/release/publish/agree/)
