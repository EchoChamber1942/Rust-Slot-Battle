# Rust Slot Battle 0.1.3 — スクラップ方式

Rustのスクラップを賭け、通常役・前兆・継続率バトルボーナスを楽しむ個人用CUIスロットプラグインです。

## 導入
1. RustSlots.cs をサーバーの oxide/plugins/RustSlots.cs に配置。
2. RCON：oxide.reload RustSlots
3. 管理者はチャットで /slot。一般プレイヤーには RCONで以下を設定。
   oxide.grant group default rustslots.use

## 実装範囲
- 個人用CUI、大型上部画面、3リール×21コマ（独自配列）、停止ボタン。
- BET1=中段1本、BET2=横3本、BET3=横3本＋斜め2本。
- 役はSTART時に確定。停止操作は表示タイミングを指定する方式。目押し／滑り制御は未実装。
- 通常配当はランタン11、アップル6、スクラップ2、REPLAYは次ゲーム無料。同時成立は黒RUST→赤7→スクラップ→斜めアップル→横アップル→ランタン→REPLAYの優先で1役だけ採用。通常払出最大11。
- LOW/NORMAL/HIGH/本前兆を非表示で管理。本前兆6～32ゲーム。
- アウトポスト→スーパー→エアフィールド→オイルリグの示唆ステージ。内部状態と完全一致しない。
- 赤・緑・黄・青の科学者示唆（画像なしでは色付き文字）。
- 押し順ナビを正しく停止しREPLAY成立でストック1個。押し順ミスはストック特典を付けない。初期版ではベル払出自体は押し順で変化しない。
- ブラッドリー66%、ヘリ79%、チヌーク84%。黒RUST当選の5%は89%枠。現段階では専用フリーズアニメーションなし。
- BBは8ゲーム/セット、セット完了報酬300スクラップ。ストック優先消費→残らなければ継続率抽選。
- 通常時のストックは次STARTでBB揃いを保証。BB中のストックは次セット継続を保証。最大10。
- 状態、未受取スクラップ、回転中の確定停止位置、押し順進捗を操作ごとに保存。閉じる／切断で抽選し直さず、/slotで再開。
- 実物モニュメント・NPCは出現させない。設置台への紐付けは未実装。どこでも/slotで個人画面を開く。

## キー設定
画面右上「キー設定」。Fキーまたはテンキープリセットを選択。
画面のbindをプレイヤー自身がF1で1行ずつ実行し、最後にwritecfg。
初期値：
```
bind f6 "slot.start"
bind f7 "slot.stop1"
bind f8 "slot.stop2"
bind f9 "slot.stop3"
bind f10 "slot.bet"
bind f11 "slot.close"
writecfg
```
個別変更はチャットで `/slotkey left f3`。
対象：start / left / center / right / bet / close。
キー：英数字1文字、f2～f12、numpad0～9。重複割当を拒否。
SteamIDごとに希望設定を保存するが、クライアントbindを自動変更しない。
既存の同キーbindは実行すると上書きされる。古いキーのbindは自動解除しない。
ESCはゲームのメニューと干渉するため割当なし。F11または閉じるボタンを使用。
画面表示中のみSTART/STOP/BETを受付。停止連打の二重精算を拒否。

## スクラップと設定
インベントリの実物スクラップを使用。1BET=10個、2BET=20個、3BET=30個。
START時に消費し、不足時は回転しない。REPLAYは追加消費なし。
通常役の配当単位は1枚=10個：ランタン110、アップル60、スクラップ役20。
BBセット報酬は既定30枚=300個。SetRewardは枚数指定（10倍して払出）。
払い出し先はメインインベントリ、次にベルト。既存のスクラップへスタックしてから空きスロットを使用する。
空きがなければ地面へ落とさず未受取として保存。「未受取」ボタン、/slot再表示、次STARTで受取を再試行。
銀行、バックパックプラグイン、ServerRewards、Economicsは使用しない。
旧版の仮想残高・無料遊技・BB・ストックは実物へ換金しない。
初回移行時に oxide/data/RustSlots_BeforeScrapMigration.json へ旧データをバックアップし、キー設定のみ引き継ぐ。
oxide/config/RustSlots.json：MaxStocks / GamesPerSet / SetReward / SpinRefreshSeconds / ImageUrls。
旧StartingCredits項目は無視される。データは oxide/data/RustSlots.json。
通常の切断／reloadでは確定停止位置と未受取額を保存する。
インベントリとデータファイルは別保存のため、電源断・強制終了時の完全な原子性は保証しない。

## 画像差し替え
以前作成された素材ファイルはこのパッケージに含まれていない。
ImageUrls のキーにクライアントからアクセスできる画像URLを指定。
背景：Outpost / Supermarket / Airfield / OilRig
科学者：ScientistBlue / ScientistYellow / ScientistGreen / ScientistRed
前兆：OmenStrong
BB告知：BonusConfirmed
戦闘：BradleyApproach / BradleyAttack / BradleyCounter / BradleyWin / BradleyLose
同じ接尾辞を PatrolHeli、Chinook にも使用。
例えば "Outpost": "https://your-host/outpost.png"。
未設定は色付き画面と文字で表示。画像指定は背景に重ねる単一シーン画像。透過多層合成、音声、復活演出、時間指定アニメは次段階。
画像はJPG/PNG推奨。上部画面は一般的な16:9モニターに合わせた近似比率。

## 検証と限界
Pythonによる全9261停止組合せの走査で全8役の停止候補を確認。
BET1/2/3それぞれBB確定役の候補存在を確認。
Rust/Oxide参照DLLとC#コンパイラがこの環境にないため、サーバーでのコンパイル・CUI表示・キー入力・再起動復元は未検証。
実機の抽選値、リール配列、出玉率の再現ではなく、指定した構造の独自試作。
サーバーで最初に確認：/slot表示→START→STOP3つ→キー設定→回転中に閉じて再開→reload後の状態復元。
エラーが出た場合はRustSlotsのコンパイルエラー全文を提示してください。

参考：Facepunchのbind/writecfg仕様
https://wiki.facepunch.com/rust/Keybinds

## 0.1.2の修正
- 払出時に既存スクラップとのスタックを許可。役ごとの払出が別スロットへ分散する問題を修正。
- キーbind用に引数なしの `slot.stop1` / `slot.stop2` / `slot.stop3` を追加。
- 画面ボタンと従来の `slot.stop 1` / `slot.stop 2` / `slot.stop 3` も引き続き使用可能。

## 0.1.3の修正
- Rustが空きスロットを優先して払出SCを分割する挙動を修正。
- `/slot`を開いた時と払出時に、メインインベントリとベルトの既存SCを最大スタックまで自動統合。
- 最大スタックを超える分だけ次のスロットを使用。通常上限が1000の場合、合計1220SCは1000＋220の2枠になる。

## 実サーバー確認済み
ローカルRustサーバーでロード、スクラップ消費、通常配当、REPLAY無料遊技、回転途中および1リール停止後の画面再開を確認済み。
