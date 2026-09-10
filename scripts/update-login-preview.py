"""Local v26.1 -> v27 login redesign; preserve rows, keys and existing contracts."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location("login_release", Path(__file__).with_name("update-transport-preview.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
release.OLD_IMAGE = "task-board:workspace-v26.1"
release.IMAGE = "task-board:login-v27"
release.PREVIOUS = "task-board-preview-before-login-v27"
release.FAILED = "task-board-preview-login-v27-failed"
release.BACKUP_PREFIX = "login-v27-"
release.DUMP_NAME = "before-login.dump"

if __name__ == "__main__":
    try:
        release.main()
    except Exception:
        print("Update not completed; private details withheld.")
        raise SystemExit(1)
