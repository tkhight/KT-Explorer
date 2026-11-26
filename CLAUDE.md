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
| 同じジェスチャとロッカーの画面テスト(実際のマウス入力)。**StrokesPlus などのマウスジェスチャの常駐ツールが、KT_Explorer を除外していないと、合成した右ボタンが横取りされて失敗する**(ツールで KT_Explorer を除外するか、無効にして実行する。除外していれば、有効なままで通る: 2026-11-26 に確認、13 件 OK・約 45 秒) | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_gesture_wheel.ps1'` |
| 指定ペインの作成(2〜3 個の条件、フォルダ入力の整理、`MainViewModel.CreatePanes`)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_panecreate.ps1'` |
| 指定ペインの作成ダイアログの画面テスト(既存タブにチェックを付けて作成、フォルダを 2〜3 個指定して作成、キャンセル) | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_pane_create.ps1'` |
| メニューバー「編集」の画面テスト(元に戻す・やり直し・切り取り・コピー・貼り付け・削除・全て選択。ファイルへの操作と、入力欄の文字への操作)。クリップボードを書き換える | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_menu_edit.ps1'` |
| アプリのアイコン(app.ico の大きさの種類、exe のアイコン、トレイのアイコンの読み込み)のロジック。画面は出ない | `& 'E:\KT_Applications\KT_Explorer\tools\test\logic_icon.ps1'` |
| グループタブの画面テスト(件数が付かない・ドラッグで並べ替え・クリックだけでは並ばない・右クリックの「グループの色」の格子で色を選ぶと色の線が付き、一覧と列見出しの背景がごく薄いその色になる・「その他の色…」の画面を開いてキャンセル・「色なし」で戻る)。実際のマウスを使う。ロジック(並べ替え・色・デフォルトグループ)は `logic_tabs.ps1` に含む | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_group_tabs.ps1'` |
| コピー・貼り付けの対象と、ツリーの Ctrl+クリックの画面テスト(タブ A でコピー → タブ B をクリックした直後に Ctrl+V が、タブ B に貼り付く / ペイン A でコピー → ペイン B のヘッダーをクリックして Ctrl+V が、ペイン B に貼り付く / ツリーのフォルダの Ctrl+クリックで新しいタブ)。クリップボードを書き換える。マウスを実際に動かす | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_paste_target.ps1'` |
| メニューのフラットなデザインの画面テスト(メニューバーのホバー・開いている見出しの色、開いたメニューのホバー・使えない項目の灰色・チェックの青いマークを、画素で確かめる)。マウスを実際に動かす。メニューの見た目は、`src/KT_Explorer/Resources/FlatMenu.xaml`(アプリ全体のリソース) | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_flat_menu.ps1'` |
| 統一したフラットなデザインの画面テスト(枠なし・ホバーの水色・プレビューのオンの色・選択中のグループタブとフォルダタブの色・一覧の列見出しのホバー・入力欄の枠と入力中の青い線・ペインタブの図柄を、画素で確かめる。ペインのボタンでペインタブができる)。ボタンの `AutomationId` は `Panes2Button` `Panes3Button` `SpecifyPaneButton` `PreviewToggle` `AddGroupButton` など。マウスを実際に動かす | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_flat_style.ps1'` |
| ツールバーのアイコンボタン(戻る・進む・一つ上・更新・元に戻す・やり直し・新しいタブ)の画面テスト(名前つきで存在する・アイコンが描かれている・使えないときは灰色・押すと操作が働く)。ボタンの `AutomationId` は `BackButton` `ForwardButton` `UpButton` `RefreshButton` `UndoButton` `RedoButton` `NewTabButton` | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_toolbar_icons.ps1'` |
| タブの複数段表示の画面テスト(名前の長いタブを 12 個開き、「表示」メニューで、1 段 → 複数段(すべてが画面の中)→ 1 段に戻る。選択中のタブは変わらない)。設定の保存・復元のロジックは `logic_tabs.ps1` に含む | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_multirow_tabs.ps1'` |
| ツリーの仮想化の画面テスト(サブフォルダ 300 個でも、画面の要素は見えている分だけ。選択したノードが表示範囲へスクロールされる。列挙が速い) | `& 'E:\KT_Applications\KT_Explorer\tools\test\ui_tree_virtual.ps1'` |
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
- 画面テストの時間は、1 本 10〜45 秒ほど(全 22 本で約 9 分)。以前は 1 本 1〜5 分かかっていた。待ち時間は、固定の `Start-Sleep` ではなく、`common.ps1` の `Wait-Until { 条件 } 最大ミリ秒`(条件が成り立つまで 0.1 秒ごとに確かめる)で書く。`Move-To`・`Set-Foreground`・`Start-KtApp`・`Find-AppMenuItem`(このアプリのウィンドウの中だけを探す)も、そうなっている。以前の遅さの原因:原因は、`%TEMP%` の数千のサブフォルダでツリーが膨らみ、ウィンドウの画面要素が 3 万個を超えて、全体の列挙(`Find-All`)が 1 回約 9 秒かかったため。ツリーを仮想化して(見えているノードだけを作る)、要素は約 500 個、列挙は約 0.15 秒になった。テストを足すときは、**ツリーの要素(`TreeItem`)は、表示範囲の外にも、余分に作られる**ので、位置で選ぶときは、ツリーの表示範囲の中にあるものだけを選ぶ。PowerShell ツールの `timeout` は、念のため長め(例: 300000)にする。
- **実行するテストは、変更に関係するものだけにする(ユーザーの指示)。** 画面テストは、実際のマウスとキーボードを使うので、実行中は、ユーザーが PC を使えない。関係のないテストまで、習慣で通し直さない。
  - **必ず実行する**: 新しい機能・修正のテストと、修正前のコードで失敗することの確認(不具合の修正のとき)。
  - **変更に合うものを選ぶ**。選んだ理由を、報告に一言で書く。下の表は、目安。
  - **全本の通しは、次のときだけ**: `common.ps1`、アプリ全体のリソース(`FlatMenu.xaml` など)、全体に効く処理(`Window_PreviewKeyDown` の全体など)を変えたとき。または、ユーザーが頼んだとき。
  - 揺れることがあるテスト(`ui_toolbar_icons` など)は、関係がなければ、実行しない。揺れて落ちたときは、原因を調べて直す(再実行で通ったから終わり、にしない)。

  | 変更した場所 | 実行するテスト(目安) |
  |---|---|
  | キー処理(`Ctrl+C/X/V`・`Window_PreviewKeyDown`) | `ui_paste_target` `ui_menu_edit` `ui_list_rename` `ui_tree_rename` |
  | ツリー | `ui_tree_click` `ui_tree_rename` `ui_tree_virtual` |
  | タブ・グループ | `logic_tabs` `logic_pin` `ui_pin_color` `ui_group_tabs` `ui_multirow_tabs` `ui_typeahead_tabs` `ui_sort_tabs` |
  | ペイン | `ui_pane_create` `ui_pane_width` `logic_panecreate` `ui_paste_target` |
  | 一覧の表示・列・並べ替え | `logic_viewmode` `ui_viewmode` `logic_columns` `ui_sort_tabs` |
  | 絞込・検索 | `logic_filter` `ui_filter` `logic_search` |
  | お気に入り・履歴 | `logic_favorites` `ui_favorites` `logic_history` |
  | プレビュー | `logic_preview` `ui_preview` |
  | メニュー | `ui_menu_file` `ui_menu_edit` `ui_menu_settings` `ui_flat_menu` |
  | マウスジェスチャ | `logic_gesture` `ui_gesture_wheel` |
  | アイコン・オーバーレイ | `logic_navicon` `ui_navicon` `logic_overlay` `ui_overlay` `ui_toolbar_icons` |
  | 見た目(フラットなデザイン) | `ui_flat_style` `ui_flat_menu` `ui_toolbar_icons` |
  | フォルダのサイズ・記憶 | `ui_folder_size` `logic_cache` |
  | ショートカット(.lnk) | `logic_shortcut` `ui_shortcut` |
  | パス欄・パンくず・コピー | `logic_pathbar` `ui_copy_path` |
- **ユーザーの指示**: KT_Explorer が起動していても、確認のために閉じてよい(ユーザーに閉じてもらうのを待たない)。画面テストは(`ui_pane_width.ps1` を除き)、起動中のアプリがあれば、先にウィンドウを閉じる操作(終了時の状態保存が走る)をしてから、残っていれば強制終了して、テストを続ける。閉じるのは、名前が `KT_Explorer` のプロセスだけ。テストのあと、ユーザーのアプリは自動では起動し直さないので、作業の報告で、閉じたことを伝え、再起動はユーザーに任せる。
- **自動操作でメニューの項目を押した(`Invoke`)あとは、メニューのキー操作の状態が残って、最初のキー入力が吸われることがある**(`ui_viewmode` の手順 6 が、まれに落ちていた原因)。そのあとにキーで操作するテストは、項目が消えるまで待ち、期待する結果になるまで、キーを(最大 3 回)送り直す(`Wait-Until`)。`Esc` で抜けるのは、名前の入力中の編集を取り消してしまうことがあるので、避ける。
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
