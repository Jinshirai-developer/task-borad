"""Local v23 -> v24, preserving rows, configuration and keys; no schema change."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location("workflow_release", Path(__file__).with_name("update-transport-preview.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
release.OLD_IMAGE = "task-board:sos-v23"
release.IMAGE = "task-board:workflow-v24"
release.PREVIOUS = "task-board-preview-before-workflow-v24"
release.FAILED = "task-board-preview-workflow-v24-failed"
release.BACKUP_PREFIX = "workflow-v24-"
release.DUMP_NAME = "before-workflow.dump"

if __name__ == "__main__":
    try:
        release.main()
    except Exception:
        print("Update not completed; private details withheld.")
        raise SystemExit(1)
