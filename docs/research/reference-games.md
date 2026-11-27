# 参考ゲーム調査（西部劇 × 能力つき騎馬レース）

調査日: 2026-11-27 ／ 対象: 本ワールド（西部の町ロビー → 荒野 canyon / 砂漠 dunes / 境目 mesa の 3 ステージ騎馬レース、最大 8 人、PC VR+デスクトップ）

> **使い方の約束**: ここに挙げる作品は「何がなぜ効いているか」を学ぶための参考であり、名前・キャラクター・ロゴ・固有の形・固有の UI を**真似しない**。本ワールドは独自の世界観のまま作る。
> また「モデルは作るな」のルールにより、下の「取り入れること」は**既存アセット（Assets/HorseRace/Models）の配置・組み合わせ・色替え、ライト・パーティクル・TMP・シェーダー・地形**だけで実現できるものに絞る。

> 調査環境の制約: store.steampowered.com と多くのレビューサイト本文（pcgamer / neowin / gamedeveloper / nintendosoup など）はこの環境から取得できなかったため、検索結果の要約と公開 wiki・レビューの抜粋をもとにした。数値に食い違いがある項目はそう書いた。

凡例: **(a) 見た目・レベルアート ／ (b) UI ／ (c) ゲームシステム ／ (d) コース設計 ／ → 取り入れること**

---

## 1. Far Far West（Evil Raptor, 2026 EA）― ユーザー指定のメイン参考

- 1〜4 人協力の一人称シューター。ロボットのガンマン、魔法、骸骨やアンデッド、呪われた鉱山、幽霊列車といった「ちょっと不思議な西部」。
- **(a)** 明るく彩度高めのスタイライズ表現で、敵とプレイヤーのシルエットが遠目でもはっきり分かる。マップはバイオームごとに色と地形がはっきり違う（wiki によって数え方が違い、Canyon / Far West / Far Far North / Woodlands の 4 つとするものや、Desert / Jungle / Hellfire なども含めて 8〜9 マップとするものがある）。「峡谷＝赤茶」「北＝雪の白青」のように、**1 バイオーム 1 主色**で一目で分かる。
- **(b)** ハブは酒場（Saloon）。保安官から依頼（コントラクト）を受け、装備を整え、出撃する。メニューを開く代わりに、**町の中の場所と物がそのまま UI になっている**。
- **(c)** 6 系統の魔法スクール（呪文数は 25 前後）を使うほど系統が育つ。95 枚以上の「ジョーカー」カードは条件つきの小さな強化（+5% など）を重ねるもの。賞金首の依頼は難しいほど報酬が増え、レビューでは Deep Rock Galactic 型の協力ループと比べられている。
- **(d)** ミッションマップは目的地が順につながっていて、鉱山の坑道や線路が**誘導の線**になっている。幽霊列車のような動くセットピースが見せ場になる。
- **→ 取り入れること**: 3 ステージの主色を固定する（canyon＝赤錆、dunes＝砂金、mesa＝紫がかった夕景）。ロビーの酒場や掲示板を「コース選び・能力選び」の場として町の中に置く。能力は小さく重ねられる効果（例: スタート直後の加速 +x%）を**カードの見た目**で見せる（TMP とマテリアルだけで作る）。各ステージに線路・柵・電柱の列のような既存アセットの連続で**誘導の線**を引く。
- 出典: https://www.neowin.net/amp/far-far-west-early-access-review-cowboys-and-magic-combine-for-a-superb-co-op-game/ ・ https://wikily.gg/far-far-west ・ https://new.wikily.gg/farfarwest ・ https://www.notebookcheck.net/The-next-co-op-hit-Wild-West-shooter-launches-with-96-positive-reviews.1290551.0.html ・ https://www.gamesradar.com/games/co-op/far-far-west-developer-q-and-a-like-with-pretty-much-everything-we-do-we-just-went-with-a-vibe/ ・ https://www.keengamer.com/games/far-far-west/features/

## 2. Weird West（WolfEye Studios, 2022）― 不思議な西部のアートと読みやすさ

