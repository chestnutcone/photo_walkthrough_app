#!/usr/bin/env python3
"""Host-side helper for on-device e2e tests (the emulator reaches it at http://10.0.2.2:5090). Development only.

  GET /code            latest sign-in code printed by the API's ConsoleEmailSender (.local/api.log)
  GET /suspend?handle= suspends the account in the local Postgres container (what an admin ban does)
  GET /health
Bound to 0.0.0.0 on a throwaway dev sandbox; it can only read a log and flip one column, and must never be exposed.
"""
import http.server
import os
import re
import subprocess
import sys
import urllib.parse

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
LOG = os.path.join(ROOT, ".local", "api.log")
HANDLE = re.compile(r"^[a-z0-9_]{3,20}$")


class H(http.server.BaseHTTPRequestHandler):
    def _send(self, code, body):
        self.send_response(code)
        self.send_header("Content-Type", "text/plain")
        self.end_headers()
        self.wfile.write(body.encode())

    def do_GET(self):
        u = urllib.parse.urlparse(self.path)
        q = urllib.parse.parse_qs(u.query)
        if u.path == "/health":
            return self._send(200, "ok")
        if u.path == "/code":
            codes = re.findall(r"\[dev email\] sign-in code: (\d{6})", open(LOG, errors="ignore").read())
            return self._send(200, codes[-1]) if codes else self._send(404, "no code")
        if u.path == "/suspend":
            handle = q.get("handle", [""])[0]
            if not HANDLE.match(handle):
                return self._send(400, "bad handle")
            sql = f"update users set suspended_at = now() where id = (select user_id from profiles where handle = '{handle}')"
            r = subprocess.run(["docker", "exec", "vela-e2e-pg", "psql", "-U", "vela", "-d", "vela", "-tAc", sql], capture_output=True, text=True)
            return self._send(200 if r.returncode == 0 else 500, r.stdout + r.stderr)
        self._send(404, "not found")

    def log_message(self, *a):
        pass


if __name__ == "__main__":
    http.server.ThreadingHTTPServer(("0.0.0.0", int(sys.argv[1]) if len(sys.argv) > 1 else 5090), H).serve_forever()
