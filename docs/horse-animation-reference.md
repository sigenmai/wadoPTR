# 馬のアニメーション：参考資料と反映内容

`Assets/HorseRace/Models/Horse.fbx` のアニメーションは `tools/blender/animate_horse.py` で作っている。
馬の歩様を研究した資料を集めて、その数値と特徴を手続き的な動きに反映した。

## 参考資料

- 歩様ごとの着地順と接地割合（duty factor）：常歩は4拍の側対歩順、駆歩・襲歩は空中期がある非対称歩様、接地割合は駆歩 約0.30 → 襲歩 約0.20
  - [Animals 2019, 9(10):763 (MDPI)](https://www.mdpi.com/2076-2615/9/10/763) / [同 PDF](https://clok.uclan.ac.uk/30496/1/30496%20animals-09-00763-v2.pdf)
  - [PMC6826507](https://pmc.ncbi.nlm.nih.gov/articles/PMC6826507)
  - [J Exp Biol 209(21):4389 — 競走馬の完歩パラメータ](https://cob.silverchair.com/jeb/article-pdf/209/21/4389/1541743/4389.pdf)
  - [Hobbs et al. 2016, PeerJ](https://knowledge.lancashire.ac.uk/id/eprint/15341/1/Hobbs%20et%20al%202016%20peerj-2190.pdf)
  - [PMC10451235](https://pmc.ncbi.nlm.nih.gov/articles/PMC10451235) / [PMC4493675](https://pmc.ncbi.nlm.nih.gov/articles/PMC4493675) / [PMC9197309](https://pmc.ncbi.nlm.nih.gov/articles/PMC9197309)
  - [IntechOpen: 馬の運動学](https://intechopen.com/chapters/83285)
- 遊脚中の関節の屈曲・伸展のタイミング、球節の沈み込み、腕節（前肢の膝）の最大屈曲
  - [PMC9502055](https://pmc.ncbi.nlm.nih.gov/articles/PMC9502055)
  - [PLOS ONE 0202584 図1（関節角度の測り方）](https://pmc.ncbi.nlm.nih.gov/articles/PMC6097835/figure/pone.0202584.g001)
  - [Animal Frontiers（襲歩の肢の傾き）](https://clok.uclan.ac.uk/42556/1/vfac025.pdf)
- 首と頭の動き（常歩はうなずく、速歩はほぼ固定、駆歩は背中と一緒に上下）
  - [PMC2768006](https://pmc.ncbi.nlm.nih.gov/articles/PMC2768006)
  - [J Exp Biol: 頭・首の安定化と可動性](https://cob.silverchair.com/jeb/article-split/211/24/3889/18018/Stabilization-and-mobility-of-the-head-neck-and)
  - [IFCE Equipedia: 馬の基本歩様](https://equipedia.ifce.fr/en/equipedia-the-universe-of-the-horse-ifce/riding/olympic-disciplines/dressage/the-horses-natural-gaits)
  - [Univ. of Minnesota: 歩様について](https://vanat.ahc.umn.edu/gaits/info.html)

## 反映した内容

| 歩様 | 1完歩 | 接地割合 | 着地順（右手前） | 胴・首 |
|---|---|---|---|---|
| Walk 常歩 | 1.10 s | 0.62 | 左後 → 左前 → 右後 → 右前（等間隔4拍） | 前肢の荷重に合わせて首がうなずく（1完歩に2回） |
| Trot 速歩 | 0.70 s | 0.42 | 左前+右後 → 右前+左後（2拍、空中期あり） | 首はほぼ固定、胴が1完歩に2回上下 |
| Canter 駆歩 | 0.62 s | 0.32 | 左後 → 右後+左前 → 右前 → 空中 | 胴が前後に揺れ、首は逆に振って頭を安定させる |
| Gallop 襲歩 | 0.45 s | 0.22 | 左後 → 右後 → 左前 → 右前 → 空中（脚を集める） | 背中が伸び縮み、首の振りが大きい |

- **接地中**：脚は前から後ろへ一定の速さで動く（蹄が地面に止まって見える）。荷重がかかると球節が沈む
- **遊脚中**：関節は遊脚の前半で曲がり、中盤で最大に屈曲、後半で伸びて着地に備える。前肢は腕節を後ろへ、後肢は飛節で管骨を前へ折る
- **胴の上下と傾き**：その瞬間にどの脚に体重がかかっているかから計算する。空中期に一番高くなり、後肢で蹴ると前が上がる

## Unity での使い方の注意

- 読み込み設定：Rig = **Generic**、各クリップ（Walk/Trot/Canter/Gallop/Idle/Stamp）は **Loop Time をオン**、Jump/Stagger はオフ
- 足が滑って見えないように、アニメーションの再生速度は馬の実際の速度に比例させる（HorseController 側で `Animator.speed` を調整）
- 歩様が切り替わるときは 0.2 秒程度のクロスフェードを入れる
- 足先の地面への吸着（IK）は入れていない。坂で蹄が浮いたり埋まったりするのは、Phase 2 で必要なら対応する
