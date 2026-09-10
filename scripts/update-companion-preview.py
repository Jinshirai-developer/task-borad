"""Local v15 -> v16 only. Backup/recovery uses the audited preview deployer."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location('preview_deployer', Path(__file__).with_name('update-usability-preview.py'))
deployer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deployer)
deployer.PREVIOUS = 'task-board-preview-before-companion-v16'
deployer.IMAGE = 'task-board:companion-v16'
deployer.OLD_IMAGE = 'task-board:usability-v15'
deployer.TOOLS_IMAGE = 'task-board:companion-v16-tools'
deployer.OLD_MIGRATION = '20260908100945_AddTaskUsability'
deployer.MIGRATION = '20260908124231_AddCompanionWork'
deployer.MIGRATION_MARKER = b'ADD companion_json'
deployer.NEW_TASK_COLUMNS = ['companion_json']
deployer.DEFAULTS_SQL = "SELECT count(*) FROM tasks WHERE companion_json <> '{}'"
deployer.BACKUP_PREFIX = 'companion-20260908.'
deployer.FAILED_CONTAINER = 'task-board-preview-companion-v16-failed'

if __name__ == '__main__':
    deployer.main()
