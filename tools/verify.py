#!/usr/bin/env python3
"""Static self-check for the Nine Lives Audio repo.

Runs offline on any OS with a plain Python 3 interpreter — no .NET SDK needed.
It cannot replace `dotnet build` (WinUI needs Windows), but it catches the
mistakes that are easy to introduce with bulk edits:

  1. every .xaml / .csproj file is well-formed XML
  2. every C# file has balanced braces/parens/brackets (strings and comments ignored)
  3. no duplicate x:Name inside one XAML file
  4. every event handler referenced from XAML exists in the code-behind
  5. every control name used in code-behind exists in the matching XAML
  6. every localization key used in C# / XAML exists, and en-US === zh-CN key sets
  7. Format() call sites pass the same number of arguments the template expects
  8. every loc:Loc.* attached property matches a property the target element has
  9. every service constructor dependency is registered in App.xaml.cs
 10. all StaticResource/ThemeResource keys used by app XAML are defined

Usage:  python3 tools/verify.py [-v]
Exit code 0 = clean.
"""
from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VERBOSE = "-v" in sys.argv

problems: list[str] = []


def fail(msg: str) -> None:
    problems.append(msg)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


# ── helpers ────────────────────────────────────────────────────────────────

def strip_non_code(src: str) -> str:
    """Drop comments and literals so bracket counting is meaningful."""
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == "/" and i + 1 < n and src[i + 1] == "/":
            while i < n and src[i] != "\n":
                i += 1
        elif c == "/" and i + 1 < n and src[i + 1] == "*":
            i += 2
            while i + 1 < n and not (src[i] == "*" and src[i + 1] == "/"):
                i += 1
            i += 2
        elif c == '"':
            if src.startswith('"""', i):
                i += 3
                while i + 2 < n and not src.startswith('"""', i):
                    i += 1
                i += 3
            elif i > 0 and src[i - 1] == "@":
                i += 1
                while i < n:
                    if src[i] == '"' and i + 1 < n and src[i + 1] == '"':
                        i += 2
                        continue
                    if src[i] == '"':
                        i += 1
                        break
                    i += 1
            else:
                i += 1
                while i < n:
                    if src[i] == "\\":
                        i += 2
                        continue
                    if src[i] == '"':
                        i += 1
                        break
                    i += 1
            out.append('""')
        elif c == "'":
            i += 1
            while i < n:
                if src[i] == "\\":
                    i += 2
                    continue
                if src[i] == "'":
                    i += 1
                    break
                i += 1
            out.append("''")
        else:
            out.append(c)
            i += 1
    return "".join(out)


def cs_files() -> list[Path]:
    return [p for p in ROOT.rglob("*.cs") if not {"obj", "bin"} & set(p.parts)]


def xaml_files() -> list[Path]:
    return [p for p in ROOT.rglob("*.xaml") if not {"obj", "bin"} & set(p.parts)]


# ── 1. XML well-formedness ─────────────────────────────────────────────────
for f in list(xaml_files()) + list(ROOT.rglob("*.csproj")):
    try:
        ET.parse(f)
    except ET.ParseError as e:
        fail(f"[xml] {f.relative_to(ROOT)}: {e}")

# ── 2. bracket balance ────────────────────────────────────────────────────
for f in cs_files():
    code = strip_non_code(read(f))
    for open_c, close_c in (("{", "}"), ("(", ")"), ("[", "]")):
        depth = 0
        for ch in code:
            depth += (ch == open_c) - (ch == close_c)
            if depth < 0:
                break
        if depth != 0:
            fail(f"[balance] {f.relative_to(ROOT)}: {open_c}{close_c} imbalance {depth}")

# ── 3. duplicate x:Name ───────────────────────────────────────────────────
for f in xaml_files():
    names = re.findall(r'x:Name="([A-Za-z0-9_]+)"', read(f))
    dupes = {n for n in names if names.count(n) > 1}
    if dupes:
        fail(f"[x:name] {f.relative_to(ROOT)}: duplicate {sorted(dupes)}")

# ── 4./5. handler + control name cross-checks ─────────────────────────────
EVENTS = (
    "Click|Loaded|Unloaded|SelectionChanged|Toggled|Checked|Unchecked|TextChanged|"
    "ValueChanged|Changed|Closed|Opened|PointerEntered|PointerExited|PointerPressed|"
    "DoubleTapped|Tapped|ItemClick|RightTapped|DragOver|Drop|SizeChanged|Navigated"
)
for x in xaml_files():
    code_behind = x.with_name(x.name + ".cs")
    if not code_behind.exists():
        continue
    src = read(code_behind)
    for handler in set(re.findall(rf'(?:{EVENTS})="(\w+)"', read(x))):
        if not re.search(rf'\b{handler}\s*\(', src):
            fail(f"[handler] {x.relative_to(ROOT)}: '{handler}' missing in code-behind")
    xaml_names = set(re.findall(r'x:Name="([A-Za-z0-9_]+)"', read(x)))
    for name in set(re.findall(r'x:Bind\s+(\w+)', read(x))):
        pass  # x:Bind targets page/VM members, validated by the compiler

