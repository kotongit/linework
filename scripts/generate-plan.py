#!/usr/bin/env python3
"""Converts Daily_Practice_Plan.pdf into Sources/LineworkCore/PracticePlanData.swift.

Usage: generate-plan.py <pdf> <output.swift>
       generate-plan.py <pdf> --list-missing

Titles, bullets and short reading lines come from scripts/plan-summaries.json, keyed by exact text.
Exits non-zero, listing every text with no entry. --list-missing prints those texts as JSON instead.
Dev-time only: the generated file is committed and the PDF isn't needed to build.
"""
import json
import re
import sys
from pathlib import Path

from pypdf import PdfReader

DAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
WORDS = {w: i + 1 for i, w in enumerate(
    "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen "
    "sixteen seventeen eighteen nineteen twenty".split())}


def fragments(page):
    """(x, y, text) for each text run, in reading order."""
    out = []

    def visit(text, cm, tm, _font, _size):
        if text.strip():
            out.append((round(cm[4] + tm[4] * cm[0], 1), round(cm[5] + tm[5] * cm[3], 1), text))

    page.extract_text(visitor_text=visit)
    return out


def lines(frags):
    """Merges runs that share a baseline into lines of (x, y, text)."""
    merged = []
    for x, y, text in frags:
        text = text.replace("\n", " ")
        if merged and merged[-1][1] == y and merged[-1][0] == x:
            merged[-1] = (x, y, merged[-1][2] + text)
        else:
            merged.append((x, y, text))
    return [(x, y, re.sub(r"\s+", " ", t).strip()) for x, y, t in merged]


def join(parts):
    return re.sub(r"\s+", " ", " ".join(parts)).strip()


# The plan's own "5-min class-mode mini", for the forever week, which only names it.
CLASS_MINI = [{"count": 6, "seconds": 30}, {"count": 2, "seconds": 60}]


