"""Patch the HTML pages docs-builder wrote, for scripts/docs-finish.sh.

    python3 -I scripts/docs-pages.py <output dir> <docs dir> <path prefix> <logo SVG, relative to docs>

- Links to the site root load it whole (hx-boost="false"). The root is the landing page, which has none of the docs
  shell, so htmx's boosted swap of #main-container would find nothing to swap and leave the reader on the docs.
- The header shows the landing page's animated lockup (docs/images/argh-lockup-header.svg, which reads "--argh_") in
  the slot the design-system header has for a logo (site-header__logo) and docs-builder never fills. It is inlined,
  not an <img>: an image cannot use the page's web fonts, and headless Chrome never advanced its animation. The theme
  hides the text title visually, since the lockup says it.
"""

import re
import sys
from pathlib import Path

out, docs, prefix, logo = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3].strip("/"), sys.argv[4].strip("/")
root = f"/{prefix}/" if prefix else "/"
if not (docs / logo).is_file() or not logo.endswith(".svg"):
    print(f"docs: the logo {docs / logo} is not an SVG file", file=sys.stderr)
    sys.exit(1)

svg = re.sub(r"<!--.*?-->\s*", "", (docs / logo).read_text(encoding="utf-8"), flags=re.S).strip()
svg, n_open = re.subn(
    r'<svg\b[^>]*>',
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 174 96" width="87" height="48" class="site-header__logo"'
    ' aria-hidden="true" focusable="false">',
    svg,
    count=1,
)
if n_open != 1 or 'viewBox="0 0 174 96"' not in (docs / logo).read_text(encoding="utf-8"):
    print(f"docs: {docs / logo} is not the 174x96 lockup this expects", file=sys.stderr)
    sys.exit(1)
logo_svg = re.sub(r">\s+<", "><", svg)

brand = re.compile(r'(<a\b[^>]*\bclass="site-header__brand"[^>]*>)(?!\s*<(?:img|svg)\b)')
root_link = re.compile(r'<a\b(?![^>]*\bhx-boost=)([^>]*\bhref="' + re.escape(root) + r'")')
any_root = re.compile(r'<a\b[^>]*\bhref="' + re.escape(root) + r'"')

pages = links = logos = 0
seen_root = seen_brand = False
for page in out.rglob("*.html"):
    html = page.read_text(encoding="utf-8")
    seen_root = seen_root or bool(any_root.search(html))
    seen_brand = seen_brand or 'class="site-header__brand"' in html
    new, n_links = root_link.subn(r'<a hx-boost="false"\1', html)
    new, n_logos = brand.subn(lambda m: m.group(1) + logo_svg, new)
    if new != html:
        page.write_text(new, encoding="utf-8")
        pages += 1
        links += n_links
        logos += n_logos

if not seen_root:
    print(f'docs: no links to "{root}" found; the docs-builder markup changed', file=sys.stderr)
    sys.exit(1)
if not seen_brand:
    print("docs: no site-header__brand found for the logo; the docs-builder markup changed", file=sys.stderr)
    sys.exit(1)
print(f"docs: {pages} pages patched ({links} root links unboosted, {logos} logos)")
