"""Local v25 -> v26: remember workspace navigation; preserve rows, keys and contracts."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location("workspace_release", Path(__file__).with_name("update-transport-preview.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
release.OLD_IMAGE = "task-board:requests-v25"
release.IMAGE = "task-board:workspace-v26"
release.PREVIOUS = "task-board-preview-before-workspace-v26"
release.FAILED = "task-board-preview-workspace-v26-failed"
release.BACKUP_PREFIX = "workspace-v26-"
release.DUMP_NAME = "before-workspace.dump"

if __name__ == "__main__":
    try:
        release.main()
    except Exception:
        print("Update not completed; private details withheld.")
        raise SystemExit(1)
