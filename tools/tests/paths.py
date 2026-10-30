"""Where things are in the repository, and where the tests put their scratch files (MSS_TEST_TMP)."""
import locale
import os
import tempfile

# Lua's string.lower works byte by byte through the C locale. Python on Windows switches LC_CTYPE to the system
# code page (cp1251 on a Russian Windows), where UTF-8 bytes of Cyrillic count as letters and get "lowered" into
# garbage. The game runs Lua in the "C" locale — so do the tests (every addon test imports this module first).
# Windows only: on Linux the locale is UTF-8 (Lua leaves those bytes alone), and "C" there would make Python's
# default encoding ASCII — the UI tests that read Russian output broke on it.
if os.name == "nt":
    locale.setlocale(locale.LC_CTYPE, "C")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
ADDON_DIR = ROOT                                   # the addon lives at the repository root (MountSheetSync.toc)
PROGRAM = os.path.join(ROOT, "program")
CODE_GS = os.path.join(ROOT, "script", "Code.gs")
HOSTS = os.path.join(ROOT, "tools", "cs_host")
FIXTURES = os.path.join(HERE, "fixtures")


def work(name):
    return os.path.join(os.environ.get("MSS_TEST_TMP") or os.path.join(tempfile.gettempdir(), "mss-tests"), name)
