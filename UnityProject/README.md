# 骨架原型

> 任务：搭建项目骨架 + 定义数据结构 + 跑通状态切换
> 目标：打开游戏，能点击按钮，在几个界面之间切换，并且能看到假数据。

## 怎么跑

**Unity 版**

1. **Unity Hub → 打开 → 添加** 这个 `UnityProject` 文件夹
2. 用 **2022.3.62f3c1**（Unity 中国版）打开
3. `File → New Scene` 建一个空场景（或者用任意已有场景）
4. **按 Play**

不需要摆任何物体、不需要做预制体、不需要连线。
`PrototypeBootstrap.cs` 会在场景加载后自动创建 `[GameFlow]` 物体并挂上状态机。

> 如果打开时提示要升级版本，选**不升级**——全队的 Unity 版本必须一致。

**浏览器版（不用装 Unity）**

直接双击 `docs/流程原型预览.html`，用浏览器打开即可。
数据和流程与 Unity 版完全一致，方便开会时直接演示。

## 目录结构

```
UnityProject/
├── Assets/Scripts/
│   ├── Data/                     ← 数据结构（任务第一部分）
│   │   ├── Ingredient.cs         食材：名字 + 6 个属性
│   │   ├── SpeedModule.cs        变速模块：名字 / 效果描述 / 影响属性 / 数值
│   │   ├── Deck.cs               牌组：牌组名 + 3 食材 + 1 变速模块
│   │   ├── LevelData.cs          关卡：目标分 + 初始手牌
│   │   └── TurnState.cs          回合：回合数 / 手牌 / 刀片 / 杯内 / 得分
│   └── Prototype/
│       ├── GameFlow.cs           ← 状态机 + 界面（任务第二部分）
│       ├── FakeData.cs           ← 全部假数据
│       └── PrototypeBootstrap.cs 自动启动
└── ProjectSettings/ProjectVersion.txt
```

## 状态流转

```
主菜单
  └─[开始游戏]→ 三选一牌组
                  └─[选牌组 A/B/C]→ 选刀片（默认铁块）
                                      └─[下一步]→ ┌─────────────┐
                                                  │  回合开始    │
                                                  │      ↓       │
                                                  │  模拟中…     │
                                                  │      ↓       │
                                                  │  回合结算    │
                                                  │      ↓       │
                                                  │  手牌空？    │
                                                  └──┬───────┬───┘
                                              否 ←───┘       └──→ 是
                                              ↓                     ↓
                                        （下一回合）            关卡结束
                                                                    ↓
                                                                关卡结算
                                                                    ↓
                                                              回主菜单
```

## 数据结构

### 食材 Ingredient

| 字段 | 类型 | 范围 |
|---|---|---|
| `name` | string | 名字 |
| `hardness` | int | 硬度 0–10 |
| `temperature` | int | 温度 −20–300 |
| `acidity` | int | 酸性 0–10 |
| `sugar` | int | 糖分 0–10 |
| `oil` | int | 油脂 0–10 |
| `water` | int | 水分 0–10 |

### 变速模块 SpeedModule

| 字段 | 类型 | 例 |
|---|---|---|
| `name` | string | 高速模块 |
| `description` | string | 刀片硬度 +2 |
| `targetAttribute` | string | 硬度 |
| `value` | int | 2 |

### 牌组 Deck

`deckName` + `ingredients[3]` + `speedModule`

### 关卡 LevelData

`levelName` + `targetScore`（1000）+ `startingHand`

### 回合 TurnState

`turnNumber` · `hand` · `currentBlade` · `cupIngredients` · `currentScore`

## 假数据（数值待策划重新设计）

| 食材 | 硬度 | 温度 | 酸性 | 糖分 | 油脂 | 水分 |
|---|---|---|---|---|---|---|
| 铁块 | 10 | 0 | 0 | 0 | 0 | 0 |
| 冰块 | 8 | −15 | 0 | 0 | 0 | 10 |
| 柠檬 | 3 | 20 | 9 | 0 | 0 | 8 |
| 巧克力 | 4 | 25 | 1 | 7 | 6 | 2 |
| 辣椒 | 3 | 22 | 3 | 2 | 1 | 7 |
| 硬糖 | 9 | 20 | 1 | 10 | 0 | 1 |
| 牛奶 | 1 | 5 | 2 | 4 | 4 | 10 |
| 咖啡豆 | 8 | 20 | 5 | 1 | 3 | 2 |
| 黄油 | 2 | 10 | 1 | 1 | 10 | 3 |

**牌组**

- **A**：铁块、冰块、柠檬 + 高速模块
- **B**：巧克力、辣椒、硬糖 + 高速模块
- **C**：牛奶、咖啡豆、黄油 + 高速模块

**关卡**：第 1 关，目标分 1000

## 今天没做的

- ❌ 真实结算逻辑（得分固定 0）
- ❌ 刀片爆裂判定
- ❌ 食材之间的化学反应 / 组合效果
- ❌ 变速模块的实际生效
- ❌ 图片、动画、音效
- ❌ 存档、关卡选择、多关卡

## 技术选择说明

**为什么用 IMGUI 而不是 UGUI？**

这个原型的目的是**验证流程**，不是做界面。IMGUI 的好处：

1. 不需要 Canvas / EventSystem / Prefab / 场景文件——空场景直接跑
2. 整个原型只有代码，多人协作时不会有场景/Prefab 冲突
3. 等玩法定下来再整体换成正式 UI，这部分代码直接删掉

**为什么用 RuntimeInitializeOnLoadMethod 自动启动？**

避免"往场景里摆物体 + 连线"这种只能在 GUI 里做、且极易冲突的操作。
游戏逻辑与场景解耦。

**中文字体**

Unity 内置 GUI 字体不含中文字形，`GameFlow.EnsureStyles()` 里用
`Font.CreateDynamicFontFromOSFont` 换成系统里的微软雅黑，否则中文会显示成方块。
