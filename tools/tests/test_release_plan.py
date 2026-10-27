"""tools/release_plan.sh: which tags a release run makes (program / addon / both, release / alpha / beta).

  python3 test_release_plan.py
"""
import os, shutil, subprocess, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1200]))


repo = tempfile.mkdtemp(prefix="mss-plan-")
os.makedirs(os.path.join(repo, "program"))
os.makedirs(os.path.join(repo, "tools"))
shutil.copy(os.path.join(ROOT, "tools", "release_plan.sh"), os.path.join(repo, "tools"))
open(os.path.join(repo, "program", "Program.cs"), "w").write('        public const string Version = "2.9.3";\n')
open(os.path.join(repo, "MountSheetSync.toc"), "w").write("## Title: x\r\n## Version: 2.9.0\r\n")


def git(*a):
    subprocess.run(["git", "-C", repo] + list(a), check=True, capture_output=True,
                   env=dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t"))


git("init", "-q")
git("add", "-A")
git("commit", "-q", "-m", "x")
for t in ("v2.9.3-alpha1", "v2.9.3-alpha2", "addon-v2.9.0-beta1", "addon-v2.8.0"):
    git("tag", t)


def plan(**env):
    out = os.path.join(repo, "out.txt")
    open(out, "w").close()
    e = dict(os.environ, GITHUB_OUTPUT=out, **env)
    e.pop("GITHUB_EVENT_NAME", None) if "GITHUB_EVENT_NAME" not in env else None
    p = subprocess.run(["bash", os.path.join(repo, "tools", "release_plan.sh")], env=e, capture_output=True, text=True)
    vals = dict(line.split("=", 1) for line in open(out).read().splitlines() if "=" in line)
    return p.returncode, vals, p.stdout + p.stderr


code, v, _ = plan(GITHUB_EVENT_NAME="workflow_dispatch", WHAT="program", KIND="release")
check("program release: v2.9.3, no addon, not a pre-release", code == 0 and v["program"] == "true" and v["addon"] == "false"
      and v["program_tag"] == "v2.9.3" and v["prerelease"] == "false" and v["suffix"] == "", v)
code, v, _ = plan(GITHUB_EVENT_NAME="workflow_dispatch", WHAT="addon", KIND="release")
check("addon release: addon-v2.9.0 only (versions from the toc)", code == 0 and v["program"] == "false" and v["addon_tag"] == "addon-v2.9.0", v)
code, v, _ = plan(GITHUB_EVENT_NAME="workflow_dispatch", WHAT="both", KIND="alpha")
check("both, alpha: the next free numbers — v2.9.3-alpha3 and addon-v2.9.0-alpha1, pre-release", code == 0
      and v["program_tag"] == "v2.9.3-alpha3" and v["addon_tag"] == "addon-v2.9.0-alpha1" and v["suffix"] == "alpha3" and v["prerelease"] == "true", v)
code, v, _ = plan(GITHUB_EVENT_NAME="workflow_dispatch", WHAT="addon", KIND="beta")
check("addon beta after beta1: beta2", code == 0 and v["addon_tag"] == "addon-v2.9.0-beta2", v)
git("tag", "v2.9.3")
code, v, out = plan(GITHUB_EVENT_NAME="workflow_dispatch", WHAT="program", KIND="release")
check("program 2.9.3 already released: refused, asks to raise the version", code != 0 and "raise Program.Version" in out, out)
code, v, _ = plan(GITHUB_EVENT_NAME="push", GITHUB_REF_NAME="addon-v2.9.0-alpha2")
check("pushed addon tag by hand: addon only", code == 0 and v["addon"] == "true" and v["program"] == "false" and v["addon_tag"] == "addon-v2.9.0-alpha2", v)
code, v, _ = plan(GITHUB_EVENT_NAME="push", GITHUB_REF_NAME="v2.9.3-beta1")
check("pushed program test tag: program only, suffix beta1, pre-release", code == 0 and v["program"] == "true" and v["addon"] == "false"
      and v["suffix"] == "beta1" and v["prerelease"] == "true", v)
code, v, out = plan(GITHUB_EVENT_NAME="push", GITHUB_REF_NAME="addon-v2.8.5")
check("a tag that does not match the toc: refused", code != 0 and "MountSheetSync.toc has 2.9.0" in out, out)
code, v, out = plan(GITHUB_EVENT_NAME="push", GITHUB_REF_NAME="v2.9.3-rc1")
check("an unknown suffix: refused", code != 0, out)

shutil.rmtree(repo, ignore_errors=True)
failed = [n for n, ok in checks if not ok]
print("%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
raise SystemExit(1 if failed else 0)
