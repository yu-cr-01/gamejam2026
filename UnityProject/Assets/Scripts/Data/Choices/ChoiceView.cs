namespace GameJam.Data
{
    /// <summary>
    /// 选择环节的**界面形态** —— 数据是同一份，只是画法不同。
    ///
    /// 【为什么加这个而不是加两个状态】
    /// 状态机只有"选择环节"这一个状态，它按顺序遍历 LevelData.choices。
    /// 如果为了"三选一"和"选刀片"各加一个 GameFlowState，
    /// 就回到了"关卡换个玩法就得改状态机"的老路。
    ///
    /// 现在改成：关卡声明选择环节 → 环节自己声明用什么形态画。
    /// 状态机仍然不认识"牌组"和"刀片"。
    ///
    /// 三种形态：
    ///   List      —— 通用竖排列表，pickCount 决定选几个（Day 1 就有）
    ///   DeckCards —— 三张卡片横排 + 详情，用来选牌组
    ///   BladeSwap —— 当前刀片 + 手牌，点手牌即换刀片
    /// </summary>
    public enum ChoiceView
    {
        /// <summary>通用竖排列表。靠 pickCount 支持单选 / 多选。</summary>
        List = 0,

        /// <summary>牌组卡片：横排若干张牌组卡，点开看详情，再确认。</summary>
        DeckCards = 1,

        /// <summary>换刀片：上方当前刀片（含全部属性），下方手牌，点食材即交换。</summary>
        BladeSwap = 2,
    }
}
