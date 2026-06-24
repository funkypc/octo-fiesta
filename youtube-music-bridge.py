#!/usr/bin/env python3
"""
YouTube Music Bridge Script
A thin wrapper around ytmusicapi for use by octo-fiesta (C#).

Accepts commands via command-line arguments, outputs JSON to stdout.
Errors are written as JSON objects with an "error" key to stderr.

Authentication:
  - Cookie-based: Set the YT_MUSIC_COOKIE env var or provide --cookie
  - OAuth: Set the YTMUSIC_OAUTH_CREDENTIALS env var or provide --oauth

Download strategy (in order):
  1. Innertube ANDROID client — direct API call, no JS runtime needed
  2. yt-dlp (if available) — with player_client fallbacks
  3. ytmusicapi get_song + urllib — last resort for auth'd users

Usage:
  python youtube-music-bridge.py <command> [args...]
  python youtube-music-bridge.py search-songs "queen" 20
  python youtube-music-bridge.py download-track dQw4w9WgXcQ FLAC /tmp/ytm
"""

import json
import os
import subprocess
import sys
import tempfile
import time
import hashlib
import urllib.request
import urllib.error
from http.cookies import SimpleCookie
from urllib.parse import parse_qs, urlparse, unquote
from typing import Optional

try:
    import requests
    _HAS_REQUESTS = True
except ImportError:
    _HAS_REQUESTS = False

try:
    from ytmusicapi import YTMusic
except ImportError as e:
    print(json.dumps({"error": f"ytmusicapi not installed: {e}"}), file=sys.stderr)
    sys.exit(1)

try:
    import yt_dlp
    _HAS_YTDLP = True
except ImportError:
    _HAS_YTDLP = False


_YTM_ORIGIN = "https://music.youtube.com"
_UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"

_ymusic: Optional[YTMusic] = None
_ymusic_noauth: Optional[YTMusic] = None
_headers_file: Optional[str] = None

_QUALITY_SPEC_MAP = {
    "FLAC": "bestaudio/best",
    "AAC_256": "bestaudio[abr<=256]/best",
    "AAC_192": "bestaudio[abr<=192]/best",
    "MP3_256": "bestaudio[abr<=256]/best",
    "MP3_320": "bestaudio/best",
    "MP3_128": "bestaudio[abr<=128]/best",
    "AAC_128": "bestaudio[abr<=128]/best",
    "AAC_64": "bestaudio[abr<=64]/best",
}


def acodec_to_ext(acodec: str) -> str:
    """Map an audio codec name to a file extension."""
    return {
        "opus": "opus", "mp4a": "m4a", "mp4a.40.2": "m4a", "mp4a.40.5": "m4a",
        "mp3": "mp3", "vorbis": "ogg", "flac": "flac",
    }.get(acodec, acodec)


def _has_ffmpeg() -> bool:
    try:
        subprocess.run(["ffmpeg", "-version"], capture_output=True, timeout=10)
        return True
    except (FileNotFoundError, subprocess.TimeoutExpired):
        return False


