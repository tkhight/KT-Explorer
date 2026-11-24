# KT_Explorer 開発メモ(Claude Code 向け)

C# + WPF (.NET 8) のタブ付きファイルブラウザ。機能の説明は [README.md](README.md) を参照。

## ビルド

```powershell
dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
```

確認用のビルドは、必ず `$TEMP/kt_build_check` に出す。ユーザーが使用中の exe(`bin\Debug`)がロックされていても失敗しないため。

## テスト(スクリプトで実行する。場当たりの長い PowerShell は書かない)

動作確認は、`tools/test/` の固定スクリプトを、**次の形のとおり**に実行する。
これらは `.claude/settings.json` で、呼び出しの文字列まで一致する形で許可されている。
形を変える(引数を足す、`2>&1` でつなぐなど)と、許可に一致せず、確認が出る。

| 目的 | 実行 |
|---|---|
| タブ・ツリーのロジック(開き直し・複製・名前変更/削除への追従・ツリー更新)。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_tabs.ps1'` |
| ツリーの新規作成・名前の変更(その場で入力)の画面テスト | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_tree_rename.ps1'` |
| 一覧の名前の変更(その場で入力)の画面テスト | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_list_rename.ps1'` |
| 先頭文字入力、閉じたタブの開き直し(`Ctrl+Shift+T`)の画面テスト | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_typeahead_tabs.ps1'` |
| フォルダのサイズ計算(右クリックの手動計算・設定の自動計算)の画面テスト | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_folder_size.ps1'` |
| 列見出しの並べ替えがタブごとに独立していること(▲▼ がタブに合わせて変わる)の画面テスト | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_sort_tabs.ps1'` |
| ペインの幅(スプリッター、ペインの増減後も比率を保つ)の画面テスト。アプリ本体は起動せず専用ウィンドウだけを使うので、KT_Explorer が起動中でも実行できる | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_pane_width.ps1'` |
| ツリーの項目の右側の空白をクリックしても、その項目が選択されること(行全体の強調表示・クリック範囲)の画面テスト | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_tree_click.ps1'` |
| プレビューの内容(文字コードの判定・バイナリの判別・大きなテキストの切り詰め・画像・フォルダ)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_preview.ps1'` |
| 絞込欄の条件(名前・ワイルドカード・`type:` `ext:` `size:` `date:`・除外)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_filter.ps1'` |
| お気に入りの整理(並べ替え・グループ・区切り線・状態への保存と復元)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_favorites.ps1'` |
| プレビューウィンドウの画面テスト(表示の切り替え、選択に応じた内容、複数選択、`Alt+P`) | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_preview.ps1'` |
| 絞込欄の条件指定と、検索結果の一覧がファイル操作のあとも検索結果のままであることの画面テスト | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_filter.ps1'` |
| お気に入りの整理ウィンドウ(並べ替え・グループの追加と移動)とメニューへの反映の画面テスト | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_favorites.ps1'` |
| ショートカット(.lnk)のリンク先の解決と、タブの「開く」(リンク先がフォルダなら、その一覧のまま移動)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_shortcut.ps1'` |
| ショートカットを Enter・ダブルクリック・右クリックで開いたとき、エクスプローラーを開かず、KT_Explorer の中でリンク先を表示することの画面テスト | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_shortcut.ps1'` |
| 表示の切り替え(詳細・アイコン・一覧)のロジック: サムネイルの取得、タブごとの表示、新しいタブの初期の表示、状態の保存と復元。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_viewmode.ps1'` |
| 表示の切り替えの画面テスト: メニュー・`Ctrl+Shift+1/2/3`、列見出しの有無、矢印キーの移動、名前の変更、メニューからの並べ替え | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_viewmode.ps1'` |
| フォルダのサイズの記憶(5分・ファイル操作で無効化)、サムネイルの記憶、アイコンの大きさ(小・中・大)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_cache.ps1'` |
| 開いたフォルダの履歴(最近開いた・よく開く・上限・状態への保存と復元・タブの移動の記録)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_history.ps1'` |
| パス欄の補助(入力補完の候補・パンくずの階層の分割・中のフォルダの一覧)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_pathbar.ps1'` |
| 検索(絞込と同じ条件 `type:` `ext:` `size:` など、ファイルの中身 `content:` / `中身:`)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_search.ps1'` |
| 詳細表示の追加の列(作成日時・属性・画像のサイズ・再生時間)と列の選択のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_columns.ps1'` |
| メニューバー「ファイル」のロジック(WinMerge の引数の組み立て・見つけ方)。画面は出ない。WinMerge は起動しない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_menu_file.ps1'` |
| メニューバー「ファイル」の画面テスト(項目の有無、新規作成でフォルダー・テキストが作られる)。ターミナル・WinMerge・終了は押さない | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_menu_file.ps1'` |
| マウスジェスチャ「右ボタン+ホイール」(上=左のタブ・下=右のタブ。Ctrl つきは前・次のタブグループ)と、ロッカー(右ボタンを押したまま左クリック=タブを閉じる)の判定のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_gesture.ps1'` |
| 同じジェスチャとロッカーの画面テスト(実際のマウス入力)。**StrokesPlus などのマウスジェスチャの常駐ツールが有効だと、合成した右ボタンが横取りされて失敗する**(無効にして実行する) | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_gesture_wheel.ps1'` |
| 指定ペインの作成(2〜3 個の条件、フォルダ入力の整理、`MainViewModel.CreatePanes`)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_panecreate.ps1'` |
| 指定ペインの作成ダイアログの画面テスト(既存タブにチェックを付けて作成、フォルダを 2〜3 個指定して作成、キャンセル) | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_pane_create.ps1'` |
| メニューバー「編集」の画面テスト(元に戻す・やり直し・切り取り・コピー・貼り付け・削除・全て選択。ファイルへの操作と、入力欄の文字への操作)。クリップボードを書き換える | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_menu_edit.ps1'` |
| アプリのアイコン(app.ico の大きさの種類、exe のアイコン、トレイのアイコンの読み込み)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_icon.ps1'` |
| ツリーとタブのアイコン(固有のアイコンを持つフォルダ・共通アイコンの使い回し・ツリーのノードとタブのアイコンとオーバーレイ・パスが変わったときの付け替え・ペインタブは無し・設定のオフ/オン)のロジック。画面は出ない。ジャンクションを作る(テスト用フォルダの中だけ) | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_navicon.ps1'` |
| ツリーとタブのアイコンの画面テスト(タブとツリーに、フォルダのアイコンが出る。ジャンクションのタブに、リンクの矢印が重なり、「表示」メニューのオフ/オンで消える・戻る) | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_navicon.ps1'` |
| オーバーレイアイコン(シェルのオーバーレイの取得・ショートカットの矢印・項目への反映・オフ/オン・既定はオン)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_overlay.ps1'` |
| オーバーレイアイコンの画面テスト(ジャンクションの矢印が、一覧のアイコンに出て、「表示」メニューのオフ/オンで消える・戻る)。ジャンクションを作る(テスト用フォルダの中だけ) | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_overlay.ps1'` |
| ピン留め(別のフォルダへ移ると新しいタブで開く・内部の移動は動く)とタブの色(パレット・複製・開き直し・状態の保存と復元・古い状態の読み込み)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_pin.ps1'` |
| パスのコピーの画面テスト(長いパスで、パンくず・パス欄・タブの右クリックからコピーできる)。クリップボードを書き換える | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_copy_path.ps1'` |
| ピン留めとタブの色の画面テスト(タブの右クリックから留める・外す、移動で新しいタブ、色の線が画面に出る)。実際のマウスを使う | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_pin_color.ps1'` |
| 旧「設定」の項目を振り分けた「表示」「ツール」メニューの画面テスト(項目の有無、オン/オフの切り替え、「設定」ボタンが無いこと)。自動起動(レジストリ)は押さない | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_menu_settings.ps1'` |
| 中断したテストの後片付け(`%TEMP%\kt_test_*` を消す) | `& 'E:\KT_Applications\KT_Explorer\tools\test\cleanup.ps1'` |

