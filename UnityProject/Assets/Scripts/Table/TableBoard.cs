using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 桌面卡槽系统：管理"哪些位置能放卡、谁占了哪个位"。
    ///
    /// 【职责单一】只管位置和占用关系，不管显示、不管输入、不管数据。
    /// 所以以后要换成《邪恶铭刻》那种不规则摆放（每行数量不同、有特殊格），
    /// 只要把 BuildGrid 换成别的布局函数，交互和卡牌代码都不用动。
    /// </summary>
    public class TableBoard : MonoBehaviour
    {
        /// <summary>所有卡槽的世界坐标</summary>
        public List<Vector3> slots = new List<Vector3>();

        /// <summary>每个卡槽上的卡（null 表示空）</summary>
        private PlayCard[] occupants = new PlayCard[0];

        public int SlotCount { get { return slots.Count; } }

        /// <summary>铺一个规则的卡槽网格。cols 列 × rows 行，居中于 origin。</summary>
        public void BuildGrid(int cols, int rows, float spacingX, float spacingZ, Vector3 origin)
        {
            slots.Clear();

            float totalW = (cols - 1) * spacingX;
            float totalD = (rows - 1) * spacingZ;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    slots.Add(new Vector3(
                        origin.x - totalW * 0.5f + c * spacingX,
                        origin.y,
                        origin.z - totalD * 0.5f + r * spacingZ));
                }
            }

            occupants = new PlayCard[slots.Count];
        }

        public Vector3 SlotPosition(int index)
        {
            if (index < 0 || index >= slots.Count) return Vector3.zero;
            return slots[index];
        }

        public bool IsValidSlot(int index)
        {
            return index >= 0 && index < slots.Count;
        }

        public bool IsFree(int index)
        {
            return IsValidSlot(index) && occupants[index] == null;
        }

        /// <summary>
        /// 卡槽矩形的尺寸（世界单位）。由 TableSetup 按卡槽指示块的实际大小填进来，
        /// 吸附判定用它当"框"。
        /// </summary>
        public float slotSizeX = 0.255f;
        public float slotSizeZ = 0.350f;

        /// <summary>
        /// 吸附判定：这一张牌该落在哪个槽。
        ///
        /// 【为什么不用"到槽中心的半径"】
        /// 原来是"离槽中心 0.13 米以内才吸附"，而槽间距是 0.30 × 0.40 ——
        /// 等于必须丢在中心附近才行，稍微偏到框边上就掉回手牌，手感很"黏手"。
        /// 但玩家脑子里判定的是一句"我有没有把它放进那个框里"，
        /// 不是"我离中心几厘米"。
        ///
        /// 现在改成**框对框**：卡槽是个矩形，再往外给一圈余量 slack，
        /// 落点只要落进「框 + 余量」就算命中。
        /// 多个框同时命中时取最近的那个（两轴归一化后比较，免得扁长的框把判定带偏）。
        ///
        /// 余量给足之后相邻框的判定区会互相重叠、整片格子连成一整块，
        /// 于是"往那片区域里随便一丢"就能吸上 —— 正是要的松手感。
        /// </summary>
        public int FindDropTarget(Vector3 world, float slackX, float slackZ)
        {
            int best = -1;
            float bestDist = float.MaxValue;

            float reachX = slotSizeX * 0.5f + slackX;
            float reachZ = slotSizeZ * 0.5f + slackZ;

            for (int i = 0; i < slots.Count; i++)
            {
                if (occupants[i] != null) continue;      // 已被占的不参与

                float dx = Mathf.Abs(world.x - slots[i].x);
                float dz = Mathf.Abs(world.z - slots[i].z);

                if (dx > reachX || dz > reachZ) continue;

                float d = dx / reachX + dz / reachZ;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// 找离 world 最近的空槽。
        /// maxDist 是吸附半径 —— 超出就不吸附（表示"放回手牌"）。
        /// </summary>
        public int FindNearestFreeSlot(Vector3 world, float maxDist)
        {
            int best = -1;
            float bestSqr = maxDist * maxDist;

            for (int i = 0; i < slots.Count; i++)
            {
                if (occupants[i] != null) continue;
                float d = (slots[i] - world).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = i; }
            }
            return best;
        }

        /// <summary>把卡放进槽位。</summary>
        public bool Place(int index, PlayCard card)
        {
            if (!IsFree(index) || card == null) return false;
            occupants[index] = card;
            card.slotIndex = index;
            return true;
        }

        /// <summary>把卡从它所在的槽位移除。</summary>
        public void Clear(PlayCard card)
        {
            if (card == null) return;
            int i = card.slotIndex;
            if (IsValidSlot(i) && occupants[i] == card) occupants[i] = null;
            card.slotIndex = -1;
        }

        public void ClearAll()
        {
            for (int i = 0; i < occupants.Length; i++) occupants[i] = null;
        }

        public PlayCard Occupant(int index)
        {
            return IsValidSlot(index) ? occupants[index] : null;
        }
    }
}