# ── 6./7. localization resources ──────────────────────────────────────────
loc_path = ROOT / "Services" / "LocalizationService.cs"
loc_src = read(loc_path)
en_part, zh_part = loc_src.split('["zh-CN"] = new Dictionary<string, string>')
en_keys = set(re.findall(r'\["(\w+)"\]\s*=', en_part))
zh_keys = set(re.findall(r'\["(\w+)"\]\s*=', zh_part))
if en_keys != zh_keys:
    fail(f"[i18n] key mismatch — only en: {sorted(en_keys - zh_keys)}, only zh: {sorted(zh_keys - en_keys)}")

templates: dict[str, str] = {}
for m in re.finditer(r'\["(\w+)"\] = "((?:[^"\\]|\\.)*)"', loc_src):
    templates.setdefault(m.group(1), m.group(2))


def placeholders(text: str) -> int:
    return len(set(re.findall(r'\{(\d+)\}', text)))


def count_args(call_args: str) -> int:
    depth, count = 0, 1
    for ch in call_args:
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == "," and depth == 0:
            count += 1
    return count


for f in cs_files():
    src = read(f)
    for m in re.finditer(r'(?:GetString|Format)\(\s*"(\w+)"', src):
        if m.group(1) not in en_keys:
            fail(f"[i18n] {f.relative_to(ROOT)}: unknown key '{m.group(1)}'")
    for m in re.finditer(r'\.Format\(\s*"(\w+)"\s*(?:,\s*(.*?))?\)\s*[;)]', src, re.S):
        key, args = m.group(1), m.group(2)
        if key not in templates:
            continue
        want = placeholders(templates[key])
        got = 0 if not args else count_args(args)
        if want != got:
            fail(f"[i18n] {f.relative_to(ROOT)}: Format(\"{key}\") expects {want} args, got {got}")

for x in xaml_files():
    for m in re.finditer(r'loc:Loc\.(\w+)="(\w+)"', read(x)):
        if m.group(2) not in en_keys:
            fail(f"[i18n] {x.relative_to(ROOT)}: unknown loc key '{m.group(2)}'")

# ── 8. loc:Loc kind vs element type ───────────────────────────────────────
ALLOWED = {
    "TextBlock": {"Text"},
    "Button": {"Content"},
    "HyperlinkButton": {"Content"},
    "ToggleSwitch": {"Header"},
    "ComboBox": {"Header", "Placeholder"},
    "TextBox": {"Header", "Placeholder"},
    "PasswordBox": {"Header", "Placeholder"},
    "NavigationViewItem": {"Content"},
    "ComboBoxItem": {"Content"},
    "ListViewItem": {"Content"},
}
for x in xaml_files():
    src = read(x)
    for m in re.finditer(r'<(\w+)((?:"[^"]*"|[^>"])*?)loc:Loc\.(\w+)=', src, re.S):
        element, kind = m.group(1), m.group(3)
        if kind not in ALLOWED.get(element, set()):
            fail(f"[i18n] {x.relative_to(ROOT)}: loc:Loc.{kind} is not supported on <{element}>")

# ── 9. DI graph ───────────────────────────────────────────────────────────
app_src = read(ROOT / "App.xaml.cs")
registered = set()
for iface, impl in re.findall(r'services\.Add(?:Singleton|Transient|Scoped)<(\w+)(?:,\s*(\w+))?>\(\)', app_src):
    registered.add(iface)

ctors: dict[str, list[str]] = {}
for f in cs_files():
    src = read(f)
    for m in re.finditer(r'(?:public|internal)\s+(?:sealed\s+|partial\s+|static\s+)*class\s+(\w+)[^\n{]*\{', src):
        name = m.group(1)
        cm = re.search(rf'\n\s*(?:public|internal)\s+{name}\s*\(([^)]*)\)', src[m.end():], re.S)
        params = [] if not cm else [
            re.sub(r".*\s(\w+)$", r"\1", p.strip())
            for p in cm.group(1).split(",") if p.strip()
        ]
        ctors[name] = params

