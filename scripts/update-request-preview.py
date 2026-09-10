"""Local v24 -> v25, preserving rows, configuration and keys; no schema change."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location("workflow_release", Path(__file__).with_name("update-transport-preview.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
release.OLD_IMAGE = "task-board:workflow-v24"
release.IMAGE = "task-board:requests-v25"
release.PREVIOUS = "task-board-preview-before-requests-v25"
release.FAILED = "task-board-preview-requests-v25-failed"
release.BACKUP_PREFIX = "requests-v25-"
release.DUMP_NAME = "before-requests.dump"

if __name__ == "__main__":
    try:
        release.main()
    except Exception:
        print("Update not completed; private details withheld.")
        raise SystemExit(1)
