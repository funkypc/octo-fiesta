#!/usr/bin/env python3
"""
Test script for YouTube Music download functionality.
Tests all download strategies: innertube API, yt-dlp, and ytmusicapi.

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
    _get_auth_value, _get_download_headers, _download_track_innertube,
    _HAS_YTDLP, _download_track_ytdlp, _INNERTUBE_CLIENTS,
)


def test_auth():
    """Test that auth is configured."""
    auth_value = _get_auth_value()
    if not auth_value:
        print("ERROR: No auth configured. Set YT_MUSIC_COOKIE env var.")
        return False
    
    print(f"✓ Auth configured (value length: {len(auth_value)})")
    
    headers = _get_download_headers()
    print(f"✓ Download headers built:")
    for k, v in headers.items():
        if k == "Cookie":
            print(f"  {k}: {v[:80]}... ({len(v)} chars)")
        else:
            print(f"  {k}: {v}")
    
    return True


def test_innertube(video_id: str):
    """Test innertube direct API download (primary strategy)."""
    print(f"\n=== Testing Innertube API download for {video_id} ===")
    
    output_dir = tempfile.mkdtemp(prefix="ytm_test_")
    
    try:
        result = _download_track_innertube(video_id, "FLAC", output_dir)
        if result:
            print(f"✓ SUCCESS!")
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
                is_mp3 = header[2:3] in (b'\xff', ) and (header[0:1] == b'\xff' or header[1:2] == b'\xff')
                
                if is_html:
                    print(f"  ✗ WARNING: File appears to be HTML, not audio!")
                    print(f"  First bytes: {header[:16]}")
                    return False
                elif is_mp4 or is_webm or is_flac:
                    print(f"  ✓ File appears to be valid audio")
                else:
                    print(f"  ? Unknown format header: {header[:16].hex()}")
            
            # Cleanup
            try:
                os.remove(result['filepath'])
            except:
                pass
            
            return True
        else:
            print("✗ FAILED: _download_track_innertube returned None")
    except Exception as e:
        print(f"✗ FAILED with exception: {e}")
    finally:
        try:
            os.rmdir(output_dir)
        except:
            pass
    
    return False


def test_ytdlp(video_id: str):
    """Test yt-dlp download (secondary strategy)."""
    if not _HAS_YTDLP:
        print("\n=== Skipping yt-dlp test (not installed) ===")
        return None
    
    print(f"\n=== Testing yt-dlp download for {video_id} ===")
    
    output_dir = tempfile.mkdtemp(prefix="ytm_test_")
    
    try:
        result = _download_track_ytdlp(video_id, "FLAC", output_dir)
        if result:
            print(f"✓ SUCCESS!")
            print(f"  Filepath: {result['filepath']}")
            print(f"  MIME: {result['mimeType']}")
            print(f"  Codec: {result['codec']}")
            
            size = os.path.getsize(result['filepath'])
            print(f"  File size: {size} bytes ({size / 1024 / 1024:.2f} MB)")
            
            try:
                os.remove(result['filepath'])
            except:
                pass
            
            return True
        else:
            print("✗ FAILED: _download_track_ytdlp returned None")
    except Exception as e:
        print(f"✗ FAILED with exception: {e}")
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
    print(f"yt-dlp available: {_HAS_YTDLP}")
    print()
    
    # Test auth
    if not test_auth():
        print("\n✗ Auth test FAILED - cannot proceed")
        sys.exit(1)
    
    # Test innertube (primary strategy - no JS runtime needed)
    innertube_ok = test_innertube(video_id)
    
    # Test yt-dlp (secondary strategy)
    ytdlp_ok = test_ytdlp(video_id)
    
    # Summary
    print(f"\n{'='*50}")
    print(f"RESULTS:")
    print(f"  Innertube API: {'✓ PASS' if innertube_ok else '✗ FAIL'}")
    print(f"  yt-dlp:         {'✓ PASS' if ytdlp_ok else '? SKIP' if ytdlp_ok is None else '✗ FAIL'}")
    
    if innertube_ok:
        print(f"\n✓ Download test PASSED (innertube works - no JS runtime needed)")
        sys.exit(0)
    else:
        print(f"\n✗ Download test FAILED")
        sys.exit(1)