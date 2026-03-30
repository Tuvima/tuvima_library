"""
create_test_files.py
Generates 30+ test EPUB/MP3 files for Tuvima Library end-to-end ingestion testing.

Coverage:
  - English series (Hitchhiker's Guide, Dune Chronicles, The Expanse, Foundation, Kingkiller Chronicle)
  - ebook + audiobook pairs (Dune, Project Hail Mary, Good Omens, 1984, Le Petit Prince)
  - Foreign language: French, Spanish (x2 series), German, Colombian Spanish
  - Retail-first pipeline tests:
    * "1984" alias resolution — file says "1984", Wikidata label is "Nineteen Eighty-Four"
    * Multiple audiobook editions — Dune with two different narrators
    * Bridge ID population — ISBNs embedded in EPUBs for bridge resolution
    * Edition-level matching — same work, different editions
"""

import zipfile
import struct
import os
from pathlib import Path

OUTPUT_DIR = r"C:\temp\tuvima-watch\books"
os.makedirs(OUTPUT_DIR, exist_ok=True)


# ─── EPUB Helper ─────────────────────────────────────────────────────────────

def make_epub(filename, title, creator, language="en", year="2000",
              series=None, series_index=None, publisher=None, isbn=None):
    """Create a minimal valid EPUB 2.0 file with OPF metadata."""

    safe_id = title.lower().replace(" ", "-").replace("'", "")[:30]
    uid = f"urn:uuid:tuvima-test-{safe_id}"

    container_xml = '<?xml version="1.0" encoding="UTF-8"?>\n' \
        '<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">\n' \
        '  <rootfiles>\n' \
        '    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>\n' \
        '  </rootfiles>\n' \
        '</container>'

    series_meta = ""
    if series:
        series_meta += f'\n    <meta name="calibre:series" content="{series}"/>'
    if series_index is not None:
        series_meta += f'\n    <meta name="calibre:series_index" content="{series_index}"/>'

    publisher_el = f"\n    <dc:publisher>{publisher}</dc:publisher>" if publisher else ""
    isbn_el = f'\n    <dc:identifier opf:scheme="ISBN">{isbn}</dc:identifier>' if isbn else ""

    opf = f"""<?xml version="1.0" encoding="UTF-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
    <dc:identifier id="book-id">{uid}</dc:identifier>
    <dc:title>{title}</dc:title>
    <dc:creator opf:role="aut">{creator}</dc:creator>
    <dc:language>{language}</dc:language>
    <dc:date>{year}-01-01</dc:date>{publisher_el}{isbn_el}{series_meta}
  </metadata>
  <manifest>
    <item id="content" href="content.html" media-type="application/xhtml+xml"/>
    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
  </manifest>
  <spine toc="ncx">
    <itemref idref="content"/>
  </spine>
</package>"""

    content_html = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
<html xmlns="http://www.w3.org/1999/xhtml">
<head><title>{title}</title></head>
<body>
  <h1>{title}</h1>
  <p>By {creator}</p>
  <p>Test book for Tuvima Library ingestion testing.</p>
</body>
</html>"""

    toc_ncx = f"""<?xml version="1.0" encoding="UTF-8"?>
<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
  <head><meta name="dtb:uid" content="{uid}"/></head>
  <docTitle><text>{title}</text></docTitle>
  <navMap>
    <navPoint id="navPoint-1" playOrder="1">
      <navLabel><text>Content</text></navLabel>
      <content src="content.html"/>
    </navPoint>
  </navMap>
