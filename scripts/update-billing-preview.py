"""One-time local v16 -> v17 upgrade. Stripe stays disabled unless separately configured."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location('preview_deployer', Path(__file__).with_name('update-usability-preview.py'))
deployer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deployer)
deployer.PREVIOUS = 'task-board-preview-before-billing-v17'
deployer.IMAGE = 'task-board:billing-v17'
deployer.OLD_IMAGE = 'task-board:companion-v16'
deployer.TOOLS_IMAGE = 'task-board:billing-v17-tools'
deployer.OLD_MIGRATION = '20260908124231_AddCompanionWork'
deployer.MIGRATION = '20260908140702_AddTeamTestBilling'
deployer.MIGRATION_MARKER = b'CREATE TABLE team_billing'
deployer.NEW_TASK_COLUMNS = []
deployer.NEW_TABLES = ['team_billing', 'billing_event_receipts']
deployer.DEFAULTS_SQL = 'SELECT (SELECT count(*) FROM team_billing) + (SELECT count(*) FROM billing_event_receipts)'
deployer.BACKUP_PREFIX = 'billing-20260908.'
deployer.FAILED_CONTAINER = 'task-board-preview-billing-v17-failed'

if __name__ == '__main__':
    deployer.main()