def _convert_webm_to_m4a(filepath: str, target_bitrate: int = 256000) -> str:
    """Convert a webm audio file to m4a (AAC) using ffmpeg. Returns new filepath."""
    if not os.path.splitext(filepath)[1].lower() in (".webm", ".opus"):
        return filepath

    m4a_path = os.path.splitext(filepath)[0] + ".m4a"
    bitrate_k = max(64, min(320, target_bitrate // 1000))
    result = subprocess.run(
        ["ffmpeg", "-y", "-i", filepath, "-vn", "-c:a", "aac", "-b:a", f"{bitrate_k}k", m4a_path],
        capture_output=True, timeout=120,
    )
    if result.returncode == 0 and os.path.exists(m4a_path):
        os.remove(filepath)
        return m4a_path
    print(f"[ffmpeg] webm->m4a conversion failed (rc={result.returncode}), keeping original", file=sys.stderr)
    return filepath


def _get_auth_value():
    """Get auth value from env vars or command args."""
    oauth_creds = os.environ.get("YTMUSIC_OAUTH_CREDENTIALS")
    if oauth_creds:
        return oauth_creds

    cookie = os.environ.get("YT_MUSIC_COOKIE")
    if cookie:
        return cookie

    for i, arg in enumerate(sys.argv):
        if arg == "--oauth" and i + 1 < len(sys.argv):
            return sys.argv[i + 1]
        if arg == "--cookie" and i + 1 < len(sys.argv):
            return sys.argv[i + 1]

    return None


def _get_sapisid_from_cookie(raw_cookie: str) -> str:
    """Extract __Secure-3PAPISID value from a raw cookie string."""
    cookie = SimpleCookie()
    cookie.load(raw_cookie.replace('"', ""))
    for key in ("__Secure-3PAPISID", "SAPISID", "__Secure-1PAPISID"):
        if key in cookie:
            return cookie[key].value
    return ""


def _make_sapisidhash(sapisid: str, origin: str = _YTM_ORIGIN) -> str:
    """Generate SAPISIDHASH authorization header value."""
    sha_1 = hashlib.sha1()
    unix_timestamp = str(int(time.time()))
    sha_1.update((unix_timestamp + " " + sapisid + " " + origin).encode("utf-8"))
    return f"SAPISIDHASH {unix_timestamp}_{sha_1.hexdigest()}"


def _parse_auth_to_cookie_string(auth_value: str) -> Optional[str]:
    """Parse any auth format into a raw cookie string for use in HTTP headers.
    Returns None if no cookies can be extracted."""
    if not auth_value:
        return None

    # File path
    if os.path.isfile(auth_value):
        try:
            with open(auth_value) as f:
                data = json.load(f)
            if isinstance(data, dict):
                if "cookie" in data or "Cookie" in data:
                    return data.get("cookie", data.get("Cookie", ""))
                if "authorization" in data:
                    return "; ".join(f"{k}={v}" for k, v in data.items()
                                     if k.lower() not in ("authorization", "user-agent", "origin", "x-goog-authuser"))
            return None
        except (json.JSONDecodeError, OSError):
            return None

    # JSON string
    if auth_value.startswith("{"):
        try:
            data = json.loads(auth_value)
            if isinstance(data, dict):
                if "cookie" in data or "Cookie" in data:
                    return data.get("cookie", data.get("Cookie", ""))
                if "authorization" in data:
                    return "; ".join(f"{k}={v}" for k, v in data.items()
                                     if k.lower() not in ("authorization", "user-agent", "origin", "x-goog-authuser"))
            return None
        except json.JSONDecodeError:
            pass

    # Raw cookie string
    if "=" in auth_value and not auth_value.startswith("{"):
        return auth_value

    return None


def _cookie_dict_to_headers(cookie_dict: dict) -> dict:
    """Convert a cookie name/value dict into ytmusicapi browser headers format."""
    if "Cookie" in cookie_dict or "cookie" in cookie_dict:
        out = dict(cookie_dict)
        if "user-agent" not in out and "User-Agent" not in out:
            out["user-agent"] = _UA
        if "origin" not in out and "x-origin" not in out:
            out["origin"] = _YTM_ORIGIN
        if "x-goog-authuser" not in out:
            out["x-goog-authuser"] = "0"
        if "authorization" not in out:
            raw_cookie = out.get("cookie", out.get("Cookie", ""))
            sapisid = _get_sapisid_from_cookie(raw_cookie)
            if sapisid:
                out["authorization"] = _make_sapisidhash(sapisid)
        return out

    cookie_str = "; ".join(f"{k}={v}" for k, v in cookie_dict.items())
    headers = {
        "cookie": cookie_str,
        "user-agent": _UA,
        "origin": _YTM_ORIGIN,
        "x-goog-authuser": "0",
    }
    sapisid = _get_sapisid_from_cookie(cookie_str)
    if sapisid:
        headers["authorization"] = _make_sapisidhash(sapisid)
    return headers


def _save_headers(headers: dict) -> str:
    """Save browser headers dict to a temp JSON file and return the path."""
    global _headers_file
    fd, path = tempfile.mkstemp(suffix=".json", prefix="ytm_headers_")
    with os.fdopen(fd, "w") as f:
        json.dump(headers, f)
    _headers_file = path
    return path


def _cookie_string_to_headers(cookie_str: str) -> dict:
    """Parse a raw cookie header string into the browser headers format."""
    pairs = {}
    for part in cookie_str.split(";"):
        part = part.strip()
        if "=" in part:
            k, v = part.split("=", 1)
            pairs[k] = v
    return _cookie_dict_to_headers(pairs)


def _create_ytmusic_instance(needs_auth=False):
    """Create a YTMusic instance with proper auth handling."""
    auth_value = _get_auth_value()

    if auth_value is None:
        if needs_auth:
            raise RuntimeError(
                "No authentication configured. Set YT_MUSIC_COOKIE "
                "(file path to browser-headers JSON) or "
                "YTMUSIC_OAUTH_CREDENTIALS."
            )
        return YTMusic()

    if os.path.isfile(auth_value):
        try:
            with open(auth_value) as f:
                data = json.load(f)
        except (json.JSONDecodeError, OSError):
            return YTMusic(auth=auth_value)

        if not isinstance(data, dict):
            return YTMusic(auth=auth_value)
        if "access_token" in data or "refresh_token" in data:
            return YTMusic(auth=auth_value)
        if "authorization" in data:
            return YTMusic(auth=auth_value)

        headers = _cookie_dict_to_headers(data)
        path = _save_headers(headers)
        return YTMusic(auth=path)

    try:
        data = json.loads(auth_value)
        if isinstance(data, dict):
            if "access_token" in data or "refresh_token" in data:
                path = _save_headers(data)
                return YTMusic(auth=path)
            if "authorization" in data:
                path = _save_headers(data)
                return YTMusic(auth=path)
            headers = _cookie_dict_to_headers(data)
            path = _save_headers(headers)
            return YTMusic(auth=path)
    except json.JSONDecodeError:
        pass

    try:
        headers = _cookie_string_to_headers(auth_value)
        path = _save_headers(headers)
        return YTMusic(auth=path)
    except Exception:
        pass

    if needs_auth:
        return YTMusic(auth=auth_value)
    return YTMusic()


def get_ytmusic(needs_auth=False):
    """Get a YTMusic client instance."""
    global _ymusic
    if _ymusic is not None:
        return _ymusic
    try:
        _ymusic = _create_ytmusic_instance(needs_auth=needs_auth)
        return _ymusic
    except Exception:
        if needs_auth:
            raise
        global _ymusic_noauth
        if _ymusic_noauth is not None:
            return _ymusic_noauth
        _ymusic_noauth = YTMusic()
        _ymusic = None
        return _ymusic_noauth
    _ymusic = _create_ytmusic_instance(needs_auth=needs_auth)
    return _ymusic


# ---------------------------------------------------------------------------
# Command helpers
# ---------------------------------------------------------------------------

def ok(data: dict):
    print(json.dumps({"ok": True, "result": data}, ensure_ascii=False))


def fail(msg: str, code: int = 1):
    print(json.dumps({"ok": False, "error": msg}, ensure_ascii=False), file=sys.stderr)
    sys.exit(code)


def _map_artist_item(a):
    if isinstance(a, str):
        return {"name": a, "id": None}
    if isinstance(a, dict):
        return {"name": a.get("name", ""), "id": a.get("id")}
    return {"name": str(a), "id": None}


def _map_album_field(t: dict):
    alb = t.get("album")
    if alb is None:
        return None
    if isinstance(alb, str):
        return {"name": alb, "id": None}
    return {"name": alb.get("name", ""), "id": alb.get("id")}


def _map_track(t: dict) -> dict:
    return {
        "videoId": t.get("videoId"),
        "title": t.get("title", ""),
        "artists": [_map_artist_item(a) for a in t.get("artists") or []],
        "album": _map_album_field(t),
        "duration": t.get("duration"),
        "durationSeconds": t.get("duration_seconds"),
        "thumbnails": t.get("thumbnails") or [],
        "isExplicit": t.get("isExplicit", False),
        "videoType": t.get("videoType", "song"),
        "year": t.get("year"),
        "trackNumber": t.get("trackNumber"),
        "trackCount": t.get("trackCount"),
    }


def _map_album(a: dict) -> dict:
    return {
        "browseId": a.get("browseId"),
        "title": a.get("title", ""),
        "artists": [_map_artist_item(ar) for ar in a.get("artists") or []],
        "year": a.get("year"),
        "trackCount": a.get("trackCount"),
        "thumbnails": a.get("thumbnails") or [],
        "type": a.get("type", "Album"),
    }


def _map_artist(ar: dict) -> dict:
    return {
        "browseId": ar.get("browseId"),
        "name": ar.get("name", ""),
        "thumbnails": ar.get("thumbnails") or [],
        "albumCount": ar.get("albumCount"),
    }


# ---------------------------------------------------------------------------
# ytmusicapi-based download (primary strategy — uses authenticated session)
# ---------------------------------------------------------------------------

def _get_download_headers():
    """Build HTTP headers for CDN download, using auth cookies and SAPISIDHASH."""
    headers = {
        "User-Agent": _UA,
        "Accept": "*/*",
        "Accept-Language": "en-US,en;q=0.9",
        "Referer": _YTM_ORIGIN + "/",
        "Origin": _YTM_ORIGIN,
    }
    auth_value = _get_auth_value()
    if not auth_value:
        return headers
    cookie_str = _parse_auth_to_cookie_string(auth_value)
    if cookie_str:
        headers["Cookie"] = cookie_str
    sapisid = _get_sapisid_from_cookie(cookie_str) if cookie_str else ""
    if sapisid:
        headers["Authorization"] = _make_sapisidhash(sapisid, _YTM_ORIGIN)
    return headers


def _download_track_ytmusicapi(video_id: str, quality: str, output_dir: str):
    """Download a track using ytmusicapi's authenticated session.
    
    This uses ytmusicapi's get_song() to get stream info, then downloads
    via the same requests session that ytmusicapi uses (with all auth
    headers and cookies). This avoids the need for Node.js/yt-dlp.
    
    For premium content, ytmusicapi's authenticated WEB_REMIX session
    returns streaming URLs that work when downloaded with the same
    session headers (Cookies + SAPISIDHASH + Referer).
    """
    if not _HAS_REQUESTS:
        raise RuntimeError("Python 'requests' package is required for downloading")

    ytm = get_ytmusic(needs_auth=True)

    # Get streaming data via ytmusicapi (uses the authenticated session)
    try:
        song_info = ytm.get_song(video_id)
    except Exception as e:
        raise RuntimeError(f"ytmusicapi get_song failed: {e}")

    if not song_info or "streamingData" not in song_info:
        raise RuntimeError("ytmusicapi returned no streamingData")

    sd = song_info["streamingData"]

    # AdaptiveFormats has audio-only streams; formats may have audio+video
    formats = sd.get("adaptiveFormats", []) + sd.get("formats", [])
    audio_formats = [f for f in formats if f.get("mimeType", "").startswith("audio/")]

    if not audio_formats:
        raise RuntimeError("No audio formats available")

    # Sort by bitrate descending
    audio_formats.sort(key=lambda f: int(f.get("bitrate", 0)), reverse=True)

    # Select format based on quality
    quality_upper = (quality or "FLAC").upper()
    selected = None

    if quality_upper in ("MP3_128", "AAC_64"):
        for f in audio_formats:
            if int(f.get("bitrate", 0)) <= 160000:
                selected = f
                break

    if selected is None:
        # Prefer formats with a direct URL (no cipher needed)
        for f in audio_formats:
            if f.get("url"):
                selected = f
                break

    if selected is None:
        # Fallback: try ciphered formats (extract URL from signatureCipher)
        for f in audio_formats:
            cipher = f.get("signatureCipher") or f.get("cipher", "")
            if cipher and "url=" in cipher:
                url_part = ""
                for part in cipher.split("&"):
                    if part.startswith("url="):
                        url_part = unquote(part[4:])
                        break
                if url_part:
                    f["url"] = url_part
                    selected = f
                    break

    if selected is None:
        raise RuntimeError("No downloadable audio format found")

    url = selected.get("url")
    if not url:
        raise RuntimeError("Selected format has no URL")

    mime = selected.get("mimeType", "audio/mp4")
    bitrate = int(selected.get("bitrate", 0))
    duration_ms = int(song_info.get("videoDetails", {}).get("lengthSeconds", 0)) * 1000

    codec = mime.split(";")[0].replace("audio/", "").strip().lower()
    ext_map = {"mp4": ".m4a", "webm": ".webm", "opus": ".opus", "mp3": ".mp3", "flac": ".flac", "ogg": ".ogg"}
    ext = ext_map.get(codec, ".m4a")

    filepath = os.path.join(output_dir, f"ytm_{video_id}{ext}")

    # Use ytmusicapi's requests session which has all the auth headers
    # This is THE KEY: the same session that authenticated successfully
    # is used for downloading, so CDN requests include proper cookies
    # and SAPISIDHASH headers.
    session = ytm._session if hasattr(ytm, '_session') else requests.Session()

    # Build the auth headers from our cookie config
    download_headers = _get_download_headers()

    try:
        resp = session.get(url, headers=download_headers, stream=True, timeout=120)
        resp.raise_for_status()

        content_type = resp.headers.get("Content-Type", "")
        with open(filepath, "wb") as f:
            for chunk in resp.iter_content(chunk_size=65536):
                if chunk:
                    f.write(chunk)
    except requests.exceptions.HTTPError as e:
        # If 403, the n-param throttling is at play — try with &rn= hack
        if e.response is not None and e.response.status_code == 403:
            # Try adding &ratebypass=yes which sometimes bypasses n-param
            if "?" in url:
                retry_url = url + "&ratebypass=yes"
            else:
                retry_url = url + "?ratebypass=yes"
            try:
                resp = session.get(retry_url, headers=download_headers, stream=True, timeout=120)
                resp.raise_for_status()
                with open(filepath, "wb") as f:
                    for chunk in resp.iter_content(chunk_size=65536):
                        if chunk:
                            f.write(chunk)
            except Exception as retry_e:
                if os.path.exists(filepath):
                    os.remove(filepath)
                raise RuntimeError(f"Download HTTP 403, retry also failed: {retry_e}")
        else:
            if os.path.exists(filepath):
                os.remove(filepath)
            raise RuntimeError(f"Download HTTP error: {e}")
    except Exception as e:
        if os.path.exists(filepath):
            os.remove(filepath)
        raise RuntimeError(f"Download failed: {e}")

    if not os.path.exists(filepath):
        raise RuntimeError(f"Downloaded file not found at {filepath}")

    file_size = os.path.getsize(filepath)
    if file_size < 1024:
        os.remove(filepath)
        raise RuntimeError(f"Downloaded file too small ({file_size} bytes), likely an error response")

    if _has_ffmpeg():
        filepath = _convert_webm_to_m4a(filepath, bitrate)

    actual_mime = {
        "mp4": "audio/mp4", "webm": "audio/webm; codecs=opus", "opus": "audio/webm; codecs=opus",
        "mp3": "audio/mp3", "flac": "audio/flac", "ogg": "audio/ogg",
    }.get(codec, mime)

    return {
        "filepath": filepath,
        "mimeType": actual_mime,
        "bitrate": bitrate,
        "codec": codec,
        "quality": selected.get("quality", ""),
        "durationMs": duration_ms,
    }


def _download_track_innertube(video_id: str, quality: str, output_dir: str):
    """Download using direct innertube API calls.
    Delegates to _download_track_ytmusicapi which uses the authenticated session."""
    return _download_track_ytmusicapi(video_id, quality, output_dir)


# ---------------------------------------------------------------------------
# yt-dlp helpers
# ---------------------------------------------------------------------------

def _write_netscape_cookie_file(raw_cookie: str, user_agent: str = "") -> str:
    from http.cookies import SimpleCookie as SC
    fd, path = tempfile.mkstemp(suffix=".txt", prefix="ytdlp_cookie_")
    with os.fdopen(fd, "w") as f:
        f.write("# Netscape HTTP Cookie File\n")
        cookie = SC()
        cookie.load(raw_cookie.replace('"', ""))
        for key, morsel in cookie.items():
            f.write(f".youtube.com\tTRUE\t/\tTRUE\t0\t{key}\t{morsel.value}\n")
    return path


class _YtdlpLogger:
    def debug(self, msg): pass
    def info(self, msg): pass
    def warning(self, msg): print(f"[yt-dlp] {msg}", file=sys.stderr, flush=True)
    def error(self, msg): print(f"[yt-dlp] ERROR: {msg}", file=sys.stderr, flush=True)


def _build_ytdlp_cookie_path(auth_value: str) -> Optional[str]:
    data = None
    try:
        if os.path.isfile(auth_value):
            with open(auth_value) as f:
                data = json.load(f)
        elif auth_value.startswith("{"):
            data = json.loads(auth_value)
    except (json.JSONDecodeError, OSError):
        return _write_netscape_cookie_file(auth_value)

    if data and isinstance(data, dict):
        if "cookie" in data or "Cookie" in data:
            raw_cookie = data.get("cookie", data.get("Cookie", ""))
            ua = data.get("user-agent", data.get("User-Agent", ""))
            return _write_netscape_cookie_file(raw_cookie, ua)
        else:
            fd, path = tempfile.mkstemp(suffix=".txt", prefix="ytdlp_cookie_")
            with os.fdopen(fd, "w") as f:
                f.write("# Netscape HTTP Cookie File\n")
                for k, v in data.items():
                    f.write(f".youtube.com\tTRUE\t/\tTRUE\t0\t{k}\t{v}\n")
            return path

    if "=" in auth_value and not auth_value.startswith("{"):
        return _write_netscape_cookie_file(auth_value)
    return None


def _download_track_ytdlp(video_id: str, quality: str, output_dir: str):
    """Try downloading with yt-dlp. Returns result dict or raises exception."""
    if not _HAS_YTDLP:
        raise RuntimeError("yt-dlp not installed")

    quality_spec = _QUALITY_SPEC_MAP.get(quality.upper(), "bestaudio")
    is_transcode = quality.upper().startswith("MP3_") or quality.upper().startswith("AAC_")

    auth_value = _get_auth_value()
    cookie_path = _build_ytdlp_cookie_path(auth_value) if auth_value else None
    
    # Try cookies then non-cookie clients. With Node.js installed, yt-dlp can
    # solve JS challenges for all clients. Without cookies, android works best.
    if cookie_path:
        clients_to_try = ["web_music", "web", "android"]
    else:
        clients_to_try = ["android", "web_music", "web"]
    last_error = None
    
    for client in clients_to_try:
        ydl_opts = {
            "format": quality_spec,
            "quiet": True,
            "outtmpl": os.path.join(output_dir, f"ytm_{video_id}_%(id)s.%(ext)s"),
            "logger": _YtdlpLogger(),
            "extractor_args": {"youtube": {"player_client": [client]}},
            "postprocessors": [],
        }

        if cookie_path:
            ydl_opts["cookiefile"] = cookie_path

        if is_transcode:
            if quality.upper().startswith("MP3_"):
                abr_str = quality.upper().replace("MP3_", "")
                ydl_opts["postprocessors"].append({
                    "key": "FFmpegExtractAudio",
                    "preferredcodec": "mp3",
                    "preferredquality": abr_str,
                })
            elif quality.upper().startswith("AAC_"):
                abr_str = quality.upper().replace("AAC_", "")
                ydl_opts["postprocessors"].append({
                    "key": "FFmpegExtractAudio",
                    "preferredcodec": "m4a",
                    "preferredquality": abr_str,
                })
            ydl_opts["prefer_ffmpeg"] = True

        try:
            with yt_dlp.YoutubeDL(ydl_opts) as ydl:
                info = ydl.extract_info(f"https://music.youtube.com/watch?v={video_id}", download=True)
                if not info:
                    continue

                requested_downloads = info.get("requested_downloads") or []
                if requested_downloads:
                    filepath = requested_downloads[0].get("__final_filepath") or requested_downloads[0].get("filepath")
                else:
                    filepath = ydl.prepare_filename(info)
                    for ext in (".mp3", ".m4a", ".opus", ".flac", ".ogg", ".wav", ".webm"):
                        candidate = os.path.splitext(filepath)[0] + ext
                        if os.path.exists(candidate):
                            filepath = candidate
                            break

                if not filepath or not os.path.exists(filepath):
                    continue

                abr = info.get("abr") or info.get("tbr") or 0

                if _has_ffmpeg():
                    if filepath.lower().endswith(".webm"):
                        # Extract stream to .opus container (stream copy instead of transcode)
                        opus_path = os.path.splitext(filepath)[0] + ".opus"
                        result = subprocess.run(
                            ["ffmpeg", "-y", "-i", filepath, "-vn", "-c:a", "copy", opus_path],
                            capture_output=True, timeout=60,
                        )
                        if result.returncode == 0 and os.path.exists(opus_path):
                            os.remove(filepath)
                            filepath = opus_path
                        else:
                            filepath = _convert_webm_to_m4a(filepath, int(abr * 1000) if abr else 0)
                    else:
                        filepath = _convert_webm_to_m4a(filepath, int(abr * 1000) if abr else 0)

                actual_ext = os.path.splitext(filepath)[1].lstrip(".")
                if is_transcode and quality.upper().startswith("MP3_"):
                    actual_ext = "mp3"
                elif is_transcode and quality.upper().startswith("AAC_"):
                    actual_ext = "m4a"

                actual_mime = {
                    "mp3": "audio/mp3", "m4a": "audio/mp4", "opus": "audio/webm; codecs=opus",
                    "flac": "audio/flac", "ogg": "audio/ogg", "wav": "audio/wav",
                    "webm": "audio/webm; codecs=opus",
                }.get(actual_ext, "audio/" + actual_ext)
                if actual_ext == "mp3":
                    actual_mime = "audio/mp3"
                elif actual_ext == "m4a":
                    actual_mime = "audio/mp4"
                elif actual_ext == "webm":
                    actual_mime = "audio/webm; codecs=opus"

                # Extract acodec from the selected format for better codec info
                requested_dls = info.get("requested_downloads") or []
                if requested_dls:
                    dl_fmt = requested_downloads[0]
                    acodec = dl_fmt.get("acodec", "")
                    vcodec = dl_fmt.get("vcodec", "")
                    # If it's an audio-only format, use audio codec; use actual_ext for combined
                    if vcodec == "none" and acodec and acodec != "none":
                        actual_ext = acodec_to_ext(acodec)

                # Extract codec from mime string: "audio/webm; codecs=opus" -> "opus"
                if "; codecs=" in actual_mime:
                    codec = actual_mime.split("; codecs=")[1].strip()
                elif "/" in actual_mime:
                    codec = actual_mime.split(";")[0].replace("audio/", "")
                else:
                    codec = actual_ext

                return {
                    "filepath": filepath,
                    "mimeType": actual_mime,
                    "bitrate": int(abr * 1000) if abr else 0,
                    "codec": codec,
                    "quality": info.get("format_note", ""),
                    "durationMs": int(info.get("duration", 0) * 1000) if info.get("duration") else 0,
                }
        except Exception as e:
            last_error = e
            print(f"[yt-dlp] Player client '{client}' failed: {e}", file=sys.stderr, flush=True)
            continue

    raise last_error or RuntimeError("All yt-dlp player clients failed")


def _get_stream_url_ytdlp(video_id: str, quality: str = "FLAC") -> dict | None:
    """Use yt-dlp to extract the stream URL for a video."""
    if not _HAS_YTDLP:
        return None

    auth_value = _get_auth_value()

    for client in ["android", "web_music", "web"]:
        ydl_opts = {
            "format": _QUALITY_SPEC_MAP.get(quality.upper(), "bestaudio"),
            "quiet": True,
            "extract_flat": False,
            "logger": _YtdlpLogger(),
            "extractor_args": {"youtube": {"player_client": [client]}},
        }
        cookie_path = _build_ytdlp_cookie_path(auth_value) if auth_value else None
        if cookie_path:
            ydl_opts["cookiefile"] = cookie_path

        try:
            with yt_dlp.YoutubeDL(ydl_opts) as ydl:
                info = ydl.extract_info(f"https://music.youtube.com/watch?v={video_id}", download=False)
                if not info:
                    continue
                url = info.get("url")
                if url:
                    mime = info.get("mime_type", "audio/mp4")
                    abr = info.get("abr") or info.get("tbr") or 0
                    return {
                        "url": url, "mimeType": mime, "bitrate": int(abr * 1000) if abr else 0,
                        "codec": mime.split(";")[0].replace("audio/", "") if "/" in mime else "m4a",
                        "quality": info.get("format_note", ""),
                        "durationMs": int(info.get("duration", 0) * 1000) if info.get("duration") else 0,
                    }
                formats = info.get("formats", [])
                audio_formats = [f for f in formats if f.get("acodec") != "none" and f.get("vcodec") == "none"]
                if not audio_formats:
                    continue
                audio_formats.sort(key=lambda f: f.get("abr", 0) or f.get("tbr", 0) or 0, reverse=True)
                selected = audio_formats[0]
                url = selected.get("url")
                if url:
                    mime = selected.get("mime_type", "audio/mp4")
                    abr = selected.get("abr") or selected.get("tbr") or 0
                    return {
                        "url": url, "mimeType": mime, "bitrate": int(abr * 1000) if abr else 0,
                        "codec": mime.split(";")[0].replace("audio/", "") if "/" in mime else "m4a",
                        "quality": selected.get("format_note", ""),
                        "durationMs": int(info.get("duration", 0) * 1000) if info.get("duration") else 0,
                    }
        except Exception as e:
            print(f"[yt-dlp] {client} extraction failed: {e}", file=sys.stderr, flush=True)
            continue
    return None


def _extract_url_from_cipher(sig_cipher: str) -> str | None:
    url_part = ""
    for part in sig_cipher.split("&"):
        if part.startswith("url="):
            url_part = unquote(part[4:])
            break
    return url_part if url_part else None


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

def cmd_search_songs(query: str, limit: int):
    ytm = get_ytmusic(needs_auth=False)
    results = ytm.search(query, filter="songs", limit=limit)
    ok({"songs": [_map_track(t) for t in results if t.get("videoType") == "MUSIC_VIDEO_TYPE_ATV" or t.get("resultType") == "song"]})


def cmd_search_albums(query: str, limit: int):
    ytm = get_ytmusic(needs_auth=False)
    results = ytm.search(query, filter="albums", limit=limit)
    ok({"albums": [_map_album(a) for a in results]})


def cmd_search_artists(query: str, limit: int):
    ytm = get_ytmusic(needs_auth=False)
    results = ytm.search(query, filter="artists", limit=limit)
    ok({"artists": [_map_artist(a) for a in results]})


def cmd_search_all(query: str, song_limit: int, album_limit: int, artist_limit: int):
    ytm = get_ytmusic(needs_auth=False)
    songs = ytm.search(query, filter="songs", limit=song_limit)
    albums = ytm.search(query, filter="albums", limit=album_limit)
    artists = ytm.search(query, filter="artists", limit=artist_limit)
    ok({
        "songs": [_map_track(t) for t in songs if t.get("videoType") == "MUSIC_VIDEO_TYPE_ATV" or t.get("resultType") == "song"],
        "albums": [_map_album(a) for a in albums],
        "artists": [_map_artist(a) for a in artists],
    })


def cmd_get_song(video_id: str):
    ytm = get_ytmusic(needs_auth=False)
    results = ytm.search(video_id, filter="songs", limit=5)
    song = None
    for r in results:
        if r.get("videoId") == video_id:
            song = r
            break
    if song is None:
        watch = ytm.get_watch_playlist(videoId=video_id, limit=1)
        if watch and "tracks" in watch and watch["tracks"]:
            song = watch["tracks"][0]
    if song is None:
        fail(f"Song not found: {video_id}")
        return
    ok({"song": _map_track(song)})


def cmd_get_album(browse_id: str):
    ytm = get_ytmusic(needs_auth=False)
    album = ytm.get_album(browse_id)
    if not album:
        fail(f"Album not found: {browse_id}")
        return
    tracks = []
    for track in album.get("tracks", []):
        mapped = _map_track(track)
        mapped["album"] = {"name": album.get("title", ""), "id": browse_id}
        tracks.append(mapped)
    ok({"album": {
        "browseId": browse_id, "title": album.get("title", ""),
        "artists": [{"name": a.get("name", ""), "id": a.get("id")} for a in album.get("artists", [])],
        "year": album.get("year"), "trackCount": album.get("trackCount"),
        "thumbnails": album.get("thumbnails", []), "type": album.get("type", "Album"), "tracks": tracks,
    }})


def cmd_get_artist(browse_id: str):
    ytm = get_ytmusic(needs_auth=False)
    artist = ytm.get_artist(browse_id)
    if not artist:
        fail(f"Artist not found: {browse_id}")
        return
    ok({"artist": {"browseId": browse_id, "name": artist.get("name", ""),
        "thumbnails": artist.get("thumbnails", []), "albumCount": artist.get("albums", {}).get("params") and 1 or None}})


def cmd_get_artist_albums(browse_id: str):
    ytm = get_ytmusic(needs_auth=False)
    artist = ytm.get_artist(browse_id)
    if not artist:
        fail(f"Artist not found: {browse_id}")
        return
    albums_list = []
    if artist.get("albums") and artist["albums"].get("results"):
        albums_list = artist["albums"]["results"]
    elif artist.get("singles") and artist["singles"].get("results"):
        albums_list = artist["singles"]["results"]
    ok({"albums": [_map_album(a) for a in albums_list]})


def cmd_download_track(video_id: str, quality: str = "FLAC", output_dir: str = ""):
    """Download a track. Strategy:
    1. yt-dlp (primary — handles n-param decoding via JS runtime)
    2. ytmusicapi authenticated download (fallback, uses premium cookies)
    """
    if not output_dir:
        output_dir = tempfile.gettempdir()
    os.makedirs(output_dir, exist_ok=True)

    # Strategy 1: yt-dlp with player_client fallbacks (handles n-param/throttling)
    if _HAS_YTDLP:
        try:
            result = _download_track_ytdlp(video_id, quality, output_dir)
            if result:
                ok(result)
                return
        except Exception as e:
            print(f"[yt-dlp] Download failed: {e}", file=sys.stderr, flush=True)

    # Strategy 2: ytmusicapi authenticated download (fallback)
    try:
        result = _download_track_ytmusicapi(video_id, quality, output_dir)
        if result:
            ok(result)
            return
    except Exception as e:
        print(f"[ytmusicapi] Download failed: {e}", file=sys.stderr, flush=True)

    fail("All download methods failed (yt-dlp failed, ytmusicapi failed or unavailable)")


def cmd_get_stream_url(video_id: str, quality: str = "FLAC"):
    result = _get_stream_url_ytdlp(video_id, quality)
    if result and result.get("url"):
        ok(result)
        return

    ytm = get_ytmusic(needs_auth=True)
    try:
        stream_info = ytm.get_song(video_id)
    except Exception as e:
        fail(f"Failed to get stream info: {e}")
        return

    if not stream_info or "streamingData" not in stream_info:
        fail("No streaming data available")
        return

    sd = stream_info["streamingData"]
    formats = sd.get("adaptiveFormats", []) + sd.get("formats", [])
    if not formats:
        fail("No audio formats available")
        return

    audio_formats = [f for f in formats if f.get("mimeType", "").startswith("audio/")]
    if not audio_formats:
        fail("No audio-only formats available")
        return

    audio_formats.sort(key=lambda f: int(f.get("bitrate", 0)), reverse=True)
    selected = audio_formats[0]

    url = selected.get("url")
    if not url:
        sig_cipher = selected.get("signatureCipher")
        if sig_cipher:
            url = _extract_url_from_cipher(sig_cipher)

    if not url:
        fail("No stream URL available")
        return

    ok({
        "videoId": video_id, "url": url, "mimeType": selected.get("mimeType"),
        "bitrate": selected.get("bitrate"), "codec": selected.get("mimeType", "").split(";")[0].replace("audio/", ""),
        "quality": selected.get("quality"),
        "durationMs": int(stream_info.get("videoDetails", {}).get("lengthSeconds", 0)) * 1000,
    })


def cmd_check_auth():
    auth_value = _get_auth_value()
    if auth_value is None:
        ok({"authenticated": False, "searchWorks": True, "reason": "No auth configured (search works anonymously)"})
        return
    try:
        ytm = get_ytmusic(needs_auth=True)
        results = ytm.search("test", filter="songs", limit=1)
        ok({"authenticated": True, "searchWorks": len(results) > 0})
    except Exception as e:
        ok({"authenticated": False, "searchWorks": False, "reason": str(e)})


# ---------------------------------------------------------------------------
# Main entry point
# ---------------------------------------------------------------------------

COMMANDS = {
    "search-songs": (cmd_search_songs, [str, int]),
    "search-albums": (cmd_search_albums, [str, int]),
    "search-artists": (cmd_search_artists, [str, int]),
    "search-all": (cmd_search_all, [str, int, int, int]),
    "get-song": (cmd_get_song, [str]),
    "get-album": (cmd_get_album, [str]),
    "get-artist": (cmd_get_artist, [str]),
    "get-artist-albums": (cmd_get_artist_albums, [str]),
    "get-stream-url": (cmd_get_stream_url, [str, str]),
    "download-track": (cmd_download_track, [str, str, str]),
    "check-auth": (cmd_check_auth, []),
}


def main():
    if len(sys.argv) < 2:
        fail("No command provided. Available: " + ", ".join(COMMANDS.keys()))

    cmd_name = sys.argv[1]
    if cmd_name not in COMMANDS:
        fail(f"Unknown command: {cmd_name}. Available: " + ", ".join(COMMANDS.keys()))

    cmd_fn, arg_types = COMMANDS[cmd_name]
    raw_args = sys.argv[2:]

    clean_args = []
    skip = False
    for a in raw_args:
        if skip:
            skip = False
            continue
        if a in ("--cookie", "--oauth"):
            skip = True
            continue
        clean_args.append(a)

    try:
        args = [arg_types[i](clean_args[i]) for i in range(len(arg_types))]
    except (IndexError, ValueError) as e:
        fail(f"Invalid arguments for {cmd_name}: {e}")
        return

    try:
        cmd_fn(*args)
    except Exception as e:
        fail(f"Command failed: {e}")


if __name__ == "__main__":
    main()