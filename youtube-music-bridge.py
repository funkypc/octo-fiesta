#!/usr/bin/env python3
"""
YouTube Music Bridge Script
A thin wrapper around ytmusicapi for use by octo-fiesta (C#).

Accepts commands via command-line arguments, outputs JSON to stdout.
Errors are written as JSON objects with an "error" key to stderr.

Authentication:
  - Cookie-based: Set the YT_MUSIC_COOKIE env var or provide --cookie
  - OAuth: Set the YTMUSIC_OAUTH_CREDENTIALS env var or provide --oauth

Usage:
  python youtube-music-bridge.py <command> [args...]
  python youtube-music-bridge.py search-songs "queen" 20
  python youtube-music-bridge.py get-stream-url dQw4w9WgXcQ FLAC
"""

import json
import os
import sys
import argparse
from typing import Optional

try:
    from ytmusicapi import YTMusic
except ImportError as e:
    print(json.dumps({"error": f"ytmusicapi not installed: {e}"}), file=sys.stderr)
    sys.exit(1)


# ---------------------------------------------------------------------------
# Auth / YTMusic instance
# ---------------------------------------------------------------------------
_ymusic: Optional[YTMusic] = None


def _get_auth_from_env():
    """Try to get auth from environment or command args.
    Returns a tuple (auth, is_cookie) where auth is either a file path,
    a dict, or None."""
    # Try OAuth first
    oauth_creds = os.environ.get("YTMUSIC_OAUTH_CREDENTIALS")
    if oauth_creds:
        return oauth_creds, False

    # Try cookie string
    cookie = os.environ.get("YT_MUSIC_COOKIE")
    if cookie:
        return cookie, True

    # Try --cookie or --oauth from command args
    cookie = None
    oauth = None
    for i, arg in enumerate(sys.argv):
        if arg == "--cookie" and i + 1 < len(sys.argv):
            cookie = sys.argv[i + 1]
        elif arg == "--oauth" and i + 1 < len(sys.argv):
            oauth = sys.argv[i + 1]

    if oauth:
        return oauth, False
    if cookie:
        return cookie, True

    return None, False


def _create_ytmusic_instance(auth_cookie=None, needs_auth=False):
    """Create a YTMusic instance with proper auth handling.
    
    Args:
        auth_cookie: The raw cookie string or None
        needs_auth: If True, raises error if auth is not available/valid.
    
    Returns:
        YTMusic instance (authenticated or anonymous)
    """
    if auth_cookie is None:
        auth_cookie, is_cookie = _get_auth_from_env()
    else:
        is_cookie = True

    # If no auth is configured, return anonymous instance
    if auth_cookie is None:
        if needs_auth:
            raise RuntimeError("No authentication method configured. Set YT_MUSIC_COOKIE or YTMUSIC_OAUTH_CREDENTIALS, or pass --cookie or --oauth.")
        return YTMusic()

    # Try to parse as JSON dict
    try:
        auth_dict = json.loads(auth_cookie)
        if isinstance(auth_dict, dict):
            return YTMusic(auth=auth_dict)
    except json.JSONDecodeError:
        pass

    # Try as file path
    if os.path.exists(auth_cookie):
        return YTMusic(auth=auth_cookie)

    # Try to save cookie as a JSON file for ytmusicapi
    if is_cookie:
        try:
            # Create a temporary cookie file with the full cookie set
            cookie_dict = {}
            # If the cookie contains multiple key=value pairs, split them
            for part in auth_cookie.split(';'):
                part = part.strip()
                if '=' in part:
                    k, v = part.split('=', 1)
                    cookie_dict[k] = v
                else:
                    cookie_dict[part] = True

            # Create a temp file with the cookie dict
            import tempfile
            fd, temp_path = tempfile.mkstemp(suffix='.json', prefix='ytm_cookie_')
            with os.fdopen(fd, 'w') as f:
                json.dump(cookie_dict, f)
            return YTMusic(auth=temp_path)
        except Exception:
            pass

    # If all attempts fail, try passing the raw string as file path
    # (might fail but gives the ytmusicapi error message)
    if needs_auth:
        return YTMusic(auth=auth_cookie)
    else:
        return YTMusic()


def get_ytmusic(needs_auth=False):
    """Lazy-load the YTMusic client."""
    global _ymusic
    if _ymusic is not None:
        return _ymusic
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


