using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.UI
{
    /// <summary>
    /// THE SKELETON BOARD (DinoDigger-5ve): the wordless collection screen the fossil finale
    /// hangs on. Five dark skeleton silhouettes — one per fossil species — that FILL IN bone by
    /// bone as the dig banks them, brighten into the real dinosaur in full colour when a
    /// skeleton is complete, and stay that way once the Dino-Matic has brought it back.
    ///
    /// TODDLER RULES, ALL OF THEM STRUCTURAL:
    ///   - NO WORDS, NO NUMBERS. A slot is a bone-shaped ghost or a bright bone; a species is
    ///     a dark shape or a coloured dinosaur. That is the entire vocabulary.
    ///   - IT PAUSES NOTHING. Opening the board does not touch <see cref="GameState"/>, the
    ///     timescale, the backhoe or any tween. The island keeps running behind it, so a board
    ///     left open by a distracted 2-year-old costs nothing.
    ///   - IT IS TRIVIAL TO LEAVE. A giant X, and tapping anywhere outside the cards. Both do
    ///     exactly the same thing.
    ///   - THE BUTTON ONLY EXISTS ONCE IT MEANS SOMETHING. The HUD bone button is hidden until
    ///     the first bone is banked, so nothing on screen is ever a dead end.
    ///
    /// BUILT AT RUNTIME, ON PURPOSE. <see cref="Build"/> constructs the whole panel under the
    /// existing Canvas and <c>GameManager</c> calls it on boot when no board is wired. That is
    /// the same choice the machine friends made — nothing is pre-placed, one construction path
    /// — and it means the shipped scene needs no rebuild to gain the board.
    ///
    /// Every sprite lookup is null-tolerant: with no imported art the cards are plain dark
    /// rectangles and the bones plain light ones, which still fill in, still tap, and still
    /// prove every behaviour a test cares about.
    /// </summary>
    public class SkeletonBoard : MonoBehaviour
    {
        // ---- layout, in CanvasScaler reference pixels (1920x1080) ----
        private const float CardWidth = 320f;
        private const float CardHeight = 440f;
        private const float CardPitch = 340f;
        private const float SilhouetteWidth = 260f;
        private const float SilhouetteHeight = 300f;
        private const float SlotSize = 84f;
        private const float PanelWidth = 1760f;
        private const float PanelHeight = 540f;
        private const float ButtonSize = 120f;

        // ---- palette ----
        private static readonly Color BackdropTint = new Color(0.05f, 0.06f, 0.10f, 0.55f);
        private static readonly Color PanelTint = new Color(0.98f, 0.94f, 0.84f, 0.98f);
        private static readonly Color CardTint = new Color(0.90f, 0.84f, 0.70f, 1f);
        private static readonly Color SilhouetteDark = new Color(0.18f, 0.19f, 0.24f, 1f);
        private static readonly Color SilhouetteLive = Color.white;
        private static readonly Color CloseBackTint = new Color(0.96f, 0.55f, 0.35f, 1f);
        private static readonly Color CloseBarTint = new Color(1f, 0.98f, 0.94f, 1f);
        private static readonly Color SparkleTint = new Color(1f, 0.93f, 0.55f, 1f);

        /// <summary>One species' card: its silhouette plus its bone slots.</summary>
        private class Card
        {
            public DinoType Species;
            public RectTransform Root;
            public Image Silhouette;
            public readonly List<SkeletonBoardSlot> Slots = new List<SkeletonBoardSlot>();
            public bool Complete;      // last-rendered completion, so the celebration fires once
            public bool Celebrated;
        }

        private PlaceholderLibrary _library;
        private GameConfig _config;
        private RectTransform _panel;      // the whole modal (backdrop + cards), toggled on open
        private RectTransform _button;     // the HUD bone button
        private readonly List<Card> _cards = new List<Card>();

        private bool _open;
        private int _opens;                // test-observable
        private int _completionCelebrations;

        /// <summary>True while the collection panel is showing.</summary>
        public bool IsOpen => _open;

        // ------------------------------------------------------------ TEST HOOKS

        internal bool TestOpen => _open;
        internal int TestOpens => _opens;
        internal int TestCardCount => _cards.Count;
        internal int TestCompletionCelebrations => _completionCelebrations;
        internal bool TestButtonVisible => _button != null && _button.gameObject.activeSelf;

        /// <summary>TEST HOOK. Press the HUD bone button (opens the board).</summary>
        internal void TestPressButton()
        {
            SkeletonBoardTap tap = _button != null ? _button.GetComponent<SkeletonBoardTap>() : null;
            if (tap != null)
            {
                tap.TestTap();
            }
            else
            {
                Open();
            }
        }

        /// <summary>TEST HOOK. How many of <paramref name="species"/>' slots are drawn FILLED.
        /// This is what the child can see, read straight off the live UI objects — so a case
        /// comparing it against the bone bank is comparing the picture to the truth.</summary>
        internal int TestFilledSlots(DinoType species)
        {
            Card c = FindCard(species);
            if (c == null)
            {
                return 0;
            }

            int n = 0;
            for (int i = 0; i < c.Slots.Count; i++)
            {
                if (c.Slots[i] != null && c.Slots[i].IsFilled)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>TEST HOOK. Total slots drawn for a species (its skeleton size).</summary>
        internal int TestSlotCount(DinoType species)
        {
            Card c = FindCard(species);
            return c != null ? c.Slots.Count : 0;
        }

        /// <summary>TEST HOOK. Is this species' silhouette drawn BRIGHT (skeleton complete)
        /// rather than dark?</summary>
        internal bool TestCardBright(DinoType species)
        {
            Card c = FindCard(species);
            return c != null && c.Silhouette != null && c.Silhouette.color.r > 0.5f;
        }

        /// <summary>TEST HOOK. Tap one bone slot (the wiggle).</summary>
        internal SkeletonBoardSlot TestSlot(DinoType species, int slot)
        {
            Card c = FindCard(species);
            return c != null && slot >= 0 && slot < c.Slots.Count ? c.Slots[slot] : null;
        }

        // ---------------------------------------------------------------- build

        /// <summary>Build the whole board (HUD button + modal panel + five cards) under
        /// <paramref name="canvas"/> and return it. ONE construction path, shared by the boot
        /// self-heal and any future scene build, so what a test drives is always what a child
        /// sees. Returns null without a canvas.</summary>
        public static SkeletonBoard Build(Canvas canvas, PlaceholderLibrary library, GameConfig config)
        {
            if (canvas == null)
            {
                return null;
            }

            var go = new GameObject("SkeletonBoard", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            Stretch(rt);

            var board = go.AddComponent<SkeletonBoard>();
            board._library = library;
            board._config = config;
            board.BuildButton(rt);
            board.BuildPanel(rt);
            board.Close();
            board.Refresh();
            return board;
        }

        private void BuildButton(RectTransform parent)
        {
            var go = new GameObject("BoneButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _button = (RectTransform)go.transform;
            _button.anchorMin = _button.anchorMax = new Vector2(1f, 1f);
            _button.pivot = new Vector2(1f, 1f);
            // Under the treasure counter (which occupies the top ~140px of the corner), so the
            // two HUD affordances stack down the same edge instead of fighting for a corner.
            _button.anchoredPosition = new Vector2(-30f, -190f);
            _button.sizeDelta = new Vector2(ButtonSize, ButtonSize);

            var img = go.AddComponent<Image>();
            img.sprite = ButtonIcon();
            img.preserveAspect = true;
            img.color = Color.white;

            go.AddComponent<SkeletonBoardTap>().Bind(Open);
        }

        /// <summary>The button's icon: the dedicated one, else the skull bone, else the treasure
        /// icon, else nothing (a plain white square — still visible, still tappable).</summary>
        private Sprite ButtonIcon()
        {
            if (_library == null)
            {
                return null;
            }

            if (_library.BoneButtonIcon != null)
            {
                return _library.BoneButtonIcon;
            }

            Sprite skull = _library.Bone((int)BoneType.Skull);
            return skull != null ? skull : _library.TreasureIcon;
        }

        private void BuildPanel(RectTransform parent)
        {
            var panelGo = new GameObject("BoardPanel", typeof(RectTransform));
            panelGo.transform.SetParent(parent, false);
            _panel = (RectTransform)panelGo.transform;
            Stretch(_panel);

            // Backdrop: dims the island and, tapped, closes the board. Tapping "anywhere else"
            // is the toddler's real close gesture; the X below is for the grown-ups.
            var backdropGo = new GameObject("Backdrop", typeof(RectTransform));
            backdropGo.transform.SetParent(_panel, false);
            Stretch((RectTransform)backdropGo.transform);
            var backdrop = backdropGo.AddComponent<Image>();
            backdrop.color = BackdropTint;
            backdropGo.AddComponent<SkeletonBoardTap>().Bind(Close);

            // The card tray. Its own opaque graphic ABSORBS taps, so a tap that lands on the
            // board itself never closes it — only the backdrop and the X do.
            var trayGo = new GameObject("Tray", typeof(RectTransform));
            trayGo.transform.SetParent(_panel, false);
            var tray = (RectTransform)trayGo.transform;
            tray.anchorMin = tray.anchorMax = new Vector2(0.5f, 0.5f);
            tray.pivot = new Vector2(0.5f, 0.5f);
            tray.anchoredPosition = new Vector2(0f, 20f);
            tray.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            var trayImg = trayGo.AddComponent<Image>();
            trayImg.color = PanelTint;

            for (int i = 0; i < SkeletonPlan.Species.Length; i++)
            {
                _cards.Add(BuildCard(tray, SkeletonPlan.Species[i], i));
            }

            BuildCloseButton(tray);
        }

        private Card BuildCard(RectTransform tray, DinoType species, int index)
        {
            var card = new Card { Species = species };

            var go = new GameObject($"Card_{species}", typeof(RectTransform));
            go.transform.SetParent(tray, false);
            card.Root = (RectTransform)go.transform;
            card.Root.anchorMin = card.Root.anchorMax = new Vector2(0.5f, 0.5f);
            card.Root.pivot = new Vector2(0.5f, 0.5f);
            card.Root.anchoredPosition = new Vector2((index - 2) * CardPitch, 0f);
            card.Root.sizeDelta = new Vector2(CardWidth, CardHeight);
            var back = go.AddComponent<Image>();
            back.color = CardTint;

            var silGo = new GameObject("Silhouette", typeof(RectTransform));
            silGo.transform.SetParent(card.Root, false);
            var sil = (RectTransform)silGo.transform;
            sil.anchorMin = sil.anchorMax = new Vector2(0.5f, 0.5f);
            sil.pivot = new Vector2(0.5f, 0.5f);
            sil.anchoredPosition = Vector2.zero;
            sil.sizeDelta = new Vector2(SilhouetteWidth, SilhouetteHeight);
            card.Silhouette = silGo.AddComponent<Image>();
            card.Silhouette.sprite = _library != null
                ? _library.SkeletonBoard(SkeletonPlan.BoardIndex(species))
                : null;
            card.Silhouette.preserveAspect = true;
            card.Silhouette.raycastTarget = false; // taps belong to the slots, not the picture
            card.Silhouette.color = SilhouetteDark;

            int slots = SkeletonPlan.SlotCount(species);
            for (int s = 0; s < slots; s++)
            {
                card.Slots.Add(BuildSlot(sil, species, s));
            }

            return card;
        }

        private SkeletonBoardSlot BuildSlot(RectTransform silhouette, DinoType species, int slot)
        {
            var go = new GameObject($"Slot_{slot}", typeof(RectTransform));
            go.transform.SetParent(silhouette, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            // SkeletonPlan hands back a NORMALISED spot on the silhouette, so the layout is
            // the same picture at any card size.
            Vector2 a = SkeletonPlan.SlotAnchor(species, slot);
            rt.anchoredPosition = new Vector2(
                (a.x - 0.5f) * SilhouetteWidth,
                (a.y - 0.5f) * SilhouetteHeight);
            rt.sizeDelta = new Vector2(SlotSize, SlotSize);

            var img = go.AddComponent<Image>();
            img.preserveAspect = true;
            int bone = SkeletonPlan.SlotBone(species, slot);
            img.sprite = _library != null ? _library.Bone(bone) : null;

            var comp = go.AddComponent<SkeletonBoardSlot>();
            comp.Bind(img, species, slot);
            comp.SetFilled(false, img.sprite);
            return comp;
        }

        /// <summary>The big X: a warm disc with two crossed bars. Drawn from plain graphics
        /// rather than a glyph so it needs no font and no imported art — it is the same shape
        /// at every resolution and on a placeholder-only run.</summary>
        private void BuildCloseButton(RectTransform tray)
        {
            var go = new GameObject("Close", typeof(RectTransform));
            go.transform.SetParent(tray, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-20f, 60f);
            rt.sizeDelta = new Vector2(130f, 130f);

            var back = go.AddComponent<Image>();
            back.color = CloseBackTint;
            go.AddComponent<SkeletonBoardTap>().Bind(Close);

            MakeCloseBar(rt, 45f);
            MakeCloseBar(rt, -45f);
        }

        private void MakeCloseBar(RectTransform parent, float degrees)
        {
            var go = new GameObject("Bar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(84f, 18f);
            rt.localRotation = Quaternion.Euler(0f, 0f, degrees);

            var img = go.AddComponent<Image>();
            img.color = CloseBarTint;
            img.raycastTarget = false; // the disc behind owns the tap
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        // ----------------------------------------------------------- open / close

        private void OnEnable()
        {
            GameEvents.BoneBanked += OnBoneBanked;
        }

        private void OnDisable()
        {
            GameEvents.BoneBanked -= OnBoneBanked;
        }

        private void OnBoneBanked(DinoType species, int boneIndex)
        {
            // The board re-derives itself whether it is showing or not: the completion
            // celebration must fire the moment a skeleton finishes, not the next time somebody
            // happens to open the panel.
            Refresh();
        }

        /// <summary>Show the collection. Changes NO game state — see the class doc.</summary>
        public void Open()
        {
            if (_panel == null)
            {
                return;
            }

            _open = true;
            _opens++;
            _panel.gameObject.SetActive(true);
            Refresh();
            GameManager.Instance?.Audio?.Chime();
            Tween.PunchScale(_panel, 0.06f, 0.25f);
        }

        /// <summary>Hide the collection. Idempotent — the X and the backdrop both land here.</summary>
        public void Close()
        {
            _open = false;
            if (_panel != null)
            {
                _panel.gameObject.SetActive(false);
            }
        }

        // -------------------------------------------------------------- refresh

        /// <summary>Every frame: the HUD button's very EXISTENCE is state-derived (no bones, no
        /// button), which is the one thing that must stay true even when nothing raised an
        /// event — a save restored mid-collection has bones but fires no bank.</summary>
        private void Update()
        {
            if (_button != null)
            {
                bool show = GameManager.Instance != null && GameManager.Instance.AnyBoneBanked;
                if (_button.gameObject.activeSelf != show)
                {
                    _button.gameObject.SetActive(show);
                    if (!show)
                    {
                        Close(); // the button went away: the panel behind it must not linger
                    }
                }
            }
        }

        /// <summary>Re-derive every card from the bone bank. Pure function of game state, so it
        /// is safe to call as often as anything likes.</summary>
        public void Refresh()
        {
            GameManager gm = GameManager.Instance;
            for (int i = 0; i < _cards.Count; i++)
            {
                RefreshCard(_cards[i], gm);
            }
        }

        private void RefreshCard(Card card, GameManager gm)
        {
            if (card == null)
            {
                return;
            }

            // A slot of bone type T at rank R is filled once the bank holds more than R of that
            // bone — so "how many ribs have I dug" drives "which rib slots are drawn" with no
            // extra bookkeeping and no way for the two to disagree.
            for (int s = 0; s < card.Slots.Count; s++)
            {
                SkeletonBoardSlot slot = card.Slots[s];
                if (slot == null)
                {
                    continue;
                }

                int bone = SkeletonPlan.SlotBone(card.Species, s);
                int have = gm != null ? gm.BoneCount(card.Species, bone) : 0;
                bool filled = have > SkeletonPlan.SlotRankWithinBone(card.Species, s);
                slot.SetFilled(filled, _library != null ? _library.Bone(bone) : null);
            }

            bool complete = gm != null && gm.SkeletonComplete(card.Species);
            bool revived = gm != null && gm.IsSpeciesRevived(card.Species);

            if (card.Silhouette != null)
            {
                // A complete skeleton brightens to the species' REAL sprite in full colour —
                // the wordless "this one is a dinosaur now". Revived or merely complete look
                // the same on purpose: the board's job is to say the collection is done, and
                // the machine's glow is what says "come and get it".
                bool bright = complete || revived;
                Sprite live = bright ? LiveSprite(card.Species) : null;
                Sprite dark = _library != null
                    ? _library.SkeletonBoard(SkeletonPlan.BoardIndex(card.Species))
                    : null;

                card.Silhouette.sprite = bright && live != null ? live : dark;
                card.Silhouette.color = bright ? SilhouetteLive : SilhouetteDark;
            }

            card.Complete = complete || revived;
            if (card.Complete && !card.Celebrated)
            {
                card.Celebrated = true;
                _completionCelebrations++;
                Celebrate(card);
            }
            else if (!card.Complete)
            {
                card.Celebrated = false; // a reset/teardown may empty a board; let it re-celebrate
            }
        }

        /// <summary>The species' real, full-colour sprite (its front-facing idle).</summary>
        private Sprite LiveSprite(DinoType species)
        {
            DinoDefinition def = _config != null ? _config.GetDino(species) : null;
            return def != null ? def.GetIdle() : null;
        }

        /// <summary>SKELETON COMPLETE: the card punches and throws a ring of sparkles. Built
        /// from throwaway uGUI images rather than a particle system, because a ScreenSpaceOverlay
        /// canvas draws over every particle in the scene — the sparkle has to BE a UI element or
        /// it is not visible at all.</summary>
        private void Celebrate(Card card)
        {
            if (card.Root == null)
            {
                return;
            }

            Tween.CancelPunch(card.Root);
            Tween.PunchScale(card.Root, 0.18f, 0.5f);
            GameManager.Instance?.Audio?.Grow();

            Sprite star = _library != null ? _library.StarParticle : null;
            const int Stars = 8;
            for (int i = 0; i < Stars; i++)
            {
                float ang = i * (Mathf.PI * 2f / Stars);
                Vector2 to = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 190f;
                SpawnSparkle(card.Root, star, to);
            }
        }

        private void SpawnSparkle(RectTransform parent, Sprite art, Vector2 to)
        {
            var go = new GameObject("Sparkle", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(46f, 46f);

            var img = go.AddComponent<Image>();
            img.sprite = art;
            img.color = SparkleTint;
            img.raycastTarget = false;
            img.preserveAspect = true;

            Tween.Run(0.65f, t =>
            {
                if (rt == null || img == null)
                {
                    return;
                }

                rt.anchoredPosition = Vector2.LerpUnclamped(Vector2.zero, to, t);
                rt.localScale = Vector3.one * (1f - 0.6f * t);
                Color c = SparkleTint;
                c.a = 1f - t;
                img.color = c;
            }, () =>
            {
                if (go != null)
                {
                    Destroy(go);
                }
            });
        }

        private Card FindCard(DinoType species)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] != null && _cards[i].Species == species)
                {
                    return _cards[i];
                }
            }

            return null;
        }
    }
}
