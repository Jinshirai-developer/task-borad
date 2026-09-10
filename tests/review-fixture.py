"""v22 uses the schema-only isolated transport fixture, never existing user rows."""
import importlib.util
from pathlib import Path
import sys

spec = importlib.util.spec_from_file_location("transport_fixture", Path(__file__).with_name("transport-fixture.py"))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
fixture.IMAGE = "task-board:review-v22"

if __name__ == "__main__":
    if sys.argv[1:] == ["start"]:
        fixture.start()
    elif sys.argv[1:] == ["stop"]:
        fixture.stop()
    else:
        raise SystemExit("Usage: review-fixture.py start|stop")
