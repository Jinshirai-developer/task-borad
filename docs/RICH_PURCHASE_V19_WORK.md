# 購入画面のリッチ化とStripe導線 v19

## 接続確認の範囲

2026-09-09に `ruby scripts/stripe-local.rb check` を再実行し、保存済み制限付きテストキーでStripeの価格GETがHTTP 200。テストモード・JPY・月額500円・固定価格IDの一致を再確認した。稼働コンテナでも `Billing__Enabled=true` を確認。秘密値を表示・変更していない。

通常アプリはStripe Sandboxを使う設定だが、`?demo=1` は実APIに接続しないシミュレーション。接続設定・価格GETの成功と、カード画面表示／購入成功の確認は区別する。今回、Stripeの一時Checkout作成・失効によるカード画面確認をユーザーへ提案したが、承認前に外部の決済オブジェクトは作成しない。

既存の `StripeTestGateway` は `Mode=subscription`、`PaymentMethodTypes=[card]`、`Locale=ja` のCheckoutを作り、フロントエンドは検証済みの `https://checkout.stripe.com/` URLへ遷移する。カード番号・有効期限・CVCの入力はStripeのホスト画面が担当し、アプリ内に独自のカードフォームや偽の入力欄は追加しない。[Stripe Checkoutの公式説明](https://docs.stripe.com/payments/checkout)。

## 変更内容

- 改修対象は購入確認・結果画面。タスクボード全体の再設計やSitesへの移行・公開は行わない。
- 購入の流れを「プランを確認 → Stripeでカード入力 → 結果を確認」の3段階で表示。デモの第2段階は「デモではカード入力なし」と明記。
- 通常の所有者向けボタンを「Stripeでカード入力へ」にし、未完了の場合は同じStripe入力画面の再開と分かる文言へ変更。
- デモ上部に「カード入力を試すにはログイン」、購入内容内に通常アプリで所有チームを選ぶ案内を追加。リンクは同一アプリの `login.html` で、デモフラグ・見本のチームIDを引き継がない。
- 深いブルーグリーンの見出しとProカード、余白・罫線・控えめな影で情報の強弱を整理。PCでは比較・注意事項と購入サマリーを左右に分離、狭い画面では縦配置。
- 対象チーム名は冒頭と購入内容に表示。画面下の固定操作エリアにも月額500円・テスト専用を表示する。
- 既存の透明な猫画像 `portfolio-cat-idle-v3.png` を装飾として再利用。新しい画像生成・画像編集・装備変更はなし。装飾画像のaltは空、サイズを固定し、動かさない。
- 結果画面の有効Proと確認待ちを視覚的にも区別。成功判定・所有者制限・二重操作防止・価格のサーバー管理・カード非保存・不正URL拒否・遅延応答無効化は既存実装を維持。
- classic／Windows風retro／dark、スマホ、キーボード操作、エラーへのフォーカス、動きを減らす設定を維持。

## 試験の区別

`tests/browser-billing.py` は実API・Stripeへの通信を遮断する。通常アプリ用の「Stripeでカード入力へ」という表示も、明示的に差し替えた画面試験用応答で検証する。このキャプチャは実Stripeのカード画面へ到達した証明ではない。

Stripeの実Checkout作成権限、カード画面の実表示、購入・通知・Pro反映・更新・解約のE2E確認は引き続き別の確認。実カードは使わず、テスト時は [公式のテストカード](https://docs.stripe.com/testing) を使う。

## 検証とローカル反映

- バックエンド243件、JavaScript115件成功。新しいruntimeイメージのビルド成功。サーバーの課金処理・Migration・Stripeキーは変更していない。
- ソース配信Chromeで購入193項目、既存相棒186項目、使いやすさ73項目成功。購入の追加検証は3段階の案内、デモから通常ログインへのリンク、実フォーム非配置、通常モード用の明示的なStripeボタン、装飾画像の読み込みとalt、冒頭のチーム名。
- ソース配信の記録: `/private/tmp/task-billing-browser-fug0v73w`、`/private/tmp/task-companion-browser-afva41f7`、`/private/tmp/task-usability-browser-k3n0_m04`。画像と画面幅を目視確認済み。全て実APIとStripeを遮断した画面試験。
- 反映後の実配信でも購入193項目成功。記録は `/private/tmp/task-billing-browser-yyj1cd_8`。最終readiness 200、`git diff --check` 成功。
- `scripts/update-rich-purchase-preview.py` で `task-board:purchase-v18-1` から **`task-board:purchase-v19`** へ反映済み。URLは `http://localhost:5097/`。16永続テーブルの全行ハッシュ、Migration、Stripeを含む全環境変数、ログイン鍵を保持。readiness 200、匿名プランAPI 401、配信29ファイル一致。
- バックアップは `.local/backups/purchase-20260909.mn7uetw_/`（700/600、DB・鍵・秘密設定を含むため共有・commit禁止）。安全な結果の要約は `verification.json`。旧コンテナ `task-board-preview-before-purchase-v19` と以前のバックアップも停止保持。
- 反映スクリプトは旧イメージ・未契約状態を前提とする一度限りの操作で、現在の環境へ再実行しない。通知転送は既存の `ruby scripts/stripe-local.rb listen-login` を継続し、重複起動しない。
- 実ユーザーデータでのテスト操作、Stripeの商品・Checkout・契約の作成／変更、公開・commit・pushは行っていない。新規画像の生成や既存画像の変更もない。
- 検証専用コンテナ `task-rich-purchase-js` は停止・削除済み（マウント・ネットワークなし、実データなし、同じ試験で再作成可能）。実アプリ・DB・メール・鍵・通知転送・バックアップ・復旧用コンテナは保持。