- **(a)** 見下ろし視点で、控えめなセルシェーディングと漫画風の絵作り。開発者は「技術の流行に縛られない、時代を超えた見た目」を狙ったと話している。一方で「雰囲気のために読みやすさを犠牲にしている」という批評もある。
- **(b)** 世界観に溶けこむ、紙や木の質感のメニュー。
- **(c)** ゾンビ、狼男、魔女、呪いといった超常要素を西部の日常に混ぜている。
- **(d)** 小さな町や拠点を道でつなぐノード型の地図。
- **→ 取り入れること**: トゥーンシェーダーの影は 2 段階程度に抑え、**遊びに関わる物（ゲート、アイテム、柵の切れ目）だけ明るさと彩度を上げて**、雰囲気と読みやすさを両立させる。
- 出典: https://store.epicgames.com/news/how-the-west-was-weird ・ https://digitaltrends.com/gaming/weird-west-preview ・ https://gameinformer.com/review/weird-west/a-fantastical-frontier

## 3. Red Dead Redemption 2（Rockstar, 2018）― 馬の手触りと控えめな HUD

- **(a)** 地平線、遠くの山、夕方の逆光、土ぼこりのような**空気遠近**で広さを出している。
- **(b)** 初期状態の HUD は最小限で、左下に小さなミニマップがあるだけ。馬の体力と持久力のコアは「危ないとき」だけ出す設定にでき、HUD を完全に消すこともできる。長い移動では映画風カメラに切り替わる。
- **(c)** 馬の動きは小さなアニメの断片を数千つないで作り、歩調の変化が途切れない。地面の種類、崖、障害物に反応する。乗り続けたりなでたりすると絆が深まる。蹄の音のリズムが「乗っている感じ」の要だと開発チームは語っている。
- **(d)** 道や川筋に沿って自然に走れる地形。
- **→ 取り入れること**: 持久力ゲージは**減ったときだけはっきり見せる**（満タンなら薄く）。速さの段階（並足、速歩、駆け足、全力）ごとに蹄の音の間隔と土ぼこりのパーティクル量を変え、速さを耳と目で伝える。VR では映画風カメラは使わない。
- 出典: https://gamedeveloper.com/design/rockstar-s-quest-for-the-ultimate-video-game-horse ・ https://www.pcgamer.com/the-mind-boggling-work-that-went-into-red-dead-2s-incredibly-lifelike-horses ・ https://www.gamespot.com/articles/here-are-red-dead-redemption-2s-hud-options/1100-6461911/ ・ https://mp1st.com/news/red-dead-redemption-2-hud-customizable-interesting-factoids

## 4. ゼルダの伝説 ブレス オブ ザ ワイルド（Nintendo, 2017）― 拍車の回数と、目印で誘う地形

- **(b)(c)** 馬は「拍車＝加速」を最大 6 回まで使え、残り回数が**ニンジンのアイコン**で画面に出る。時間がたつと回復する。数えられる少ない資源にしているので、ゲージより判断がしやすい。
- **(d)** 「三角形の設計」（CEDEC 2017 で紹介）: 丘や岩山の三角形のシルエットで先の景色を隠し、越えるたびに新しい目印が見えるようにして、自然に前へ進ませる。目立つ塔や煙が遠くからの誘導になる。
- **→ 取り入れること**: 全力ダッシュの残りを**3〜5 個の蹄鉄アイコン**で見せる（TMP やスプライトを点灯させるだけ）。コースの曲がり角の外側に、メサや岩山の既存アセットで**三角のシルエット**を置いて先を隠し、その先の目印（風車、給水塔、煙のパーティクル）をゴールの方向に置く。
- 出典: https://zelda.fandom.com/wiki/Horse ・ https://gamedeveloper.com/design/breath-of-the-wild-open-world-analysis-gravity-to-go-forward ・ https://gamedeveloper.com/art/a-ui-ux-analysis-of-zelda-breath-of-the-wilds

## 5. Sable（Shedworks, 2021）― 砂漠を「色と線」で見せる

