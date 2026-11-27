# セットアップ手順（Unity が初めてでも進められるように）

このワールドは **「HorseRace/Build World」メニューを 1 回押すだけで、地形・コース・ロビー・乗り物・配線まで全部できあがる**ように作ってあります。あなたがやることは「Unity プロジェクトを用意して、このリポジトリの `Assets/HorseRace` を入れて、メニューを押す」だけです。

> **正直な注意**：このコードは**まだ一度も Unity でコンパイルしていません**（開発環境に Unity が無いため）。C# の型は、VRChat SDK / UdonSharp / Unity の API を手書きで真似た「スタブ」に対して検査しただけです。そのため、最初のコンパイルで API 名の違いによるエラーが出る可能性があります（下の「未検証のポイント」）。出たら Console のエラー文をそのまま Claude に送ってください。たいてい 1 行の修正で直ります。

## ★ ワンボタンで全部やる（おすすめ）

VRChat の Worlds プロジェクト（下の 1〜2）が用意できていれば、あとは **`HorseRace_Install.bat` をダブルクリックするだけ**です。

1. このリポジトリの [`HorseRace_Install.bat`](../HorseRace_Install.bat) をダウンロードする（GitHub のファイル画面の「Download raw file」ボタン）。置き場所はどこでもよい（Unity プロジェクトのフォルダに置くと、場所を聞かれません）。
2. ダブルクリック。初回だけ「Unity プロジェクトのフォルダを選んでください」と出るので、`Assets` が入っているフォルダを選ぶ（次からは覚えています）。
   - Windows の「PC を保護しました」が出たら「詳細情報 → 実行」。
3. bat が最新の `Assets/HorseRace` を GitHub から取ってきてプロジェクトに入れ、Unity を起動します（もう開いていれば Unity の画面に切り替えるだけ）。
4. Unity が自動で **TMP の素材の読み込み → シーン `Assets/Scenes/HorseRace.unity` の作成 → UdonSharp のコンパイル待ち → Build World → 保存** まで進めます。「ワールドができました」のダイアログが出たら完成。次は 9.（Build & Test）へ。

- 更新が来たときも同じ bat をダブルクリックするだけです（`Layout/LayoutOverrides.json` の手直しは上書きしません）。
- Unity の中だけでやり直したいときは、メニュー「**HorseRace/★ ワンボタンで全部やる**」。
- 止まったときは、Unity の Console の赤い行（または bat の黒い画面の文）を Claude に送ってください。
- 以下の 3〜8 は、ワンボタンが中でやっていることを手でやる場合の手順です。

## 0. 用意するもの
- Windows の PC（VRChat Creator Companion は Windows / Mac 対応。ここでは Windows 想定）
- VRChat のアカウント（アップロードできる権限が必要。新しいアカウントは「New User」制限でワールド公開が一定期間できないことがあります）
- VRChat クライアント（Steam 版またはスタンドアロン版）
- このリポジトリ一式（`git clone` するか ZIP でダウンロード）

## 1. VRChat Creator Companion（VCC）を入れる
1. ブラウザで「VRChat Creator Companion」を検索し、公式ページ（vcc.docs.vrchat.com / vrchat.com/home/download）から VCC をダウンロードしてインストール。
2. 起動して VRChat アカウントでログイン。
3. 最初の画面で Unity が無いと言われたら、画面の指示に従い **Unity Hub** と、VCC が指定する Unity（**2022.3 系**。VCC が「この Unity を入れてください」と出したバージョン）をインストール。自分で別のバージョンを選ばないこと。

## 2. ワールドのプロジェクトを作る
1. VCC の「Projects」→「**Create New Project**」。
2. テンプレートは **「Worlds」→「World (VRChat SDK - Worlds)」**（名前は版により「World Template」など）。
3. Project Name に `HorseRaceWorld` など、保存先を決めて「Create Project」。
4. 作られたら、プロジェクトの横の「**Open Project**」で Unity が開く（初回は数分かかります）。
5. VCC のプロジェクト画面の「Manage Project」で、**VRChat SDK - Worlds が 3.8 以降**になっていることを確認（古ければ Update）。このワールドは引数つきのネットワークイベント `[NetworkCallable]` を使うため **SDK 3.8 以降・UdonSharp 1.2 以降**が必要です。World テンプレートには UdonSharp が最初から入っています。

