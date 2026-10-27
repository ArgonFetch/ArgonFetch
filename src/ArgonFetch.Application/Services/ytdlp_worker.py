# Keeps yt-dlp imported between lookups: starting it costs more than most lookups take.
# One JSON request per line on stdin; one "<id> ok|err <payload>" line per answer on stdout.
import json
import sys
import threading
from concurrent.futures import ThreadPoolExecutor

library = sys.argv[1]
sys.path.insert(0, library)

import yt_dlp  # noqa: E402

protocol = sys.stdout
sys.stdout = sys.stderr  # nothing yt-dlp prints may land between answers
lock = threading.Lock()


def reply(request_id, ok, payload):
    with lock:
        protocol.write(f"{request_id} {'ok' if ok else 'err'} {payload}\n")
        protocol.flush()


def lookup(request):
    # No JS runtime: with one, YouTube is asked as its TV app too, and those URLs answer a
    # server's address with 403. Without, yt-dlp keeps to the client the binary has always used.
    options = {"quiet": True, "no_warnings": True, "noprogress": True, "js_runtimes": {}}
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
