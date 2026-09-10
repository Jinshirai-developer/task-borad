"""Local v21 -> v22, no schema change. Preserves every row, env, and key volume.

Reuses the guarded backup/rollback workflow. Never runs migrations or Stripe writes.
"""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location("transport_release", Path(__file__).with_name("update-transport-preview.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
release.OLD_IMAGE = "task-board:transport-v21"
release.IMAGE = "task-board:review-v22"
release.PREVIOUS = "task-board-preview-before-review-v22"
release.FAILED = "task-board-preview-review-v22-failed"
release.BACKUP_PREFIX = "review-v22-"
release.DUMP_NAME = "before-review.dump"

if __name__ == "__main__":
    try:
        release.main()
    except Exception:
        print("Update not completed; private details withheld.")
        raise SystemExit(1)
