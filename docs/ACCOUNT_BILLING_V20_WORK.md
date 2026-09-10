# v20: completion dismissal and account-scoped Pro

## Scope

- Completed create/join: X, Escape, backdrop and OK dismiss both nested dialogs to the selected board. Incomplete forms still return to settings.
- Pro is purchased once per account, covering all teams it owns, including teams created later. A paid member joining another person's free team does not upgrade it.
- Free capacity stays three current members per team, including the owner. Membership limit (10 teams), task limits, XP, pets and rewards are unchanged.
- Settings → Account → Plan/contract management works even without a team. Team management remains another entry point to the owner's account plan.
- Transfer applies the next owner's plan without transferring the subscription. Deleting a team does not cancel it. Expiry blocks only new joins over the free cap, retaining existing members/tasks.
- Account deletion is blocked until pending/active billing has ended, even after all teams have been deleted.

## Backend and migration

TeamBilling/TeamBillingService keep their internal names for compatibility, but the persisted table is account_billing, keyed/FK'd by UserProfileId. Historical TeamId is only a snapshot, with no team FK. Receipts follow the account. GET/POST /api/user/billing exposes only the authenticated account; the original team billing routes remain membership-protected and owner-only for mutations.

Migration 20260908183357_AccountBilling maps the original team owner to the billing account and preserves every original field: identifiers, amount, paid-through, cancellation and attempt parameters. MetadataScope=team preserves legacy Stripe verification and idempotent recovery parameters. New attempts use account metadata and return to ?billing=return&account=1. Migration does not create or change any Stripe subscription, invoice, customer, price or Checkout.

Two legacy billing records for one owner (including ended records) cause the whole migration to abort without selecting, deleting or cancelling a record. An in-flight lease also blocks migration. Guarded Down works only before account-specific activity or incompatible ownership changes; it never overwrites the database from a dump automatically.

The workspace transaction, account-wide lease and PK prevent checkouts through different teams from creating parallel subscriptions. Signed webhooks, deduplication, live-mode rejection, fixed-price matching and authoritative paid-state checks remain intact.

Stripe primary references: [metadata and propagation](https://docs.stripe.com/metadata), [subscription events](https://docs.stripe.com/billing/subscriptions/webhooks). Metadata binds records but does not itself prove payment.

## Verification

- Docker/.NET 10: 246 backend tests passed, none skipped.
- Node 22: 116 frontend tests passed.
- Isolated Chrome: 221 billing/closing/account checks, 73 usability checks, 186 companion checks. Includes 320/375/1440px, classic/retro/dark, focus/keyboard, all four completion dismissals, personal-scope account management, new-team Pro, no duplicate buy button and zero real API/Stripe calls.
- Inspected screenshots under /private/tmp/task-billing-browser-0s81w009/: account-purchase-375.png and account-management-375.png.
- PostgreSQL 16 clone rehearsal: 17 original tables preserved (billing projections exclude only new columns); Up/Down and owner mapping verified; seeded duplicate-account migration rejected atomically. Rehearsal container/database/anonymous volume are removed afterwards. Protected backup dumps are retained under .local/backups/account-v20-*.
- CI pins the old billing migration before its original assertions, then tests the account migration separately. Remote CI has not been run.
- No real card details, new Stripe business objects or payment submissions used in these tests. The existing local database has one active test contract, preserved rather than repurchased.

## Local rollout

scripts/update-account-preview.py --rehearse performs the isolated rehearsal. --deploy is a guarded one-time v19→v20 update: stop, protected dump/keys/env backup, transaction, verify, restart with identical env and key volume, health/auth/static-file checks. It refuses to reuse an existing rollback target. Never rerun after successful deployment.

Deployment succeeded: task-board:account-v20 at http://localhost:5097/, one Pro account covering three owned teams. All 17 tables' original values were preserved during migration, all environment settings/keys preserved, 29 frontend files matched, readiness 200 and anonymous account/team billing APIs 401.

Recovery: task-board-preview-before-account-v20 and .local/backups/account-v20-958cwzwf/ (private). Do not simply start the old app against the new schema. Up/Down scripts and pre-migration dump/keys/env are preserved for reviewed recovery; never print/share/commit them. Migration tools were held in disposable task-account-v20-build during implementation.

Final served-screen checks: 221 passed against localhost:5097 (offline API fixtures), artifacts /private/tmp/task-billing-browser-7bb4b9a_/. No pending EF model changes; git diff --check passed. Disposable task-account-v20-js and task-account-v20-build were inspected (network none, no mounts), stopped and removed. They can be recreated from source/images. The running app/DB/mail, Stripe listener, old rollback containers, images and all protected backups were retained.
