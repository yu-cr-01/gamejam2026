using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 桌面上的两个选择环节：三选一牌组、选刀片。
    ///
    /// 【为什么要单独一个 rig，而不是塞进 TableInteraction】
    /// TableInteraction 管的是"把牌从这里拖到那里"，是回合循环里的动作；
    /// 这两个环节是**开局前的准备**，没有拖拽，只有"点一下选中"。
    /// 混在一起的话，交互层里会多出一堆"现在是什么阶段所以该不该拖"的分支。
    ///
    /// 【和 Flow2D 的关系】
    /// 规则、文案、候选项全部来自同一份配置（LevelData.choices / game_config.json），
    /// 这里只负责把它摆到桌面上：牌组是一排大卡，刀片是"点手牌即交换"。
    /// </summary>
    public class TableChoiceRig : MonoBehaviour
    {
        public Camera         cam;
        public TableSetup     setup;
        public TableTurnLoop  loop;

        /// <summary>牌组卡片的容器</summary>
        public Transform root;

        // ══════════════════════════════════════════════════════════════
        //  牌组卡片
        // ══════════════════════════════════════════════════════════════

        /// <summary>桌上的一张牌组卡。只是个数据壳，不是 MonoBehaviour。</summary>
        private class DeckCard
        {
            public GameObject go;
            public Renderer   body;
            public Renderer   face;
            public Color      bodyBase;
            public Color      faceBase;
            public float      lift;      // 平滑后的抬升量
        }

        // 比手牌大一整圈 —— 三张并排摆在桌子中央，是开局最显眼的东西。
        //
        // ★ 整排往左偏 0.14：榨汁机在 x≈0.74 那侧，卡片如果居中摆，
        //   最右那张会插进机器的立柱里。整体让开之后构图也更平衡。
        private const float CardW     = 0.40f;
        private const float CardD     = 0.52f;
        private const float CardT     = 0.018f;
        private const float Spacing   = 0.44f;
        private const float RowZ      = 0.16f;
        private const float RowXOff   = -0.14f;

        /// <summary>刀片摆哪 —— 桌面正中偏近侧，和 Flow2D 的"上方当前刀片"对应。</summary>
        public static readonly Vector3 BladeSpot = new Vector3(0f, 0f, 0.14f);

        private static readonly Color EdgeColor = new Color(0.130f, 0.118f, 0.104f);
        private static readonly Color InkOnPaper = new Color(0.145f, 0.132f, 0.118f);

        private readonly List<DeckCard> deckCards = new List<DeckCard>();

        /// <summary>鼠标悬停在第几张牌组卡上（-1 = 没有）。</summary>
        public int DeckHovered { get; private set; } = -1;

        /// <summary>当前选中的牌组卡（-1 = 还没选）。</summary>
        /// <remarks>
        /// ★ 一定要初始化成 -1。默认值 0 的意思是"第 0 张已被选中"，
        /// 于是确认键一开局就是可用的、玩家什么都没点就选定了第一副牌组。
        /// </remarks>
        public int DeckSelected { get; private set; } = -1;

        /// <summary>
        /// 选中第 index 张牌组卡。玩家点卡片走的是同一条路 ——
        /// 自动化探针和鼠标点击共用这个入口，不另开一条捷径。
        /// </summary>
        public void SelectDeck(int index)
        {
            if (index < 0 || index >= deckCards.Count) return;
            DeckSelected = index;
        }

        // ══════════════════════════════════════════════════════════════
        //  建 / 拆
        // ══════════════════════════════════════════════════════════════

        public void BuildDeckCards(Choice choice)
        {
            ClearDeckCards();

            if (choice == null || choice.options == null) return;

            int n = choice.options.Count;
            if (n == 0) return;

            for (int i = 0; i < n; i++)
            {
                ChoiceOption o = choice.options[i];
                if (o == null) continue;

                float x = RowXOff + (i - (n - 1) * 0.5f) * Spacing;
                deckCards.Add(BuildOne(o, new Vector3(x, 0f, RowZ)));
            }
        }

        public void ClearDeckCards()
        {
            for (int i = 0; i < deckCards.Count; i++)
                CardFactory.DestroySafe(deckCards[i].go);

            deckCards.Clear();
            DeckHovered  = -1;
            DeckSelected = -1;
        }

        private DeckCard BuildOne(ChoiceOption o, Vector3 pos)
        {
            Color accent = ProceduralArt.IngredientColor(o.id);

            // 印记形状用牌组的开局刀片决定 —— 它是这副牌的门面
            AttrId dominant = AttrId.Salt;
            Ingredient blade = o.deck != null ? o.deck.InitialBlade() : null;
            if (blade != null) dominant = ProceduralArt.DominantAttr(blade);

            GameObject go = new GameObject("DeckCard_" + o.id);
            if (root != null) go.transform.SetParent(root, false);
            go.transform.position = pos;

            // ── 卡身 ──
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale    = new Vector3(CardW * 0.98f, CardT, CardD * 0.98f);
            body.transform.localPosition = new Vector3(0f, CardT * 0.5f, 0f);

            Renderer bodyR = body.GetComponent<Renderer>();
            if (bodyR != null)
            {
                Shader sh = Shader.Find("Standard");
                if (sh == null) sh = Shader.Find("Diffuse");
                if (sh != null)
                {
                    bodyR.material = new Material(sh);
                    bodyR.material.color = EdgeColor;
                }
            }

            // ── 卡面 ──
            // 和手牌用同一张卡面生成器：圆角、顶部名字色带、中间印记都在，
            // 只是尺寸不同。朝向推导见 CardFactory，别在这里另写一套。
            GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            face.transform.SetParent(go.transform, false);
            face.transform.localPosition = new Vector3(0f, CardT + 0.0006f, 0f);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.transform.localScale    = new Vector3(CardW, CardD, 1f);

            Collider fc = face.GetComponent<Collider>();
            if (fc != null) fc.enabled = false;      // 拾取只认卡身

            Renderer faceR = face.GetComponent<Renderer>();
            if (faceR != null)
                faceR.material = CardFactory.MakeUnlit(ProceduralArt.CardFace(accent, dominant));

            // ── 文字 ──
            //
            // ★ 全部落在 localZ 的**负半区**（靠近玩家这一侧）。
            //   牌组名一开始是写在正半区、也就是色带上的，结果一个字都渲染不出来 ——
            //   放大到 4 倍确认过是纯色块，而且诊断日志证明字符串本身没问题
            //   （title='硫硝爆燃'(4)），同一张卡上另外三行在负半区、全都正常。
            //   根因没查到，但规律很明确：这几张卡的正面文字只有近侧能显示。
            //   所以名字改成第一行，其余三行依次往下让位。
            //
            // ★ 高度必须显式给 CardT + 一点余量。用 CardFactory 的默认值
            //   （按手牌的 0.008 厚度算）会把字埋进这块 0.018 厚的卡身里。
            float textY = CardT + 0.0022f;

            string deckTitle = (o.deck != null && !string.IsNullOrEmpty(o.deck.name))
                ? o.deck.name
                : o.title;

            CardFactory.AddText(go.transform, deckTitle, -0.075f, 0.0115f,
                                InkOnPaper, textY);

            string ingredients = o.deck != null ? o.deck.DescribeIngredients() : "（无）";
            CardFactory.AddText(go.transform, "食材：" + ingredients, -0.115f, 0.0043f,
                                InkOnPaper, textY);

            string module = "无";
            if (o.deck != null && o.deck.modules != null && o.deck.modules.Count > 0
                && o.deck.modules[0] != null)
                module = o.deck.modules[0].name + "（" + o.deck.modules[0].Description() + "）";
            CardFactory.AddText(go.transform, "模块：" + module, -0.165f, 0.0039f,
                                InkOnPaper, textY);

            string bladeName = blade != null ? blade.name : "（无）";
            CardFactory.AddText(go.transform, "开局刀片：" + bladeName, -0.215f, 0.0039f,
                                InkOnPaper, textY);

            DeckCard c = new DeckCard();
            c.go       = go;
            c.body     = bodyR;
            c.face     = faceR;
            c.bodyBase = bodyR != null && bodyR.material != null ? bodyR.material.color : Color.white;
            c.faceBase = Color.white;
            return c;
        }

        // ══════════════════════════════════════════════════════════════
        //  输入
        // ══════════════════════════════════════════════════════════════

        void Update()
        {
            if (cam == null || loop == null) return;

            if (loop.phase == TablePhase.DeckPick)       TickDeckPick();
            else if (loop.phase == TablePhase.BladePick) TickBladePick();
        }

        /// <summary>
        /// 牌组环节：点一张即选中（不走拖拽 —— 位置是固定的，拖来拖去没有意义）。
        ///
        /// 选中和确认分开：点一下只是高亮，真正定下来要按「确认选择该卡组」。
        /// 和投放区那条规则保持一致 —— 玩家应该有反悔的余地。
        /// </summary>
        private void TickDeckPick()
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            DeckHovered = HitDeckIndex(ray);

            if (Input.GetMouseButtonDown(0) && DeckHovered >= 0)
                DeckSelected = DeckHovered;

            AnimateDeckCards();
        }

        /// <summary>
        /// 刀片环节：点手牌里的食材即与当前刀片交换。
        ///
        /// 悬停状态直接借用 TableInteraction.Hovered —— 它每帧都在做同一件事，
        /// 这里再射线一次纯属重复劳动，而且两处判定迟早会不一致。
        /// </summary>
        private void TickBladePick()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (setup == null || setup.interaction == null) return;

            PlayCard hit = setup.interaction.Hovered;
            if (hit == null) return;

            loop.SwapBladeWith(hit);
        }

        /// <summary>鼠标底下是哪张牌组卡。</summary>
        private int HitDeckIndex(Ray ray)
        {
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 50f)) return -1;

            // 从命中的碰撞体往上走，找到属于列表里的那个根节点。
            // 不用 transform.root —— 卡片的祖先一直到整张桌子，那样会全部命中。
            Transform t = hit.collider.transform;
            while (t != null)
            {
                for (int i = 0; i < deckCards.Count; i++)
                    if (deckCards[i].go != null && deckCards[i].go == t.gameObject) return i;
                t = t.parent;
            }
            return -1;
        }

        /// <summary>抬升 / 放大 / 提亮 —— 三件事一起做，选中的那张一眼可辨。</summary>
        private void AnimateDeckCards()
        {
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);

            for (int i = 0; i < deckCards.Count; i++)
            {
                DeckCard c = deckCards[i];
                if (c.go == null) continue;

                bool selected = (i == DeckSelected);
                bool hovered  = (i == DeckHovered);

                c.lift = Mathf.Lerp(c.lift, selected ? 0.045f : (hovered ? 0.020f : 0f), k);

                Vector3 p = c.go.transform.position;
                c.go.transform.position = new Vector3(p.x, c.lift, p.z);

                float wantScale = selected ? 1.05f : 1f;
                c.go.transform.localScale = Vector3.Lerp(c.go.transform.localScale,
                                                         Vector3.one * wantScale, k);

                float mul = selected ? 1.35f : (hovered ? 1.18f : 1f);
                Tint(c.body, c.bodyBase, mul);
                Tint(c.face, c.faceBase, mul);
            }
        }

        private static void Tint(Renderer r, Color baseColor, float mul)
        {
            if (r == null || r.material == null) return;

            Color want = baseColor * mul;
            want.a = 1f;

            float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
            r.material.color = Color.Lerp(r.material.color, want, k);
        }
    }
}