## 3. TextMeshPro の素材を入れる
1. Unity のメニュー「**Window → TextMeshPro → Import TMP Essential Resources**」。
2. 出てきた「TMP Importer」ウィンドウで「**Import TMP Essentials**」を押す（Examples & Extras は不要）。
3. 日本語フォントは同梱（`Assets/HorseRace/Fonts/ipag.ttf`、IPA ゴシック）から Build World が自動で作るので、手作業は要りません。

## 4. このリポジトリの `Assets/HorseRace` をコピーする
1. Unity を閉じなくてもよい。エクスプローラーで、このリポジトリの **`Assets/HorseRace` フォルダ**を、プロジェクトの **`Assets` フォルダの中**にフォルダごとコピー（結果：`<プロジェクト>/Assets/HorseRace/Editor`、`.../Scripts`、`.../Models`、`.../Fonts`、`.../Shaders`）。
2. Unity の画面に戻ると、右下で読み込みとコンパイルが始まる。**止まるまで待つ**。
3. 上のメニューバーに **「HorseRace」** が出れば成功。**Console**（下のタブ。無ければ「Window → General → Console」）に赤いエラーが出ていないか見る。出ていたら「トラブルシューティング」へ。
4. （`tests/`・`tools/`・`docs/` は Unity には入れません。入れなくて構いません）

## 5. （任意）毛色マテリアルを作る
メニュー「**HorseRace/Create Horse Materials**」。Build World が自動で同じことをするので、飛ばして構いません。
（同様に「HorseRace/Build Mount Animator」も Build World が自動で実行します。）

## 6. 空のシーンを作って保存する
1. メニュー「**File → New Scene**」→ **Basic (Built-in)** など空に近いもの →「Create」。
2. すぐ「**File → Save As…**」で `Assets/Scenes/HorseRace.unity` などの名前で保存。

## 7. ワールドを生成する：HorseRace/Build World
1. メニュー「**HorseRace/Build World**」。
2. **初回だけ**、UdonSharp の「プログラムアセット」（スクリプトごとの .asset）がまだ無いので、Build World はそれを作ってコンパイルを始め、次のようなダイアログを出して**いったん止まります**：

   > UdonSharp のプログラムアセットを ○○ 個作り、コンパイルを始めました。
   > 完了の確認：Unity 右下の読み込み表示が止まり、Console に UdonSharp のコンパイルエラー（赤）が無いこと …
   > 確認できたら、もう一度「HorseRace/Build World」を実行してください。

   これは失敗ではありません。「OK」を押し、**Unity 右下の回転する読み込み表示が止まるまで待つ**。Console に赤いエラーが無いことを確かめてから、**もう一度「HorseRace/Build World」**を押します。
3. 2 回目は進行バーが出て、数分かかります（地形 3 枚を作るため）。終わると Console に「Build World が完了しました」が出て、続けて Validate が自動で走ります。
4. 途中でエラーが起きると「ワールド生成中にエラーが起きました」というダイアログが出て、作りかけは自動で消されます。Console の文面を Claude に送ってください。
5. **Build World が自動で変更するもの**（知っておいてください）：
   - **レイヤー**：`Ground`・`Mount`・`Rider`・`Block` が無ければユーザーレイヤー 23〜26（埋まっていれば 22〜31 の空き）に追加。「Edit → Project Settings → Tags and Layers」で確認できます。
   - **衝突マトリクス**（Project Settings → Physics）：`Rider` は全レイヤーと衝突しない、`Mount` は Player / PlayerLocal と衝突しない。
   - シーン内の既存の Directional Light は無効にされ、生成した太陽が使われる。
   - シーン直下に `VRCWorld`（VRC Scene Descriptor）が 1 つ作られる（作り直しても消えず、ワールド ID は保たれる）。生成物は全部 `HorseRace_Generated` の下で、実行のたびに作り直し。
   - `Assets/HorseRace/Generated/` に TerrainData・マテリアル・RenderTexture・フォントなどが作られる。
   - 各ステージのコースの両側（中心から 28m）に**見えない柵**（`Stage*/CourseBarrier`：当たりの壁 約 1000 区間＋見た目の帯）が作られる。遠くからは見えず、16m 以内に近づくと砂金色の網目が浮かぶ（シェーダー `HorseRace/ProximityBarrier`、マテリアル `Generated/Materials/ProximityBarrier.mat`）。崖下りの近道・岩橋の溝・ゴールの手前 45m から先には張らない。柵は毎回コースから計算して作り直すもので、**配置の手直し（8b）の対象ではありません**（id なし。動かすには `CourseBarrier.cs` の値を変える）。
