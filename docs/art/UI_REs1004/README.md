# 美术交付 UI_REs1004（2026-10-04，nicole 美术）

来源：飞书群 **gamejam2026讨论群**，nicole 美术 2026-10-04 16:11 发的 `UI_REs1004.zip`（1.8 MB），
同批三条说明：**「card通用资源」「破壁机 / 动画资源已更新」「内附了摆放说明」**。

## 一、交付清单与落位

| 交付物 | 落在工程里的位置 | 谁在用 |
|---|---|---|
| `card_common_bg.png`（卡底，通用）<br>`card_{元素}_bg/img.png`（元素底色 + 插画）<br>`card_name_bg.png` / `card_number_bg.png`（名字牌 / 数值牌） | `UnityProject/Assets/Resources/Art/Card/` | `Scripts/Table/CardArt.cs` 运行期拼成一张卡面 |
| `gameblender_normal_img.png`<br>`gameblender_move_01/02/03_img.png`（574x672，整机立绘 + 3 帧动画） | `UnityProject/Assets/Resources/Art/Juicer/` | `Scripts/Table/BlenderArt.cs` |
| `card_{元素}_效果图.png` ×4、`card文字标注.png`（带标注的摆放说明）<br>`截屏2026-10-04 16.10.04.png`（使用说明） | 本目录（**不进构建**，只作参考） | 人看 |

`*_bg_bg.png`（`card_name_bg_bg` / `card_number_bg_bg`）在效果图里没有出现，暂未使用。

## 二、美术写的使用说明（从截图里认出来的原文）

> 使用说明
> 静止时使用 `normal` ｜ 动画时使用 `01 02 03` 循环播放 帧率 **10 帧/秒**

已按这条接进 `BlenderArt`：不冲压时贴 `normal`，`JuicerRig.IsStamping` 期间按 10 帧/秒
循环 `move_01 → 02 → 03`。

## 三、版面是从效果图里量出来的（不是估的）

效果图坐标系是 **136x182**（= 卡底尺寸），卡面贴图是 256x358，按宽度等比放大、纵向居中。
把每个切片在效果图里做**遮罩模板匹配**，得到唯一低误差位置：

| 切片 | 位置（效果图坐标，左上角原点） | 匹配误差 |
|---|---|---|
| `card_name_bg`（62x26） | (4, 4) 左上角 | rmse ≈ 24~36 |
| `card_number_bg`（38x86） | (97, 95) 右侧偏下 | rmse ≈ 32 |
| `card_ice_img`（94x114） | (15, 31) | rmse 21 |
| `card_water_img`（86x102） | (20, 39) | rmse 13 |
| `card_vagour_img`（96x114） | (19, 29) | rmse 6 |
| `card_extraterrestrialalloy_img`（102x96） | (14, 43) | rmse 12 |

元素底色块 `*_bg` 与插画同尺寸（差 0~2 px），按中心对齐垫在插画后面。

## 四、元素皮肤 ↔ 卡牌的映射（**这处需要策划/美术确认**）

美术给的是**物态皮肤**，不是逐张食材插画 —— 效果图上写的名字就是 `ice` / `water` / `vapour` / `alloy`。
当前按"物态 + 金属"分配（代码在 `CardArt.ElementOf`）：

| 皮肤 | 给谁 | 依据 |
|---|---|---|
| `ice` | 盐性(H) 主导的食材 | 固 |
| `water` | 汞性(D) 主导的食材 | 液 |
| `vagour` | 硫性(V) 主导的食材 | 气 |
| `extraterrestrialalloy` | 铁块 / 外星合金 / 秘银锭 / 钛钨胚 | 这四张正好是四把"刀片" |
| （不给皮肤） | 变速模块 | 策划要靠冷色把模块和食材分开，套皮肤这层区分就没了 |

要改：只动 `CardArt.ElementOf()` 一处，改完不用重导图。

## 五、覆盖范围与遗留

- 已覆盖：**卡面**（13 张食材牌里 12 张有皮肤，模块保持程序化）+ **破壁机机身**（立绘顶掉程序搭的四柱/顶板/冲头/残渣/刀片）。
- 未覆盖：液体罐与刻度（罐身液面就是得分面板，不能盖）、桌面木纹、HUD 面板、卡槽角标、烛光/辉光 ——
  这些都还是 `ProceduralArt` 的程序化贴图，等对应美术到位再换。
- 刀片/残渣的冲压反馈目前被立绘盖住（整组 `Press` 关掉了）。
  想保留：`JuicerRig.HideProceduralMachineWhenArtPresent = false`。
