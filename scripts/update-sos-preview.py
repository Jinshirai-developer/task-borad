"""Local v22 -> v23 without migration or Stripe writes. Guarded backup/rollback."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location("transport_release", Path(__file__).with_name("update-transport-preview.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
release.OLD_IMAGE = "task-board:review-v22"
release.IMAGE = "task-board:sos-v23"
release.PREVIOUS = "task-board-preview-before-sos-v23"
release.FAILED = "task-board-preview-sos-v23-failed"
release.BACKUP_PREFIX = "sos-v23-"
release.DUMP_NAME = "before-sos.dump"

if __name__ == "__main__":
    try:
        release.main()
    except Exception:
        print("Update not completed; private details withheld.")
        raise SystemExit(1)
