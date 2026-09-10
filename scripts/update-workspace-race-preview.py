"""Local v26 -> v26.1: preserve an already opened form during slow restoration."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location("workspace_race_release", Path(__file__).with_name("update-transport-preview.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
release.OLD_IMAGE = "task-board:workspace-v26"
release.IMAGE = "task-board:workspace-v26.1"
release.PREVIOUS = "task-board-preview-before-workspace-v26.1"
release.FAILED = "task-board-preview-workspace-v26.1-failed"
release.BACKUP_PREFIX = "workspace-v26.1-"
release.DUMP_NAME = "before-workspace.dump"

if __name__ == "__main__":
    try:
        release.main()
    except Exception:
        print("Update not completed; private details withheld.")
        raise SystemExit(1)
