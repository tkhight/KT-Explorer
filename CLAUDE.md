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
| 中断したテストの後片付け(`%TEMP%\kt_test_*` を消す) | `& 'E:\KT_Applications\KT_Explorer\tools\test\cleanup.ps1'` |

- 実行前に、上のビルドを行う。結果は `OK` / `NG` の行と、最後の `RESULT: ok=… ng=…`。
- 画面テストは時間がかかる(`ui_tree_rename.ps1` は 2〜3 分)。ツリーの画面要素の列挙が重いため。PowerShell ツールの `timeout` を長め(例: 300000)にするか、バックグラウンドで実行して、完了の通知を待つ。
- **画面テストは、ユーザーが KT_Explorer を起動中だと `ABORT` で中止する**(ユーザーのインスタンスを止めないための仕様)。その場合は、アプリを閉じてもらってから再実行する。
- テストを足す・変えるときは、`tools/test/common.ps1` の決まりを守る。
  - 操作するフォルダは `%TEMP%\kt_test_*` の中だけ。
  - 止めるプロセスは、スクリプトが起動したものだけ(名前での強制終了はしない)。
  - キーを送る前に、入力欄の中身などが想定どおりか確認し、違えば中止する。
  - 引数でコマンドを受け取らない。
- **許可済みスクリプトの中身を変えると、確認なしで実行される。** 変更したら、コミット前に必ず内容を見直す。新しいスクリプトを許可に加えるときは、`.claude/settings.json` に、スクリプトごとに1行ずつ(フォルダ全体のワイルドカードにしない)足す。

## 既知の事情

- `Ctrl+W` は、自動操作(SendKeys など)では、キーがアプリへ届かない環境がある。タブは `✕` ボタンで閉じる。実機(ユーザーの操作)では、`Ctrl+W` でタブが閉じることを確認済みなので、アプリの不具合ではない。
- ユーザーの実機の `C:\OneDrive(link)`(ジャンクション)など、**`%TEMP%\kt_test_*` の外のフォルダを、テストで操作しない**。過去に、画面テストが誤って実機のフォルダの名前を変えた(復旧済み)。