- 実行前に、上のビルドを行う。結果は `OK` / `NG` の行と、最後の `RESULT: ok=… ng=…`。
- 画面テストは時間がかかる(`ui_tree_rename.ps1` は 2〜3 分)。ツリーの画面要素の列挙が重いため。PowerShell ツールの `timeout` を長め(例: 300000)にするか、バックグラウンドで実行して、完了の通知を待つ。
- **ユーザーの指示**: KT_Explorer が起動していても、確認のために閉じてよい(ユーザーに閉じてもらうのを待たない)。画面テストは(`ui_pane_width.ps1` を除き)、起動中のアプリがあれば、先にウィンドウを閉じる操作(終了時の状態保存が走る)をしてから、残っていれば強制終了して、テストを続ける。閉じるのは、名前が `KT_Explorer` のプロセスだけ。テストのあと、ユーザーのアプリは自動では起動し直さないので、作業の報告で、閉じたことを伝え、再起動はユーザーに任せる。
- テストを足す・変えるときは、`tools/test/common.ps1` の決まりを守る。
  - 操作するフォルダは `%TEMP%\kt_test_*` の中だけ。
  - 止めてよいプロセスは、名前が `KT_Explorer` のものだけ(それ以外のプロセスは止めない)。
  - キーを送る前に、入力欄の中身などが想定どおりか確認し、違えば中止する。
  - キーを送る・クリックする前に、前面にあるのがテスト用の KT_Explorer であることを確かめ、違えば、何も送らずに中止する(`Assert-AppForeground`)。ユーザーが別のアプリ(ブラウザなど)を使っているとき、そのアプリへキーを送ってしまわないため。**画面テストの実行中は、ユーザーにキーボードとマウスに触らないでもらう**(触ると、入力が乱れて失敗する)。
  - 引数でコマンドを受け取らない。
