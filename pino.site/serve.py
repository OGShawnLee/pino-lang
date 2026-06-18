#!/usr/bin/env python3
"""Dev server for pino.site — no-cache headers + correct WASM MIME type."""
import http.server, socketserver, os

PORT = 3000

class NoCacheHandler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
        self.send_header("Pragma", "no-cache")
        super().end_headers()

    def guess_type(self, path):
        mime, _ = super().guess_type(path), None
        if str(path).endswith(".wasm"):
            return "application/wasm"
        return mime

os.chdir(os.path.dirname(os.path.abspath(__file__)))
with socketserver.TCPServer(("", PORT), NoCacheHandler) as httpd:
    print(f"[Pino] Dev server running at http://localhost:{PORT}")
    httpd.serve_forever()