def _map_track(t: dict) -> dict:
    """Map a ytmusicapi track dict to a normalized bridge track."""
    return {
        "videoId": t.get("videoId"),
        "title": t.get("title", ""),
        "artists": [{
            "name": a.get("name", ""),
            "id": a.get("id")
        } for a in t.get("artists", [])],
        "album": {
            "name": t.get("album", {}).get("name", ""),
            "id": t.get("album", {}).get("id")
        } if t.get("album") else None,
        "duration": t.get("duration"),
        "durationSeconds": t.get("duration_seconds"),
        "thumbnails": t.get("thumbnails", []),
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
        "artists": [{
            "name": ar.get("name", ""),
            "id": ar.get("id")
        } for ar in a.get("artists", [])],
        "year": a.get("year"),
        "trackCount": a.get("trackCount"),
        "thumbnails": a.get("thumbnails", []),
        "type": a.get("type", "Album"),
    }


def _map_artist(ar: dict) -> dict:
    return {
        "browseId": ar.get("browseId"),
        "name": ar.get("name", ""),
        "thumbnails": ar.get("thumbnails", []),
        "albumCount": ar.get("albumCount"),
    }


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
        # fallback: try get_watch_playlist
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
        mapped["album"] = {
            "name": album.get("title", ""),
            "id": browse_id
        }
        tracks.append(mapped)
    ok({
        "album": {
            "browseId": browse_id,
            "title": album.get("title", ""),
            "artists": [{
                "name": a.get("name", ""),
                "id": a.get("id")
            } for a in album.get("artists", [])],
            "year": album.get("year"),
            "trackCount": album.get("trackCount"),
            "thumbnails": album.get("thumbnails", []),
            "type": album.get("type", "Album"),
            "tracks": tracks,
        }
    })


def cmd_get_artist(browse_id: str):
    ytm = get_ytmusic(needs_auth=False)
    artist = ytm.get_artist(browse_id)
    if not artist:
        fail(f"Artist not found: {browse_id}")
        return
    ok({
        "artist": {
            "browseId": browse_id,
            "name": artist.get("name", ""),
            "thumbnails": artist.get("thumbnails", []),
            "albumCount": artist.get("albums", {}).get("params") and 1 or None,
        }
    })


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


def cmd_get_stream_url(video_id: str, quality: str = "FLAC"):
    # Stream URL may need auth for premium quality
    ytm = get_ytmusic(needs_auth=False)
    # Map quality to ytmusicapi signatureType
    sig_type = None
    if quality.upper() in ("FLAC", "MP3_256", "MP3_320"):
        sig_type = "signatureCipher"
    else:
        sig_type = "signatureCipher"
    try:
        stream_info = ytm.get_song(video_id)
    except Exception as e:
        fail(f"Failed to get stream info: {e}")
        return

    # Extract stream URL and format info
    if not stream_info or "streamingData" not in stream_info:
        fail("No streaming data available")
        return

    sd = stream_info["streamingData"]
    formats = sd.get("adaptiveFormats", []) + sd.get("formats", [])
    if not formats:
        fail("No audio formats available")
        return

    # Filter audio-only formats
    audio_formats = [f for f in formats if f.get("mimeType", "").startswith("audio/")]
    if not audio_formats:
        fail("No audio-only formats available")
        return

    # Sort by bitrate descending
    audio_formats.sort(key=lambda f: int(f.get("bitrate", 0)), reverse=True)

    # Select based on quality preference
    selected = audio_formats[0]
    if quality.upper() in ("MP3_128", "AAC_64"):
        # Pick lower bitrate
        for f in audio_formats:
            if int(f.get("bitrate", 0)) <= 128000:
                selected = f
                break

    ok({
        "videoId": video_id,
        "url": selected.get("url"),
        "mimeType": selected.get("mimeType"),
        "bitrate": selected.get("bitrate"),
        "codec": selected.get("mimeType", "").split(";")[0].replace("audio/", ""),
        "quality": selected.get("quality"),
        "durationMs": int(stream_info.get("videoDetails", {}).get("lengthSeconds", 0)) * 1000,
    })


def cmd_check_auth():
    try:
        ytm = get_ytmusic(needs_auth=False)
        # Do a lightweight search to verify auth
        results = ytm.search("test", filter="songs", limit=1)
        ok({"authenticated": True, "searchWorks": len(results) > 0})
    except Exception as e:
        fail(f"Auth check failed: {e}")


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

    # Strip --cookie / --oauth args from the command list
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
