"""Where things are in the repository, and where the tests put their scratch files (MSS_TEST_TMP)."""
import os
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
ADDON_DIR = ROOT                                   # the addon lives at the repository root (MountSheetSync.toc)
PROGRAM = os.path.join(ROOT, "program")
CODE_GS = os.path.join(ROOT, "script", "Code.gs")
HOSTS = os.path.join(ROOT, "tools", "cs_host")
FIXTURES = os.path.join(HERE, "fixtures")


def work(name):
    return os.path.join(os.environ.get("MSS_TEST_TMP") or os.path.join(tempfile.gettempdir(), "mss-tests"), name)