6. 完了したら **Ctrl + S でシーンを保存**。

## 8. 結果を確認する：HorseRace/Validate
メニュー「**HorseRace/Validate**」（Build World の最後にも自動で走ります）。Console に日本語のまとめが出ます。

- 「・」で始まる行は情報：ステージごとの全長・最大勾配・CP（チェックポイント）数・砂地の割合・遺物の数など。**全長が約 2.5km、勾配が 25° 以下**ならおおむね正常。
- 最後に「**問題は見つかりませんでした。**」なら OK。
- 「**警告 N 件:**」のあとに「  ! …」が並ぶ場合は黄色の警告です。素材の欠け（コピーし忘れ）、つなぎ忘れ、勾配超過などが書いてあります。内容を読んで、素材なら `Assets/HorseRace` を入れ直し、分からなければそのまま Claude に送ってください。
- 「HorseRace_Generated がありません」と出たら、先に Build World を実行する。

## 8b. 配置を手で直す（任意）
Build World は毎回 `HorseRace_Generated` を作り直しますが、**飾りの位置を手で直した結果は残せます**。

1. Scene ビューで飾り（岩・木・崖・サボテン・建物・柵・手配書・遠景の山・木の帯など）を選び、**移動・回転・拡大**、または **Delete で削除**する。
   - 手で直せる飾りは、Hierarchy の名前の末尾に ` {S1/CliffWalls/Cliffs|Cliffs_03#12}` のような **id** が付いているものです（名前の末尾は消さない・変えない）。
2. メニュー「**HorseRace/Save Layout**」。シーンと「Build World が計算した配置」を比べ、違いを `Assets/HorseRace/Layout/LayoutOverrides.json` に保存します（「移動 N 個・削除 M 個を保存しました」と出ます）。シーンは作り直しません。
3. 以後の「**HorseRace/Build World**」は自動で配置したあとにこのファイルを当てるので、直した配置のままになります（Console に「移動 N 個・削除 M 個」。ステージ定義や素材を変えて合わなくなった id は黄色の警告で一覧）。
4. 元に戻すときはメニュー「**HorseRace/Reset Layout**」（確認のあとファイルを消す）→ Build World。
5. **自分で物を足すとき**は、シーン直下の **`HorseRace_Custom`**（Build World が無ければ空で作る）の子に置いてください。Build World はこの中を一切触りません。Validate が個数を出し、道（コース中心から 12m 以内）に乗っている物や CP・遺物・スタート位置・ゴール後の囲い・スターターの立ち位置をふさぐ物を警告します。

