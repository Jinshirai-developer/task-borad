"""One-time v18-1 -> v19 local purchase UI update; preserve Stripe and all data."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location('purchase_deployer', Path(__file__).with_name('update-purchase-preview.py'))
deployer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deployer)
deployer.OLD_IMAGE = 'task-board:purchase-v18-1'
deployer.IMAGE = 'task-board:purchase-v19'
deployer.PREVIOUS = 'task-board-preview-before-purchase-v19'
deployer.FAILED = 'task-board-preview-purchase-v19-failed'

if __name__ == '__main__':
    try:
        deployer.main()
    except Exception:
        print('Local update did not complete; secret-bearing details withheld.', flush=True)
        raise SystemExit(1)
