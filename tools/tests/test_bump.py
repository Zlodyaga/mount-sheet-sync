"""tools/bump.py on a copy of the files that carry a version: every place is set, line endings stay, a wrong version
or a file that lost its version line stops the script.

    python3 test_bump.py
"""
import os
import shutil
import subprocess
import sys
import paths

ROOT = paths.ROOT
W = paths.work("bump")
shutil.rmtree(W, ignore_errors=True)
FILES = ["MountSheetSync.toc", "MountSheetSync.lua", "program/Program.cs", "program/AssemblyInfo.cs",
         "tools/tests/e2e_install.py", "tools/tests/e2e_update.py", "tools/screens/program.py"]
for f in FILES:
    os.makedirs(os.path.dirname(os.path.join(W, f)), exist_ok=True)
    shutil.copyfile(os.path.join(ROOT, f), os.path.join(W, f))
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:800]))


def bump(*args):
    p = subprocess.run([sys.executable, "-X", "utf8", os.path.join(ROOT, "tools", "bump.py")] + list(args),
                       env=dict(os.environ, MSS_BUMP_ROOT=W), capture_output=True)
    return p.returncode, (p.stdout + p.stderr).decode("utf-8", "replace")


def raw(f):
    return open(os.path.join(W, f), "rb").read()


def crlf(b):
    return b.count(b"\r\n"), b.count(b"\n")


before = {f: raw(f) for f in FILES}
code, out = bump()
old_addon = out.split()[1]
old_program = out.split("program")[1].split()[0]
check("without arguments: both versions and where they are written", code == 0 and old_addon.count(".") == 2 and old_program.count(".") == 2, out)

code, out = bump("addon", "9.8.7")
toc, core = raw("MountSheetSync.toc").decode("utf-8"), raw("MountSheetSync.lua").decode("utf-8")
check("addon: the toc and the core get the version", code == 0 and "## Version: 9.8.7" in toc and 'local VERSION = "9.8.7"' in core, out)
check("…and nothing else in them changes: same line endings, same size but for the number",
      crlf(raw("MountSheetSync.toc")) == crlf(before["MountSheetSync.toc"]) and crlf(raw("MountSheetSync.lua")) == crlf(before["MountSheetSync.lua"])
      and len(raw("MountSheetSync.lua")) - len(before["MountSheetSync.lua"]) == len("9.8.7") - len(old_addon))
check("…the program's files are untouched", all(raw(f) == before[f] for f in FILES[2:]))

code, out = bump("program", "9.8.6")
cs = raw("program/Program.cs").decode("utf-8")
asm = raw("program/AssemblyInfo.cs").decode("utf-8")
check("program: Program.Version and the three lines of AssemblyInfo.cs", code == 0 and 'Version = "9.8.6"' in cs and asm.count("9.8.6") == 3 and old_program not in asm, out)
check("…the tests and the picture that name the version follow",
      all(old_program.encode() not in raw(f) and b"9.8.6" in raw(f) for f in FILES[4:]), out)
check("…line endings stay", all(crlf(raw(f))[0] == crlf(before[f])[0] for f in FILES[2:]))

code, out = bump("addon", "3.1")
check("a version that is not three numbers is refused", code != 0 and 'local VERSION = "9.8.7"' in raw("MountSheetSync.lua").decode("utf-8"), out[:200])
code, out = bump("game", "1.2.3")
check("…and so is an unknown part", code != 0)
open(os.path.join(W, "MountSheetSync.lua"), "wb").write(raw("MountSheetSync.lua").replace(b'local VERSION = "9.8.7"', b"local VERSION = nil"))
code, out = bump("addon", "9.9.9")
check("a file without its version line stops the script with its name", code != 0 and "MountSheetSync.lua" in out, out[:300])

failed = [nm for nm, ok in checks if not ok]
print("\n%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
