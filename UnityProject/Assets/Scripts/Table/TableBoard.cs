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
