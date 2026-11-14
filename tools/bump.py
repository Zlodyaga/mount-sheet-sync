"""Set the version of the addon or of the tray program everywhere it is written.

    python3 tools/bump.py addon 3.0.8
    python3 tools/bump.py program 2.10.3
    python3 tools/bump.py                 # show both versions and where they are written

The addon: "## Version" in MountSheetSync.toc and VERSION in MountSheetSync.lua (the tests read the version from the
toc). The program: Program.Version, AssemblyInfo.cs and the tests and pictures that name the version.
It does not touch the changelogs, CLAUDE.md or docs/reference/README.md — those are written by hand
(the list is printed at the end). Line endings of every file are kept.
"""
import io
import os
import re
import sys

ROOT = os.environ.get("MSS_BUMP_ROOT") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")   # the variable: for test_bump.py
VERSION = re.compile(r"^\d+\.\d+\.\d+$")


def read(path):
    return io.open(os.path.join(ROOT, path), encoding="utf-8", newline="").read()


def write(path, text):
    io.open(os.path.join(ROOT, path), "w", encoding="utf-8", newline="").write(text)


def addon_version():
    return re.search(r"^## Version: (\S+)", read("MountSheetSync.toc"), re.M).group(1)


def program_version():
    return re.search(r'public const string Version = "([^"]+)"', read("program/Program.cs")).group(1)


def replace(path, pattern, text, count):
    """Replace exactly `count` matches (None: at least one) and say what was done."""
    old = read(path)
    new, n = re.subn(pattern, text, old, flags=re.M)
    if (count is None and n == 0) or (count is not None and n != count):
        sys.exit("%s: expected %s place(s) for %r, found %d — nothing written there" % (path, count or "some", pattern, n))
    if new != old:
        write(path, new)
    print("  %-34s %d place(s)" % (path, n))


def main():
    if len(sys.argv) == 1:
        print("addon   %s   MountSheetSync.toc, MountSheetSync.lua" % addon_version())
        print("program %s   program/Program.cs, program/AssemblyInfo.cs, tools/tests/e2e_install.py, tools/tests/e2e_update.py,"
              " tools/screens/program.py" % program_version())
        return 0
    if len(sys.argv) != 3 or sys.argv[1] not in ("addon", "program") or not VERSION.match(sys.argv[2]):
        sys.exit(__doc__)
    what, new = sys.argv[1], sys.argv[2]
    if what == "addon":
        old = addon_version()
        print("addon %s -> %s" % (old, new))
        replace("MountSheetSync.toc", r"^## Version: \S+", "## Version: " + new, 1)
        replace("MountSheetSync.lua", r'^local VERSION = "[^"]+"', 'local VERSION = "%s"' % new, 1)
        print("By hand: CHANGELOG-addon.md (## %s), CLAUDE.md («Состояние»), docs/reference/README.md;" % new)
        print("a feature that needs a newer program: MIN_PROGRAM in MountSheetSync.lua.")
    else:
        old = program_version()
        print("program %s -> %s" % (old, new))
        q = re.escape(old)
        replace("program/Program.cs", r'public const string Version = "%s"' % q, 'public const string Version = "%s"' % new, 1)
        replace("program/AssemblyInfo.cs", q, new, 3)
        replace("tools/tests/e2e_install.py", q, new, None)
        replace("tools/tests/e2e_update.py", q, new, None)
        replace("tools/screens/program.py", q, new, None)
        print("By hand: CHANGELOG.md (## %s), CLAUDE.md («Состояние»), docs/reference/README.md;" % new)
        print("a feature that needs a newer addon: Program.MinAddon.")
    return 0


sys.exit(main())