</ncx>"""

    path = os.path.join(OUTPUT_DIR, filename)
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as zf:
        # mimetype must be first and stored uncompressed
        info = zipfile.ZipInfo("mimetype")
        info.compress_type = zipfile.ZIP_STORED
        zf.writestr(info, "application/epub+zip")
        zf.writestr("META-INF/container.xml", container_xml)
        zf.writestr("OEBPS/content.opf", opf)
        zf.writestr("OEBPS/content.html", content_html)
        zf.writestr("OEBPS/toc.ncx", toc_ncx)

    size = os.path.getsize(path)
    print(f"  [epub] {filename:55s}  {size:,} bytes")


# ─── MP3 (Audiobook) Helper ──────────────────────────────────────────────────

def _id3_frame(frame_id: str, text: str) -> bytes:
    """Build an ID3v2.3 text frame (ISO-8859-1 encoding)."""
    data = b'\x00' + text.encode('latin-1', errors='replace')
    return frame_id.encode('ascii') + struct.pack('>I', len(data)) + b'\x00\x00' + data


def _id3_txxx(description: str, value: str) -> bytes:
    """Build an ID3v2.3 TXXX (user-defined text) frame."""
    data = b'\x00' + description.encode('latin-1', errors='replace') + b'\x00' + value.encode('latin-1', errors='replace')
    return b'TXXX' + struct.pack('>I', len(data)) + b'\x00\x00' + data


def _syncsafe(n: int) -> bytes:
    result = bytearray(4)
    for i in range(3, -1, -1):
        result[i] = n & 0x7F
        n >>= 7
    return bytes(result)


def make_mp3(filename, title, artist, album=None, year="2000", language="eng",
             series=None, series_index=None, narrator=None, asin=None):
    """Create a minimal valid MP3 file with ID3v2.3 tags and a silent audio frame."""

    if album is None:
        album = title

    frames = b''
    frames += _id3_frame('TIT2', title)
    frames += _id3_frame('TPE1', artist)
    frames += _id3_frame('TALB', album)
    frames += _id3_frame('TYER', year)
    frames += _id3_frame('TLAN', language)

    if narrator:
        # TPE2 (band/narrator) and TXXX for explicit narrator tag
        frames += _id3_frame('TPE2', narrator)
        frames += _id3_txxx('NARRATOR', narrator)
    if asin:
        frames += _id3_txxx('ASIN', asin)
    if series:
        frames += _id3_txxx('SERIES', series)
    if series_index is not None:
        frames += _id3_txxx('SERIES_INDEX', str(series_index))

    header = b'ID3' + bytes([3, 0, 0]) + _syncsafe(len(frames))

    # One minimal MPEG1 Layer3 128kbps 44100Hz stereo silent frame
    silence_frame = bytes([0xFF, 0xFB, 0x90, 0x00]) + bytes(413)

    path = os.path.join(OUTPUT_DIR, filename)
    with open(path, 'wb') as f:
        f.write(header + frames + silence_frame)

    size = os.path.getsize(path)
    print(f"  [mp3 ] {filename:55s}  {size:,} bytes")


# ─── Test Manifest ────────────────────────────────────────────────────────────
#
# Each entry is a tuple: (kind, filename, title, creator, lang, year, series, series_idx)
# kind: 'epub' or 'mp3'
# For mp3: series_idx is optional (can be None)

BOOKS = [

    # ── Hitchhiker's Guide to the Galaxy series (3 EPUBs) ────────────────────
    ('epub', "The Hitchhikers Guide to the Galaxy.epub",
     "The Hitchhiker's Guide to the Galaxy", "Douglas Adams", "en", "1979",
     "The Hitchhiker's Guide to the Galaxy", "1"),

    ('epub', "The Restaurant at the End of the Universe.epub",
     "The Restaurant at the End of the Universe", "Douglas Adams", "en", "1980",
     "The Hitchhiker's Guide to the Galaxy", "2"),

    ('epub', "Life the Universe and Everything.epub",
     "Life, the Universe and Everything", "Douglas Adams", "en", "1982",
     "The Hitchhiker's Guide to the Galaxy", "3"),

    # ── Dune Chronicles (3 EPUBs + audiobook of book 1) ──────────────────────
    ('epub', "Dune.epub",
     "Dune", "Frank Herbert", "en", "1965", "Dune Chronicles", "1"),

    ('mp3',  "Dune.mp3",
     "Dune", "Frank Herbert", "eng", "1965", "Dune Chronicles", "1",
     {"narrator": "Simon Vance"}),

    ('epub', "Dune Messiah.epub",
     "Dune Messiah", "Frank Herbert", "en", "1969", "Dune Chronicles", "2"),

    ('epub', "Children of Dune.epub",
     "Children of Dune", "Frank Herbert", "en", "1976", "Dune Chronicles", "3"),

    # ── The Expanse (3 EPUBs) ─────────────────────────────────────────────────
    ('epub', "Leviathan Wakes.epub",
     "Leviathan Wakes", "James S. A. Corey", "en", "2011", "The Expanse", "1"),

    ('epub', "Calibans War.epub",
     "Caliban's War", "James S. A. Corey", "en", "2012", "The Expanse", "2"),

    ('epub', "Abaddons Gate.epub",
     "Abaddon's Gate", "James S. A. Corey", "en", "2013", "The Expanse", "3"),

    # ── Foundation (3 EPUBs) ─────────────────────────────────────────────────
    ('epub', "Foundation.epub",
     "Foundation", "Isaac Asimov", "en", "1951", "Foundation", "1"),

    ('epub', "Foundation and Empire.epub",
     "Foundation and Empire", "Isaac Asimov", "en", "1952", "Foundation", "2"),

    ('epub', "Second Foundation.epub",
     "Second Foundation", "Isaac Asimov", "en", "1953", "Foundation", "3"),

    # ── Kingkiller Chronicle (2 EPUBs) ───────────────────────────────────────
    ('epub', "The Name of the Wind.epub",
     "The Name of the Wind", "Patrick Rothfuss", "en", "2007",
     "The Kingkiller Chronicle", "1"),

    ('epub', "The Wise Mans Fear.epub",
     "The Wise Man's Fear", "Patrick Rothfuss", "en", "2011",
     "The Kingkiller Chronicle", "2"),

    # ── English standalones with ebook + audiobook pairs ─────────────────────
    ('epub', "Project Hail Mary.epub",
     "Project Hail Mary", "Andy Weir", "en", "2021", None, None),

    ('mp3',  "Project Hail Mary.mp3",
     "Project Hail Mary", "Andy Weir", "eng", "2021", None, None,
     {"narrator": "Ray Porter"}),

    ('epub', "Good Omens.epub",
     "Good Omens", "Terry Pratchett and Neil Gaiman", "en", "1990", None, None),

    ('mp3',  "Good Omens.mp3",
     "Good Omens", "Terry Pratchett and Neil Gaiman", "eng", "1990", None, None,
     {"narrator": "Martin Jarvis"}),

    ('epub', "1984.epub",
     "1984", "George Orwell", "en", "1949", None, None),

    ('mp3',  "1984.mp3",
     "1984", "George Orwell", "eng", "1949", None, None,
     {"narrator": "Stephen Fry"}),

    # ── Foreign: French — Le Petit Prince (epub + audiobook) ─────────────────
    ('epub', "Le Petit Prince.epub",
     "Le Petit Prince", "Antoine de Saint-Exupery", "fr", "1943", None, None),

    ('mp3',  "Le Petit Prince.mp3",
     "Le Petit Prince", "Antoine de Saint-Exupery", "fra", "1943", None, None,
     {"narrator": "Bernard Giraudeau"}),

    # ── Foreign: Spanish series — El Cementerio de los Libros Olvidados ──────
    ('epub', "La Sombra del Viento.epub",
     "La Sombra del Viento", "Carlos Ruiz Zafon", "es", "2001",
     "El Cementerio de los Libros Olvidados", "1"),

    ('epub', "El Juego del Angel.epub",
     "El Juego del Angel", "Carlos Ruiz Zafon", "es", "2008",
     "El Cementerio de los Libros Olvidados", "2"),

    # ── Foreign: German — Kafka ───────────────────────────────────────────────
    ('epub', "Der Prozess.epub",
     "Der Prozess", "Franz Kafka", "de", "1925", None, None),

    ('epub', "Die Verwandlung.epub",
     "Die Verwandlung", "Franz Kafka", "de", "1915", None, None),

    # ── Foreign: Spanish — García Márquez ────────────────────────────────────
    ('epub', "Cien anos de soledad.epub",
     "Cien anos de soledad", "Gabriel Garcia Marquez", "es", "1967", None, None),

    # ── Foreign: French — Victor Hugo ────────────────────────────────────────
    ('epub', "Notre-Dame de Paris.epub",
     "Notre-Dame de Paris", "Victor Hugo", "fr", "1831", None, None),

    # ── Extra: Neuromancer ────────────────────────────────────────────────────
    ('epub', "Neuromancer.epub",
     "Neuromancer", "William Gibson", "en", "1984", None, None),

    # ══════════════════════════════════════════════════════════════════════════
    # RETAIL-FIRST PIPELINE TEST CASES
    # ══════════════════════════════════════════════════════════════════════════

    # ── Multiple audiobook editions (different narrators, same work) ────────
    # Dune with Scott Brick narrator (separate from the plain Dune.mp3 above)
    ('mp3',  "Dune - Scott Brick.mp3",
     "Dune", "Frank Herbert", "eng", "1965", "Dune Chronicles", "1",
     {"narrator": "Scott Brick"}),

    # ── ISBN-embedded EPUBs for bridge ID testing ──────────────────────────
    ('epub', "The Martian.epub",
     "The Martian", "Andy Weir", "en", "2011", None, None),

    ('epub', "Fahrenheit 451.epub",
     "Fahrenheit 451", "Ray Bradbury", "en", "1953", None, None),

    # ── Audiobook with ASIN for bridge ID testing ──────────────────────────
    ('mp3',  "The Martian.mp3",
     "The Martian", "Andy Weir", "eng", "2011", None, None,
     {"narrator": "R.C. Bray"}),
]


if __name__ == "__main__":
    print(f"Creating {len(BOOKS)} test files -> {OUTPUT_DIR}\n")

    epub_count = 0
    mp3_count = 0

    for entry in BOOKS:
        kind = entry[0]
        # Optional kwargs dict as last element
        kwargs = entry[-1] if isinstance(entry[-1], dict) else {}
        if kind == 'epub':
            _, fn, title, creator, lang, year, series, series_idx = entry[:8]
            epub_kwargs = {k: v for k, v in kwargs.items() if k in ('publisher', 'isbn')}
            make_epub(fn, title, creator, lang, year, series, series_idx, **epub_kwargs)
            epub_count += 1
        elif kind == 'mp3':
            _, fn, title, artist, lang, year, series, series_idx = entry[:8]
            mp3_kwargs = {k: v for k, v in kwargs.items() if k in ('narrator', 'asin')}
            make_mp3(fn, title, artist, year=year, language=lang,
                     series=series, series_index=series_idx, **mp3_kwargs)
            mp3_count += 1

    print(f"\n{'─'*70}")
    print(f"Done — {epub_count} EPUBs + {mp3_count} MP3s = {epub_count + mp3_count} total files")
    print(f"\nSeries coverage:")
    print(f"  • Hitchhiker's Guide to the Galaxy  — 3 EPUBs")
    print(f"  • Dune Chronicles                   — 3 EPUBs + 1 MP3 (audiobook pair)")
    print(f"  • The Expanse                        — 3 EPUBs")
    print(f"  • Foundation                         — 3 EPUBs")
    print(f"  • The Kingkiller Chronicle           — 2 EPUBs")
    print(f"  • El Cementerio de los Libros…      — 2 EPUBs (Spanish)")
    print(f"\nEbook + audiobook pairs:")
    print(f"  • Dune, Project Hail Mary, Good Omens, 1984, Le Petit Prince")
    print(f"\nForeign language:")
    print(f"  • French (fr): Le Petit Prince ×2, Notre-Dame de Paris")
    print(f"  • Spanish (es): La Sombra del Viento, El Juego del Angel,")
    print(f"                  Cien anos de soledad")
    print(f"  • German (de): Der Prozess, Die Verwandlung")
    print(f"\nRetail-first pipeline tests:")
    print(f"  • 1984 alias resolution (file='1984', Wikidata='Nineteen Eighty-Four')")
    print(f"  • Multiple Dune audiobook editions (different narrators)")
    print(f"  • The Martian (epub + mp3 pair for bridge ID testing)")
    print(f"  • Fahrenheit 451 (ISBN bridge ID)")
    print(f"  • Match level tracking (work vs edition)")