- **(a)** Moebius 風の平らな色面と線画。色は塗り分けだけでなく**時間帯の空気**も表す。砂漠のどこにでも「遠くの何か」が見えている。影と光で、プレイヤーが地形のどこにいるかを分かりやすくしている（GDC 2022 の講演より）。
- **(c)** ホバーバイクで砂丘を走ること自体が気持ちいい。
- **→ 取り入れること**: dunes ステージは空と地面の色の差（補色）を強めにして、砂丘の稜線にリムライトが出る向きに太陽を置く。遠景に巨大な残骸や岩の**シルエットを 1〜2 個だけ**置き、方向の目印にする。
- 出典: https://www.gamedeveloper.com/marketing/how-shedworks-refined-the-art-of-sable-in-pursuit-of-readability ・ https://gdcvault.com/play/1027721/The-Art-of-Sable-Imperfection ・ https://www.popmatters.com/moebius-art-influence-video-games

## 6. Sea of Thieves（Rare, 2018）― 少ないルールで通すスタイライズと、世界の中の UI

- **(a)** GDC 2018「Visual Adventures on Sea of Thieves」: いくつかの単純なルール（形を少し誇張する、手描き風のテクスチャ、空と水を主役にする）を**全アセットに一貫して**当てて統一感を出している。
- **(b)** 地図、羅針盤、ランタンといった道具を手に持って見る。数字の HUD をほとんど使わない。VR ワールドでは**世界の中の UI** がとても相性がいい。
- **(c)** 複数人で同じ船を動かすことで、自然に協力と会話が生まれる。
- **→ 取り入れること**: 雑多なモデルパックを混ぜても統一して見えるよう、**共通パレットとトゥーンのランプを全マテリアルに当てる**（色替えの方針を 1 つの表にまとめる）。ロビーの順位表やコース地図は宙に浮く板ではなく、町の看板や掲示板の既存アセットに TMP で書く。
- 出典: https://80.lv/articles/gdc18-visual-adventures-on-sea-of-thieves ・ https://gamedeveloper.com/art/video-how-rare-crafted-the-look-and-feel-of-i-sea-of-thieves-i-

## 7. マリオカート ワールド（Nintendo, 2025）― アイテムの配り方とノックアウト形式

- **(c)** 熱心なプレイヤーの映像分析では、アイテムは**順位で配られている**（MK8DX の距離ベースではない）。5 位でも、Wii の 10 位と同じくらい強いアイテムが出るという。1.7.0 の更新以降は、スタートから 20 秒間は全員に中位向けのアイテム（攻撃系はほぼ除く）が出る、とも報告されている。わざと後ろに下がって強いアイテムを貯める「サンドバッギング」が批判されている。
- **(d)** 全コースが 1 つのつながった世界になっている。ノックアウトツアーでは 24 人が長い一本道を走り、**チェックポイントのゲートごとに下位が脱落する**。レビューの評価は非常に高い。自由走行モードは「レースの待合室」程度だという声が多い。
- **(b)** 順位は大きな数字、アイテム枠は左上、周回やゲートの残り数を表示（一般的な観察）。
- **→ 取り入れること**: 逆転の仕組みは**順位ベース**にし、出す効果の強さに上限を設ける（強すぎる妨害は先頭だけを狙わない）。スタートから最初の数秒は全員同じ能力が出るようにして、開幕の混乱を防ぐ。3 ステージを**1 本の旅としてつなぐ**見せ方（ステージの境目に門や看板）は、境目 mesa の構成と相性がいい。脱落形式は 8 人では向かないので、各区間の順位表示だけ取り入れる。
- 出典: https://nintendosoup.com/in-depth-analysis-reveals-how-mario-kart-worlds-item-system-works/amp/ ・ https://kotaku.com/mario-kart-world-items-tweak-power-ups-new-routes-knockout-2000711989 ・ https://www.gamepro.de/artikel/mario-kart-world-sandbagging,3435134.html ・ https://www.gamesradar.com/games/racing/mario-kart-world-review ・ https://www.gameinformer.com/review/mario-kart-world/roam-if-you-want-to

