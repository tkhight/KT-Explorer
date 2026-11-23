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
| 中断したテストの後片付け(`%TEMP%\kt_test_*` を消す) | `& 'E:\KT_Applications\KT_Explorer\tools\test\cleanup.ps1'` |

- 実行前に、上のビルドを行う。結果は `OK` / `NG` の行と、最後の `RESULT: ok=… ng=…`。
- 画面テストは時間がかかる(`ui_tree_rename.ps1` は 2〜3 分)。ツリーの画面要素の列挙が重いため。PowerShell ツールの `timeout` を長め(例: 300000)にするか、バックグラウンドで実行して、完了の通知を待つ。
- **ユーザーの指示**: KT_Explorer が起動していても、確認のために閉じてよい(ユーザーに閉じてもらうのを待たない)。画面テストは(`ui_pane_width.ps1` を除き)、起動中のアプリがあれば、先にウィンドウを閉じる操作(終了時の状態保存が走る)をしてから、残っていれば強制終了して、テストを続ける。閉じるのは、名前が `KT_Explorer` のプロセスだけ。テストのあと、ユーザーのアプリは自動では起動し直さないので、作業の報告で、閉じたことを伝え、再起動はユーザーに任せる。
- テストを足す・変えるときは、`tools/test/common.ps1` の決まりを守る。
  - 操作するフォルダは `%TEMP%\kt_test_*` の中だけ。
  - 止めてよいプロセスは、名前が `KT_Explorer` のものだけ(それ以外のプロセスは止めない)。
  - キーを送る前に、入力欄の中身などが想定どおりか確認し、違えば中止する。
  - 引数でコマンドを受け取らない。
- **許可済みスクリプトの中身を変えると、確認なしで実行される。** 変更したら、コミット前に必ず内容を見直す。新しいスクリプトを許可に加えるときは、`.claude/settings.json` に、スクリプトごとに1行ずつ(フォルダ全体のワイルドカードにしない)足す。

## 既知の事情

- `Ctrl+W` は、自動操作(SendKeys など)では、キーがアプリへ届かない環境がある。タブは `✕` ボタンで閉じる。実機(ユーザーの操作)では、`Ctrl+W` でタブが閉じることを確認済みなので、アプリの不具合ではない。
- ユーザーの実機の `C:\OneDrive(link)`(ジャンクション)など、**`%TEMP%\kt_test_*` の外のフォルダを、テストで操作しない**。過去に、画面テストが誤って実機のフォルダの名前を変えた(復旧済み)。
