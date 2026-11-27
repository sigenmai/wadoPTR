using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// 乗り物 1 頭（PLAYTEST Task 6/9 の構成をそのまま自動化）:
    ///   Mount_N [MountController, AbilityCaster, VRCStation(Immobilize, 入る位置 = SeatAnchor), Rigidbody(kinematic), 体の BoxCollider（Mount）]
    ///     SeatAnchor / ExitPoint
    ///     Visual [MountVisual] → BodyPivot → Horse / Camel / SkeletonHorse（頭が +Z を向くよう回す）＋代用カプセル
    ///     ReinsVR [MountInputVR]
    ///     RiderHitbox（騎手の上半身のカプセル、Is Trigger、Rider）
    ///     CharmPouch [CharmPouch]（袋・光・刻印 3 個）
    ///     Revolver [VRCPickup(Auto Hold = Yes, Exact Gun), Rigidbody(kinematic), RevolverHolder] → Revolver.fbx（部品そのまま）
    ///     DustCloud / Lasso / RockWall（壁 3 枚、Block）/ IronBall ×3 / Haze（AbilityCaster の見た目。起動時に親から外れる）
    /// MountPicker（柵の馬）用の見た目だけの馬も BuildVisual で作る。
    /// </summary>
    public static class MountBuilder
    {
        public const string MOUNT_CONTROLLER = "Assets/HorseRace/Animations/Mount.controller";
        public const string CAMEL_CONTROLLER = "Assets/HorseRace/Animations/Mount_Camel.overrideController";
        public const string SKELETON_CONTROLLER = "Assets/HorseRace/Animations/Mount_SkeletonHorse.overrideController";
        public static readonly string[] CoatPaths =
        {
            "Assets/HorseRace/Materials/Horse/Horse_Bay.mat",
            "Assets/HorseRace/Materials/Horse/Horse_Chestnut.mat",
            "Assets/HorseRace/Materials/Horse/Horse_Black.mat",
            "Assets/HorseRace/Materials/Horse/Horse_Gray.mat",
            "Assets/HorseRace/Materials/Horse/Horse_Palomino.mat",
            "Assets/HorseRace/Materials/Horse/Horse_Dun.mat",
        };

        static readonly Color Leather = new Color(0.40f, 0.25f, 0.15f);
        static readonly Color Gold = new Color(1.00f, 0.82f, 0.35f);
        static readonly Color Iron = new Color(0.18f, 0.18f, 0.20f);
        static readonly Color Rope = new Color(0.62f, 0.48f, 0.30f);
        static readonly Color Sand = new Color(0.85f, 0.72f, 0.50f, 0.8f);

        public static MountController Build(WorldContext ctx, Transform parent, string name, Vector3 pos, float yaw,
            int slot, int casterId, bool practice)
        {
            var root = GenUtil.World(parent, name, pos, yaw);
            var t = root.transform;

            // 座席：腰が鞍に来る位置（馬の既定値。着席後に SeatFit がアバターの腰を測って動かす）
            float seatY = SeatMath.AnchorY(SeatMath.SADDLE_HORSE_Y, SeatMath.FallbackHipsY(1.6f), 0f);
            var seat = GenUtil.Child(t, "SeatAnchor", new Vector3(0f, seatY, SeatMath.SADDLE_HORSE_Z)).transform;
            var exit = GenUtil.Child(t, "ExitPoint", new Vector3(-1.6f, 0f, 0f)).transform;

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var body = root.AddComponent<BoxCollider>();
            body.size = new Vector3(1.0f, 1.7f, 2.6f);
            body.center = new Vector3(0f, 1.05f, 0f);

            var station = root.AddComponent<VRCStation>();
            station.PlayerMobility = VRC.SDKBase.VRCStation.Mobility.Immobilize;
            station.stationEnterPlayerLocation = seat;
            station.stationExitPlayerLocation = exit;
            station.disableStationExit = false;   // レース中だけ MountController が true にする
            station.canUseStationFromStation = false;
            station.seated = true;
            // 騎乗ポーズ（RidePoseBuilder：Sitting レイヤーをまたがるポーズに置き換える）。無ければ VRChat の既定の座りポーズ
            var ridePose = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(RidePoseBuilder.STATION_CONTROLLER);
            if (ridePose == null) GenUtil.Warn("騎乗ポーズのコントローラーが見つかりません: " + RidePoseBuilder.STATION_CONTROLLER);
            station.animatorController = ridePose;

            var mc = GenUtil.AddU<MountController>(root);
            mc.slotIndex = slot;
            mc.station = station;
            mc.seatAnchor = seat;
            mc.seatHeight = seatY;
            mc.seatForward = SeatMath.SADDLE_HORSE_Z;
            mc.seatFit = BuildSeatFit(t, mc, seat);
            mc.groundMask = WorldLayers.GroundMask;
            mc.obstacleMask = WorldLayers.ObstacleMask;
            mc.mountMask = WorldLayers.MountMask;   // 押し合い・ドラフト（system-r1-13）

            var visualGo = GenUtil.Child(t, "Visual", Vector3.zero);
            // 練習用は slot = -1 なので、練習用の番号（casterId - 8 = 0, 1）で毛色を変える
            mc.visual = BuildVisual(visualGo, practice ? GenUtil.Mod(casterId - RaceManager.MAX_SLOTS, CoatPaths.Length) : 0);
            BuildStaminaCues(mc.visual, visualGo.transform);

            var reins = GenUtil.Child(t, "ReinsVR", Vector3.zero);
            var vr = GenUtil.AddU<MountInputVR>(reins);
            vr.mount = mc;
            vr.seatAnchor = seat;
            mc.vrInput = vr;

            var hit = GenUtil.Child(t, "RiderHitbox", new Vector3(0f, 2.3f, -0.1f));
            var cap = hit.AddComponent<CapsuleCollider>();
            cap.isTrigger = true;
            cap.radius = 0.35f;
            cap.height = 1.1f;
            hit.layer = WorldLayers.Rider;

            // ---- 能力 ----
            var caster = GenUtil.AddU<AbilityCaster>(root);
            caster.mount = mc;
            caster.casterId = casterId;
            caster.practiceMount = practice;
            caster.targetMask = WorldLayers.TargetMask;
            caster.blockMask = WorldLayers.BlockMask;
            caster.groundMask = WorldLayers.GroundMask;
            var sounds = GenUtil.Child(t, "Sounds", new Vector3(0f, 1.5f, 0f));
            caster.castSound = GenUtil.Audio(sounds, false, false, SfxBuilder.Clip(SfxMath.WHOOSH));
            caster.relicSound = GenUtil.Audio(sounds, false, false, SfxBuilder.Clip(SfxMath.SPARKLE));
            caster.pouch = BuildPouch(t);
            caster.revolver = BuildRevolver(t, caster);
            caster.dustCloud = BuildDust(t);
            caster.lasso = BuildLasso(t);
            caster.rockWall = BuildRockWall(t);
            var balls = new IronBall[3];
            for (int i = 0; i < 3; i++) balls[i] = BuildBall(t, i);
            caster.balls = balls;
            caster.haze = BuildHaze(t);

            root.layer = WorldLayers.Mount;
            ctx.casters.Add(caster);
            ctx.revolvers.Add(caster.revolver);
            return mc;
        }

        // ------------------------------------------------------------------
        // 鞍の位置合わせ（SeatFit）と調整札（文字＋見えない当たり判定。モデルは作らない）
        // ------------------------------------------------------------------

        static readonly string[] ButtonLabels = { "▲", "▼", "▲", "▼", "▲", "▼", "戻す" };
        static readonly string[] ButtonHints = { "鞍：上へ 1cm", "鞍：下へ 1cm", "鞍：前へ 1cm", "鞍：後ろへ 1cm", "鞍：前に 1° 傾ける", "鞍：後ろに 1° 傾ける", "鞍：元に戻す" };

        static SeatFit BuildSeatFit(Transform t, MountController mc, Transform seat)
        {
            var go = GenUtil.Child(t, "SeatFit", Vector3.zero);
            var sf = GenUtil.AddU<SeatFit>(go);
            sf.mount = mc;
            sf.seatAnchor = seat;
            // 鞍の点は FBX を Blender で測った値（SeatMath の SADDLE_*）
            var sy = new float[SeatMath.SEAT_TYPES];
            var sz = new float[SeatMath.SEAT_TYPES];
            for (int i = 0; i < SeatMath.SEAT_TYPES; i++) { sy[i] = SeatMath.DefaultSaddleY(i); sz[i] = SeatMath.DefaultSaddleZ(i); }
            sf.saddleY = sy;
            sf.saddleZ = sz;

            // 札：鞍の右前（体の箱の外）。乗り手から見て読める向き（右前の下を見る）。位置は SeatFit が毎フレーム鞍に合わせる
            sf.panelOffset = new Vector3(0.56f, 0.12f, 0.25f);
            var panel = GenUtil.Child(go.transform, "SeatPanel", new Vector3(0.56f, SeatMath.SADDLE_HORSE_Y + 0.12f, SeatMath.SADDLE_HORSE_Z + 0.25f));
            panel.transform.localRotation = Quaternion.Euler(55f, 20f, 0f);
            sf.panelRoot = panel;
            var pt = panel.transform;
            // 札の裏板（ui-r1-16 → ui-r2-10）：手配書の紙（WantedPosters_05、0.40 × 0.30m）。文字は紙の上にインク（×1.4）、ボタンだけ真鍮。
            // 縮んでも 0.6m 先で 1.3° を下回らない（UiStyle.MinPtFor）。キーの説明はデスクトップだけ（SeatFit.keysHint）
            const float SW = UiStyle.SEAT_CARD_W, SH = UiStyle.SEAT_CARD_H, K = UiStyle.SEAT_FONT_SCALE;
            const float GX = SW / 0.32f, GY = SH / 0.24f;   // 旧 0.32 × 0.24m からの配置の倍率
            float eye = UiStyle.SEAT_CARD_EYE_M;
            var plate = GenUtil.PaperSheet(pt, "Backplate", "WantedPosters_05", new Vector3(0.01f * GX, -SH * 0.5f, 0.006f), null, SW, SH);
            foreach (var c in plate.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var title = GenUtil.Text(pt, "Title", new Vector3(0f, 0.098f * GY, 0f), Quaternion.identity, "鞍の位置", 0.22f * K, UiStyle.InkFace,
                new Vector2(0.24f * GX, 0.03f * GY), TextStyle.Ink, true);
            title.fontStyle = TMPro.FontStyles.Bold;
            title.fontSizeMin = UiStyle.MinPtFor(0.22f * K, eye);
            string[] heads = { "高さ", "前後", "傾き" };
            for (int c = 0; c < 3; c++)
            {
                var hd = GenUtil.Text(pt, "Head_" + c, new Vector3((-0.075f + 0.075f * c) * GX, 0.072f * GY, 0f), Quaternion.identity, heads[c], 0.16f * K,
                    UiStyle.InkFace, new Vector2(0.07f * GX, 0.025f * GY), TextStyle.Ink, true);
                hd.fontSizeMin = UiStyle.MinPtFor(0.16f * K, eye);
            }
            var buttons = new Transform[SeatFit.BUTTONS];
            var labels = new Transform[SeatFit.BUTTONS];
            for (int b = 0; b < SeatFit.BUTTONS; b++)
            {
                Vector3 p = b == SeatFit.BTN_RESET ? new Vector3(0.115f * GX, -0.03f * GY, 0f)
                    : new Vector3((-0.075f + 0.075f * (b / 2)) * GX, ((b % 2 == 0) ? 0.04f : -0.005f) * GY, 0f);
                var btn = GenUtil.Child(pt, "Button_" + b, p);
                var col = btn.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3((b == SeatFit.BTN_RESET ? 0.07f : 0.055f) * GX, 0.04f * GY, 0.03f);
                var sab = GenUtil.AddU<SeatAdjustButton>(btn);
                sab.seatFit = sf;
                sab.button = b;
                GenUtil.SetInteractText(sab, ButtonHints[b]);
                float bpt = (b == SeatFit.BTN_RESET ? 0.16f : 0.22f) * K;
                var lab = GenUtil.Text(btn.transform, "Label", Vector3.zero, Quaternion.identity, ButtonLabels[b], bpt,
                    UiStyle.BrassFace, new Vector2(0.06f * GX, 0.035f * GY), TextStyle.Brass, true);
                lab.fontSizeMin = UiStyle.MinPtFor(bpt, eye);
                labels[b] = lab.transform;
                buttons[b] = btn.transform;
            }
            sf.buttons = buttons;
            sf.buttonLabels = labels;
            sf.clickAudio = GenUtil.Audio(go, false, false, SfxBuilder.Clip(SfxMath.CLICK));
            sf.readout = GenUtil.Text(pt, "Readout", new Vector3(-0.02f * GX, -0.05f * GY, 0f), Quaternion.identity, "", 0.13f * K, UiStyle.InkFace,
                new Vector2(0.2f * GX, 0.05f * GY), TextStyle.Ink, true);
            sf.readout.fontSizeMin = UiStyle.MinPtFor(0.13f * K, eye);
            var keys = GenUtil.Text(pt, "KeysHint", new Vector3(0f, -0.093f * GY, 0f), Quaternion.identity, HudMath.SeatKeysHint(), UiStyle.SEAT_KEYS_PT,
                UiStyle.InkFace, new Vector2(SW - 0.02f, 0.03f * GY), TextStyle.Ink, true);
            keys.fontSizeMin = UiStyle.MinPtFor(UiStyle.SEAT_KEYS_PT, eye);
            sf.keysHint = keys.gameObject;
            // 当たり判定は Rider（物理の衝突なし。CP・砂地のトリガーを踏まない）
            GenUtil.SetLayerRecursive(panel, WorldLayers.Rider);
            panel.SetActive(false);
            return sf;
        }

        // ------------------------------------------------------------------
        // 見た目（MountVisual）
        // ------------------------------------------------------------------

        public static MountVisual BuildVisual(GameObject visualGo, int initialCoat)
        {
            var pivot = GenUtil.Child(visualGo.transform, "BodyPivot", Vector3.zero).transform;
            var mv = GenUtil.AddU<MountVisual>(visualGo);
            var horseCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MOUNT_CONTROLLER);
            var camelCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CAMEL_CONTROLLER);
            var skelCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(SKELETON_CONTROLLER);
            if (horseCtrl == null) GenUtil.Warn("アニメーターが見つかりません: " + MOUNT_CONTROLLER);
            if (camelCtrl == null) GenUtil.Warn("アニメーターが見つかりません: " + CAMEL_CONTROLLER);
            if (skelCtrl == null) GenUtil.Warn("アニメーターが見つかりません: " + SKELETON_CONTROLLER);
            mv.horseController = horseCtrl;
            mv.camelController = camelCtrl != null ? camelCtrl : horseCtrl;
            mv.skeletonController = skelCtrl != null ? skelCtrl : horseCtrl;

            mv.horseModel = Model(GenUtil.HORSE_FBX, pivot, "Horse", horseCtrl, out mv.horseAnimator);
            mv.camelModel = Model(GenUtil.CAMEL_FBX, pivot, "Camel", mv.camelController, out mv.camelAnimator);
            mv.skeletonModel = Model(GenUtil.SKELETON_FBX, pivot, "SkeletonHorse", mv.skeletonController, out mv.skeletonAnimator);

            var coats = new Material[CoatPaths.Length];
            for (int i = 0; i < coats.Length; i++)
            {
                coats[i] = AssetDatabase.LoadAssetAtPath<Material>(CoatPaths[i]);
                if (coats[i] == null) GenUtil.Warn("毛色マテリアルが見つかりません: " + CoatPaths[i]);
            }
            mv.coatMaterials = coats;
            if (mv.horseModel != null)
            {
                mv.horseCoatRenderers = mv.horseModel.GetComponentsInChildren<Renderer>(true);
                if (coats[0] != null) foreach (var r in mv.horseCoatRenderers) r.sharedMaterial = coats[GenUtil.Mod(initialCoat, coats.Length)] ?? coats[0];
                mv.earLeft = GenUtil.FindDeep(mv.horseModel.transform, "EarL");
                mv.earRight = GenUtil.FindDeep(mv.horseModel.transform, "EarR");
            }
            mv.bodyPivot = pivot;
            var fb = GenUtil.Prim(PrimitiveType.Capsule, "FallbackPrimitive", pivot, new Vector3(0f, 1.1f, 0f), new Vector3(0.9f, 1.2f, 0.9f), Leather);
            fb.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            fb.SetActive(mv.horseModel == null);
            mv.fallbackPrimitive = fb;
            var breath = GenUtil.Child(visualGo.transform, "Breath", new Vector3(0f, 1.6f, 1.0f));
            mv.breathAudio = GenUtil.Audio(breath, false, true, SfxBuilder.Clip(SfxMath.BREATH));
            if (mv.camelModel != null) mv.camelModel.SetActive(false);
            if (mv.skeletonModel != null) mv.skeletonModel.SetActive(false);
            return mv;
        }

        /// <summary>
        /// 相手から見える持久力（system-r2-08）と加速の合図（r2-14）：汗（持久力 40 未満で出る白い粒）、ばてた瞬間の砂ぼこり、
        /// 金の火花（きれいな着地・カーブの立ち上がり）、頭の上の札「ばてている」（後ろから読める向き、ほかの人の乗り物が 25m 以内でばてている間）。
        /// パーティクルと文字だけ（モデルは作らない）。
        /// </summary>
        static void BuildStaminaCues(MountVisual mv, Transform visual)
        {
            var sweat = GenUtil.Particles(visual, "Sweat", new Vector3(0f, 1.35f, 0.4f), new Color(0.95f, 0.97f, 1f, 0.75f), 0.06f, 0.5f, 0.7f, true, 16f, 0.5f);
            var sm = sweat.main;
            sm.playOnAwake = false;
            sweat.Stop();
            mv.sweat = sweat;
            mv.exhaustPuff = GenUtil.Particles(visual, "ExhaustPuff", new Vector3(0f, 0.4f, 0f), Sand, 1.3f, 2.2f, 1.4f, false, 0f, 0.9f);
            mv.boostSparkle = GenUtil.Particles(visual, "BoostSparkle", new Vector3(0f, 1.2f, 0.2f), Gold, 0.14f, 3.5f, 0.6f, false, 0f, 0.7f);
            var tag = GenUtil.Text(visual, "SpentTag", new Vector3(0f, 3.2f, -0.4f), Quaternion.identity, "<b>ばてている</b>", 2.2f, UiStyle.Gold,
                new Vector2(2.6f, 0.6f), TextStyle.Sign, true);
            tag.gameObject.SetActive(false);
            mv.spentTag = tag.gameObject;
        }

        static GameObject Model(string path, Transform parent, string name, RuntimeAnimatorController ctrl, out Animator anim)
        {
            anim = null;
            var m = GenUtil.InstantiateModel(path, parent, name);
            if (m == null) return null;
            // 頭（Head ボーン）が +Z を向くように Y 回転
            var head = GenUtil.FindDeep(m.transform, "Head");
            if (head != null)
            {
                Vector3 local = parent.InverseTransformPoint(head.position);
                local.y = 0f;
                if (local.sqrMagnitude > 0.01f) GenUtil.YawAlign(m.transform, parent.TransformDirection(local), parent.forward);
            }
            anim = m.GetComponentInChildren<Animator>(true);
            if (anim == null) anim = m.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return m;
        }

        // ------------------------------------------------------------------
        // お守り袋・リボルバー・能力の見た目
        // ------------------------------------------------------------------

        static readonly string[] AbilityCards = { "Wanted_Dust", "Wanted_Lasso", "Wanted_Wall", "Wanted_Ball" };

        /// <summary>
        /// 能力の札（ui-r1-04）：首の札の右隣（乗り物の首の上、乗っている本人だけに見える）に、その能力の手配書（WantedPosters_02 に Wanted_&lt;能力&gt;.png）。
        /// 使える = 明るい手配書と金の火花、クールダウン中 = 35% の灰色と「あと 7」、Lv = 宝石（Gems_01_02〜04）3 つ。位置は CharmPouch が鞍の高さに合わせる。
        /// </summary>
        static CharmPouch BuildPouch(Transform t)
        {
            var go = GenUtil.Child(t, "CharmPouch", new Vector3(0.15f, SeatMath.SADDLE_HORSE_Y + 0.25f, SeatMath.SADDLE_HORSE_Z + 0.55f));
            go.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            var cardRoot = GenUtil.Child(go.transform, "Card", Vector3.zero);
            const float CW = 0.07f, CH = 0.10f;
            var dustTex = AssetDatabase.LoadAssetAtPath<Texture2D>(GenUtil.WANTED_CARDS + "/" + AbilityCards[0] + ".png");
            var sheet = GenUtil.PaperSheet(cardRoot.transform, "Poster", "WantedPosters_02", new Vector3(0f, -CH * 0.5f, 0.002f), dustTex, CW, CH);
            foreach (var c in sheet.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var ready = new Material[AbilityCards.Length];
            var idle = new Material[AbilityCards.Length];
            for (int i = 0; i < AbilityCards.Length; i++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GenUtil.WANTED_CARDS + "/" + AbilityCards[i] + ".png");
                if (tex == null) GenUtil.Warn("能力の札の手配書が見つかりません: " + AbilityCards[i]);
                ready[i] = GenUtil.ToonTextureTint(tex, "Pouch_" + AbilityCards[i], Color.white);
                idle[i] = GenUtil.ToonTextureTint(tex, "Pouch_" + AbilityCards[i], new Color(0.35f, 0.35f, 0.35f));
            }
            var cooldown = GenUtil.Text(cardRoot.transform, "Cooldown", new Vector3(0f, -0.015f, -0.004f), Quaternion.identity, "", 0.3f,
                UiStyle.BrassFace, new Vector2(CW, 0.035f), TextStyle.Brass, true);
            cooldown.fontStyle = TMPro.FontStyles.Bold;
            var sparks = GenUtil.Particles(cardRoot.transform, "ReadySparks", new Vector3(0f, 0f, -0.01f), new Color(1f, 0.82f, 0.35f), 0.012f, 0.4f, 0.5f, false, 0f, 0.04f);
            // 被弾の赤い火花（ui-r2-06）：同じ形の粒を赤で
            var hitSparks = GenUtil.Particles(cardRoot.transform, "HitSparks", new Vector3(0f, 0f, -0.01f), new Color(0.9f, 0.2f, 0.1f), 0.014f, 0.5f, 0.45f, false, 0f, 0.04f);
            var marks = new GameObject[3];
            var unlit = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = new Vector3(-0.022f + i * 0.022f, -CH * 0.5f - 0.016f, -0.004f);
                marks[i] = Gem(cardRoot.transform, "LevelMark_" + (i + 1), "Gems_01_0" + (i + 2), p, GenUtil.ToonColor(Gold));
                marks[i].SetActive(false);
                unlit[i] = Gem(cardRoot.transform, "LevelMarkUnlit_" + (i + 1), "Gems_01_0" + (i + 2), p, GenUtil.ToonColor(new Color(0.25f, 0.18f, 0.1f)));
            }
            var cp = GenUtil.AddU<CharmPouch>(go);
            cp.cardRoot = cardRoot;
            cp.cardRenderers = sheet.GetComponentsInChildren<Renderer>(true);
            cp.readyMaterials = ready;
            cp.idleMaterials = idle;
            cp.sparks = sparks;
            cp.hitSparks = hitSparks;
            cp.cooldownText = cooldown;
            cp.levelMarks = marks;
            cp.levelMarksUnlit = unlit;
            cp.readySound = GenUtil.Audio(go, false, false, SfxBuilder.Clip(SfxMath.CHIME));
            cp.neckOffset = new Vector3(UiStyle.NECK_CARD_W * 0.5f + UiStyle.POUCH_OFFSET_X * 0.55f, 0.25f, 0.55f);
            GenUtil.SetLayerRecursive(go, WorldLayers.Rider);
            return cp;
        }

        /// <summary>小さな宝石 1 つ（Packs/Gems の部品、いちばん長い辺 0.018m、色だけのトゥーン）。</summary>
        static GameObject Gem(Transform parent, string name, string part, Vector3 local, Material mat)
        {
            var g = GenUtil.InstantiatePart(GenUtil.Pack("Gems"), part, parent, parent.position, 0f, 1f, new Vector3(0.015f, 0.018f, 0.015f), Gold);
            g.name = name;
            GenUtil.FitMax(g, 0.018f);
            g.transform.localPosition = local;
            g.transform.localRotation = Quaternion.identity;
            foreach (var r in g.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
            return g;
        }

        static RevolverHolder BuildRevolver(Transform t, AbilityCaster caster)
        {
            var go = GenUtil.Child(t, "Revolver", new Vector3(0.42f, 1.2f, 0.1f));
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var model = GenUtil.InstantiateModel(GenUtil.Pack("Revolver"), go.transform, "Model");
            var rh = GenUtil.AddU<RevolverHolder>(go);
            rh.caster = caster;

            Vector3 dir = go.transform.forward, up = go.transform.up, muzzlePos = go.transform.position + dir * 0.15f;
            Vector3 gripPos = go.transform.position;
            if (model != null)
            {
                Transform m = model.transform;
                rh.cylinder = GenUtil.FindDeep(m, "Revolving_Cylinder");
                rh.hammer = GenUtil.FindDeep(m, "Hammer");
                rh.triggerPart = GenUtil.FindDeep(m, "Trigger");
                var bullets = new GameObject[6];
                for (int i = 0; i < 6; i++)
                {
                    var b = GenUtil.FindDeep(m, "Bullet" + (i + 1));
                    bullets[i] = b != null ? b.gameObject : null;
                    if (b == null) GenUtil.Warn("Revolver.fbx に Bullet" + (i + 1) + " がありません");
                }
                rh.bullets = bullets;
                // 銃身の向き：Magnum_Barrel の外形の一番長い軸を、シリンダーから離れる向きに
                var barrel = GenUtil.FindDeep(m, "Magnum_Barrel");
                var grip = GenUtil.FindDeep(m, "Grip");
                if (barrel != null && rh.cylinder != null)
                {
                    Bounds bb = GenUtil.RendererBounds(barrel.gameObject);
                    Bounds cb = GenUtil.RendererBounds(rh.cylinder.gameObject);
                    Vector3 axis = bb.size.x >= bb.size.y && bb.size.x >= bb.size.z ? Vector3.right : (bb.size.y >= bb.size.z ? Vector3.up : Vector3.forward);
                    if (Vector3.Dot(bb.center - cb.center, axis) < 0f) axis = -axis;
                    dir = axis;
                    float ext = Mathf.Abs(Vector3.Dot(bb.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z))));
                    muzzlePos = bb.center + axis * ext;
                    if (grip != null)
                    {
                        Bounds gb = GenUtil.RendererBounds(grip.gameObject);
                        gripPos = gb.center;
                        Vector3 u = bb.center - gb.center;
                        u = u - dir * Vector3.Dot(u, dir);
                        if (u.sqrMagnitude > 1e-6f) up = u.normalized;
                    }
                    rh.cylinderAxis = rh.cylinder.InverseTransformDirection(dir).normalized;
                    Vector3 side = Vector3.Cross(up, dir).normalized;
                    if (rh.hammer != null) rh.hammerAxis = rh.hammer.InverseTransformDirection(side).normalized;
                    if (rh.triggerPart != null) rh.triggerAxis = rh.triggerPart.InverseTransformDirection(side).normalized;
                }
                else GenUtil.Warn("Revolver.fbx の Magnum_Barrel / Revolving_Cylinder が見つからないため銃口の向きは既定値です");
            }
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(go.transform, false);
            muzzle.transform.position = muzzlePos;
            muzzle.transform.rotation = Quaternion.LookRotation(dir, up);
            rh.muzzle = muzzle.transform;

            var exact = new GameObject("ExactGun");
            exact.transform.SetParent(go.transform, false);
            exact.transform.position = gripPos;
            exact.transform.rotation = Quaternion.LookRotation(dir, up);

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            Bounds all = model != null ? GenUtil.RendererBounds(model) : new Bounds(go.transform.position, new Vector3(0.05f, 0.2f, 0.3f));
            col.center = go.transform.InverseTransformPoint(all.center);
            col.size = go.transform.InverseTransformVector(all.size);
            col.size = new Vector3(Mathf.Abs(col.size.x) + 0.04f, Mathf.Abs(col.size.y) + 0.04f, Mathf.Abs(col.size.z) + 0.04f);

            var pickup = go.AddComponent<VRCPickup>();
            pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.Yes;
            pickup.orientation = VRC.SDKBase.VRC_Pickup.PickupOrientation.Gun;
            pickup.ExactGun = exact.transform;
            pickup.InteractionText = "リボルバー";
            pickup.UseText = "撃つ";
            rh.pickup = pickup;

            var fx = GenUtil.Child(go.transform, "Fx", Vector3.zero).transform;
            fx.position = muzzlePos;
            fx.rotation = muzzle.transform.rotation;
            rh.fxRoot = fx;
            rh.muzzleFlash = GenUtil.Particles(fx, "MuzzleFlash", Vector3.zero, new Color(1f, 0.8f, 0.3f), 0.12f, 2f, 0.08f, false, 0f, 0.02f);
            rh.smoke = GenUtil.Particles(fx, "Smoke", Vector3.zero, new Color(0.8f, 0.8f, 0.8f, 0.6f), 0.25f, 0.6f, 1.2f, false, 0f, 0.03f);
            rh.gunshot = GenUtil.Audio(fx.gameObject, false, false, SfxBuilder.Clip(SfxMath.SHOT));
            rh.dryFire = GenUtil.Audio(go, false, false, SfxBuilder.Clip(SfxMath.CLICK));
            var tracerGo = GenUtil.Child(fx, "Tracer", Vector3.zero);
            var lr = tracerGo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.startWidth = 0.02f;
            lr.endWidth = 0.01f;
            lr.sharedMaterial = GenUtil.ToonColor(new Color(1f, 0.9f, 0.6f));
            lr.enabled = false;
            rh.tracer = lr;

            // 掴む Collider は Rider（物理の衝突なし）：CP・砂地のトリガーを二重に踏まない
            GenUtil.SetLayerRecursive(go, WorldLayers.Rider);
            return rh;
        }

        static DustCloud BuildDust(Transform t)
        {
            var go = GenUtil.Child(t, "DustCloud", new Vector3(0f, 0.5f, -4f));
            var dc = GenUtil.AddU<DustCloud>(go);
            var visual = GenUtil.Child(go.transform, "Cloud", Vector3.zero);
            dc.particles = GenUtil.Particles(visual.transform, "Particles", Vector3.zero, Sand, 1.4f, 0.5f, 1.8f, true, 70f, 1f);
            dc.visual = visual;
            dc.baseRadius = 1f;
            dc.whoosh = GenUtil.Audio(go, false, false, SfxBuilder.Clip(SfxMath.WHOOSH));
            visual.SetActive(false);   // 使うまで隠す（起動時の一瞬のちらつき防止）
            return dc;
        }

        static LassoEffect BuildLasso(Transform t)
        {
            var go = GenUtil.Child(t, "Lasso", new Vector3(0f, 1.8f, 0f));
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 12;
            lr.useWorldSpace = true;
            lr.startWidth = 0.03f;
            lr.endWidth = 0.03f;
            lr.sharedMaterial = GenUtil.ToonColor(Rope);
            lr.enabled = false;
            var le = GenUtil.AddU<LassoEffect>(go);
            le.line = lr;
            le.mountMask = WorldLayers.MountMask;
            le.throwSound = GenUtil.Audio(go, false, false, SfxBuilder.Clip(SfxMath.WHOOSH));
            return le;
        }

        static RockWall BuildRockWall(Transform t)
        {
            var go = GenUtil.Child(t, "RockWall", new Vector3(0f, 0f, 8f));
            var rw = GenUtil.AddU<RockWall>(go);
            var segs = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                var seg = GenUtil.Child(go.transform, "Wall_" + (i + 1), new Vector3((i - 1) * AbilityMath.WALL_WIDTH, 0f, 0f));
                var box = seg.AddComponent<BoxCollider>();
                box.size = new Vector3(AbilityMath.WALL_WIDTH, 4f, 1.6f);
                box.center = new Vector3(0f, 2f, 0f);
                var rock = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), "DesertRocks_06", seg.transform, seg.transform.position, 0f, 1f,
                    new Vector3(AbilityMath.WALL_WIDTH, 4f, 1.6f), new Color(0.6f, 0.45f, 0.35f));
                rock.transform.localPosition = Vector3.zero;
                rock.transform.localRotation = Quaternion.identity;
                Bounds b = GenUtil.RendererBounds(rock);
                if (b.size.x > 0.01f && b.size.y > 0.01f && b.size.z > 0.01f)
                    rock.transform.localScale = new Vector3(AbilityMath.WALL_WIDTH / b.size.x, 4.3f / b.size.y, 1.8f / b.size.z);
                GenUtil.SetLayerRecursive(seg, WorldLayers.Block);
                seg.SetActive(false);   // 使うまで隠す（当たり判定も無効）
                segs[i] = seg;
            }
            rw.segments = segs;
            rw.height = 4f;
            rw.groundMask = WorldLayers.GroundMask;
            rw.obstacleMask = WorldLayers.ObstacleMask;   // 道の空いている幅を測る（見えない柵・岩、system-r2-10）
            rw.mountMask = WorldLayers.MountMask;         // 乗り物の足元に立てない
            rw.riseSound = GenUtil.Audio(go, false, false, SfxBuilder.Clip(SfxMath.THUD));
            return rw;
        }

        static IronBall BuildBall(Transform t, int i)
        {
            var go = GenUtil.Child(t, "IronBall_" + (i + 1), new Vector3(0.3f * (i - 1), 1.8f, 1f));
            var ib = GenUtil.AddU<IronBall>(go);
            ib.visual = GenUtil.Prim(PrimitiveType.Sphere, "Ball", go.transform, Vector3.zero, Vector3.one * 0.7f, Iron);
            ib.radius = 0.35f;
            ib.groundMask = WorldLayers.BlockMask;   // 地形＋岩壁ではねる
            ib.bounceSound = GenUtil.Audio(go, false, false, SfxBuilder.Clip(SfxMath.THUD));
            ib.hitSound = GenUtil.Audio(go, false, false, SfxBuilder.Clip(SfxMath.THUD));
            ib.visual.SetActive(false);
            return ib;
        }

        /// <summary>視界のかすみ：裏面だけ見える半透明の砂色の球（半径 0.3m）。効果用なのでトゥーン対象外。</summary>
        static GameObject BuildHaze(Transform t)
        {
            var go = GenUtil.Child(t, "Haze", new Vector3(0f, 2.2f, 0f));
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = HazeAssets.InsideSphere();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = HazeAssets.Material();
            go.transform.localScale = Vector3.one * 0.6f;
            go.SetActive(false);
            return go;
        }
    }

    /// <summary>かすみ用の内向き球メッシュと半透明マテリアル（Generated/ に上書き保存）。</summary>
    public static class HazeAssets
    {
        const string MeshPath = GenUtil.GENERATED + "/HazeSphere.asset";
        const string MatPath = GenUtil.GEN_MATERIALS + "/M_Haze.mat";

        public static Mesh InsideSphere()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing != null) return existing;
            GenUtil.EnsureFolder(GenUtil.GENERATED);
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh src = tmp.GetComponent<MeshFilter>().sharedMesh;
            var m = new Mesh();
            m.name = "HazeSphere";
            m.vertices = src.vertices;
            var n = src.normals;
            for (int i = 0; i < n.Length; i++) n[i] = -n[i];
            m.normals = n;
            m.uv = src.uv;
            var tri = src.triangles;
            for (int i = 0; i < tri.Length; i += 3) { int a = tri[i]; tri[i] = tri[i + 1]; tri[i + 1] = a; }
            m.triangles = tri;
            m.RecalculateBounds();
            Object.DestroyImmediate(tmp);
            AssetDatabase.CreateAsset(m, MeshPath);
            return m;
        }

        public static Material Material()
        {
            GenUtil.EnsureFolder(GenUtil.GEN_MATERIALS);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            var sh = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, MatPath);
            }
            if (sh != null) mat.shader = sh;
            mat.color = new Color(0.85f, 0.72f, 0.5f, 0.55f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