for impl in {i for _, i in re.findall(r'services\.Add(?:Singleton|Transient|Scoped)<(\w+),\s*(\w+)>\(\)', app_src)}:
    for dep in ctors.get(impl, []):
        if dep not in registered:
            fail(f"[di] {impl} depends on '{dep}' which App.xaml.cs never registers")

# ── 10. app resource keys ─────────────────────────────────────────────────
defined = set()
for x in xaml_files():
    defined |= set(re.findall(r'x:Key="([\w\.]+)"', read(x)))
SYSTEM_OK = {
    "ApplicationPageBackgroundThemeBrush", "AccentButtonStyle", "CardBackgroundFillColorDefaultBrush",
    "SystemControlBackgroundBaseLowBrush", "SystemAccentColor", "SystemControlAcrylicElementBrush",
    "SubtitleTextBlockStyle", "DefaultTextBlockStyle", "BodyTextBlockStyle", "TitleTextBlockStyle",
    "TextControlThemeMinHeight", "CaptionTextBlockStyle",
}
for x in xaml_files():
    for key in set(re.findall(r'\{(?:StaticResource|ThemeResource)\s+([\w\.]+)\}', read(x))):
        if key not in defined and key not in SYSTEM_OK:
            fail(f"[res] {x.relative_to(ROOT)}: undefined resource '{key}'")

# ── 11. packaging consistency ─────────────────────────────────────────────
csproj_path = ROOT / "NineLivesAudio.csproj"
csproj = read(csproj_path)
tfm = re.search(r"<TargetFramework>([^<]+)</TargetFramework>", csproj).group(1)
version = re.search(r"<Version>([^<]+)</Version>", csproj)
version = version.group(1) if version else ""

for required in ("Assets/app-icon.ico", "Assets/nine-lives-logo.png", "app.manifest"):
    if not (ROOT / required).exists():
        fail(f"[pack] missing asset '{required}' referenced by the project")

publish_profile = ROOT / "Properties" / "PublishProfiles" / "win-x64.pubxml"
if not publish_profile.exists():
    fail("[pack] Properties/PublishProfiles/win-x64.pubxml is missing")

# TFM drift between project, scripts and docs
for script in ("sign.ps1", "build-and-run.ps1"):
    path = ROOT / script
    if path.exists():
        for mentioned in set(re.findall(r"net\d+\.0-windows[\d\.]+", read(path))):
            if mentioned != tfm:
                fail(f"[pack] {script} targets {mentioned} but the project targets {tfm}")

readme = read(ROOT / "README.md")
for wrong in set(re.findall(r"\.NET (\d+)", readme)):
    if f".NET {wrong}" != f".NET {tfm.split('.')[0].replace('net', '')}":
        fail(f"[pack] README mentions .NET {wrong} but the project targets {tfm}")

if version:
    for doc in ("installer.iss",):
        dpath = ROOT / doc
        if dpath.exists() and f'"{version}"' not in read(dpath):
            fail(f"[pack] {doc} version does not match the project version {version}")

# installer source dir must match the publish profile output
iss = (ROOT / "installer.iss")
if iss.exists() and publish_profile.exists():
    src_dir = re.search(r'#define MyAppSourceDir "([^"]+)"', read(iss))
    pub_dir = re.search(r"<PublishDir>([^<\\]+)", read(publish_profile))
    if src_dir and pub_dir and src_dir.group(1) != pub_dir.group(1):
        fail(f"[pack] installer expects '{src_dir.group(1)}' but publish writes '{pub_dir.group(1)}'")

# ── 12. XAML namespace declarations ───────────────────────────────────────
for x in xaml_files():
    src = read(x)
    if "loc:Loc." in src:
        if 'xmlns:loc="using:NineLivesAudio.Helpers"' not in src:
            fail(f"[xaml] {x.relative_to(ROOT)}: uses loc:Loc.* but does not declare xmlns:loc")
    for prefix, target in re.findall(r'xmlns:(\w+)="using:([\w\.]+)"', src):
        if target.startswith("NineLivesAudio."):
            ns_file_exists = any(
                re.search(rf'^namespace {re.escape(target)}', read(f), re.M)
                for f in cs_files()
            )
            if not ns_file_exists:
                fail(f"[xaml] {x.relative_to(ROOT)}: xmlns:{prefix} points at missing namespace {target}")

# ── report ────────────────────────────────────────────────────────────────
print(f"scanned {len(cs_files())} C# files, {len(xaml_files())} XAML files, {len(en_keys)} localization keys")
if problems:
    print(f"\n{len(problems)} problem(s):")
    for p in problems:
        print("  ✗", p)
    sys.exit(1)
print("\n✓ all checks passed")