## 8. Crash Team Racing Nitro-Fueled（Beenox, 2019）― 近道とブーストの技術

- **(c)** ドリフトをうまく決めるとブーストの「リザーブ」が貯まり、着地時の加速が延びる。操作がうまい人ほど速くなる。
- **(d)** **すべてのコースに近道がある**。多くの近道は、ブーストを持っていないと届かない、または得をしない（加速パッドからのジャンプでカーブを飛ばす、砂地を突っ切る、など）。近道にはトロフィーという別のごほうびもある。
- **→ 取り入れること**: 各ステージに 1〜2 本、**ダッシュを残していないと得をしない近道**を作る（崖の切れ目、川の浅瀬、廃線の上など。既存の岩、橋、線路で作り、見えない当たり判定で区切る）。入口には読みやすい目印（旗、ランタン、色付きライト）を置く。近道を通ったら TMP で一言出す。
- 出典: https://www.gamesradar.com/crash-team-racing-shortcuts ・ https://www.pushsquare.com/news/2019/10/guide_crash_team_racing_nitro-fueled_-_where_to_find_all_shortcuts ・ https://www.gamesradar.com/our-best-tips-and-tricks-for-crash-team-racing-nitro-fueled

## 9. Sonic Racing CrossWorlds（SEGA, 2025）― 先頭が選ぶ行き先とロードアウト

- **(d)** 2 周目は、**先頭のプレイヤーが 2 つのトラベルリングから行き先を選び**、全員がそこへ飛ばされる。同じコースでも毎回違う。乗り物は場所によって陸、水、空の形に変わる。コースが狭いという批判もある。
- **(c)** 最大 6 枠のガジェット盤にパーツを組み込み、プリセットとして保存できる。小さな効果を組み合わせて自分の型を作る。
- **→ 取り入れること**: 2 つ目の区間の**分岐を先頭がその場で選ぶ**（左右の門の TMP 看板が光る）と、先頭に役割を与えつつ毎回展開が変わる。能力はロビーで 2〜3 枠の小さな組み合わせにする（Far Far West のジョーカーと同じ考え方）。コースの幅は 8 頭が並んでも詰まらない広さを保つ。
- 出典: https://www.techradar.com/gaming/sonic-racing-crossworlds-review ・ https://www.gameinformer.com/review/sonic-racing-crossworlds/a-new-dimension-of-kart-racing ・ https://wccftech.com/review/sonic-racing-crossworlds/ ・ https://press-start.com.au/reviews/ps5-reviews/2025/09/22/sonic-racing-crossworlds-review-out-of-this-world/

## 10. Fall Guys（Mediatonic, 2020）― 色の決まりで「触れる物」を伝える

- **(a)** 色と模様のルールを決めて、**触れる物と動く物がひと目で分かる**ようにしている。ステージに飾りの小物がほとんどなく、目的がはっきりしている。パステルの色調全体に合わない要素は差し替えた。
- **(b)** 残り人数と通過人数を大きく出し、通過したら派手な演出。UI とメニューもゲームと同じ色調。
- **(c)** 短いラウンドの連続と段階的な脱落。色覚サポートがないこと（チームの色が見分けにくい）は批判された。
- **→ 取り入れること**: 「ぶつかると止まる物」「通れる物」「加速する物」の 3 種類に**色と形の決まり**を作り、全ステージで同じにする（加速＝青緑の発光、危険＝赤、ゴール方向＝金色）。色だけに頼らずアイコンや模様も併用する。
- 出典: https://gamespot.com/articles/fall-guys-heres-how-the-developers-make-its-levels/1100-6481433/ ・ https://gamespot.com/articles/fall-guys-is-accessible-but-it-has-a-long-way-to-g/1100-6481059/ ・ https://www.pressreader.com/usa/los-angeles-times/20201006/282153588739560

## 11. Riders Republic（Ubisoft, 2021）― ハブに集まって一斉スタート

