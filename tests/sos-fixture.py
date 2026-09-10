"""v23 test image in the guarded schema-only, billing-disabled QA fixture."""
import importlib.util
from pathlib import Path
import sys

spec = importlib.util.spec_from_file_location("transport_fixture", Path(__file__).with_name("transport-fixture.py"))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
fixture.IMAGE = "task-board:sos-v23"

if __name__ == "__main__":
    assert sys.argv[1:] in (["start"], ["stop"])
    (fixture.start if sys.argv[1] == "start" else fixture.stop)()
