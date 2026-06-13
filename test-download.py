#!/usr/bin/env python3
"""
Test script for YouTube Music download functionality.
Tests the ytmusicapi-based download (primary strategy).

Usage:
  python test-download.py [video_id]
  
Requires YT_MUSIC_COOKIE env var to be set (same as the main bridge script).
Default video ID: dQw4w9WgXcQ (Rick Astley - Never Gonna Give You Up)
"""

import json
import os
import sys
import tempfile

# Add current directory to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from youtube_music_bridge import (
    _get_auth_value, _get_download_headers,
    _HAS_REQUESTS, _download_track_ytmusicapi,
    _HAS_YTDLP,
)


def test_auth():
    """Test that auth is configured."""
    auth_value = _get_auth_value()
    if not auth_value:
        print("ERROR: No auth configured. Set YT_MUSIC_COOKIE env var.")
        return False
    
    print(f"Auth configured: yes (length={len(auth_value)})")
    
    headers = _get_download_headers()
    print(f"Download headers:")
    for k, v in headers.items():
        if k == "Cookie":
            print(f"  {k}: {v[:60]}... ({len(v)} chars)")
        else:
            print(f"  {k}: {v}")
    
    return True


def test_ytmusicapi_download(video_id: str):
    """Test ytmusicapi authenticated download."""
    print(f"\n=== Testing ytmusicapi download for {video_id} ===")
    print(f"requests available: {_HAS_REQUESTS}")
    
    if not _HAS_REQUESTS:
        print("SKIPPED: requests package not installed")
        return None
    
    output_dir = tempfile.mkdtemp(prefix="ytm_test_")
    
    try:
        result = _download_track_ytmusicapi(video_id, "FLAC", output_dir)
        if result:
            print(f"SUCCESS!")
            print(f"  Filepath: {result['filepath']}")
            print(f"  MIME: {result['mimeType']}")
            print(f"  Bitrate: {result['bitrate']}")
            print(f"  Codec: {result['codec']}")
            print(f"  Duration: {result['durationMs']}ms")
            
            size = os.path.getsize(result['filepath'])
            print(f"  File size: {size} bytes ({size / 1024 / 1024:.2f} MB)")
            
            # Verify file is audio (not HTML error page)
            with open(result['filepath'], 'rb') as f:
                header = f.read(16)
                is_html = header[:5] in (b'<!DOC', b'<html', b'<HTML')
                is_mp4 = header[4:8] == b'ftyp'
                is_webm = header[:4] == b'\x1a\x45\xdf\xa3'
                is_flac = header[:4] == b'fLaC'
                
                if is_html:
                    print(f"  FAIL: File appears to be HTML, not audio!")
                    print(f"  First bytes: {header[:16]}")
                    return False
                elif is_mp4 or is_webm or is_flac:
                    print(f"  OK: File appears to be valid audio")
                else:
                    print(f"  ? Unknown format header: {header[:16].hex()}")
            
            # Cleanup
            try:
                os.remove(result['filepath'])
                print(f"  Cleaned up test file")
            except:
                pass
            
            return True
        else:
            print("FAILED: _download_track_ytmusicapi returned None")
    except Exception as e:
        print(f"FAILED with exception: {e}")
    finally:
        try:
            os.rmdir(output_dir)
        except:
            pass
    
    return False


if __name__ == "__main__":
    video_id = sys.argv[1] if len(sys.argv) > 1 else "dQw4w9WgXcQ"
    
    print(f"YouTube Music Download Test")
    print(f"Video ID: {video_id}")
    print(f"ytmusicapi: installed")
    print(f"requests: {'installed' if _HAS_REQUESTS else 'NOT INSTALLED'}")
    print(f"yt-dlp: {'installed' if _HAS_YTDLP else 'not installed'}")
    print()
    
    if not test_auth():
        print("\nAuth test FAILED - cannot proceed")
        sys.exit(1)
    
    success = test_ytmusicapi_download(video_id)
    
    if success:
        print(f"\nDownload test PASSED")
        sys.exit(0)
    else:
        print(f"\nDownload test FAILED")
        sys.exit(1)