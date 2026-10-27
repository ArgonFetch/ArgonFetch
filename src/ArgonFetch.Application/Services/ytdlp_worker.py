# Keeps yt-dlp imported between lookups: starting it costs more than most lookups take.
# One JSON request per line on stdin; one "<id> ok|err <payload>" line per answer on stdout.
import json
import os
import sys
import threading
from concurrent.futures import ThreadPoolExecutor

library = sys.argv[1]
sys.path.insert(0, library)
# pip --target puts the bundled Deno here, where yt-dlp would not look for it.
os.environ["PATH"] = os.path.join(library, "bin") + os.pathsep + os.environ.get("PATH", "")

import yt_dlp  # noqa: E402

protocol = sys.stdout
sys.stdout = sys.stderr  # nothing yt-dlp prints may land between answers
lock = threading.Lock()


def reply(request_id, ok, payload):
    with lock:
        protocol.write(f"{request_id} {'ok' if ok else 'err'} {payload}\n")
        protocol.flush()


def lookup(request):
    options = {"quiet": True, "no_warnings": True, "noprogress": True}
    if request.get("proxy"):
        options["proxy"] = request["proxy"]
    if request.get("cookies"):
        options["cookiefile"] = request["cookies"]
    if request.get("flat"):
        options["extract_flat"] = "in_playlist"

    try:
        with yt_dlp.YoutubeDL(options) as ydl:
            info = ydl.sanitize_info(ydl.extract_info(request["url"], download=False))
        reply(request["id"], True, json.dumps(info))
    except BaseException as error:  # yt-dlp's own message, as the binary would print it
        reply(request["id"], False, " ".join(str(error).split()))


reply(0, True, yt_dlp.version.__version__)

with ThreadPoolExecutor(16) as pool:
    for line in sys.stdin:
        if line.strip():
            pool.submit(lookup, json.loads(line))