- 保存していない手直しがあるまま Build World を押すと「保存していない配置の変更があります（移動 N・削除 M）」と聞かれます：「保存して続ける」「破棄して続ける」「キャンセル」。
- 名前の末尾の `{id}` を変えたり、Ctrl+D で複製したり（末尾に ` (1)` が付く）、別の親へ移したりすると、その飾りは手直しの対象から外れ、元の物は「削除」として保存されます。Save Layout は削除の一覧（最初の 10 個）を出し、削除が 20 個を超える・全体の 10% を超えるときは保存前に確認します。複製した物は `HorseRace_Custom` へ移してください。
- id は「計画の中の番号」なので、ステージ定義・素材・配置の計算（Claude に頼んだ変更を含む）が変わると番号がずれることがあります。特に**ロビーの飾り・木の帯・柵**は町の大きさや柵の長方形で並びが決まるため、町の向きや柵の大きさが変わるとずれます（ロビーを町くらいの大きさ＝柵 約 163m × 255m に縮めた変更でも、さらにロビーを町だけ＝柵 約 97m × 180m（町 + 8m）にした変更でも、木の帯・柵・ロビーの飾り・町の足元の岩の id は変わりました。町だけにした変更の前に保存したロビーの手直しは、ほぼすべて「今の配置に無い id」になります）。ずれた手直しは Build World の Console に「今の配置に無い id」として出るので、直し直して Save Layout してください。
- 遊びに関わる物（CP・遺物・スタートの並び・ゴール後の囲い・ゴールゲート・スターター・表彰台・岩橋とジャンプ台・ロビーの受付・手配書のカード・柵の馬・鐘・レバー・練習場の杭とカカシ・掲示板・乗り物）には id が付かないので、この方法では動かせません。動かすには `StageDefinitions.cs` / `LobbyBuilder.cs` を変えます（Claude に頼む）。
- 背景専用の物（メサ・厩舎）を動かすと、まわりの見えない壁も一緒に動きます（拡大の変化は壁には反映されません）。
- id は名前の末尾に付けるだけで、実行時のコンポーネントは足していません（VRChat のアップロードに影響なし）。

## 9. VRChat SDK で動かす（Build & Test）
1. メニュー「**VRChat SDK → Show Control Panel**」を開き、**Authentication** タブでログイン。
2. **Builder** タブ（Build タブ）を開く。上部の Validations（赤はビルドを止めるエラー、黄は警告）を確認。**赤が出たらその文面を Claude に送る**。
3. **クライアントの数を 2 に**：Builder タブの「**Local Testing**」にある「**Number of Clients**」を `2` にする（無ければ「Force Non-VR」も確認。2 つ目以降の窓はデスクトップモードで開く）。
4. 「**Build & Test**」を押す。初回のビルドは時間がかかります（10〜20 分かかることがある）。完了すると VRChat が 2 窓起動し、同じテストインスタンスに入ります。
5. **1 つ目の窓がマスター**。ロビーで台帳（受付カウンター）にサインして、2 つの窓でレースを試す。チェック項目は [PLAYTEST.md](PLAYTEST.md)。まず一番下の「最終チェック」から。
6. 「Build & Test」は VRChat を一度起動して閉じた経験が必要です（初回ログイン済みであること）。

## 10. 非公開でアップロードする
1. Build & Test でプレイテストが一通り終わってから。
2. VRChat SDK Control Panel の **Builder** タブで、ワールド名・説明・サムネイル・容量（Capacity は 8〜16 目安）を入力。
3. **Visibility は必ず「Private」**（または Friends+ など非公開）にして「**Build & Upload**」。
4. 完了後、VRChat 内の「Worlds → Mine（自分のワールド）」から非公開インスタンスを立てて、友だちと確認。公開（Community Labs 申請）は、プレイテストの結果を見てから別に考えます。

## 見た目だけ先に確認したいとき
`docs/preview/world/*.jpg`（`00_overview.jpg`、各ステージ、ロビーの町、騎乗視点など）は、Unity の**外**で別のツール（`tools/preview`、Blender）を使って作った**完成イメージの目安**です。実際の Unity での見た目とは細部が異なります。

---

## 未検証のポイント（最初のコンパイルで出うる問題）

コードは手書きスタブで型検査しただけで、実物の SDK では確かめていません。次のあたりは API 名・型の記憶に頼っています。**エラーが出たら、Console の赤い文面（ファイル名と行番号つき）を Claude に送ってください**。

