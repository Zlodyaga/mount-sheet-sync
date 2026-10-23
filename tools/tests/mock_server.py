"""Mock of a deployed Apps Script web app.

POST /macros/s/<id>/exec  -> 302 to /macros/echo?user_content_key=<k>  (like Google)
GET  /macros/echo?...     -> runs Code.gs (via gas_harness.js) on the workbook, returns JSON
POST /html/s/x/exec       -> 200 HTML page (deployment not shared with "Anyone")
anything else             -> 404
"""
import json
import os
import subprocess
import sys
import tempfile
import uuid
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import parse_qs, urlparse

PORT = int(sys.argv[1])
WORKBOOK = sys.argv[2]
LOG = sys.argv[3]
HERE = os.path.dirname(os.path.abspath(__file__))
STORE = {}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def _send(self, code, body, ctype):
        data = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        with open(LOG, "a", encoding="utf-8") as f:
            f.write("POST %s ctype=%s\n" % (self.path, self.headers.get("Content-Type")))
        if self.path.startswith("/html/"):
            return self._send(200, "<!DOCTYPE html><html><body>Sign in - Google Accounts</body></html>", "text/html; charset=utf-8")
        if not (self.path.startswith("/macros/s/") and self.path.endswith("/exec")):
            return self._send(404, "<html>Not Found</html>", "text/html")
        key = uuid.uuid4().hex
        STORE[key] = body
        self.send_response(302)
        self.send_header("Location", "http://127.0.0.1:%d/macros/echo?user_content_key=%s" % (PORT, key))
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self):
        u = urlparse(self.path)
        if u.path != "/macros/echo":
            return self._send(404, "<html>Not Found</html>", "text/html")
        key = parse_qs(u.query)["user_content_key"][0]
        body = STORE.pop(key)
        with tempfile.NamedTemporaryFile("wb", delete=False, suffix=".json") as tf:
            tf.write(body)
        try:
            out = subprocess.run(["node", os.path.join(HERE, "gas_harness.js"), WORKBOOK, tf.name],
                                 capture_output=True, check=True).stdout.decode("utf-8")
        finally:
            os.unlink(tf.name)
        with open(LOG, "a", encoding="utf-8") as f:
            f.write("GET echo -> %s\n" % out[:300])
        self._send(200, out, "application/json; charset=utf-8")


HTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