- **(a)(d)** ハブの Riders Ridge に集まり、そこから大人数のマスレースへ。区間ごとに乗り物（自転車、スキー、ウィングスーツ）が変わる。
- **(b)(c)** マスレースは 30 分ごとに始まり、数分以内にハブの開始地点まで移動する。**開始時刻と集合場所が決まっている**ので人が集まる。ぶつかり合いの混乱も楽しさの一部だが、毎回同じで飽きるという声もある。
- **→ 取り入れること**: ロビーの町の大通りに「次のレースまで残り○秒」の大きな時計（既存の時計や看板に TMP）を置き、**スタート台まで馬で歩いて集まる**流れにする。ステージごとに地形の性格（崖の下り、砂丘の起伏、メサの台地）を変え、区間ごとに違う乗り心地にする。
- 出典: https://www.stuff.tv/review/riders-republic-review ・ https://www.digitaltrends.com/?p=2718575 ・ https://www.tomsguide.com/reviews/riders-republic ・ https://attackofthefanboy.com/reviews/riders-republic-review/

## 12. Rival Stars Horse Racing（PikPok）― 少ない操作で駆け引きのある競馬

- **(c)** 操作はほぼ「左右」と「スプリント」だけ。駆け引きは、**持久力をどこで使うか**とポジション取りに集まる。馬ごとに好む位置（先行、差し）があり、短距離と長距離で作戦が変わる。
- **(d)** 内ラチ、コーナー、直線の長さが仕掛けどころを決める。
- **→ 取り入れること**: 操作は増やさず、**どこでダッシュを使うか**を判断のポイントにする。最後の直線を長めにし、手前に下り坂を置いて、追い込みの見せ場を作る。
- 出典: https://www.pocketgamer.com/rival-stars-horse-racing/rival-stars-horse-racing-tips-to-help-you-flying-out-of-the-gate/ ・ https://news.xbox.com/en-us/2026/05/07/five-tips-for-glory-in-rival-stars-horse-racing/ ・ https://finalboss.io/rival-stars-horse-racing-how-to-win-races

## 13. VRChat のレースワールド（例: VR Jetski Rush / VR C1 Runner）と VR の酔い対策

- 研究（arXiv 2508.01070）では、VRChat のレースワールドで「強い視界の絞り（周辺を大きく暗くする）」と「弱い絞り」を比べている。人工的な移動の速いレースは酔いやすい。
- Meta の指針: 移動中は視界の周辺を暗くする。速さと加速を急に変えない。「推奨」と「快適重視」の 2 段階の設定を用意する。Unity の XR Toolkit には視界を絞る仕組み（Tunneling Vignette）がある。
- **→ 取り入れること**: VR プレイヤーにだけ、速度と旋回に応じて**視界の周辺を暗くする**（設定でオン/オフと強さを選べる）。加速は急に跳ね上げず、なめらかに変える。地平線と馬の首（乗り物の枠）を常に見える位置に置く。HUD は顔に貼りつけず、**馬の首元や手元**（世界の中）に置く。
- 出典: https://arxiv.org/html/2508.01070v3 ・ https://developers.meta.com/horizon/resources/locomotion-design-techniques-best-practices/ ・ https://developers.meta.com/horizon/resources/locomotion-comfort-usability/ ・ https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.5/manual/tunneling-vignette-controller.html

## 14. Walkabout Mini Golf VR（Mighty Coconut）― ローポリでも VR で映える

- **(a)** 色の多いローポリ表現で、VR でも読みやすく、性能も安定する。コースごとに世界観とライティングを大きく変え、**夕景や夜の発光**で雰囲気を出す。テクスチャの解像度が低いという批評もある。
- **(c)** 最大 6 人で同じコースを回り、待ち時間も会話になる。
- **→ 取り入れること**: 解像度ではなく**色面、ライティング、発光（ランタン、窓明かり、焚き火のパーティクル）**で質感を出す。ドローコールを抑えるため、マテリアルはパレット単位でまとめる。
- 出典: https://www.6dofreviews.com/reviews/walkabout-mini-golf-review ・ https://pushsquare.com/reviews/psvr2/walkabout-mini-golf ・ https://www.meta.com/blog/walkabout-meow-wolf-mini-golf/