| ポイント | 起きうること |
|---|---|
| `[NetworkCallable]` と引数つき `SendCustomNetworkEvent` | SDK が 3.8 未満だと「NetworkCallable が見つからない」エラー。SDK を更新（手順 2-5） |
| TextMeshPro の型（`TextMeshPro` / `TextMeshProUGUI`） | Udon に公開されていない、という警告・エラー。Udon で使える TMP の型は SDK の版による |
| Udon から `Animator.runtimeAnimatorController` を設定（乗り物のオーバーライドコントローラ） | Udon で公開されていないと、ラクダ・骨の馬が馬と同じ歩様になる／ビルドエラー |
| Udon から `VRCStation.disableStationExit` を設定（レース中の降車禁止） | 設定できないとレース中に Jump で降りてしまう（保険の再着席は入っている） |
| エディタ API：`UdonSharpProgramAsset.CompileAllCsPrograms`（初回のプログラムアセット作成後のコンパイル開始） | 名前が違うとエディタのコンパイルエラー。`Assets/HorseRace/Editor/SceneAssembler.cs` 内の `CompileAllCsPrograms` を呼ぶ 1 行を消せば、Refresh 後に U# が自動でコンパイルする |
| エディタ API：`UdonSharpUndo.AddComponent`（UdonSharpBehaviour の追加に使用） | 名前・引数が版で違う場合のコンパイルエラー |
| エディタ API：`TMP_FontAsset.CreateFontAsset`（日本語フォントの自動作成） | 失敗すると「フォントが作れない」警告。下の「日本語が□」を参照 |

そのほかの既知の細かい点（修正は後回し）：峡谷の溝の壁に盛り上がった斜面で隙間が出る場合がある／橋の手前が直線の道であることを前提にしている／命中の場面を拾う中継は砂塵・岩壁の命中を拾わない、など。

## トラブルシューティング

**Console に赤いエラーが出る（コピー直後）**
- 「Assembly … could not be found」「namespace VRC.* が無い」→ プロジェクトが World テンプレートではない、または SDK が入っていない。VCC で「Manage Project」から VRChat SDK - Worlds と UdonSharp を追加。
- 「NetworkCallable」関連 → SDK を 3.8 以降へ。
- 「TMPro が無い」→ 手順 3 の TMP Essentials を入れる。それでも出るなら Package Manager で TextMeshPro を入れ直す。
- 上の表のどれかに当てはまるなら、文面を Claude に送る。1 つ直すと次のエラーが出ることがあります（1 回の編集でコンパイルが止まるため）。数回のやり取りは想定内です。

**「HorseRace」メニューが出ない**
- Console のコンパイルエラーが先に直っていない。Editor フォルダのスクリプトはエラーが 1 つでもあると読み込まれません。

**Build World が「もう一度実行してください」と言ったまま進まない**
- 手順 7-2 のとおり。待ってから再実行。Console にコンパイルエラーがある間は何度押しても同じです。エラー文を送る。

**Build World が途中で失敗する**
- 「ワールド生成中にエラー」のダイアログ内容と Console の文面を送る。作りかけは自動で消えるので、そのまま再実行してよい。

**レイヤー・プロジェクト設定が変わった、元に戻したい**
- Tags and Layers の `Ground` `Mount` `Rider` `Block` と Physics の Layer Collision Matrix が変わります。ほかのワールドと同じプロジェクトで使うなら、別のプロジェクトを作ることをおすすめします。

**日本語が□や空白になる**
- `Assets/HorseRace/Fonts/ipag.ttf` が入っているか確認。Build World の警告（Validate の「Build World の警告」）にフォント作成失敗が出ていないか見る。TMP のフォントアセット（`Assets/HorseRace/Generated/Fonts/JPGothic SDF.asset`）が作れなかった場合は、手動で「Window → TextMeshPro → Font Asset Creator」で ipag.ttf から作り、`Assets/HorseRace/` 以下に置けばそちらが優先されます。

**素材が足りない警告（Validate）**
- `Assets/HorseRace/Models` や `Shaders` のコピー漏れ。手順 4 をやり直す。

**重い・カクつく**
- 8 人で接戦のときに VR でカクつくなら、Unity の Profiler（Window → Analysis → Profiler）で Udon の時間を確認（目安：1 フレーム 2ms 未満）。Quest（Android）向けには作っていません（PC 向けのみ）。遅いと感じたら、どの場面か・人数・VR かデスクトップかを添えて Claude に送る。
- ビルド時の容量が大きい場合は Build Report を見て、大きい素材を教えてください。

**Build & Test で VRChat が起動しない／2 窓にならない**
- VRChat SDK の Settings タブで VRChat クライアントの場所（Steam 版の VRChat.exe）を指定。一度 VRChat を手動で起動しログインしておく。

---

次に読むもの：[PLAYTEST.md](PLAYTEST.md)（プレイテストのチェックリスト）。