def timed_steps(text):
    # Benchmarks use one saved set of photos, so a shuffled session would spoil the comparison.
    if "own hand" in text or text.startswith("Benchmark"):
        return None
    found = []  # (position, count, seconds), so a class followed by a single pose stays in order
    if "class-mode" in text.lower() or "class mode" in text.lower():
        for m in re.finditer(r"(\d+) ?× ?(\d+) ?(s|min)\b", text):
            found.append((m.start(), int(m[1]), int(m[2]) * (60 if m[3] == "min" else 1)))
    words = "|".join(WORDS)
    pattern = rf"\b({words}|\d+) (\d+(?:\.\d+)?)-(s|min) (?:[\w-]+ )?(?:gestures?|poses?|heads?|figures?|lines?)\b"
    for m in re.finditer(pattern, text, re.I):
        c = WORDS.get(m[1].lower()) or int(m[1])
        found.append((m.start(), c, int(float(m[2]) * (60 if m[3] == "min" else 1))))
    # "Twenty heads from photos, 45 s each" and "Two figures from photos (10 min each)".
    # Only these subjects: elsewhere "N things, X min each" means one photo several ways, or no photo at all.
    each = rf"\b({words}) (?:heads from photos|head-and-neck studies|figures from photos)(?:, | \()(\d+) ?(s|min) each\b"
    for m in re.finditer(each, text, re.I):
        found.append((m.start(), WORDS[m[1].lower()], int(m[2]) * (60 if m[3] == "min" else 1)))
    # "15 min: twenty more heads" shares the time out between them.
    for m in re.finditer(rf"\b(\d+) min: ({words}) more heads\b", text, re.I):
        c = WORDS[m[2].lower()]
        found.append((m.start(), c, int(m[1]) * 60 // c))
    # Drawing from memory has no photo to show, unless the timed poses come first.
    memory = text.split("·")[0].find("from memory")
    if memory >= 0 and not any(position < memory for position, _, _ in found):
        return None
    steps = [(c, s) for _, c, s in sorted(found)]
    return [{"count": c, "seconds": s} for c, s in steps] or None


def minutes_in(text, default=30):
    m = re.search(r"(?<![\d-])(\d+)(?:-\d+)? min\b", text)
    return int(m.group(1)) if m else default


def block(kind, text, minutes):
    out = {"kind": kind, "text": text, "minutes": minutes}
    steps = timed_steps(text)
    if steps:
        out["timed"] = steps
    return out


def saturday(rows):
    """rows are the spanning lines of a Saturday. Warm-up runs up to the first line ending in a period."""
    texts = [t for _, _, t in rows]
    assert texts[0].startswith("Warm-up:"), texts[0]
    end = next(i for i, t in enumerate(texts) if t.endswith("."))
    warmup = join(texts[: end + 1]).removeprefix("Warm-up:").strip()
    rest = join(texts[end + 1:])
    blocks = [block("warmup", warmup, 5)]
    split = re.search(r"(?:10-min break\s*→\s*)?Block B\s*·\s*", rest)
    if split:
        a = re.sub(r"^Block A\s*·\s*", "", rest[: split.start()]).strip()
        b = rest[split.end():].strip()
        blocks.append(block("blockA", a, minutes_in(a)))
        blocks.append(block("blockB", b, minutes_in(b)))
    else:
        blocks.append(block("exercise", rest, minutes_in(rest)))
    return blocks


def parse_week(page, titles):
    rows = lines(fragments(page))
    rows = [r for r in rows if not r[2].startswith("Daily practice plan") and r[2] != "☐" and not r[2].isdigit()]
    head = next(t for _, _, t in rows if t.startswith("WEEK "))
    m = re.match(r"WEEK (\d+) · BOOK (\d+) · (.+) · WEEK (\d+) OF (\d+)", head)
    number, book, upper, of_book, in_book = m.groups()
    i_head = next(i for i, r in enumerate(rows) if r[2] == head)
    # Both can wrap, so each runs up to the next labelled row.
    i_read = next(i for i, r in enumerate(rows) if r[2].startswith("Read this week:"))
    i_check = next(i for i, r in enumerate(rows) if r[2].startswith("Daily check"))
    i_day = next(i for i, r in enumerate(rows) if r[2] == "Day")
    reading = join(t for _, _, t in rows[i_read:i_check]).split(":", 1)[1].strip()
    check = join(t for _, _, t in rows[i_check:i_day]).split(":", 1)[1].strip()
    theme_row = next(r for r in rows[i_head + 1:] if r[0] < 60)
    x_warm = next(x for x, _, t in rows if t.startswith("Warm-up · 5 min"))
    x_focus = next(x for x, _, t in rows if t.startswith("Focus · 20 min"))

    starts = [i for i, r in enumerate(rows) if r[0] < 60 and r[2] in DAYS]
    assert len(starts) == 7, head
    days = []
    for n, s in enumerate(starts):
        e = starts[n + 1] if n < 6 else len(rows)
        body = [r for r in rows[s + 1:e] if r[0] >= x_warm - 1 and r[2] != "Done"]
        if n == 6:
            days.append({"isRest": True, "restText": join(t for _, _, t in body), "blocks": []})
        elif n == 5:
            days.append({"isRest": False, "blocks": saturday(body)})
        else:
            warmup = join(t for x, _, t in body if x < x_focus - 1)
            focus = join(t for x, _, t in body if x >= x_focus - 1)
            blocks = [] if warmup.startswith("None") else [block("warmup", warmup, 5)]
            # The setup day says how long it takes ("about 45 min"); every other focus is 20.
            about = re.search(r"about (\d+) min", focus) if warmup.startswith("None") else None
            blocks += [block("focus", focus, int(about[1]) if about else 20), {"kind": "check", "text": check, "minutes": 5}]
            days.append({"isRest": False, "blocks": blocks})
    # "Class mode. Emphasis: …" days run the class the week's first day spells out.
    # "Same structure" days repeat the day before.
    class_steps = previous = None
    for day in days:
        for b in day["blocks"]:
            if b["kind"] != "focus":
                continue
            if b.get("timed") and b["text"].lower().startswith("class mode"):
                class_steps = b["timed"]
            elif class_steps and b["text"].startswith("Class mode.") and "timed" not in b:
                b["timed"] = class_steps
            elif previous and b["text"].startswith("Same structure.") and "timed" not in b:
                b["timed"] = previous
            previous = b.get("timed")
    return {
        "number": int(number), "book": int(book), "bookTitle": titles.get(int(book), upper.title()),
        "weekOfBook": int(of_book), "weeksInBook": int(in_book), "theme": theme_row[2],
        "reading": reading, "dailyCheck": check, "days": days,
    }


def parse_forever(page):
    rows = lines(fragments(page))
    x_warm = next(x for x, _, t in rows if t == "Warm-up")
    x_focus = next(x for x, _, t in rows if t == "Focus")
    starts = [i for i, r in enumerate(rows) if r[0] < 60 and r[2] in DAYS]
    days = []
    for n, s in enumerate(starts):
        e = starts[n + 1] if n < 6 else next(i for i, r in enumerate(rows) if r[2].startswith("Every piece"))
        body = rows[s + 1:e]
        warmup = join(t for x, _, t in body if x_warm - 1 <= x < x_focus - 1)
        focus = join(t for x, _, t in body if x >= x_focus - 1)
        if DAYS[n] == "Sun":
            days.append({"isRest": True, "restText": focus, "blocks": []})
        else:
            first = block("warmup", warmup, 5)
            if warmup == "5-min class mode":
                first["timed"] = CLASS_MINI
            days.append({"isRest": False, "blocks": [
                first, block("focus", focus, 20),
                {"kind": "check", "text": "Predict, compare, write one rule", "minutes": 5},
            ]})
    ritual_at = next(i for i, r in enumerate(rows) if r[2].startswith("Monthly ritual"))
    ritual = [clean_bullet(t) for _, _, t in rows[ritual_at + 1:] if not t.startswith("Daily practice plan")]
    return days, regroup(ritual)


def clean_bullet(text):
    """The PDF draws bullets in a symbol font, which extract as control or private-use characters."""
    return re.sub(r"^[\s\x00-\x1f\x7f\ue000-\uf8ff]*[\x00-\x1f\x7f\ue000-\uf8ff]\s*", "• ", text).replace("•  ", "• ").strip()


def regroup(ls):
    """Joins wrapped lines onto their bullet."""
    out = []
    for t in ls:
        if t.startswith("•") or not out:
            out.append(t)
        else:
            out[-1] += " " + t
    return "\n".join(out)


def guide(pages):
    out = []
    for page in pages:
        out += [clean_bullet(t) for _, _, t in lines(fragments(page))
                if not t.startswith("Daily practice plan") and not t.isdigit()]
    return reflow(out)


def reflow(ls):
    """Rejoins lines the PDF wrapped: a nearly full line continues onto the next. A bare bullet joins its text."""
    out = []
    previous = ""
    for t in ls:
        if out and out[-1] == "•":
            out[-1] = "• " + t
        elif out and len(previous) >= 90 and not t.startswith("•"):
            out[-1] += " " + t
        else:
            out.append(t)
        previous = t
    return "\n".join(out)


SUMMARIES = Path(__file__).with_name("plan-summaries.json")
REWRITES = json.loads(Path(__file__).with_name("plan-rewrites.json").read_text())


def undated(text):
    """The plan runs on weeks and weekdays, so its text never names a calendar date."""
    for old, new in sorted(REWRITES, key=lambda r: -len(r[0])):
        text = text.replace(old, new)
    return text


def undate_all(value):
    if isinstance(value, str):
        return undated(value)
    if isinstance(value, list):
        return [undate_all(v) for v in value]
    if isinstance(value, dict):
        return {undate_all(k): undate_all(v) for k, v in value.items()}
    return value


def week_ranges(guide, weeks):
    """Turns the guide's book table from date ranges into plan weeks."""
    first = {}
    for week in weeks:
        first.setdefault(week["book"], [week["number"], week["number"]])[1] = week["number"]
    ranges = iter(sorted(first.values()))

    def label(_):
        a, b = next(ranges)
        return f"Week {a}" if a == b else f"Weeks {a}–{b}"

    guide = re.sub(r"\b\d{1,2} [A-Z][a-z]{2} – \d{1,2} [A-Z][a-z]{2} \d{4}", label, guide)
    return re.sub(r"(?m)^Dates$", "Plan weeks", guide)


def merge_summaries(plan):
    """Adds title, bullets, readingShort and monthlyRitualBullets. Returns the texts that have no entry."""
    summaries = undate_all(json.loads(SUMMARIES.read_text())) if SUMMARIES.exists() else {}
    blocks, reading = summaries.get("blocks", {}), summaries.get("reading", {})
    missing = {"blocks": [], "reading": [], "ritual": []}
    for week in plan["weeks"]:
        if week["reading"] in reading:
            week["readingShort"] = reading[week["reading"]]
        elif week["reading"] not in missing["reading"]:
            missing["reading"].append(week["reading"])
    for day in [d for w in plan["weeks"] for d in w["days"]] + plan["foreverWeek"]:
        for b in day["blocks"]:
            entry = blocks.get(b["text"])
            if entry:
                b["title"], b["bullets"] = entry["title"], entry["bullets"]
            elif b["text"] not in missing["blocks"]:
                missing["blocks"].append(b["text"])
    if summaries.get("ritual"):
        plan["monthlyRitualBullets"] = summaries["ritual"]
    else:
        missing["ritual"].append(plan["monthlyRitual"])
    return missing


def main(pdf, output):
    pages = PdfReader(pdf).pages
    titles = {}
    for _, _, t in lines(fragments(pages[2])):
        m = re.match(r"^(\d\d) · (.+)$", t)
        if m:
            titles[int(m.group(1))] = m.group(2)
    weeks = [parse_week(p, titles) for p in pages if re.search(r"^WEEK \d+ · BOOK", p.extract_text(), re.M)]
    forever, ritual = parse_forever(pages[41])
    plan = undate_all({"weeks": weeks, "foreverWeek": forever, "monthlyRitual": ritual})
    plan["guideText"] = undated(week_ranges(guide(pages[1:3]), weeks))
    missing = merge_summaries(plan)
    if output == "--list-missing":
        print(json.dumps(missing, ensure_ascii=False, indent=1))
        return
    if any(missing.values()):
        print("No entry in plan-summaries.json for:", file=sys.stderr)
        for kind, texts in missing.items():
            for t in texts:
                print(f"  [{kind}] {t}", file=sys.stderr)
        sys.exit(1)
    body = json.dumps(plan, ensure_ascii=False, indent=1)
    with open(output, "w") as f:
        f.write("// Generated by scripts/generate-plan.py from Daily_Practice_Plan.pdf. Do not edit.\n\n")
        f.write('enum PracticePlanData {\n    static let json = ##"""\n' + body + '\n"""##\n}\n')


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