- **許可済みスクリプトの中身を変えると、確認なしで実行される。** 変更したら、コミット前に必ず内容を見直す。新しいスクリプトを許可に加えるときは、`.claude/settings.json` に、スクリプトごとに1行ずつ(フォルダ全体のワイルドカードにしない)足す。

## アプリのアイコン

`src/KT_Explorer/Resources/app.ico`(16〜256px)。水色のフォルダを、2 つのペインに分けた図案。図案を変えるときは、`tools/icon/make_icon.ps1` を直して実行すると、`app.ico` が作り直される(`& 'E:\KT_Applications\KT_Explorer\tools\icon\make_icon.ps1'`)。exe・ウィンドウ・トレイのアイコンは、すべてこの `.ico` を使う。

## リリースの手順(ユーザーの指示)

機能を実装して、ユーザーが確認し、「コミットして」と言ったら、作業ブランチにコミットする。そのあと、ユーザーが **「main にマージして push して」** と言ったら、次の3つを**まとめて**行う(個別に聞き直さない)。

1. **`main` へマージして、push する**: `git checkout main` → `git merge --no-ff <作業ブランチ>`(メッセージは「<ブランチ名> をマージ」)→ `git push origin main`。
2. **不要なブランチを削除する**: マージ済みのローカルブランチだけを、`git branch -d` で消す(`-D` は使わない)。リモートに作業ブランチは作っていない。
3. **実装履歴を更新して保存する**: ユーザーの OneDrive の「ツール履歴\KT_Explorer」にある、最新の `*_実装履歴.md`(Python のスクリプトファイルを使うと、日本語のパスも壊れない)。コミット表(コミットとマージコミットの行)、ブランチとリモートの節(`main` の先頭、削除済みのブランチ、push の回数と範囲)、機能の節、主なファイル、確認した内容(テストの件数、ユーザーが実機で確認したこと、未確認のこと)、既知の制約と今後の候補を、更新する。

止めて、ユーザーに確認するのは、次のとき。
- マージで衝突が起きた、`git push` が失敗した、`git branch -d` が「未マージ」で断った。
- 直前の確認(テスト)が通っていない、または、ユーザーが実機で確認していない変更が混ざっている。
- `.claude/settings.json` など、push すると**公開される**ものに、新しい種類の内容が増えている(許可の追加だけなら、そのまま進める)。

終わったら、3つの結果(マージコミット、push の範囲、削除したブランチ、履歴の更新内容と、未確認の点)を、短く報告する。コミットは、ユーザーが「コミットして」と言ったときだけ行う(マージ・push の指示に、コミットは含まれない)。

## 既知の事情

- アイコン・一覧の表示では、項目の中身(名前の入力欄など)が、画面要素(UI Automation)の一覧に出ない。また、見えている範囲の項目だけが出る(全部で数十件を超えても、件数は数十)。そのため、画面テストでは、入力欄の中身やフォーカスを確かめられない。名前の変更を試すときは、Enter ではなく、フォーカスを受け取れない場所(ラベル「絞込:」)のクリックで確定する。

- `Ctrl+W` は、自動操作(SendKeys など)では、キーがアプリへ届かない環境がある。タブは、タブの右クリックの「タブを閉じる」で閉じる(タブに ✕ は無い)。実機(ユーザーの操作)では、`Ctrl+W` でタブが閉じることを確認済みなので、アプリの不具合ではない。
- ユーザーの実機の `C:\OneDrive(link)`(ジャンクション)など、**`%TEMP%\kt_test_*` の外のフォルダを、テストで操作しない**。過去に、画面テストが誤って実機のフォルダの名前を変えた(復旧済み)。
