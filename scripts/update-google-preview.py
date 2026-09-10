"""Local v27 -> v28 Google login support; provider stays disabled until configured.
Preserves every row, session key and existing Stripe test contract. One time only.
"""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location("google_release", Path(__file__).with_name("update-transport-preview.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
release.OLD_IMAGE = "task-board:login-v27"
release.IMAGE = "task-board:google-v28"
release.PREVIOUS = "task-board-preview-before-google-v28"
release.FAILED = "task-board-preview-google-v28-failed"
release.BACKUP_PREFIX = "google-v28-"
release.DUMP_NAME = "before-google.dump"

if __name__ == "__main__":
    try:
        release.main()
    except Exception:
        print("Update not completed; private details withheld.")
        raise SystemExit(1)
