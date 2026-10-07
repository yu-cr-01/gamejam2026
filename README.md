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

## 最近更新（2026-10-07）

- **规则逻辑 v2.1 跑通**：规则引擎（附魔规则表 + 中文规则解析 + 六步启动结算）+ 接进 3D 桌面回合循环，
  离线断言 210 项全绿；旧流程保留在模式开关后面。
- **卡牌数据**：`Resources/Config/cards_v21.json`（32 素材 + 8 法术）；牌组新增 水·冷热流 / 铁·熔融流 / 硫·酸蚀流。
- **界面/表现**：面板半透明可拖动、牌组卡自适应不越界、关卡窗口、卡牌图鉴（**F1**）、
  蜡烛下面的量筒得分板、卡身侧面改卡纸色。

细节与已知问题见 [CHANGELOG.md](CHANGELOG.md)。

## 快速开始

```bash
git clone <repo-url>
cd taptapgamejam2026
```

打开 `docs/开发文档.md`，按 §17「今晚组会必须产出」逐项确认。

## 目录结构

```text
.
├── docs/              # 文档（开发文档 / 规则正文 / 架构说明 / 美术交付说明）
├── UnityProject/      # Unity 工程（2022.3.62f3c1）
│   ├── Assets/Scripts/Data/        # 数据层：卡牌 / 属性 / 效果 / 配置 DTO（不碰渲染）
│   ├── Assets/Scripts/Rules/       # v2.1 规则内核：层数账本 / 附魔规则表 / 中文解析 / 结算引擎
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