---

## まとめ: 本ワールドに入れる設計の決まり（モデルは作らず実現できるもの）

| 分野 | 決まり | 主な参考 |
|---|---|---|
| アート | ステージごとに主色を 1 つ決める（canyon 赤錆 / dunes 砂金 / mesa 紫の夕景）。全マテリアルを共通パレットとトゥーンのランプで統一する | 1, 5, 6 |
| アート | 遊びに関わる物だけ彩度を上げて発光させる（加速＝青緑、危険＝赤、ゴール方向＝金）。色に加えて形やアイコンでも伝える | 2, 10 |
| レベル | 曲がり角の外側に岩山やメサで三角のシルエットを置いて先を隠し、遠くの目印（風車、給水塔、煙）で誘う | 4, 5 |
| レベル | 線路、柵、電柱の列で「誘導の線」を引く。動くセットピース（列車、風車）を区間に 1 つずつ置く | 1, 3 |
| コース | ステージごとに、ダッシュを残していると得をする近道を 1〜2 本作る。最後の直線は長くし、手前に下り坂を置く | 8, 12 |
| コース | 2 つ目の区間の分岐を先頭が選ぶ。コースの幅は 8 頭が並べる広さにする | 9 |
| システム | 逆転は順位ベース。効果の強さに上限を設け、開幕数秒は全員同じ能力にする | 7 |
| システム | 能力は小さな効果の組み合わせ（ロビーで 2〜3 枠）にし、カード風に見せる | 1, 9 |
| UI | 持久力とダッシュは残り回数のアイコン（蹄鉄 3〜5 個）で見せ、減ったときだけ強調する | 3, 4 |
| UI | 順位は大きな数字にする。VR では HUD を馬の首元や手元に置き、ロビーの情報は町の看板に TMP で出す | 6, 7, 13 |
| VR | 速度と旋回に応じて視界の周辺を暗くする（設定で変えられる）。加速をなめらかにし、地平線を常に見せる | 13 |
| ロビー | 町の大通りに「次のレースまで」の時計を置き、スタート台まで歩いて集まる流れにする。酒場と掲示板が選択の場になる | 1, 11 |

## オーナーからの指摘（見た目の4回目以降で必ず直す）

art round 3 のあとの下見画像を見て、オーナーが「直してくれ」と言ったもの。見た目の批評役は、この4つを must の項目に必ず入れる。
1. 地面がのっぺり：荒野のカーブの手前（7_rider_1_b_corner）や境目の切り通し（7_rider_3_c_setpiece）の地面が一色の面で、轍・草・小石がほとんど無い。既存の素材の小物と、マテリアル／シェーダーの模様で地面に変化をつける。
2. 境目の崖が暗くて単調：切り通しの両側の崖が大きな茶色の面で、岩の素材が点々と貼り付いているだけ。既存の岩の素材を崖に沿って並べ、色と光で層や陰影を出す。
3. 切り通しの右下の白い箱（7_rider_3_c_setpiece）：何の部品か突き止める。ゲームに出るものなら素材に置き換えるか消す。下見専用なら下見で描かない。
4. ロビーの手前が空っぽ（9_lobby_eye_N）：手前の地面が画面のほぼ半分を占めていて何も無い。既存の素材（板の歩道・樽・馬つなぎ・荷車など）で、手前を町の通りらしく埋める。
5. 砂漠の道が舗装道路に見える（2_stage_2）：黒くて太い帯が一本通っていて、車の道に見える。砂を踏み固めた明るい道と轍にする。
6. 境目の地面の色がまだら（3_stage_3）：濃い紫茶と黄色の大きなシミが混ざっていて、汚れに見える。地層や高さに沿った、筋の通った色分けにする。
7. ステージが板の上に乗っているように見える（00_overview・各全景）：ステージの四角い縁と、外の白っぽい高い土手がはっきり見えて、模型の台のように見える。縁を地形や岩・遠景でなじませる。
新しいモデルは作らない。今ある素材と、マテリアル・シェーダー・光・粒だけで直す。
