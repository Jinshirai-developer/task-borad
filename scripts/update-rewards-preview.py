"""Update only the existing localhost:5097 app; retain DB, keys, config and rollback.

Task-specific deployment of task-board:rewards-v14. Never changes account data.
Backups contain secrets and must stay in the ignored, mode-700 .local directory.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.request

REPO = Path(__file__).resolve().parents[1]
CURRENT = "task-board-preview"
PREVIOUS = "task-board-preview-before-rewards-v14"
IMAGE = "task-board:rewards-v14"


def run(*args):
    result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        # Do not print inspect data, env files, or a command containing configuration.
        raise RuntimeError(f"{args[0]} command failed (exit {result.returncode})")
    return result.stdout


def sql(statement):
    return run("docker", "exec", "task-board-preview-db", "psql", "-X", "-U", "preview",
               "-d", "preview", "-At", "-v", "ON_ERROR_STOP=1", "-c", statement).decode().strip()


def digest():
    names = sql("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename").splitlines()
    result = {}
    for name in names:
        identifier = '"' + name.replace('"', '""') + '"'
        result[name] = sql("SELECT count(*) || ':' || md5(coalesce(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM " + identifier + " t")
    return result


def main():
    os.umask(0o077)
    config = json.loads(run("docker", "inspect", CURRENT))[0]
    names = run("docker", "ps", "-a", "--format", "{{.Names}}").decode().splitlines()
    assert PREVIOUS not in names, "Rollback container already exists; inspect before another deployment"
    assert config["State"]["Running"]
    assert config["Config"]["Image"] == "task-board:cat-cutout-v13"
    assert config["Config"]["User"] == "1654"
    assert list(config["NetworkSettings"]["Networks"]) == ["task-board-preview-net"]
    assert config["HostConfig"]["PortBindings"] == {"8080/tcp": [{"HostIp": "127.0.0.1", "HostPort": "5097"}]}
    assert [(m["Type"], m.get("Name"), m["Destination"]) for m in config["Mounts"]] == [("volume", "task-board-preview-keys", "/keys")]
    assert config["HostConfig"]["RestartPolicy"]["Name"] == "no"
    run("docker", "image", "inspect", IMAGE)
    backup = Path(tempfile.mkdtemp(prefix="rewards-20260908.", dir=REPO / ".local/backups"))
    (backup / "runtime-inspect.json").write_text(json.dumps(config))
    (backup / "runtime.env").write_text("\n".join(config["Config"]["Env"]) + "\n")
    stopped, renamed, created = False, False, False
    try:
        run("docker", "stop", CURRENT)
        stopped = True
        with (backup / "before-rewards.dump").open("xb") as output:
            result = subprocess.run(["docker", "exec", "task-board-preview-db", "pg_dump", "-U", "preview", "-d", "preview", "-Fc"], stdout=output, stderr=subprocess.PIPE)
            assert result.returncode == 0 and output.tell() > 0, "Database backup failed"
        run("docker", "cp", CURRENT + ":/keys", str(backup / "keys"))
        for path in (backup / "keys").rglob("*"):
            path.chmod(0o700 if path.is_dir() else 0o600)
        before = digest()
        run("docker", "rename", CURRENT, PREVIOUS)
        renamed = True
        run("docker", "run", "-d", "--name", CURRENT, "--network", "task-board-preview-net",
            "--user", config["Config"]["User"], "--restart", "no",
            "-p", "127.0.0.1:5097:8080", "-v", "task-board-preview-keys:/keys",
            "--env-file", str(backup / "runtime.env"), IMAGE)
        created = True
        for attempt in range(60):
            try:
                with urllib.request.urlopen("http://localhost:5097/health/ready", timeout=2) as response:
                    if response.status == 200:
                        break
            except Exception:
                time.sleep(.5)
        else:
            raise RuntimeError("Updated application did not become ready")
        after = digest()
        assert before == after, "Database changed across update; stop and inspect"
        current = json.loads(run("docker", "inspect", CURRENT))[0]
        assert sorted(current["Config"]["Env"]) == sorted(config["Config"]["Env"])
        files = ["index.html", "app.js", "pet-play.js", "pet-play.css", "pet-reward-art.js", "pet-reward-metrics.js"]
        files += [f"assets/pet/portfolio-cat-{pose}-v3.png" for pose in ("idle", "pet", "hat", "bow")]
        files += [f"assets/pet/portfolio-{species}-atlas-v2-alpha.png" for species in ("dog", "cat", "rabbit", "fox", "panda", "dragon")]
        files += [f"assets/pet/rewards-v2/{species}/lv-{level}-{kind}.png" for species in ("dog", "cat", "rabbit", "fox", "panda", "dragon") for level in range(1, 6) for kind in ("hat", "bow", "mat")]
        for file in files:
            with urllib.request.urlopen("http://localhost:5097/" + file) as response:
                assert hashlib.sha256(response.read()).digest() == hashlib.sha256((REPO / "frontend" / file).read_bytes()).digest(), file
        report = {"image": IMAGE, "url": "http://localhost:5097/", "backup": str(backup),
                  "rollback_container": PREVIOUS, "database_tables_unchanged": len(before),
                  "environment_preserved": True, "served_files_match": len(files)}
        (backup / "verification.json").write_text(json.dumps(report, indent=2))
        print(json.dumps(report, indent=2), flush=True)
    except Exception:
        if created:
            run("docker", "stop", CURRENT)
            run("docker", "rename", CURRENT, "task-board-preview-rewards-v14-failed")
        if renamed:
            run("docker", "rename", PREVIOUS, CURRENT)
        if stopped:
            run("docker", "start", CURRENT)
        raise


if __name__ == "__main__":
    main()
