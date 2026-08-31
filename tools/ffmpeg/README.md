# Packaged FFmpeg

Windows installers bundle the FFmpeg and FFprobe executables downloaded by
`tools/Download-FFmpeg.ps1`. The executables themselves are intentionally
gitignored because of their size.

- Upstream: BtbN/FFmpeg-Builds
- Release: `autobuild-2026-08-28-17-08`
- Archive: `ffmpeg-n9.0.1-11-ge47273f4d9-win64-gpl-9.0.zip`
- Archive SHA-256: `DEE63142094F79F6A50CDECE65384B7793181EAB3B6DB2EC907834981BB8AB10`
- FFmpeg SHA-256: `989A60089B9B1A98896A5BD99EE793AB6841724E1B2441D5EF3E5D17DB0B0938`
- FFprobe SHA-256: `001D80FDDF67BC303E91C6B8ECCDF53AF29A5F87ECF3837056B391CC3DD3F7B4`

Containers use static GPL builds from the same release:

- Linux AMD64 archive: `ffmpeg-n9.0.1-11-ge47273f4d9-linux64-gpl-9.0.tar.xz`
- Linux AMD64 SHA-256: `BE5F44D1062386B2A9B4ED75FA1AF03873E2BBC1AE82842EF4D479C8E05A76DE`
- Linux ARM64 archive: `ffmpeg-n9.0.1-11-ge47273f4d9-linuxarm64-gpl-9.0.tar.xz`
- Linux ARM64 SHA-256: `1CB67F7FD3DE30BF2AE28B7AB9727DC3A84F1AEEF9F791B309023F9D7AC0AFF5`

The download script validates all three hashes. `build-installer.bat` validates
the two executable hashes and required HLS codecs/muxers before publishing.
`LICENSE.txt` contains the GPLv3 license shipped by the upstream bundle.
